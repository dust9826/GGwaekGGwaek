using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// <b>이 시뮬레이터의 상태 전부.</b> 지면 위 눈 깊이를 밀리미터 정수로 담은 배열 하나와,
    /// relax 가 쓰는 델타 스크래치, 청크별 휴면 카운터, 그리고 보존 원장.
    ///
    /// 왜 정수인가 — 정밀도가 아니라 두 가지 정확성 때문이다.
    /// <list type="number">
    /// <item>피어 간 비트단위 동일성. 정수 연산은 플랫폼·컴파일러·SIMD 재배열과 무관하게 같은 답을
    /// 낸다. 데디서버가 원인만 복제하고 각 피어가 필드를 재생성하는 구조에서는 이게 전제조건이다.</item>
    /// <item>질량 보존이 근사가 아니라 항등식이 된다. 부동소수면 "엡실론 이하여야 함"이 되고,
    /// 그러면 진짜 누수와 드리프트를 구별할 수 없다. v7 의 누수는 컷 텍셀당 평균 +0.05 고정단위,
    /// 초당 46 mL 였고 3분을 돌린 뒤에야 계측기에 잡혔다.</item>
    /// </list>
    ///
    /// 왜 <c>ushort</c> 인가 — 0~65.5 m 는 눈 최고 4 m 남짓인 이 시뮬에서 사실상 제한이 아니고,
    /// 무엇보다 <c>ushort[]</c> 가 R16 UNorm 텍스처와 <b>같은 바이트</b>라 업로드가 memcpy 다.
    /// int32 면 매 프레임 변환 패스가 하나 더 붙는다.
    ///
    /// 이 클래스는 <c>Texture2D</c> · <c>Shader</c> · <c>Camera</c> · <c>MonoBehaviour</c> · <c>Time</c> 을
    /// 참조하지 않는다. 데디서버가 <c>-batchmode -nographics</c> 로 돌기 때문이고, 그 규칙을
    /// 어셈블리 경계가 강제한다.
    /// </summary>
    public sealed class SnowHeightFieldCpu
    {
        /// <summary>ushort 천장. 65,535 mm = 65.535 m.</summary>
        public const int MaxHeightMm = 65535;

        /// <summary>조용한 스텝이 이만큼 이어지면 청크가 잠든다.</summary>
        public const int RestStepsToSleep = 8;

        public SnowFieldGeometry Geo { get; }

        /// <summary>높이. 렌더러가 <c>SetPixelData</c> 로 그대로 memcpy 한다.</summary>
        public ushort[] HeightMm { get; }

        /// <summary>relax 스크래치. 반영 후 항상 0 으로 되돌려 놓는다 — 안 그러면 다음 스텝이 오염된다.</summary>
        public int[] DeltaMm { get; }

        private readonly byte[] _chunkRest;
        private long _totalHeightMm;

        /// <summary>
        /// dirty 색인. 휴면 카운터가 진실이고 트리는 그 색인일 뿐이다 — 두 곳에 같은 상태를 두면
        /// 반드시 갈라지므로, 트리는 여기서만 갱신된다. 붙이지 않아도 필드는 그대로 동작한다.
        /// </summary>
        private SnowChunkQuadtree _dirtyIndex;

        /// <summary>
        /// 이번 스텝에 <b>실제로 무언가 움직인</b> 청크. 활성 집합과 다르다 - 활성 집합은 블레이드
        /// 반경 전체이고 그 대부분은 손대지 않은 처녀설이다.
        ///
        /// 표현 데이터(로브·둥근 어깨·상한)를 다시 굽는 범위가 정확히 이것이어야 한다. 활성 집합을
        /// 쓰면 안 바뀐 눈을 매 프레임 다시 굽게 되고, 실측으로 그것이 프레임의 79% 였다.
        /// </summary>
        private readonly List<int> _changed = new List<int>(256);
        private readonly int[] _changedStamp;
        private int _stepStamp;

        public IReadOnlyList<int> ChangedChunks => _changed;

        /// <summary>
        /// 렌더가 아직 안 가져간 청크. <see cref="_changed"/> 와 목적이 다르다.
        ///
        /// <para>⚠ <b><see cref="BeginStep"/> 이 이것을 비우지 않는다.</b> 한 프레임에
        /// <c>FixedUpdate</c> 가 <b>0번일 수도 여러 번일 수도</b> 있으므로, 스텝마다 비우면
        /// <c>LateUpdate</c> 는 마지막 스텝 것만 보거나 아예 빈 목록을 본다 — 그러면 앞 스텝이
        /// 바꾼 셀이 화면에 안 올라간다. <b>렌더가 가져갈 때만</b>
        /// (<see cref="ClearRenderDirty"/>) 비운다.</para>
        /// </summary>
        private readonly List<int> _renderDirty = new List<int>(256);

        /// <summary>중복 방지. 스텝 스탬프가 아니라 <b>플래그</b>다 — 스텝을 가로질러 누적하므로.</summary>
        private readonly bool[] _renderDirtyFlag;

        /// <inheritdoc cref="_renderDirty"/>
        public IReadOnlyList<int> RenderDirtyChunks => _renderDirty;

        /// <summary>렌더가 다 올린 뒤에 부른다. 이것만이 <see cref="RenderDirtyChunks"/> 를 비운다.</summary>
        public void ClearRenderDirty()
        {
            for (int i = 0; i < _renderDirty.Count; i++) _renderDirtyFlag[_renderDirty[i]] = false;
            _renderDirty.Clear();
        }

        /// <summary>
        /// 이 스텝에서 <b>값이 실제로 바뀐 셀</b>. 청크 목록과 목적이 다르다 - 저쪽은 다시 <b>구울</b>
        /// 범위이고(굽는 단위가 청크다), 이쪽은 네트워크로 <b>보낼</b> 범위다.
        ///
        /// <para><b>왜 셀 단위여야 하는가(2026-08-20 실측).</b> 청크 단위로 보내면 한 셀이 바뀌어도
        /// 256 셀 전부(512 바이트)를 보내고, 공 밑의 청크는 매 틱 바뀌므로 매 틱 다시 보낸다.
        /// 6 초 굴리기에 762 청크 = 390 KB 를 보냈는데 자국이 덮은 서로 다른 청크는 20~25 개였다 -
        /// 30 배 중복이다. 실제로 값이 변하는 것은 자국의 앞날 한 줄(10~30 셀/틱)뿐이다.</para>
        ///
        /// <para><see cref="AddAt"/> 가 높이를 바꾸는 <b>유일한 창구</b>이고 실제로 반영된 양을 이미
        /// 알고 있으므로, 여기서 잡는 것이 공짜다 - 잘려서 0 이 된 변경은 목록에 안 들어간다.</para>
        /// </summary>
        private readonly List<int> _changedCells = new List<int>(1024);
        private readonly int[] _cellStamp;

        /// <inheritdoc cref="_changedCells"/>
        public IReadOnlyList<int> ChangedCells => _changedCells;

        /// <summary>
        /// <b>절삭이 바꾼</b> 셀. 네트워크로 보내는 것은 이쪽뿐이다.
        ///
        /// <para><b>왜 나누는가.</b> 한 스텝에서 값이 바뀌는 셀은 둘로 갈린다 - 날이나 공이 깎은
        /// 자리와, 그 뒤 이완(안식각)이 고쳐 쓴 자리다. 실측(2026-08-20)으로 <b>이완이 절삭의 4.2 배</b>
        /// 다(컷 102,615 / 이완 428,947). 이완은 각 피어가 자기 격자에서 돌리므로 결과 모양을 스스로
        /// 만든다 - 보낼 이유가 없다.</para>
        ///
        /// <para><b>인덱스 경계로는 안 된다.</b> 처음에는 "앞의 N 개가 절삭" 으로 두었는데, 공의 수확이
        /// <c>SnowPlowStepCpu.Step</c>(이완 포함) <b>뒤</b>에 오므로 경계 뒤로 밀려 통째로 빠졌다 -
        /// 클라이언트가 공 자국을 하나도 못 받았다(실측 0.580 m). 한 스텝에 절삭 구간이 <b>여럿</b>이고
        /// 순서가 섞이므로, 구간을 명시로 열고 닫는 것이 유일하게 맞는 모양이다.</para>
        ///
        /// <para>절삭 뒤 이완이 <b>같은 셀</b>을 또 고쳐도 목록에는 한 번만 담기고, 보낼 때 읽는 값은
        /// 스텝이 끝난 <b>최종 높이</b>이므로 이완의 결과까지 반영된다. 즉 이 분리는 "무엇을 보낼지" 만
        /// 줄이고 "무엇을 보낼 값인지" 는 바꾸지 않는다.</para>
        /// </summary>
        private readonly List<int> _cutCells = new List<int>(512);
        private readonly int[] _cutStamp;
        private bool _recordCut;

        /// <inheritdoc cref="_cutCells"/>
        public IReadOnlyList<int> CutCells => _cutCells;

        /// <summary>절삭 구간을 연다. 닫을 때까지 바뀐 셀이 <see cref="CutCells"/> 에 담긴다.</summary>
        public void BeginCutPhase() => _recordCut = true;

        /// <inheritdoc cref="BeginCutPhase"/>
        public void EndCutPhase() => _recordCut = false;

        /// <summary>스텝 시작에서 부른다. 변경 목록을 비운다.</summary>
        public void BeginStep()
        {
            _changed.Clear();
            _changedCells.Clear();
            _cutCells.Clear();
            _recordCut = false;
            _stepStamp++;
        }

        /// <summary>
        /// 이 격자가 앉은 바닥. <c>null</c> 이면 <b>평지·전면 적설 가능</b>이고 그것이 2026-08-24
        /// 까지의 동작이다 — 배열을 하나도 만들지 않으므로 비용도 0 이다.
        ///
        /// <para>가변 상태가 아니다. 굽힌 데이터이므로 생성 시에 받고 그 뒤로 안 바뀐다.</para>
        /// </summary>
        public SnowGroundFieldCpu Ground { get; }

        public SnowHeightFieldCpu(SnowFieldGeometry geo, int initialDepthMm)
            : this(geo, initialDepthMm, null) { }

        public SnowHeightFieldCpu(SnowFieldGeometry geo, int initialDepthMm, SnowGroundFieldCpu ground)
        {
            Geo = geo;
            Ground = ground;
            HeightMm = new ushort[geo.CellCount];
            DeltaMm = new int[geo.CellCount];
            _chunkRest = new byte[geo.ChunkCount];
            _changedStamp = new int[geo.ChunkCount];
            _renderDirtyFlag = new bool[geo.ChunkCount];
            _cellStamp = new int[geo.CellCount];
            _cutStamp = new int[geo.CellCount];

            int d = initialDepthMm < 0 ? 0 : (initialDepthMm > MaxHeightMm ? MaxHeightMm : initialDepthMm);
            long total = 0;
            if (d != 0)
            {
                // <b>초기 적설도 마스크를 따른다.</b> 안 그러면 눈이 없기로 한 자리에 총량이 실려
                // 원장이 처음부터 어긋나고, 화면에서는 clip 이 그것을 감춰 조용히 사라진다.
                if (ground == null)
                {
                    for (int i = 0; i < HeightMm.Length; i++) HeightMm[i] = (ushort)d;
                    total = (long)geo.CellCount * d;
                }
                else
                {
                    for (int i = 0; i < HeightMm.Length; i++)
                    {
                        if (!ground.IsSnowableAt(i)) continue;
                        int cellDepth = ground.InitialDepthAt(i, d);
                        HeightMm[i] = (ushort)cellDepth;
                        total += cellDepth;
                    }
                }
            }
            _totalHeightMm = total;

            for (int i = 0; i < _chunkRest.Length; i++) _chunkRest[i] = RestStepsToSleep;
        }

        /// <summary>
        /// 이 셀이 담을 수 있는 최대 높이. 눈이 불가능한 셀은 <b>용량 0</b> 이다 — 마스크를 예외
        /// 경로가 아니라 천장으로 표현하면 퇴적·이완·재적설이 각자 마스크를 기억할 필요가 없다.
        /// </summary>
        public int CapacityAt(int cellIndex)
            => Ground == null || Ground.IsSnowableAt(cellIndex) ? MaxHeightMm : 0;

        /// <summary>증분 유지되는 총합. <see cref="RecomputeTotalHeightMm"/> 과 항상 같아야 한다.</summary>
        public long TotalHeightMm => _totalHeightMm;

        /// <summary>O(N) 재계산. 증분 원장이 갈라졌는지 확인하는 유일한 방법이다.</summary>
        public long RecomputeTotalHeightMm()
        {
            long sum = 0;
            for (int i = 0; i < HeightMm.Length; i++) sum += HeightMm[i];
            return sum;
        }

        public double TotalVolumeM3
            => _totalHeightMm * 1e-3 * (SnowFieldGeometry.CellSizeM * (double)SnowFieldGeometry.CellSizeM);

        public ushort Get(int cx, int cz) => HeightMm[Geo.CellIndex(cx, cz)];

        public ushort GetAt(int cellIndex) => HeightMm[cellIndex];

        /// <summary>
        /// 직접 대입. <b>용량을 넘지 못한다</b> — 눈이 불가능한 셀에 값을 넣으려는 시도는 0 이 된다.
        /// 스냅샷 적용(클라이언트가 서버 청크를 받는 경로)이 이것을 쓰므로, 마스크가 어긋난 피어가
        /// 눈을 그리는 대신 그 자리를 비운다.
        /// </summary>
        public void Set(int cx, int cz, ushort mm)
        {
            int i = Geo.CellIndex(cx, cz);
            int cap = CapacityAt(i);
            int v = mm > cap ? cap : mm;
            _totalHeightMm += v - HeightMm[i];
            HeightMm[i] = (ushort)v;
        }

        /// <summary>
        /// 더하고 <b>실제로 반영된 양</b>을 돌려준다. 계산한 양이 아니라.
        /// 바닥이나 천장에서 잘린 양을 호출자가 모르면 그만큼이 장부 없이 사라진다 —
        /// v7 의 누수가 정확히 그 형태였다.
        /// </summary>
        public int Add(int cx, int cz, int deltaMm) => AddAt(Geo.CellIndex(cx, cz), deltaMm);

        public int AddAt(int cellIndex, int deltaMm)
        {
            int before = HeightMm[cellIndex];
            int after = before + deltaMm;
            int cap = CapacityAt(cellIndex);
            if (after < 0) after = 0;
            else if (after > cap) after = cap;
            HeightMm[cellIndex] = (ushort)after;
            int applied = after - before;
            _totalHeightMm += applied;

            // 실제로 바뀐 셀만 기록한다. 스텝 안에서 같은 셀이 여러 번(컷 + 이완) 바뀔 수 있으므로
            // 스탬프로 한 번만 담는다 - 목록이 중복으로 부풀면 보낼 예산을 헛되이 쓴다.
            if (applied != 0)
            {
                if (_cellStamp[cellIndex] != _stepStamp)
                {
                    _cellStamp[cellIndex] = _stepStamp;
                    _changedCells.Add(cellIndex);
                }

                if (_recordCut && _cutStamp[cellIndex] != _stepStamp)
                {
                    _cutStamp[cellIndex] = _stepStamp;
                    _cutCells.Add(cellIndex);
                }
            }

            return applied;
        }

        /// <summary>델타를 높이에 반영하고 델타를 0 으로. 실제로 반영된 양을 돌려준다.</summary>
        public int ApplyDelta(int cx, int cz) => ApplyDeltaAt(Geo.CellIndex(cx, cz));

        public int ApplyDeltaAt(int cellIndex)
        {
            int d = DeltaMm[cellIndex];
            DeltaMm[cellIndex] = 0;
            return d == 0 ? 0 : AddAt(cellIndex, d);
        }

        // ---------------------------------------------------------------- 청크 휴면

        /// <summary>dirty 색인을 붙인다. 현재 깨어 있는 청크를 트리에 반영하고 시작한다.</summary>
        public void AttachDirtyIndex(SnowChunkQuadtree tree)
        {
            _dirtyIndex = tree;
            if (tree == null) return;
            for (int ci = 0; ci < _chunkRest.Length; ci++)
            {
                if (IsChunkAwake(ci)) tree.MarkDirty(ci);
                else tree.ClearDirty(ci);
            }
        }

        public void WakeChunk(int chunkIndex)
        {
            if (_chunkRest[chunkIndex] >= RestStepsToSleep) _dirtyIndex?.MarkDirty(chunkIndex);
            _chunkRest[chunkIndex] = 0;

            if (_changedStamp[chunkIndex] != _stepStamp)
            {
                _changedStamp[chunkIndex] = _stepStamp;
                _changed.Add(chunkIndex);
            }

            // 렌더용은 스텝을 가로질러 누적한다 - 위 _renderDirty 주석 참고.
            if (!_renderDirtyFlag[chunkIndex])
            {
                _renderDirtyFlag[chunkIndex] = true;
                _renderDirty.Add(chunkIndex);
            }
        }

        public void RestChunk(int chunkIndex)
        {
            if (_chunkRest[chunkIndex] >= RestStepsToSleep) return;
            if (++_chunkRest[chunkIndex] >= RestStepsToSleep) _dirtyIndex?.ClearDirty(chunkIndex);
        }

        public bool IsChunkAwake(int chunkIndex) => _chunkRest[chunkIndex] < RestStepsToSleep;

        public void WakeChunkOfCell(int cx, int cz)
            => WakeChunk(Geo.ChunkIndex(Geo.ChunkOfCellX(cx), Geo.ChunkOfCellZ(cz)));
    }
}

using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 씬의 눈을 소유하고 굴리는 스테이지. <b>서버가 알 수 있는 눈</b>이다 — 격자가 CPU 위의
    /// <see cref="SnowHeightFieldCpu"/>(밀리미터 정수)라서 그래픽 장치가 없어도 돈다.
    ///
    /// <para><b>서버만 시뮬레이션하고, 클라이언트는 바뀐 청크를 받아 적용한다.</b> 원인 복제(자세·블레이드)
    /// 만으로 각 피어가 같은 결과를 재생하게 하려 했지만 <b>실측에서 분포가 갈렸다</b> — 원격 차량의
    /// 트랜스폼은 클라이언트에서 표시용으로 보간된 값이고 틱 지연도 있어, 같은 입력이라도 스윕이 서로
    /// 다른 자세로 찍힌다. 총량은 재분배라 그래도 일치해서 <b>총량 비교로는 이 결함을 잡지 못했다</b>
    /// (그 약한 검사가 통과하는 동안 변한 셀은 514/514/492 로 달랐다).</para>
    ///
    /// <para>그래서 권위는 서버의 격자 하나이고, 전파는 <see cref="SnowHeightFieldCpu.ChangedChunks"/> 를
    /// 신뢰 데이터 채널로 보내는 것이다. RPC 는 이 프로젝트에서 쓸 수 없다(위버가 심는 호출이
    /// <c>Fusion.Runtime</c> 의 internal 이라 접근 검사에서 막힌다) — 대신
    /// <see cref="NetworkRunner.SendReliableDataToPlayer"/> 를 쓴다.</para>
    ///
    /// <para><b>스텝은 <see cref="FixedUpdateNetwork"/> 에서만 한다.</b> <c>FixedUpdate</c> 로 굴리면 피어마다
    /// 호출 횟수와 순서가 달라져 같은 입력이 다른 결과를 낸다 — 결정론이 깨지는 가장 흔한 방식이다.</para>
    ///
    /// <para>차량은 <see cref="NetworkRunner.GetAllBehaviours{T}(List{T})"/> 로 <b>런너 범위</b>에서 찾는다.
    /// 전역 검색(<c>FindObjectsByType</c>)은 한 프로세스에 피어가 여럿일 때 남의 피어 차량까지 긁어
    /// 남의 화면의 눈을 깎는다. 이 프로젝트는 그 실패를 v7 에서 이미 한 번 겪었다.</para>
    /// </summary>
    public sealed class SnowCpuStage : SimulationBehaviour
    {
        /// <summary>필드의 월드 원점(XZ). v7 리그와 같은 값을 넣으면 같은 자리를 덮는다.</summary>
        [SerializeField] private Vector2 _originXZ = new Vector2(-60f, -55f);

        /// <summary>필드 크기(m). 셀 크기는 코어가 0.125 m 로 고정한다.</summary>
        [SerializeField] private Vector2 _sizeMeters = new Vector2(120f, 110f);

        [SerializeField, Min(0)] private int _initialDepthMm = 300;

        /// <summary>
        /// 구운 바닥. <b>비우면 평지</b>(바닥 0 · 전 셀 눈 가능)이고 그것이 2026-08-24 까지의 동작이다.
        /// 넣으면 경사·계단 위에 눈이 앉고, 마스크가 꺼진 자리에는 눈이 아예 없다.
        ///
        /// <para>⚠ 맵의 원점·크기·해상도는 이 컴포넌트의 값과 <b>같아야 한다</b>. 어긋나면 로드에서
        /// 에러를 찍고 평지로 돈다 — 조용히 밀린 바닥은 화면에서 원인을 못 찾는다.</para>
        /// </summary>
        [Tooltip("구운 바닥 맵. 비우면 평지 · 전면 적설 가능(예전 동작).")]
        [SerializeField] private SnowGroundMap _groundMap;

        [Tooltip("지면 시트 가장자리에서 눈을 0 으로 재우는 폭(m). 0 이면 시트 경계에 수직 벽이 선다.")]
        [SerializeField, Range(0f, 4f)] private float _edgeFadeM = 0.45f;

        /// <summary>블레이드 폭. 차량 프리팹의 날 폭과 맞춰야 그리는 것과 깎는 것이 어긋나지 않는다.</summary>
        [SerializeField, Min(0.2f)] private float _bladeWidthM = 2.3f;

        [SerializeField, Min(0.05f)] private float _bladeThicknessM = 0.35f;

        /// <summary>차량 원점에서 날까지의 전방 거리.</summary>

        /// <summary>
        /// 바닥 눈을 클라이언트에 복제할 것인가. <b>끄면 각 피어가 자기 격자만 본다</b> — 남이 깎은
        /// 자리는 안 보이고, 그 대신 청크 델타가 한 바이트도 나가지 않는다.
        ///
        /// <para>왜 끌 수 있어야 하는가: 눈덩이와 펭귄만 먼저 맞추고 싶을 때 바닥 눈 동기화가
        /// 변수를 하나 더 얹는다. 눈덩이는 <c>NetworkRigidbody</c> 로, 펭귄은 <c>NetworkTransform</c> 으로
        /// 각각 복제되므로 이 스위치와 무관하게 동작한다 — 끄고 켜며 원인을 가를 수 있다.</para>
        /// </summary>
        [Tooltip("끄면 바닥 눈을 클라이언트에 보내지 않는다. 눈덩이·펭귄 복제는 영향받지 않는다.")]
        [SerializeField] private bool _replicateSnowToClients = true;

        /// <summary>이 채널로 눈 청크 델타만 흐른다. 다른 시스템이 같은 채널을 쓰지 않도록 고른 값이다.</summary>
        private static readonly ReliableKey SnowDeltaKey = ReliableKey.FromInts(0x53, 0x4E, 0x4F, 0x57);

        /// <summary>플레이어 주변 이 반경(m) 안의 청크만 보낸다. 멀리 있는 눈은 가까워질 때 받는다.</summary>
        private const float InterestRadiusM = 28f;

        /// <summary>
        /// 한 번 보낼 때 담는 청크 수 상한. 청크 하나는 16×16 × 2바이트 = 512바이트다.
        ///
        /// <para><see cref="SendEveryNTicks"/> 와 <b>같이 읽어야 한다</b> — 대역폭은 두 값의 비율이고
        /// 손실률은 <b>메시지 수</b>에 걸린다(실측). 8청크 / 4틱은 2청크 / 1틱과 대역폭이 같고 메시지가
        /// 1/4 이다.</para>
        /// </summary>
        private const int MaxCellsPerPlayerTick = 256;

        /// <summary>
        /// 이 틱 간격으로 <b>전체 청크 하나</b>를 되돌려 보낸다(복구 스윕). 셀 델타는 "바뀐 것만"
        /// 보내므로 어떤 이유로든 한 메시지가 사라지면 그 셀은 <b>다음 변경까지 틀린 채 남는다</b> -
        /// 청크 통째 전송에는 없던 위험이고, 그래서 값싼 자기 치유가 하나 필요하다.
        ///
        /// <para>30 틱(0.5 초)마다 512 바이트 = 1 KB/s 다. 주 경로가 아니라 보험이다.</para>
        /// </summary>
        private const int RepairEveryNTicks = 30;

        /// <summary>
        /// 이 틱 간격으로만 보낸다. 1 이면 매 틱이다.
        ///
        /// <para><b>왜 있는가:</b> 예산만 올리면(2 → 8, 매 틱) 대역폭도 4배가 되어 손실이 급증했다
        /// (보낸 8,288 / 적용 1,560). 메시지 수와 바이트를 분리하려면 간격이 필요하다.</para>
        /// </summary>
        private const int SendEveryNTicks = 1;

        /// <summary>
        /// (사용 안 함) 한 메시지에 담는 셀 수 상한. <b>청크 통째로 보내지 않는다</b> — 청크(16×16=256셀, 512바이트)를
        /// 바뀔 때마다 다시 보내면 4초에 5,011청크(약 2.6MB)가 나가고 클라이언트는 1,664개만 소화했다
        /// (실측). 실제로 값이 달라진 셀만 <c>(인덱스 4바이트 + 높이 2바이트)</c>로 보낸다.
        /// </summary>
        private const int MaxCellsPerMessage = 1024;

        private SnowHeightFieldCpu _field;
        private SnowPlowStepCpu _sim;
        private readonly List<int> _pendingChunks = new List<int>(256);

        /// <summary>
        /// 플레이어마다 "아직 안 보낸 청크" 집합.
        ///
        /// <para><b>왜 플레이어별인가:</b> 주변만 보내면(관심 반경) 멀리 있던 사람은 그 변화를 받지 못한
        /// 상태로 남는다. 전역 스냅샷 하나로 관리하면 그 사람이 가까워졌을 때 이미 "보냈다" 로 처리돼
        /// 영원히 낡은 눈을 본다. 청크 단위 미전송 목록을 사람마다 들고 있으면 가까워지는 순간 밀린 것이
        /// 흘러간다.</para>
        /// </summary>
        private readonly Dictionary<int, HashSet<int>> _staleByPlayer = new Dictionary<int, HashSet<int>>();

        /// <summary>검증용 계수기 — 보낸 메시지·청크와 적용한 청크.</summary>
        public static int DebugMessagesSent;

        /// <inheritdoc cref="DebugMessagesSent"/>
        public static int DebugChunksSent;

        /// <inheritdoc cref="DebugMessagesSent"/>
        public static int DebugChunksApplied;

        /// <summary>적용한 셀 수. 셀 델타가 주 경로이므로 이것이 실제 진척이다.</summary>
        public static int DebugCellsApplied;

        /// <summary>보낸 복구 청크 수. 이것만 늘고 셀이 안 늘면 표시 쪽이 죽은 것이다.</summary>
        public static int DebugRepairsSent;

        /// <summary>절삭이 바꾼 셀 수(누적). <see cref="DebugRelaxCells"/> 와의 비가 A 의 이득이다.</summary>
        public static int DebugCutCells;

        /// <summary>이완이 바꾼 셀 수(누적). 이쪽은 <b>보내지 않는다</b>.</summary>
        public static int DebugRelaxCells;

        /// <inheritdoc cref="DebugMessagesSent"/>
        public static int DebugPendingPeak;

        /// <summary>
        /// 사람마다 아직 안 보낸 청크 수. <b>진단용이다</b> — 자국이 클라이언트에 안 보일 때 원인이
        /// "보낼 것이 큐에 남아 있다"(예산 부족)인지 "큐가 비었는데도 안 갔다"(관심 반경에 걸렸다)인지
        /// 가르는 유일한 값이고, 그 둘은 고치는 방법이 정반대다.
        /// </summary>
        public int PendingStaleFor(int playerId)
            => _staleByPlayer.TryGetValue(playerId, out HashSet<int> stale) ? stale.Count : 0;
        private byte[] _sendBuffer;


        /// <summary>차량마다 마지막 날 자세. 스윕은 이전과 현재 사이를 훑으므로 차량별로 들고 있어야 한다.</summary>

        private readonly List<SnowBallCarrier> _balls = new List<SnowBallCarrier>();

        /// <summary>
        /// 이 씬에서 발자국을 만드는 펭귄. 컴포넌트가 활성화될 때 등록하므로 런타임 스폰도 포함하고,
        /// 전역 검색을 매 틱 반복하지 않는다.
        /// </summary>
        private readonly List<PenguinSnowInteraction> _penguins = new List<PenguinSnowInteraction>();

        private readonly Dictionary<PenguinSnowInteraction, SnowFootprintCpu> _penguinFootprints =
            new Dictionary<PenguinSnowInteraction, SnowFootprintCpu>();

        private readonly HashSet<SnowHeightFieldCpu> _penguinStepFields = new HashSet<SnowHeightFieldCpu>();

        /// <summary>
        /// 공마다 권위 저울과 마지막 중심. <b>공의 상태는 스테이지가 들고 있다</b> — 필드가 여기 있으니
        /// 걷는 쪽도 여기여야 하고, 그래야 차량과 공이 같은 틱·같은 순서로 같은 격자를 만진다.
        /// </summary>
        private readonly Dictionary<SnowBallCarrier, SnowBallCpu> _ballSims =
            new Dictionary<SnowBallCarrier, SnowBallCpu>();

        /// <summary>이번 물리 틱에 공별 단일 스윕이 필드에서 실제로 제거한 양(mm·셀).</summary>
        private readonly Dictionary<SnowBallCarrier, long> _lastBallHarvestMm =
            new Dictionary<SnowBallCarrier, long>();

        private readonly Dictionary<SnowBallCarrier, Vector2> _prevBallXZ =
            new Dictionary<SnowBallCarrier, Vector2>();

        /// <summary>
        /// 공이 어느 상자의 눈을 파는가. <c>null</c>(= 목록에 없음)이면 지면 시트다.
        /// <b>만들 때 정해지고 바뀌지 않는다</b> — 질량이 두 필드에 걸치면 보존이 항등식이 아니게 된다.
        /// </summary>
        private readonly Dictionary<SnowBallCarrier, SnowZone> _ballZone =
            new Dictionary<SnowBallCarrier, SnowZone>();

        /// <summary>이 피어의 눈. 서버에서는 이것이 권위 사본이다.</summary>
        public SnowHeightFieldCpu Field => _field;

        public int InitialDepthMm => _initialDepthMm;

        public Vector2 OriginXZ => _originXZ;

        public Vector2 SizeMeters => _sizeMeters;

        public bool HasSimulationAuthority => _standalone || Runner == null || Runner.IsServer;

        /// <summary>보존 검사용 총량. 밀기는 재분배이므로 이 값은 변하지 않아야 한다.</summary>
        public long TotalHeightMm
        {
            get
            {
                long total = _field == null ? 0L : _field.TotalHeightMm;
                for (int i = 0; i < _zones.Count; i++)
                {
                    if (_zones[i] != null) total += _zones[i].TotalHeightMm;
                }
                return total;
            }
        }

        /// <summary>마지막 틱에 실제로 스텝을 받은 차량 수. 0 이면 아무도 밀지 않았다는 뜻이다.</summary>

        /// <summary>마지막 틱에 눈을 걷은 공의 수. 검증이 "아무도 굴리지 않았다" 와 구분하려고 읽는다.</summary>
        public int SteppedBallsLastTick { get; private set; }

        /// <summary>
        /// 공들이 지금 실제로 취득한 눈의 합(mm·셀). 필드 수확량에 성장 가중치를 적용한 뒤의 값이라
        /// 가중치가 1보다 작으면 필드 감소량 전체와 같지 않다.
        /// </summary>
        public long BallHeldMm
        {
            get
            {
                long sum = 0;
                foreach (SnowBallCpu ball in _ballSims.Values) sum += ball.MassMm;
                return sum;
            }
        }

        public long LastBallHarvestMm(SnowBallCarrier ball)
            => ball != null && _lastBallHarvestMm.TryGetValue(ball, out long harvestedMm)
                ? harvestedMm
                : 0L;

        /// <summary>
        /// 움직이는 눈폭풍이 지난 캡슐 영역에 눈을 더한다. 한 이벤트의 중첩 스윕은
        /// <paramref name="maximumExposureR8"/> 로 합쳐 셀마다 <paramref name="eventAmountMm"/> 이상
        /// 더하지 않고, 결과 높이는 <paramref name="maximumDepthMm"/> 를 넘지 않는다.
        /// </summary>
        public int ApplyBlizzardSweep(Vector2 previousCenter, Vector2 currentCenter,
            float coreRadiusM, float featherM, int eventAmountMm, int maximumDepthMm,
            int boundarySeed, byte[] maximumExposureR8)
        {
            if (_field == null || !HasSimulationAuthority) return 0;
            if (maximumExposureR8 == null || maximumExposureR8.Length != _field.HeightMm.Length)
                return 0;

            coreRadiusM = Mathf.Max(0.1f, coreRadiusM);
            featherM = Mathf.Max(0f, featherM);
            eventAmountMm = Mathf.Max(0, eventAmountMm);
            maximumDepthMm = Mathf.Max(0, maximumDepthMm);
            float outerRadius = coreRadiusM + featherM;
            SnowFieldGeometry geo = _field.Geo;
            float cellSize = SnowFieldGeometry.CellSizeM;
            int cx0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(previousCenter.x, currentCenter.x)
                                                   - outerRadius - geo.OriginXM) / cellSize), 0, geo.ResX - 1);
            int cz0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(previousCenter.y, currentCenter.y)
                                                   - outerRadius - geo.OriginZM) / cellSize), 0, geo.ResZ - 1);
            int cx1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(previousCenter.x, currentCenter.x)
                                                   + outerRadius - geo.OriginXM) / cellSize), 0, geo.ResX - 1);
            int cz1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(previousCenter.y, currentCenter.y)
                                                   + outerRadius - geo.OriginZM) / cellSize), 0, geo.ResZ - 1);

            _field.BeginStep();
            // CutCells 는 이름과 달리 네트워크로 보낼 명시적 권위 변경 목록이다. 양의 날씨 변경도
            // 같은 신뢰 채널을 타야 클라이언트가 폭풍 뒤의 눈을 받는다.
            _field.BeginCutPhase();
            int changed = 0;
            Vector2 segment = currentCenter - previousCenter;
            float segmentLengthSq = segment.sqrMagnitude;
            for (int z = cz0; z <= cz1; z++)
            {
                float worldZ = geo.OriginZM + (z + 0.5f) * cellSize;
                for (int x = cx0; x <= cx1; x++)
                {
                    int cell = geo.CellIndex(x, z);
                    if (_field.Ground != null && !_field.Ground.IsSnowableAt(cell)) continue;

                    Vector2 point = new Vector2(
                        geo.OriginXM + (x + 0.5f) * cellSize,
                        worldZ);
                    float t = segmentLengthSq > 0.000001f
                        ? Mathf.Clamp01(Vector2.Dot(point - previousCenter, segment) / segmentLengthSq)
                        : 0f;
                    float distance = Vector2.Distance(point, previousCenter + segment * t);
                    float jitter = featherM > 0f
                        ? (Hash01(x, z, boundarySeed) * 2f - 1f) * Mathf.Min(featherM * 0.22f, 1.25f)
                        : 0f;
                    float noisyOuterRadius = Mathf.Max(coreRadiusM, outerRadius + jitter);
                    float weight;
                    if (distance <= coreRadiusM) weight = 1f;
                    else if (distance >= noisyOuterRadius || noisyOuterRadius <= coreRadiusM) weight = 0f;
                    else
                    {
                        float normalized = 1f - (distance - coreRadiusM) / (noisyOuterRadius - coreRadiusM);
                        weight = normalized * normalized * (3f - 2f * normalized);
                    }

                    byte targetExposure = (byte)Mathf.Clamp(Mathf.RoundToInt(weight * 255f), 0, 255);
                    byte previousExposure = maximumExposureR8[cell];
                    if (targetExposure <= previousExposure) continue;

                    maximumExposureR8[cell] = targetExposure;
                    int previousContribution = (eventAmountMm * previousExposure + 127) / 255;
                    int targetContribution = (eventAmountMm * targetExposure + 127) / 255;
                    int room = maximumDepthMm - _field.GetAt(cell);
                    int delta = Mathf.Min(targetContribution - previousContribution, room);
                    if (delta <= 0) continue;

                    int applied = _field.AddAt(cell, delta);
                    if (applied <= 0) continue;
                    _field.WakeChunkOfCell(x, z);
                    changed++;
                }
            }

            _field.EndCutPhase();
            MarkAllCutCellsStale();
            return changed;
        }

        private static float Hash01(int x, int z, int seed)
        {
            uint value = unchecked((uint)(x * 73856093 ^ z * 19349663 ^ seed * 83492791));
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }

        /// <summary>단독 모드에서 선물로 변환되어 필드 밖으로 이동한 눈의 누적량(mm·셀).</summary>
        public long ConvertedOutMm { get; private set; }

        /// <summary>
        /// 단독 모드의 눈덩이를 로컬 교환 시스템이 소비한다. 소비한 질량은 별도 원장에 기록한다.
        /// </summary>
        public bool TryConsumeBallForLocalConversion(SnowBallCarrier ball, out long consumedMassMm)
        {
            consumedMassMm = 0L;
            if (!_standalone || ball == null) return false;
            if (!_ballSims.TryGetValue(ball, out SnowBallCpu sim)) return false;

            consumedMassMm = sim.MassMm;
            ConvertedOutMm += consumedMassMm;
            RemoveBallTracking(ball);
            Destroy(ball.gameObject);
            return true;
        }

        /// <summary>
        /// 멀티 서버에서 선물로 바뀐 눈덩이를 장부와 네트워크에서 함께 제거한다.
        /// 터진 것이 아니므로 <see cref="SnowBallCarrier.ServerMarkBursting"/> 은 호출하지 않는다.
        /// </summary>
        public bool TryConsumeBallForNetworkConversion(SnowBallCarrier ball, out long consumedMassMm)
        {
            consumedMassMm = 0L;
            if (_standalone || Runner == null || !Runner.IsRunning || !Runner.IsServer || ball == null)
                return false;
            if (ball.Object == null || !ball.Object.IsValid || !ball.Object.HasStateAuthority)
                return false;
            if (!_ballSims.TryGetValue(ball, out SnowBallCpu sim)) return false;

            consumedMassMm = sim.MassMm;
            ConvertedOutMm += consumedMassMm;
            RemoveBallTracking(ball);
            Runner.Despawn(ball.Object);
            return true;
        }

        [Header("눈덩이 만들기")]
        [Tooltip("뭉칠 때 손이 덮는 반지름(m). 공의 반지름이 아니라 손의 크기다 — 처음에는 공이 없다.")]
        [SerializeField, Min(0.05f)] private float _gatherRadiusM = 0.45f;

        [Tooltip("뭉칠 때 그 자리에 남기는 눈(mm). 0 이면 발밑이 맨땅이 된다.\n\n" +
                 "실측(2026-08-19): 0 이면 반지름 0.45 손으로 한 번에 지름 0.83 m 를 뭉친다 — 손으로 " +
                 "뭉친 눈덩이로는 너무 크다. 250(눈 300 에서 셀당 50 mm)이면 지름 0.5 m 근처가 되고 " +
                 "발밑도 맨땅이 되지 않는다.")]
        [SerializeField, Min(0)] private int _gatherResidueMm = 250;

        [Tooltip("만들 눈덩이 프리팹. 비우면 Resources 의 PF_SnowBall 을 쓴다.")]
        [SerializeField] private SnowBallCarrier _ballPrefab;

        /// <summary>마지막 <see cref="TryCreateBall"/> 가 뭉친 양(mm·셀). 0 이면 눈이 얕아 실패했다.</summary>
        public long LastGatheredMm { get; private set; }

        /// <summary>
        /// 그 자리의 눈을 뭉쳐 <b>새 눈덩이를 만든다</b>. 필드를 건드리므로 권위가 소유하는 연산이다.
        ///
        /// <para>가중 적용 후 취득 질량이 씨앗 반지름을 뒷받침하지 못하면
        /// (<see cref="SnowBallCpu.MinCreateMassMm"/>) 만들지 않는다. 실패하면 공이 실제로 취득한 양만
        /// 되돌리고, 성장 가중치에서 제외된 양은 압축·비산된 것으로 복구하지 않는다.</para>
        /// </summary>
        /// <returns>만든 눈덩이. 눈이 얕으면 <c>null</c>.</returns>
        public SnowBallCarrier TryCreateBall(Vector3 worldPos)
        {
            if (_field == null) return null;
            if (!_standalone && (Runner == null || !Runner.IsServer)) return null;

            SnowBallCarrier prefab = _ballPrefab != null
                ? _ballPrefab
                : Resources.Load<GameObject>("PF_SnowBall")?.GetComponent<SnowBallCarrier>();
            if (prefab == null) return null;

            // <b>어느 눈을 파는지 먼저 정한다.</b> 상자 안이면 그 상자의 격자이고, 좌표도 상자 로컬로
            // 바꿔야 한다 — 지붕 위에서 뭉친 눈이 땅의 격자를 깎으면 지붕은 그대로인데 아래 땅에
            // 구멍이 난다.
            SnowZone zone = ResolveZone(worldPos);
            SnowHeightFieldCpu field = zone != null ? zone.Field : _field;
            SnowPlowStepCpu sim = zone != null ? zone.Sim : _sim;
            Vector3 dig = zone != null ? zone.ToLocal(worldPos) : worldPos;

            int growthWeightPermille = prefab.GrowthWeightPermille;
            var probe = new SnowBallCpu(field, _gatherResidueMm, growthWeightPermille);

            // 뭉치기도 절삭이다 - 구간으로 감싸야 클라이언트가 그 자리를 받는다.
            field.BeginCutPhase();
            long gathered = probe.Gather(dig.x, dig.z, _gatherRadiusM, _gatherResidueMm);
            field.EndCutPhase();
            LastGatheredMm = gathered;

            if (probe.MassMm < SnowBallCpu.MinCreateMassMm)
            {
                // 공이 실제로 취득한 양만 되돌린다. 가중치 제외분은 장부에 없다.
                if (probe.MassMm > 0)
                {
                    field.BeginCutPhase();
                    long unplaced = probe.Release(dig.x, dig.z,
                                                  sim.Material.MaxPileHeightMm,
                                                  sim.Material.DepositSpreadRings);
                    field.EndCutPhase();
                    UnaccountedOutMm += unplaced;
                }

                return null;
            }

            if (zone != null) MarkZoneDirty(zone);

            float r = probe.RadiusM;
            var at = new Vector3(worldPos.x, SurfaceYAt(worldPos) + r, worldPos.z);

            SnowBallCarrier ball;
            if (_standalone)
            {
                ball = Instantiate(prefab, at, Quaternion.identity);
            }
            else
            {
                NetworkObject spawned = Runner.Spawn(prefab.GetComponent<NetworkObject>(), at, Quaternion.identity);
                ball = spawned == null ? null : spawned.GetComponent<SnowBallCarrier>();
            }

            if (ball == null) return null;

            // 뭉친 양을 그대로 물려준다 — 새로 만든 저울에 옮겨 담으면 두 번 걷게 된다.
            _ballSims[ball] = probe;
            _prevBallXZ[ball] = new Vector2(dig.x, dig.z);
            if (zone != null) _ballZone[ball] = zone;
            else _ballZone.Remove(ball);
            ball.ServerApplyMass(probe.MassMm);

            // 상자는 셀 델타가 아니라 전체 스냅샷으로 복제한다 — 상자 하나가 셀 수천이라 스냅샷이
            // 청크 큐보다 싸고, 유실이 저절로 낫는다.
            if (zone == null)
            {
                var gatherPose = new SnowBladePose
                {
                    CenterX = dig.x, CenterZ = dig.z, ForwardX = 1f, ForwardZ = 0f,
                };
                MarkChangedCellsStale(gatherPose, gatherPose, new SnowBladeShape
                {
                    HalfWidthM = _gatherRadiusM,
                    HalfDepthM = _gatherRadiusM,
                    Profile = SnowBladeProfileKind.Straight,
                });
            }

            return ball;
        }

        /// <summary>
        /// 격자에 다시 놓지 못해 사라진 눈(누적, mm). 더미가 상한에 닿거나 필드 밖으로 밀리면 생긴다.
        ///
        /// <para>이 값이 있어야 <b>보존 검사가 의미를 갖는다</b>. 총량이 줄었을 때 "누출" 인지 "설명되는
        /// 손실" 인지 구분하는 유일한 방법이고, 구분하지 못하면 검사는 실패를 알려주기만 하고 원인을
        /// 알려주지 못한다. 총량 감소가 이 값과 같으면 원장은 닫혀 있다.</para>
        /// </summary>
        public long UnaccountedOutMm { get; private set; }


        /// <summary>
        /// 자기 씬의 런너에 스스로 등록한다.
        ///
        /// <para><b>이 컴포넌트는 복제되지 않는다.</b> 피어마다 같은 원인(자세·블레이드)으로 같은 정수
        /// 재분배를 재생하므로 높이도, 검산값도 네트워크로 보낼 필요가 없다. 그래서
        /// <see cref="NetworkObject"/> 가 없고, <see cref="SimulationBehaviour"/> 로서
        /// <see cref="NetworkRunner.AddGlobal"/> 로 붙어 틱만 받는다.</para>
        ///
        /// <para>스폰 경로를 버린 이유는 실측이다. 서버가 프리팹으로 스폰하게 하면 프리팹 테이블의 동기
        /// 로드 실패(<c>Failed to load prefab synchronously</c>)를 만나고, 그것을 큐잉으로 넘기면
        /// <c>Spawn</c> 이 <c>null</c> 을 줘 "대기 중" 을 알 수 없어 스테이지가 0개(가드 과함) 또는
        /// 10개(재시도 과함)가 됐다. 복제가 필요 없는 것을 복제하려 해서 생긴 문제였다.</para>
        /// </summary>
        /// <summary>등록해 둔 런너. 이것이 죽으면 다시 붙어야 한다.</summary>
        private NetworkRunner _registeredWith;

        /// <summary>네트워크 없이 도는 판(싱글플레이)인가.</summary>
        private bool _standalone;

        /// <summary>단독 모드에서 블레이드 상태를 읽어 오는 곳. 없으면 날은 내려간 것으로 본다.</summary>

        /// <summary>단독 모드의 차량. 비운동학 Rigidbody 를 찾는다(v7 리그와 같은 규칙).</summary>

        /// <summary>구 눈의 블레이드. 싱글 씬의 차량이 들고 있는 것이 이것이다.</summary>

        private void Update()
        {
            // 이미 살아 있는 런너에 붙어 있으면 할 일이 없다.
            if (_registeredWith != null && _registeredWith.IsRunning) return;

            // <b>런너가 없는 판(싱글플레이)에서도 눈은 있어야 한다.</b> 네트워크가 없으면 틱을 줄 주인이
            // 없으므로 스스로 만들고 <c>FixedUpdate</c> 로 돈다 - 그쪽에는 예측도 재시뮬도 없어서
            // 순서 문제가 생기지 않는다.
            //
            // <b>남의 세션이 살아 있어도 이 씬의 세션이 아니면 싱글이다.</b> 전에는
            // <c>NetworkRunner.Instances.Count == 0</c> 으로 판정했는데, MPPM host 태그가 남은 채
            // SinglePlay 에 들어가면 러너가 <c>DontDestroyOnLoad</c> 로 따라와 그 판정이 조용히
            // 뒤집혔다(2026-08-31 실측). 그 대조는 <see cref="StageSession"/> 이 갖는다.
            StageSession session = StageSession.For(gameObject);
            if (session.Runner == null)
            {
                if (_field == null) Build();
                _standalone = true;
                return;
            }

            _standalone = false;

            NetworkRunner runner = session.Runner;

            // <b>런너가 바뀌면 다시 등록하고 격자를 새로 만든다.</b> 씬은 세션보다 오래 살 수 있다 —
            // 앞선 세션의 씬이 남아 재사용되면 이 컴포넌트는 죽은 런너를 들고 조용히 아무 일도 하지 않는다
            // (실측: 피어 3개인데 스테이지가 2개만 살아 있었다). 새 세션의 눈은 새 격자여야 한다.
            _registeredWith = runner;
            runner.AddGlobal(this);
            Build();
        }

        private void Build()
        {
            foreach (SnowFootprintCpu footprint in _penguinFootprints.Values) footprint.Reset();

            // 바닥 기준 Y 는 맵이 정한다 — 격자의 나머지(원점·크기)는 이 컴포넌트가 주인이고,
            // 맵이 그것과 어긋나면 아래 검사가 잡는다.
            float originYM = _groundMap != null ? _groundMap.OriginYM : 0f;
            var geo = new SnowFieldGeometry(_sizeMeters.x, _sizeMeters.y, _originXZ.x, _originXZ.y, originYM);

            SnowGroundFieldCpu ground = null;
            if (_groundMap != null && !_groundMap.TryBuildField(geo, out ground, out string groundError))
            {
                Debug.LogError($"{nameof(SnowCpuStage)}: 바닥 맵을 쓸 수 없다 — {groundError}. 평지로 돈다.");
                ground = null;
            }

            // 굽힌 맵이 없어도 커버리지는 만든다 — 시트 가장자리에서 눈이 맨바닥으로 내려가야
            // 한다. 전에는 여기도 수직 벽이었다. 지면 시트는 경계선을 흔들지 않는다(jitter 0):
            // 필드 경계는 플레이 영역 밖이라 흔들 이유가 없다. 사각형이 격자와 딱 맞지만
            // FromRect 가 테두리 한 칸을 0 으로 두므로 시트는 바닥에 닿아서 끝난다.
            if (ground == null)
            {
                float w = geo.ResX * SnowFieldGeometry.CellSizeM;
                float d = geo.ResZ * SnowFieldGeometry.CellSizeM;

                // <b>사각형을 한 칸 안으로 넣는다.</b> 상자와 달리 지면 시트는 사각형이 격자와 딱 맞아서,
                // 그대로 두면 재우는 띠의 가장 낮은 값들이 격자 밖에 떨어져 커버리지가 0 에서 106/255 로
                // 한 칸 만에 뛴다 — 둥근 어깨를 지나면 <b>52 cm 를 12.5 cm 안에서</b> 오르는 벽이다.
                float inset = SnowFieldGeometry.CellSizeM;
                ground = SnowGroundFieldCpu.FromRect(geo, null,
                                                     geo.OriginXM + inset, geo.OriginZM + inset,
                                                     geo.OriginXM + w - inset, geo.OriginZM + d - inset,
                                                     _edgeFadeM, 0f);
            }

            _field = new SnowHeightFieldCpu(geo, _initialDepthMm, ground);
            _sim = new SnowPlowStepCpu(_field)
            {
                Shape = new SnowBladeShape
                {
                    HalfWidthM = _bladeWidthM * 0.5f,
                    HalfDepthM = _bladeThicknessM * 0.5f,
                    Profile = SnowBladeProfileKind.Straight,
                },
            };

            BuildZones();

            // 마처는 지면을 스칼라 하나(0)로 알고 있어서 구운 바닥도 상자도 못 읽는다. 그 조합에서는
            // 눈이 경사를 무시하고 평면으로 그려지는데, 화면만 보면 "굽기가 실패했다" 와 구별되지 않는다.
            if (ground != null || _zones.Count > 0)
            {
                var look = GetComponent<SnowSystem>();
                if (look != null && look.EffectiveLook == ESnowLook.Raymarch)
                {
                    Debug.LogWarning($"{nameof(SnowCpuStage)}: 바닥 맵 또는 눈 상자가 있는데 룩이 " +
                                     "Raymarch 다 — 마처는 그 둘을 읽지 않으므로 경사·상자의 눈이 " +
                                     $"그려지지 않는다. {nameof(ESnowLook)}.{nameof(ESnowLook.Displace)} 로 둘 것.");
                }
            }
        }

        public void RegisterPenguin(PenguinSnowInteraction penguin)
        {
            if (penguin == null || penguin.gameObject.scene != gameObject.scene) return;
            if (!_penguins.Contains(penguin)) _penguins.Add(penguin);
            if (!_penguinFootprints.ContainsKey(penguin))
                _penguinFootprints.Add(penguin, new SnowFootprintCpu());
        }

        public void UnregisterPenguin(PenguinSnowInteraction penguin)
        {
            if (penguin == null) return;
            _penguins.Remove(penguin);
            if (_penguinFootprints.TryGetValue(penguin, out SnowFootprintCpu footprint)) footprint.Reset();
            _penguinFootprints.Remove(penguin);
        }


        // ------------------------------------------------------------------ 눈 상자(zone)

        /// <summary>
        /// 이 씬의 눈 상자들. <b>계층 경로로 정렬</b>한다 — 복제가 인덱스로 상자를 가리키므로 피어마다
        /// 같은 순서여야 한다. 씬 안에서만 찾는다(한 프로세스에 피어가 여럿인 판에서 남의 상자를 잡지
        /// 않으려는 것이고, 차량 탐색과 같은 이유다).
        /// </summary>
        private readonly List<SnowZone> _zones = new List<SnowZone>(8);

        /// <summary>스냅샷을 보내야 하는 상자. 인덱스다.</summary>
        private readonly HashSet<int> _zonesDirty = new HashSet<int>();

        /// <inheritdoc cref="_zones"/>
        public IReadOnlyList<SnowZone> Zones => _zones;

        /// <summary>
        /// 상자 필드를 세우고 정렬한다. 지면 시트와 <b>다른 격자</b>이므로 여기서 하는 일은 등록뿐이다.
        /// </summary>
        private void BuildZones()
        {
            _zones.Clear();
            _zonesDirty.Clear();

            foreach (SnowZone zone in FindObjectsByType<SnowZone>(FindObjectsInactive.Exclude,
                                                                  FindObjectsSortMode.None))
            {
                if (zone.gameObject.scene != gameObject.scene) continue;
                zone.Release();
                zone.EnsureBuilt();
                if (zone.Field == null) continue;      // 상한을 넘겼다 - 상자가 스스로 에러를 찍었다
                _zones.Add(zone);
            }

            _zones.Sort((a, b) => string.CompareOrdinal(a.StableId, b.StableId));
        }

        /// <summary>
        /// 이 점이 속한 눈. 상자가 먼저이고, 어느 상자에도 안 들면 지면 시트다.
        ///
        /// <para><b>겹치면 바닥면이 높은 쪽이 이긴다.</b> 지붕 상자와 그 아래 지면 상자가 XZ 로 겹치는
        /// 것이 이 구조의 존재 이유이므로, 우선순위 규칙이 없으면 "지붕에 선 액터가 땅의 눈을 판다" 가
        /// 된다. 높이로 가르는 것이 저작자가 예상하는 규칙이다.</para>
        /// </summary>
        public SnowZone ResolveZone(Vector3 worldPos)
        {
            SnowZone best = null;
            float bestY = float.NegativeInfinity;

            for (int i = 0; i < _zones.Count; i++)
            {
                SnowZone zone = _zones[i];
                if (zone == null || zone.Field == null) continue;
                if (!zone.Contains(worldPos)) continue;

                float y = zone.transform.position.y;
                if (y <= bestY) continue;
                bestY = y;
                best = zone;
            }

            return best;
        }

        /// <summary>
        /// 상자들의 이완을 한 스텝 돌린다. <b>서버(또는 단독)만</b> — 상자는 전체 스냅샷으로 복제되므로
        /// 클라이언트가 같이 이완하면 권위 값과 싸운다.
        ///
        /// <para>비용은 "최근에 건드린 면적" 에 비례한다 — <b>아래의 가짜 자세를 격자 밖에 두는 한</b>.
        /// 2026-08-24 까지 이 주석은 "상자가 조용하면 사실상 공짜다" 라고만 적혀 있었고 그것은
        /// <b>사실이 아니었다</b>: 자세가 상자 중심에 있어서 활성 반경이 상자를 통째로 덮었고, 조용한
        /// 상자 하나가 2.09 ms/틱을 영원히 먹었다. 왜 그런지와 실측은 루프 안의 주석에 있다.</para>
        ///
        /// <para><b>비용은 상자 수에 선형이다</b>(실측, 6 × 10 m 상자 · 상자당 6,144 셀):
        /// 1개 0.007 ms · 16개 0.105 ms · 64개 0.421 ms · 128개 0.850 ms · 256개 1.693 ms per tick.
        /// 상자를 늘릴 때 먼저 무는 것은 이 함수가 아니라 <see cref="SnowDisplaceView"/> 의 프레임
        /// 업로드와 <see cref="ResolveZone"/> 의 선형 스캔이다 — 폴더 <c>AGENTS.md</c> "상자가 많아지면"
        /// 절 참고.</para>
        /// </summary>
        private void StepZones(float dtSeconds)
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                SnowZone zone = _zones[i];
                if (zone == null || zone.Field == null || zone.Sim == null) continue;

                // ⚠ <b>가짜 자세는 격자 밖에 둔다.</b> 활성 집합은 "블레이드 주변 사각형 ∪ 아직 안 잠든
                // 청크" 인데, 상자에는 블레이드가 없어서 이 자세는 오로지 그 사각형을 정하려고 만든
                // 가짜다. 그것을 상자 중심 (0, 0) 에 두면 <see cref="SnowPlowStepCpu.ActiveRadiusM"/>
                // (10 m) 사각형이 6 × 10 m 상자를 <b>통째로</b> 덮어 전 청크가 매 틱 활성이 된다 —
                // 아무 일도 안 하는데 상자 하나가 <b>2.09 ms/틱</b>을 먹고 영원히 안 잠들었다
                // (2026-08-24 실측: 1,200 틱 뒤에도 24/24 청크 · 6,144 셀 · relax 4회). 격자 밖으로
                // 빼면 남는 것은 QueryDirty 가 고른 청크 하나뿐이라 <b>0.007 ms</b> 다 — 약 290배.
                // 상자 64개로 재면 121.9 ms/틱 → 0.42 ms/틱이다.
                //
                // 지면 시트는 반대다. 거기서는 차량 주변을 강제로 깨우는 것이 의도된 동작이고
                // (스텝당 비용은 활성 반경에만 의존한다), 그래서 고칠 자리는 이 가짜 자세 하나다.
                SnowFieldGeometry zoneGeo = zone.Field.Geo;
                var pose = new SnowBladePose
                {
                    CenterX = zoneGeo.OriginXM - SnowPlowStepCpu.ActiveRadiusM * 2f,
                    CenterZ = zoneGeo.OriginZM - SnowPlowStepCpu.ActiveRadiusM * 2f,
                    ForwardX = 1f,
                    ForwardZ = 0f,
                };
                SnowPlowStepStats stats = zone.Sim.Step(new SnowPlowStepInput
                {
                    Prev = pose,
                    Now = pose,
                    BladeDown = false,
                    SignedSpeedMps = 0f,
                    DtSeconds = dtSeconds,
                });

                UnaccountedOutMm += stats.ClampedMm;
                if (zone.Field.ChangedCells.Count > 0) _zonesDirty.Add(i);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (_field == null) return;

            // <b>재시뮬레이션에서는 밀지 않는다.</b> 클라이언트는 예측 때문에 같은 틱을 여러 번 도는데,
            // 격자는 되돌릴 수 없는 누적 상태라 그때마다 또 깎여 서버와 갈린다 — 실측으로 피어마다 총량이
            // 5만 mm 단위로 어긋났다. <c>IsForward</c> 는 그 틱을 처음(앞으로) 도는 경우만 참이다.
            if (!Runner.IsForward) return;

            // <b>지면 시트는 모든 피어가 자기 격자를 깎는다 (2026-08-24 전환).</b>
            //
            // <para>전에는 서버만 깎고 바뀐 셀을 청크 델타로 보냈다. 그 경로는 4인에서 전달률 2.4% 로
            // 무너졌고, <b>눈이 더 이상 점수가 아니게 되면서 지워진 영역을 동기화할 이유 자체가
            // 사라졌다</b>(2026-08-23 전제 변경). 지금 필요한 것은 "남이 판 자리가 내 화면에도 보인다"
            // 뿐이고, 그것은 <b>원인</b>인 눈덩이가 이미 복제되므로 각 피어가 스스로 만들 수 있다.</para>
            //
            // <para>예전에 이 방향을 폐기시킨 사유는 원격 <b>차량</b>의 트랜스폼이 표시용 보간값이라
            // 스윕이 서로 다른 자세로 찍힌다는 것이었다. 눈덩이는 <c>NetworkRigidbody</c> 로 복제되고
            // 각 피어가 물리를 함께 돌리며 서버 상태로 보정되므로 그 조건이 다르다.</para>
            //
            // <para><b>상자(<see cref="StepZones"/>)는 그대로 서버만 굴린다</b> — 전체 스냅샷으로
            // 복제하므로 클라이언트가 같이 이완하면 권위 값과 싸운다. 지면 시트와 정반대인 이유는
            // 크기다(상자는 셀 수천, 지면은 수십만).</para>
            if (Runner.IsServer) StepZones(Runner.DeltaTime);

            StepBalls(Runner.DeltaTime);

            // <b>발자국도 지면 시트라 위 전환이 그대로 적용된다 — 모든 피어가 자기 격자를 깎는다.</b>
            // 2026-08-24 에 눈덩이만 게이트 앞으로 옮기고 발자국은 뒤에 남겨 둔 것이 누락이었다.
            //
            // <para><c>IsServer</c> 뒤에 두면 <b>게임이 토폴로지에 따라 달라진다</b>. Host 모드에서는
            // 호스트가 플레이어이기도 해서 그 화면만 파이고, <b>Server 모드에서는 서버 피어에
            // 아바타가 없어 <see cref="_penguins"/> 가 비므로 아무 화면에서도 안 파인다</b>(실측).
            // 이 프로젝트는 루트 AGENTS 대로 Server 모드가 기본이라 후자가 실제 증상이었다.</para>
            //
            // <para>펍귀은 <c>NetworkRigidbody</c> 로 복제되고 각 피어가 물리를 함께 돌리며 서버 상태로
            // 보정된다 — 위 주석이 눈덩이를 허용한 그 조건과 같다. 예전에 이 방향을 폐기시켰던
            // 원격 <b>차량</b>의 표시용 보간 트랜스폼과는 조건이 다르다.</para>
            StepPenguinFootprints();

            if (!Runner.IsServer) return;

            SendDelta();
            SendZoneSnapshots();
        }

        /// <summary>
        /// 굴러가는 눈덩이가 지나간 자리의 눈을 걷어 공에 쌓는다. <b>차량과 같은 순서·같은 격자</b>를 쓴다.
        ///
        /// <para><b>relax 를 먼저, 걷기를 나중에</b> 한다. <see cref="SnowPlowStepCpu.Step"/> 이 첫 줄에서
        /// <see cref="SnowHeightFieldCpu.BeginStep"/> 을 불러 변경 목록을 비우므로, 걷기를 먼저 하면 그
        /// 걷은 청크가 목록에서 지워지고 화면이 한 틱 늦게 갱신된다. 순서를 뒤집으면 공이 판 골은 다음
        /// 틱에 무너지는데 — 그 한 틱은 눈에 보이지 않는다.</para>
        ///
        /// <para>공은 블레이드가 아니므로 <see cref="SnowPlowStepInput.BladeDown"/> 을 내리지 않는다.
        /// 그 스텝이 하는 일은 <b>공 주변을 활성 집합으로 잡아 안식각 이완을 돌리는 것</b>뿐이고,
        /// 그것이 없으면 공이 판 골의 벽이 수직으로 남는다.</para>
        /// </summary>
        private void StepBalls(float dtSeconds)
        {
            _balls.Clear();
            Runner.GetAllBehaviours(_balls);
            StepBallList(dtSeconds);
        }

        /// <summary>
        /// 런너가 없는 판(싱글플레이·펭귄 테스트)의 공. <b>씬으로 범위를 좁혀서</b> 찾는다 —
        /// 전역 검색은 한 프로세스에 여러 판이 뜰 때 남의 공을 긁는다(v7 에서 겪은 실패와 같은 형태).
        /// </summary>
        private void StepBallsStandalone(float dtSeconds)
        {
            _balls.Clear();
            foreach (SnowBallCarrier candidate in FindObjectsByType<SnowBallCarrier>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != gameObject.scene) continue;
                _balls.Add(candidate);
            }

            StepBallList(dtSeconds);
        }

        /// <summary>두 경로가 공유하는 본체. 목록을 채우는 방식만 다르고 규칙은 하나여야 한다.</summary>
        /// <summary>
        /// 이 피어가 <b>눈덩이 상태의 주인</b>인가. 격자를 깎는 것과는 별개다.
        ///
        /// <para>모든 피어가 자기 지면 격자를 깎지만(시각), 눈덩이의 질량·크기·터짐은 서버만 정한다.
        /// 비권위 피어가 <see cref="SnowBallCarrier.ServerApplyMass"/> 를 부르면 <c>ApplySize</c> 가
        /// 콜라이더 반지름까지 바꿔 <b>그 프레임의 로컬 물리가 서버와 달라진다</b> — 다음
        /// <c>Render</c> 가 복제 값으로 되돌리기 전까지.</para>
        /// </summary>
        private bool OwnsBallState => _standalone || Runner == null || Runner.IsServer;

        private void StepBallList(float dtSeconds)
        {
            SteppedBallsLastTick = 0;
            _lastBallHarvestMm.Clear();

            for (int i = 0; i < _balls.Count; i++)
            {
                SnowBallCarrier ball = _balls[i];
                if (ball == null) continue;
                _lastBallHarvestMm[ball] = 0L;

                if (!_ballSims.TryGetValue(ball, out SnowBallCpu sim))
                {
                    sim = new SnowBallCpu(_field, ball.ResidueMm, ball.GrowthWeightPermille);
                    _ballSims[ball] = sim;
                }

                sim.ResidueMm = ball.ResidueMm;

                // <b>공은 지금 밟고 있는 눈을 걷는다.</b> 지붕에서 굴려 떨어뜨리면 그 순간부터 땅의
                // 눈이 걷힌다 — 만든 필드에 영구히 묶어 두면 "지붕에서 만든 공은 땅에서 안 큰다" 는
                // 설명할 수 없는 규칙이 된다.
                //
                // <b>접지점으로 가린다.</b> 중심으로 가리면 공이 커질수록 중심이 상자 높이를 넘어가
                // 지붕 위에 있으면서 땅의 눈을 걷는다. 공은 표면에 닿아 있으므로 아래쪽 끝이 맞다.
                float controlledRadiusM = ball.RadiusM;
                Vector3 center = ball.transform.position;
                Vector3 contact = center + Vector3.down * (controlledRadiusM * 0.9f);
                SnowZone nowZone = ResolveZone(contact) ?? ResolveZone(center);

                _ballZone.TryGetValue(ball, out SnowZone ballZone);
                SnowHeightFieldCpu field = nowZone != null ? nowZone.Field : _field;
                SnowPlowStepCpu fieldSim = nowZone != null ? nowZone.Sim : _sim;
                if (field == null || fieldSim == null) continue;

                Vector3 p = nowZone != null ? nowZone.ToLocal(center) : center;
                var nowXZ = new Vector2(p.x, p.z);

                if (!ReferenceEquals(ballZone, nowZone))
                {
                    // <b>좌표계가 바뀌었으므로 이전 중심을 버린다.</b> 안 그러면 두 필드를 가로지르는
                    // 선을 훑어 엉뚱한 자리가 깎인다 — 차량이 첫 틱을 넘기는 것과 같은 이유다.
                    sim.Rebind(field);
                    if (nowZone != null) _ballZone[ball] = nowZone;
                    else _ballZone.Remove(ball);
                    _prevBallXZ[ball] = nowXZ;
                    if (OwnsBallState) ball.ServerApplyMass(sim.MassMm);
                    continue;
                }

                ballZone = nowZone;

                // 첫 틱에는 이전 중심이 없다. 원점에서 훑으면 맵을 가로지르는 한 줄이 깎인다 —
                // 차량 루프가 같은 이유로 첫 틱을 넘긴다.
                if (!_prevBallXZ.TryGetValue(ball, out Vector2 prevXZ))
                {
                    _prevBallXZ[ball] = nowXZ;
                    continue;
                }

                var pose = new SnowBladePose
                {
                    CenterX = nowXZ.x,
                    CenterZ = nowXZ.y,
                    ForwardX = 1f,
                    ForwardZ = 0f,
                };

                float r = controlledRadiusM;
                var shape = new SnowBladeShape
                {
                    HalfWidthM = r,
                    HalfDepthM = r,
                    Profile = SnowBladeProfileKind.Straight,
                    WingLengthM = 0f,
                };

                // <b>relax 는 필요할 때만 돈다.</b> 전에는 공마다 매 틱 무조건 한 스텝을 돌렸고, 실측으로
                // 그 한 스텝이 **2.14 ms**(844,800셀 격자, M5 Pro)다. 그런데 기본 구성(잔량 250 · 눈 300)에서
                // 공이 만드는 단은 50 mm 이고 안식각 55도의 낙차는 178 mm 라, relax 가 옮기는 양은
                // 실측으로 **0 mm** 였다(잔량 0 으로 300 mm 골을 파면 14,212 mm 로 반응한다 - 대조군).
                // 즉 아무 일도 하지 않는 계산에 틱당 2 ms 를 쓰고 있었다. 모바일이면 그 하나로 예산이 끝난다.
                //
                // 문턱은 물성에서 나온다 — `MaxDropOrthoMm + CohesionMm` 을 넘는 단이 생길 때만 흐른다.
                // 공이 만들 단의 높이는 <b>지금 자리의 눈 높이 − 잔량</b> 이고, 그것은 셀 하나 읽기다.
                int hereMm = field.Geo.TryWorldToCell(nowXZ.x, nowXZ.y, out int bcx, out int bcz)
                    ? field.Get(bcx, bcz)
                    : 0;
                int stepMm = hereMm - sim.ResidueMm;
                bool needsRelax = stepMm > fieldSim.Material.MaxDropOrthoMm + fieldSim.Material.CohesionMm;

                if (OwnsBallState && ball.ServerBurstRequested)
                {
                    ball.ServerBurstRequested = false;
                    if (OwnsBallState) ball.ServerApplyMass(sim.MassMm);
                    BurstBall(ball, sim, nowXZ, dtSeconds, ballZone);
                    continue;
                }

                if (OwnsBallState && ball.ServerReleaseRequested)
                {
                    ball.ServerReleaseRequested = false;

                    // 놓는 것은 항상 더미를 만든다 — 여기서는 relax 를 건너뛰지 않는다.
                    fieldSim.Step(new SnowPlowStepInput
                    {
                        Prev = pose, Now = pose, BladeDown = false, SignedSpeedMps = 0f, DtSeconds = dtSeconds,
                    });

                    field.BeginCutPhase();
                    long unplaced = sim.Release(nowXZ.x, nowXZ.y,
                                                fieldSim.Material.MaxPileHeightMm,
                                                fieldSim.Material.DepositSpreadRings);
                    field.EndCutPhase();
                    UnaccountedOutMm += unplaced;
                    if (ballZone != null) MarkZoneDirty(ballZone);
                    else MarkChangedCellsStale(pose, pose, shape);
                }
                else if (ball.HasSupport)
                {
                    // 깊은 눈이나 더미로 굴러 들어갔을 때만 이완이 필요하다.
                    if (needsRelax)
                    {
                        fieldSim.Step(new SnowPlowStepInput
                        {
                            Prev = pose, Now = pose, BladeDown = false, SignedSpeedMps = 0f, DtSeconds = dtSeconds,
                        });
                    }
                    else
                    {
                        // 이완 스텝이 없으면 CutCells의 스탬프도 시작되지 않는다. 첫 스텝의 기본
                        // 스탬프는 배열 초기값과 같아서, 이 호출이 없으면 실제 수확을 해도 전송할
                        // 절삭 셀이 0개로 기록된다.
                        field.BeginStep();
                    }

                    field.BeginCutPhase();
                    long harvested = sim.Harvest(prevXZ.x, prevXZ.y, nowXZ.x, nowXZ.y,
                        controlledRadiusM);
                    field.EndCutPhase();
                    _lastBallHarvestMm[ball] = harvested;

                    if (harvested > 0)
                    {
                        if (ballZone != null)
                        {
                            MarkZoneDirty(ballZone);
                        }
                        else
                        {
                            var prevPose = new SnowBladePose
                            {
                                CenterX = prevXZ.x,
                                CenterZ = prevXZ.y,
                                ForwardX = 1f,
                                ForwardZ = 0f,
                            };
                            MarkChangedCellsStale(prevPose, pose, shape);
                        }

                        SteppedBallsLastTick++;
                    }
                }

                if (OwnsBallState) ball.ServerApplyMass(sim.MassMm);
                _prevBallXZ[ball] = nowXZ;
            }
        }


        /// <summary>
        /// 터질 때 놓기보다 이만큼 넓게 흩뿌린다. 놓기는 <b>공 밑에 더미</b>를 만들고, 터짐은
        /// <b>넓고 낮게</b> 깔려야 그 자리가 다시 굴릴 만한 눈으로 보인다.
        ///
        /// <para>같은 <c>Deposit</c> 을 쓰고 링 수만 곱한다 — 링은 한 번에 셀 두 칸이라 링 수가 곧
        /// 반경이다. 새 퇴적 경로를 만들지 않는 이유는 보존이 그 함수 하나에 걸려 있기 때문이다.</para>
        /// </summary>
        private const int BurstSpreadFactor = 4;

        /// <summary>
        /// 공을 터뜨린다 — <b>들고 있던 눈을 넓게 되돌려 놓고 사라진다.</b>
        ///
        /// <para>성장의 상한을 없앤 대가를 여기서 갚는다. 상한이 있던 시절에는 큰 공이 "더 이상 걷지
        /// 않는 물체" 로 남아 지나간 자리에 눈을 그대로 두었다(실측 뒤 3 m 가 60 cm) — 청소가 공 하나
        /// 분량에서 멈춘다는 뜻이다. 터지면 그 눈이 다시 바닥에 깔리므로 멈추는 지점이 없다.</para>
        ///
        /// <para><b>눈은 사라지지 않는다.</b> 놓기와 같은 <see cref="SnowBallCpu.Release"/> 를 쓰고,
        /// 그 안의 <c>Deposit</c> 은 넓혀도 못 놓으면 상한을 무시한다. 그래서 잔량이 0 이 아닌 경우는
        /// 필드 밖에서 터졌을 때뿐이고, 그때는 장부(<see cref="UnaccountedOutMm"/>)에 적는다.</para>
        ///
        /// <para>서버만 부른다 — 이 메서드가 오브젝트를 없앤다.</para>
        /// </summary>
        private void BurstBall(SnowBallCarrier ball, SnowBallCpu sim, Vector2 atXZ, float dtSeconds,
                               SnowZone zone)
        {
            var pose = new SnowBladePose
            {
                CenterX = atXZ.x,
                CenterZ = atXZ.y,
                ForwardX = 1f,
                ForwardZ = 0f,
            };
            float r = sim.RadiusM;
            var shape = new SnowBladeShape
            {
                HalfWidthM = r,
                HalfDepthM = r,
                Profile = SnowBladeProfileKind.Straight,
                WingLengthM = 0f,
            };

            SnowHeightFieldCpu field = zone != null ? zone.Field : _field;
            SnowPlowStepCpu fieldSim = zone != null ? zone.Sim : _sim;

            // 터짐은 언제나 더미를 만든다 — 놓기와 같은 이유로 이완을 건너뛰지 않는다.
            fieldSim.Step(new SnowPlowStepInput
            {
                Prev = pose, Now = pose, BladeDown = false, SignedSpeedMps = 0f, DtSeconds = dtSeconds,
            });

            field.BeginCutPhase();
            long unplaced = sim.Release(atXZ.x, atXZ.y,
                                        fieldSim.Material.MaxPileHeightMm,
                                        fieldSim.Material.DepositSpreadRings * BurstSpreadFactor);
            field.EndCutPhase();
            UnaccountedOutMm += unplaced;
            if (zone != null) MarkZoneDirty(zone);
            else MarkChangedCellsStale(pose, pose, shape);

            BurstsTotal++;
            LastBurstRadiusM = r;

            RemoveBallTracking(ball);

            ball.ServerMarkBursting();

            // 스폰된 공은 런너가 없앤다 — Destroy 로 지우면 클라이언트에 남는다.
            if (!_standalone && Runner != null && ball.Object != null && ball.Object.IsValid)
                Runner.Despawn(ball.Object);
            else
                Destroy(ball.gameObject);
        }

        private void RemoveBallTracking(SnowBallCarrier ball)
        {
            _ballSims.Remove(ball);
            _prevBallXZ.Remove(ball);
            _ballZone.Remove(ball);
        }

        /// <summary>터진 횟수(누적). 검증과 HUD 가 읽는다.</summary>
        public static int BurstsTotal;

        /// <summary>마지막으로 터진 공의 반지름(m).</summary>
        public static float LastBurstRadiusM;

        /// <summary>
        /// 런너가 없는 판(싱글플레이)의 한 스텝. 차량이 사라진 뒤로 눈을 무너뜨리는 것은
        /// 눈덩이와 상자뿐이다.
        /// </summary>
        private void FixedUpdate()
        {
            if (!_standalone || _field == null) return;

            StepBallsStandalone(Time.fixedDeltaTime);
            StepZones(Time.fixedDeltaTime);
            StepPenguinFootprints();
        }

        private void StepPenguinFootprints()
        {
            if (_penguins.Count == 0) return;

            _penguinStepFields.Clear();
            bool groundCut = false;

            for (int i = _penguins.Count - 1; i >= 0; i--)
            {
                PenguinSnowInteraction penguin = _penguins[i];
                if (penguin == null)
                {
                    _penguinFootprints.Remove(penguin);
                    _penguins.RemoveAt(i);
                    continue;
                }

                if (!_penguinFootprints.TryGetValue(penguin, out SnowFootprintCpu footprint))
                {
                    footprint = new SnowFootprintCpu();
                    _penguinFootprints.Add(penguin, footprint);
                }

                Vector3 contact = penguin.ContactWorldPosition;
                SnowZone zone = ResolveZone(contact);
                SnowHeightFieldCpu field = zone != null ? zone.Field : _field;
                if (field == null)
                {
                    footprint.Step(null, 0f, 0f, penguin.FootprintRadiusM, false);
                    continue;
                }

                Vector3 sample = zone != null ? zone.ToLocal(contact) : contact;
                bool grounded = penguin.HasSnowContact;
                if (grounded && _penguinStepFields.Add(field)) field.BeginStep();

                field.BeginCutPhase();
                long removedMm = footprint.Step(field, sample.x, sample.z,
                    penguin.FootprintRadiusM, grounded);
                field.EndCutPhase();

                if (removedMm <= 0) continue;
                if (zone != null) MarkZoneDirty(zone);
                else groundCut = true;
            }

            if (groundCut) MarkAllCutCellsStale();
        }

        /// <summary>절삭이 닿은 사각형 안에서 <b>실제로 값이 바뀐 셀</b>을 전파 대상으로 표시한다.</summary>
        private void MarkChangedCellsStale(in SnowBladePose prev, in SnowBladePose now)
            => MarkChangedCellsStale(prev, now, _sim.Shape);

        /// <summary>
        /// 형상을 지정하는 쪽. 눈덩이는 블레이드와 다른 접지면을 쓰므로 <see cref="SnowPlowStepCpu.Shape"/>
        /// 를 그대로 쓰면 전파 범위가 실제로 걷은 자리와 어긋난다.
        ///
        /// <para><b>조건이 둘이고 둘 다 필요하다.</b></para>
        ///
        /// <para>1. <b>절삭 사각형 안</b>이어야 한다. relax 가 고쳐 쓰는 셀까지 보내면 4 초에 셀 35 만
        /// 개(2.1 MB)가 나가 클라이언트가 따라오지 못한다(실측). relax 는 각 피어가 자기 격자에서
        /// 돌리므로 결과 모양은 스스로 만든다. <b>이 조건을 빼고
        /// <see cref="SnowHeightFieldCpu.ChangedCells"/> 를 그대로 보내 봤고 그것이 바로 그 실패를
        /// 재현했다</b> - 보낸 셀 160,026 개, 클라이언트가 서버가 깎은 자리에서 최대 1.543 m
        /// 뒤처졌다(2026-08-20).</para>
        ///
        /// <para>2. <b>실제로 값이 바뀐 셀</b>이어야 한다. 사각형 전체를 청크 단위로 보내면 한 셀만
        /// 바뀌어도 256 셀(512 바이트)을 보내고, 공 밑의 청크는 매 틱 바뀌므로 매 틱 다시 보낸다 -
        /// 6 초 굴리기에 762 청크 390 KB 였고 자국이 덮은 청크는 20~25 개였다(중복 30 배). 사각형은
        /// 공 지름만 해도 30x30 셀인데 그 안에서 값이 변하는 것은 앞날 한 줄이다.</para>
        ///
        /// <para><b>관심 반경은 여기서 본다 - 보낼 때가 아니다.</b> 전에는 보낼 순간의 거리로 걸렀고,
        /// 그래서 큐가 밀리는 동안 차가 멀어지면 그 자리는 <b>영구히 스킵</b>됐다. 필요한 시점은
        /// 깎인 순간이고 그때 플레이어가 근처였는지가 판단 기준이다(2026-08-19 실측).</para>
        /// </summary>
        private void MarkChangedCellsStale(in SnowBladePose prev, in SnowBladePose now,
                                           in SnowBladeShape shape)
        {
            if (_field == null) return;
            SnowFieldGeometry geo = _field.Geo;
            if (!SnowBladeSweep.SweptCellRect(geo, prev, now, shape,
                                              out int cx0, out int cz0, out int cx1, out int cz1))
            {
                return;
            }

            MarkCutCellsStale(cx0, cz0, cx1, cz1);
        }

        /// <summary>플레이어 발자국처럼 이미 정확한 CutCells 목록을 가진 절삭은 사각형 필터가 필요 없다.</summary>
        private void MarkAllCutCellsStale()
        {
            if (_field == null) return;
            MarkCutCellsStale(0, 0, _field.Geo.ResX - 1, _field.Geo.ResZ - 1);
        }

        private void MarkCutCellsStale(int cx0, int cz0, int cx1, int cz1)
        {
            // 단독 모드에는 보낼 상대가 없다. 공 경로가 두 판에서 같은 코드를 쓰므로 여기서 막는다.
            if (_standalone || Runner == null || _field == null) return;
            if (!_replicateSnowToClients) return;

            // <b>절삭이 바꾼 셀만 본다.</b> 이완이 고쳐 쓴 셀은 각 피어가 자기 격자에서 스스로
            // 만든다 - 그것까지 보내면 4 초에 셀 35 만 개가 나가 클라이언트가 따라오지 못한다(실측).
            IReadOnlyList<int> cells = _field.CutCells;
            DebugCutCells += cells.Count;
            DebugRelaxCells += _field.ChangedCells.Count - cells.Count;
            if (cells.Count == 0) return;

            SnowFieldGeometry geo = _field.Geo;

            const float half = SnowFieldGeometry.CellSizeM * 0.5f;

            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (!Runner.TryGetPlayerObject(player, out NetworkObject avatar) || avatar == null) continue;

                Vector3 at = avatar.transform.position;

                if (!_staleByPlayer.TryGetValue(player.PlayerId, out HashSet<int> stale))
                {
                    stale = new HashSet<int>();
                    _staleByPlayer[player.PlayerId] = stale;
                }

                for (int i = 0; i < cells.Count; i++)
                {
                    int cell = cells[i];
                    int cx = cell % geo.ResX;
                    int cz = cell / geo.ResX;
                    if (cx < cx0 || cx > cx1 || cz < cz0 || cz > cz1) continue;

                    float wx = geo.OriginXM + cx * SnowFieldGeometry.CellSizeM + half;
                    float wz = geo.OriginZM + cz * SnowFieldGeometry.CellSizeM + half;
                    float dx = wx - at.x;
                    float dz = wz - at.z;
                    if (dx * dx + dz * dz > InterestRadiusM * InterestRadiusM) continue;

                    stale.Add(cell);
                }
            }
        }

        /// <summary>
        /// 바뀐 셀을 플레이어별로 보낸다. <b>메시지는 두 종류</b>이고 첫 바이트가 종류다.
        ///
        /// <para><c>0</c> = 셀 델타. <c>(셀 색인 4B, 높이 2B)</c> 의 나열이다. 굴리는 동안 실제로 값이
        /// 변하는 것은 자국의 앞날 한 줄(10~30 셀)이라 틱당 60~180 바이트다 - 청크 통째로 보내던
        /// 4 KB/틱의 1/20~1/60 이다(2026-08-20 실측).</para>
        ///
        /// <para><c>1</c> = 청크 하나 전체. <see cref="RepairEveryNTicks"/> 마다 하나씩 돌려 보내는
        /// 보험이고, 셀 델타가 하나 사라져도 그 자리가 영구히 틀린 채 남지 않게 한다.</para>
        /// </summary>
        private void SendDelta()
        {
            if (Runner == null || _field == null) return;
            if (!_replicateSnowToClients) return;
            if (SendEveryNTicks > 1 && Runner.Tick % SendEveryNTicks != 0) return;

            const int cellsPerChunk = SnowFieldGeometry.ChunkCells * SnowFieldGeometry.ChunkCells;
            int size = 5 + MaxCellsPerPlayerTick * 6;
            int repairSize = 5 + 4 + cellsPerChunk * 2;
            if (size < repairSize) size = repairSize;
            if (_sendBuffer == null || _sendBuffer.Length < size) _sendBuffer = new byte[size];

            bool repairTick = Runner.Tick % RepairEveryNTicks == 0;

            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                _staleByPlayer.TryGetValue(player.PlayerId, out HashSet<int> stale);

                if (stale != null && stale.Count > 0) SendCells(player, stale);
                if (repairTick) SendRepairChunk(player);
            }
        }

        /// <summary>
        /// 바뀐 셀을 예산만큼 보낸다. <b>플레이어에게 가까운 것부터</b>.
        ///
        /// <para><b>왜 순서가 중요한가.</b> 큐는 <c>HashSet</c> 이라 순회 순서가 임의다. 예산이 모자라는
        /// 동안 그 임의 순서가 곧 "무엇이 먼저 보이는가" 를 정하고, 그러면 플레이어 발밑의 자국이
        /// 20 m 밖의 자국보다 늦게 도착할 수 있다 - 바이트를 한 개도 안 늘리고 <b>체감 지연</b>만
        /// 나빠지는 경우다. 거리로 정렬하면 같은 대역폭에서 보이는 곳이 먼저 채워진다.</para>
        ///
        /// <para>정렬 비용은 큐 길이에 로그를 곱한 것이고 큐는 예산에 눌려 수백을 넘지 않는다.
        /// 큐가 예산보다 작으면 정렬을 건너뛴다 - 그때는 순서가 결과를 바꾸지 않는다.</para>
        /// </summary>
        private void SendCells(PlayerRef player, HashSet<int> stale)
        {
            int written = 0;
            int cursor = 5;

            SnowFieldGeometry geo = _field.Geo;
            _drain.Clear();
            _drain.AddRange(stale);

            if (_drain.Count > MaxCellsPerPlayerTick
                && Runner.TryGetPlayerObject(player, out NetworkObject avatar) && avatar != null)
            {
                Vector3 at = avatar.transform.position;
                float ax = at.x;
                float az = at.z;
                int resX = geo.ResX;
                float ox = geo.OriginXM;
                float oz = geo.OriginZM;
                const float cell = SnowFieldGeometry.CellSizeM;

                _drain.Sort((a, b) =>
                {
                    float adx = ox + (a % resX) * cell - ax;
                    float adz = oz + (a / resX) * cell - az;
                    float bdx = ox + (b % resX) * cell - ax;
                    float bdz = oz + (b / resX) * cell - az;
                    return (adx * adx + adz * adz).CompareTo(bdx * bdx + bdz * bdz);
                });
            }

            for (int i = 0; i < _drain.Count; i++)
            {
                if (written >= MaxCellsPerPlayerTick) break;

                int c = _drain[i];

                // 거리 필터는 <b>표시할 때</b> 이미 걸렀다(`MarkChangedCellsStale`). 여기서 다시 걸면
                // 큐가 밀리는 동안 멀어진 자리가 영구히 스킵된다.
                WriteInt(c, ref cursor);
                ushort mm = _field.GetAt(c);
                _sendBuffer[cursor++] = (byte)(mm & 0xFF);
                _sendBuffer[cursor++] = (byte)(mm >> 8);

                _sent.Add(c);
                written++;
            }

            for (int i = 0; i < _sent.Count; i++) stale.Remove(_sent[i]);
            _sent.Clear();

            if (written == 0) return;

            _sendBuffer[0] = KindCells;
            int header = 1;
            WriteInt(written, ref header);

            DebugMessagesSent++;
            DebugChunksSent += written;

            Runner.SendReliableDataToPlayer(player, SnowDeltaKey,
                                            new System.ReadOnlySpan<byte>(_sendBuffer, 0, cursor));
        }

        /// <summary>
        /// 관심 반경 안의 청크를 하나씩 돌아가며 통째로 보낸다. 셀 델타가 유실돼도 여기서 낫는다.
        /// </summary>
        private void SendRepairChunk(PlayerRef player)
        {
            if (!Runner.TryGetPlayerObject(player, out NetworkObject avatar) || avatar == null) return;

            SnowFieldGeometry geo = _field.Geo;
            Vector3 at = avatar.transform.position;

            _repairCursor.TryGetValue(player.PlayerId, out int cursorChunk);

            // 관심 안의 첫 청크를 찾는다. 전부 밖이면 이번 틱은 보내지 않는다.
            int found = -1;
            for (int step = 0; step < geo.ChunkCount; step++)
            {
                int chunk = (cursorChunk + step) % geo.ChunkCount;
                float cx = geo.OriginXM + ((chunk % geo.ChunksX) + 0.5f)
                                          * SnowFieldGeometry.ChunkCells * SnowFieldGeometry.CellSizeM;
                float cz = geo.OriginZM + ((chunk / geo.ChunksX) + 0.5f)
                                          * SnowFieldGeometry.ChunkCells * SnowFieldGeometry.CellSizeM;
                float dx = cx - at.x;
                float dz = cz - at.z;
                if (dx * dx + dz * dz > InterestRadiusM * InterestRadiusM) continue;

                found = chunk;
                _repairCursor[player.PlayerId] = (chunk + 1) % geo.ChunkCount;
                break;
            }

            if (found < 0) return;

            int baseX = (found % geo.ChunksX) * SnowFieldGeometry.ChunkCells;
            int baseZ = (found / geo.ChunksX) * SnowFieldGeometry.ChunkCells;

            _sendBuffer[0] = KindChunk;
            int cursor = 1;
            WriteInt(1, ref cursor);
            WriteInt(found, ref cursor);

            for (int z = 0; z < SnowFieldGeometry.ChunkCells; z++)
            {
                for (int x = 0; x < SnowFieldGeometry.ChunkCells; x++)
                {
                    ushort mm = _field.Get(baseX + x, baseZ + z);
                    _sendBuffer[cursor++] = (byte)(mm & 0xFF);
                    _sendBuffer[cursor++] = (byte)(mm >> 8);
                }
            }

            DebugMessagesSent++;
            DebugRepairsSent++;

            Runner.SendReliableDataToPlayer(player, SnowDeltaKey,
                                            new System.ReadOnlySpan<byte>(_sendBuffer, 0, cursor));
        }

        private readonly List<int> _sent = new List<int>(256);

        /// <summary>배출 후보를 담아 정렬하는 재사용 버퍼. 매 틱 새로 담으므로 상태가 없다.</summary>
        private readonly List<int> _drain = new List<int>(512);

        /// <summary>복구 스윕이 다음에 볼 청크. 플레이어별로 돌아간다.</summary>
        private readonly Dictionary<int, int> _repairCursor = new Dictionary<int, int>();

        /// <summary>메시지 종류. 첫 바이트다 - 늘리려면 여기에 값을 더한다.</summary>
        private const byte KindCells = 0;

        /// <inheritdoc cref="KindCells"/>
        private const byte KindChunk = 1;

        /// <summary>
        /// 상자 하나의 <b>전체</b> 높이 배열. <c>[kind][zoneIndex:4][cellCount:4][ushort x cells]</c>.
        ///
        /// <para><b>왜 상자는 델타가 아니라 스냅샷인가.</b> 셀 델타 기계는 격자가 84만 셀이라서 생겼다
        /// (전송 예산·거리 정렬·복구 스윕 전부 그 크기의 결과다). 상자는 셀 수천이라 통째로 보내는 것이
        /// 청크 큐를 유지하는 것보다 싸고, <b>유실이 저절로 낫는다</b> — 복구 스윕이 필요 없다.
        /// 6x10 m 상자는 6,144 셀 = 12 KB 이고, 바뀐 상자만 <see cref="ZoneSnapshotEveryNTicks"/> 마다
        /// 한 번 나간다.</para>
        /// </summary>
        private const byte KindZoneSnapshot = 2;

        /// <summary>바뀐 상자를 이 틱 간격으로 보낸다. 안 바뀐 상자는 아무것도 보내지 않는다.</summary>
        private const int ZoneSnapshotEveryNTicks = 6;

        /// <summary>이 상자를 다음 스냅샷에 실어야 한다고 표시한다.</summary>
        private void MarkZoneDirty(SnowZone zone)
        {
            int index = _zones.IndexOf(zone);
            if (index >= 0) _zonesDirty.Add(index);
        }

        /// <summary>
        /// 바뀐 상자의 전체 배열을 보낸다. 서버만 부른다.
        /// </summary>
        private void SendZoneSnapshots()
        {
            if (_standalone || Runner == null || !_replicateSnowToClients) return;
            if (_zonesDirty.Count == 0) return;
            if (Runner.Tick % ZoneSnapshotEveryNTicks != 0) return;

            foreach (int index in _zonesDirty)
            {
                if (index < 0 || index >= _zones.Count) continue;
                SnowZone zone = _zones[index];
                if (zone == null || zone.Field == null) continue;

                ushort[] height = zone.Field.HeightMm;
                int need = 1 + 4 + 4 + height.Length * 2;
                if (_sendBuffer == null || _sendBuffer.Length < need) _sendBuffer = new byte[need];

                int cursor = 0;
                _sendBuffer[cursor++] = KindZoneSnapshot;
                WriteInt(index, ref cursor);
                WriteInt(height.Length, ref cursor);
                for (int i = 0; i < height.Length; i++)
                {
                    _sendBuffer[cursor++] = (byte)(height[i] & 0xFF);
                    _sendBuffer[cursor++] = (byte)(height[i] >> 8);
                }

                DebugMessagesSent++;
                DebugZoneSnapshotsSent++;

                foreach (PlayerRef player in Runner.ActivePlayers)
                {
                    Runner.SendReliableDataToPlayer(player, SnowDeltaKey,
                                                    new System.ReadOnlySpan<byte>(_sendBuffer, 0, cursor));
                }
            }

            _zonesDirty.Clear();
        }

        /// <summary>검증용 — 보낸·적용한 상자 스냅샷 수.</summary>
        public static int DebugZoneSnapshotsSent;

        /// <inheritdoc cref="DebugZoneSnapshotsSent"/>
        public static int DebugZoneSnapshotsApplied;

        /// <summary>
        /// 받은 상자 스냅샷을 적용한다. 인덱스가 맞으려면 양쪽의 상자 목록이 <b>같은 순서</b>여야 하고,
        /// 그것을 <see cref="SnowZone.StableId"/> 정렬이 보장한다.
        /// </summary>
        private void ApplyZoneSnapshot(byte[] data, ref int at, int zoneIndex, int cellCount)
        {
            if (zoneIndex < 0 || zoneIndex >= _zones.Count)
            {
                Debug.LogError($"{nameof(SnowCpuStage)}: 상자 스냅샷 인덱스 {zoneIndex} 가 이 피어의 " +
                               $"상자 수 {_zones.Count} 밖이다 — 씬이 서로 다르다.");
                at += cellCount * 2;
                return;
            }

            SnowZone zone = _zones[zoneIndex];
            SnowHeightFieldCpu field = zone == null ? null : zone.Field;
            if (field == null || field.HeightMm.Length != cellCount)
            {
                at += cellCount * 2;
                return;
            }

            SnowFieldGeometry geo = field.Geo;
            for (int i = 0; i < cellCount; i++)
            {
                var mm = (ushort)(data[at] | (data[at + 1] << 8));
                at += 2;
                if (field.GetAt(i) == mm) continue;

                field.Set(i % geo.ResX, i / geo.ResX, mm);
                field.WakeChunkOfCell(i % geo.ResX, i / geo.ResX);
                DebugCellsApplied++;
            }

            DebugZoneSnapshotsApplied++;
        }

        private void WriteInt(int value, ref int at)
        {
            _sendBuffer[at++] = (byte)(value & 0xFF);
            _sendBuffer[at++] = (byte)((value >> 8) & 0xFF);
            _sendBuffer[at++] = (byte)((value >> 16) & 0xFF);
            _sendBuffer[at++] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>검증용 — 필터 전 핸들러 진입 횟수. <see cref="DebugMessagesSent"/> 와 맞아야 한다.</summary>
        public static int DebugHandlerCalls;

        /// <summary>검증용 — 필터에서 조용히 버린 횟수(런너 불일치·키 불일치·필드 없음).</summary>
        public static int DebugFilteredOut;

        /// <summary>검증용 — 파싱 중 터진 횟수. 0 이 아니면 프레이밍 버그다.</summary>
        public static int DebugParseErrors;

        /// <summary>
        /// 검증용 — <b>내 런너의 메시지인데</b> 스테이지가 준비되지 않아 버린 횟수(등록 전·필드 없음).
        /// 0 이 아니면 그만큼이 진짜 유실이고, 원인은 전송이 아니라 <b>수신 측 준비 순서</b>다.
        /// </summary>
        public static int DebugFilteredMine;

        /// <summary>서버가 보낸 청크를 그대로 적용한다.</summary>
        private void OnReliableData(NetworkRunner runner, PlayerRef from, ReliableKey key, byte[] data)
        {
            DebugHandlerCalls++;

            // <b>여기서 버리는 것이 유실의 후보다.</b> 정적 이벤트라 한 프로세스의 모든 피어가 모든
            // 콜백을 받고 각자 필터한다. 클라의 스테이지가 아직 런너에 등록되지 않았으면
            // (`_registeredWith == null`) 그 메시지는 조용히 사라지고, 보낸 쪽은 이미 잊었다.
            if (runner != _registeredWith || key != SnowDeltaKey || _field == null)
            {
                if (key == SnowDeltaKey)
                {
                    DebugFilteredOut++;
                    if (runner == Runner) DebugFilteredMine++;
                }

                return;
            }

            if (Runner.IsServer) return;

            SnowFieldGeometry geo = _field.Geo;
            int at = 0;

            try
            {
                byte kind = data[at++];
                int count = ReadInt(data, ref at);

                // 상자 스냅샷은 첫 정수가 셀 수가 아니라 <b>상자 인덱스</b>다. 종류로 갈리므로
                // 같은 자리에 다른 뜻을 두어도 안전하다.
                if (kind == KindZoneSnapshot)
                {
                    int cells = ReadInt(data, ref at);
                    ApplyZoneSnapshot(data, ref at, count, cells);
                }
                else if (kind == KindCells)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int cell = ReadInt(data, ref at);
                        ushort mm = (ushort)(data[at] | (data[at + 1] << 8));
                        at += 2;

                        int cx = cell % geo.ResX;
                        int cz = cell / geo.ResX;
                        _field.Set(cx, cz, mm);

                        // <b>깨워야 렌더가 다시 굽는다.</b> `Set` 은 청크 변경 목록을 건드리지 않으므로
                        // 이것을 빠뜨리면 클라이언트의 표면이 자기 relax 가 그 청크를 건드릴 때까지
                        // 낡은 채로 남는다 - 전에는 relax 가 우연히 덮어 주고 있었다.
                        _field.WakeChunkOfCell(cx, cz);
                        DebugCellsApplied++;
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        int chunk = ReadInt(data, ref at);
                        int baseX = (chunk % geo.ChunksX) * SnowFieldGeometry.ChunkCells;
                        int baseZ = (chunk / geo.ChunksX) * SnowFieldGeometry.ChunkCells;

                        for (int z = 0; z < SnowFieldGeometry.ChunkCells; z++)
                        {
                            for (int x = 0; x < SnowFieldGeometry.ChunkCells; x++)
                            {
                                ushort mm = (ushort)(data[at] | (data[at + 1] << 8));
                                at += 2;
                                _field.Set(baseX + x, baseZ + z, mm);
                            }
                        }

                        _field.WakeChunk(chunk);
                        DebugChunksApplied++;
                    }
                }
            }
            catch (System.Exception e)
            {
                // 한 메시지가 깨졌다고 다음 메시지까지 버리지 않는다. 다만 조용히 넘기지도 않는다 -
                // 이 값이 0 이 아니면 원인은 흐름 제어가 아니라 프레이밍이다.
                DebugParseErrors++;
                if (DebugParseErrors == 1)
                    Debug.LogError($"[SnowCpuStage] 눈 델타 파싱 실패 (len={data?.Length ?? -1} at={at}): {e.Message}");
            }
        }

        private static int ReadInt(byte[] data, ref int at)
        {
            int value = data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
            at += 4;
            return value;
        }

        private void OnEnable() => SessionLauncher.ReliableDataReceived += OnReliableData;

        private void OnDisable() => SessionLauncher.ReliableDataReceived -= OnReliableData;

        /// <summary>
        /// 격자 전체의 체크섬. <b>총량 비교는 동기화를 증명하지 못한다</b> — 밀기는 재분배라 총량은 어느
        /// 피어에서도 항상 같고, 분포가 전혀 달라도 값이 일치한다. 실제로 그 약한 검사가 통과하는 동안
        /// 변한 셀 수는 피어마다 달랐다(514 / 514 / 492). 위치별 높이를 섞어 넣어야 "같은 눈" 을 본다.
        ///
        /// <para>FNV-1a 를 셀 인덱스와 함께 섞는다 — 같은 양이 다른 자리에 있으면 값이 달라져야 한다.</para>
        /// </summary>
        public ulong FieldChecksum()
        {
            if (_field == null) return 0UL;

            ulong hash = 14695981039346656037UL;
            int cells = _field.Geo.CellCount;
            for (int i = 0; i < cells; i++)
            {
                ushort h = _field.GetAt(i);
                if (h == 0) continue;

                hash ^= (ulong)(uint)i;
                hash *= 1099511628211UL;
                hash ^= h;
                hash *= 1099511628211UL;
            }

            return hash;
        }

        /// <summary>월드 XZ 의 눈 높이(m). 화면 없이도 답할 수 있는 질의이므로 서버가 쓴다.</summary>
        public float HeightAtM(float worldX, float worldZ)
        {
            if (_field == null) return 0f;

            SnowFieldGeometry geo = _field.Geo;
            if (!geo.TryWorldToCell(worldX, worldZ, out int cx, out int cz)) return 0f;

            return _field.Get(cx, cz) * 0.001f;
        }

        /// <summary>
        /// 이 격자가 앉은 바닥. <c>null</c> 이면 평지다.
        /// </summary>
        public SnowGroundFieldCpu Ground => _field?.Ground;

        /// <summary>
        /// 월드 XZ 의 <b>바닥</b> 월드 Y(m). 눈이 아니라 눈이 얹힌 면이다.
        /// 바닥 맵이 없으면 0 이고, 그것이 예전 동작이다.
        /// </summary>
        public float FloorYAtM(float worldX, float worldZ)
        {
            SnowGroundFieldCpu ground = Ground;
            return ground == null ? 0f : ground.FloorYAtWorld(worldX, worldZ);
        }

        /// <summary>
        /// 월드 XZ 의 <b>눈 표면</b> 월드 Y(m) = 바닥 + 깊이. 무언가를 눈 위에 놓을 때 쓴다 —
        /// <see cref="HeightAtM"/> 는 깊이일 뿐이라 경사에서는 지면 높이가 되지 못한다.
        /// </summary>
        public float SurfaceYAtM(float worldX, float worldZ)
            => FloorYAtM(worldX, worldZ) + HeightAtM(worldX, worldZ);

        /// <summary>월드 XZ 에 눈이 있을 수 있나. 바닥 맵이 없으면 어디든 참이다.</summary>
        public bool SnowableAtM(float worldX, float worldZ)
        {
            SnowGroundFieldCpu ground = Ground;
            if (ground == null) return true;
            SnowFieldGeometry geo = ground.Geo;
            if (!geo.TryWorldToCell(worldX, worldZ, out int cx, out int cz)) return false;
            return ground.IsSnowable(cx, cz);
        }

        /// <summary>
        /// <b>점</b>에서의 눈 깊이(m). <see cref="HeightAtM"/> 와 다른 점은 <b>Y 를 본다</b>는 것이고,
        /// 그래서 상자를 가릴 수 있다 — 지붕 위에 선 액터는 지붕의 눈을, 그 아래 선 액터는 땅의 눈을 읽는다.
        /// XZ 만 아는 호출자(구형 경로)는 계속 <see cref="HeightAtM"/> 을 쓰고, 그것은 언제나 지면 시트다.
        /// </summary>
        public float DepthAt(Vector3 worldPos)
        {
            SnowZone zone = ResolveZone(worldPos);
            if (zone != null && zone.TrySurfaceLocalY(worldPos, out _, out float zoneDepth)) return zoneDepth;
            return HeightAtM(worldPos.x, worldPos.z);
        }

        /// <summary>
        /// 액터가 <paramref name="supportWorldPos"/>에서 실제로 밟고 있는 바닥에 속한 눈 깊이.
        /// 같은 XZ 아래에 다른 눈 필드가 있어도 지지면 높이가 다르면 그 눈을 반환하지 않는다.
        /// </summary>
        public bool TryDepthAtSupport(Vector3 supportWorldPos, float floorToleranceM, out float depthM)
        {
            depthM = 0f;
            float toleranceM = Mathf.Max(0f, floorToleranceM);

            SnowZone zone = ResolveZone(supportWorldPos);
            if (zone != null)
            {
                if (!zone.TrySurfaceLocalY(supportWorldPos, out float localSurfaceY, out float zoneDepthM))
                    return false;

                Vector3 localSupport = zone.ToLocal(supportWorldPos);
                float localFloorY = localSurfaceY - zoneDepthM;
                if (Mathf.Abs(localSupport.y - localFloorY) > toleranceM) return false;

                depthM = zoneDepthM;
                return depthM > 0f;
            }

            if (_field == null) return false;
            SnowFieldGeometry geo = _field.Geo;
            if (!geo.TryWorldToCell(supportWorldPos.x, supportWorldPos.z, out int cx, out int cz))
                return false;

            SnowGroundFieldCpu ground = _field.Ground;
            if (ground != null && !ground.IsSnowable(cx, cz)) return false;

            float floorY = ground != null
                ? ground.FloorYAtWorld(supportWorldPos.x, supportWorldPos.z)
                : geo.OriginYM;
            if (Mathf.Abs(supportWorldPos.y - floorY) > toleranceM) return false;

            depthM = _field.Get(cx, cz) * 0.001f;
            return depthM > 0f;
        }

        /// <summary>점에서의 눈 <b>표면</b> 월드 Y(m). 무언가를 눈 위에 놓을 때 쓴다.</summary>
        public float SurfaceYAt(Vector3 worldPos)
        {
            SnowZone zone = ResolveZone(worldPos);
            if (zone != null && zone.TrySurfaceWorldY(worldPos, out float worldY, out _)) return worldY;
            return SurfaceYAtM(worldPos.x, worldPos.z);
        }
    }
}

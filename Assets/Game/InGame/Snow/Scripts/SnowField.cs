using System;

namespace PPack
{
    /// <summary>
    /// 눈 깊이의 <b>권위</b>. 순수 C# 이고 <c>UnityEngine</c> 그래픽 타입을 참조하지 않는다 —
    /// 데디 서버(<c>-batchmode -nographics</c>)에서 그대로 돈다.
    ///
    /// 규약(<c>docs/specs/2026-08-14-snow-surface.md</c> §3):
    /// <list type="bullet">
    /// <item>셀당 <b>1바이트</b>(깊이 cm). 12.5cm 셀·64×64m 에서 262KB</item>
    /// <item>세계 좌표 격자 — 패널 수와 무관하게 스테이지당 하나</item>
    /// <item>연산은 <b>스탬프 하나뿐</b>. 밀기는 <c>deltaCm &lt; 0</c>, 이벤트 적설은 &gt; 0</item>
    /// <item>재시뮬레이션 idempotency 는 셀이 아니라 <b>(tick, stampId)</b> 단위</item>
    /// <item><c>Time</c> 을 읽지 않는다. 틱은 항상 인자로 받는다 — Fusion 이 오면 <c>Runner.Tick</c> 을 넘긴다</item>
    /// </list>
    ///
    /// 이 필드는 <b>롤백 정확하지 않고 수렴한다.</b> 파괴적 상태는 히스토리 없이 되돌릴 수 없으므로
    /// 입력 보정으로 경로가 바뀌면 오예측한 셀이 남고, 교정은 서버의 주기적 동기화가 한다.
    /// </summary>
    public sealed class SnowField
    {
        /// <summary>적용된 스탬프를 기억하는 링 버퍼의 길이. 롤백 창보다 넉넉하면 된다.</summary>
        private const int AppliedHistory = 256;

        private readonly byte[] _depthCm;
        private readonly int _width;
        private readonly int _height;
        private readonly float _cellSize;
        private readonly float _originX;
        private readonly float _originZ;
        private readonly byte _maxDepthCm;

        // (tick, stampId) 링 버퍼. 셀마다 틱을 두는 대신 스탬프 단위로 막는다 —
        // 셀당 4바이트가 싸고, 같은 틱에 겹친 두 도구 중 하나를 조용히 버리지 않는다.
        private readonly int[] _appliedTick = new int[AppliedHistory];
        private readonly int[] _appliedStamp = new int[AppliedHistory];
        private int _appliedCount;

        public int Width => _width;
        public int Height => _height;
        public float CellSize => _cellSize;
        public byte MaxDepthCm => _maxDepthCm;

        /// <summary>격자의 월드 원점. 셀을 직접 훑는 쪽(제설 힙의 모양 있는 방출)이 필요하다.</summary>
        public float OriginX => _originX;
        public float OriginZ => _originZ;

        /// <summary>
        /// 격자 전체의 깊이 합(cm·셀). <b>부동소수가 아니라 <c>long</c> 이고, 값을 바꾸는 모든 경로가
        /// 여기에 같은 정수를 더한다.</b> 그래서 질량 불변식의 필드 항이 근사가 아니라 <b>정확</b>하다 —
        /// AnyTest v7 은 float32 필드를 훑어 합을 냈고 3,960 m³ 에서 ULP 0.25 L 가 계기 자체의
        /// 바닥이었다(그래서 톨러런스가 필요했다). 1바이트 정수 격자는 그 바닥이 없다.
        ///
        /// 매 스텝 262k 셀을 다시 훑지 않으려고 <b>증분</b>으로 유지한다. 값을 바꾸는 경로를 새로
        /// 만들면 여기도 같이 더해야 한다 — 빠뜨리면 불변식이 그 즉시 <b>전체 크기로</b> 어긋난다.
        /// 조용히 흐르지 않는 것이 요점이다.
        /// </summary>
        public long TotalDepthCm { get; private set; }

        /// <summary>이번 프레임에 스탬프가 건드린 셀의 바운딩 박스. 업로드 rect 로 쓴다. 비어 있으면 w·h 가 0.</summary>
        public (int x, int y, int w, int h) DirtyRect { get; private set; }

        public SnowField(float originX, float originZ, float sizeX, float sizeZ,
                         float cellSize, byte maxDepthCm)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));

            _cellSize = cellSize;
            _originX = originX;
            _originZ = originZ;
            _maxDepthCm = maxDepthCm;
            _width = Math.Max(1, (int)MathF.Round(sizeX / cellSize));
            _height = Math.Max(1, (int)MathF.Round(sizeZ / cellSize));

            _depthCm = new byte[_width * _height];
            FillAll(maxDepthCm);
        }

        /// <summary>전면을 한 값으로 채운다. 스테이지 시작 상태를 세울 때 쓴다.</summary>
        public void FillAll(byte depthCm)
        {
            byte v = depthCm > _maxDepthCm ? _maxDepthCm : depthCm;
            for (int i = 0; i < _depthCm.Length; i++) _depthCm[i] = v;
            TotalDepthCm = (long)v * _depthCm.Length;
            MarkAllDirty();
        }

        public byte DepthCmAtCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return 0;
            return _depthCm[y * _width + x];
        }

        /// <summary>월드 XZ 의 깊이(cm). 차량 감속 판정이 이걸 읽는다 — 텍스처는 읽지 않는다.</summary>
        public byte DepthCmAtWorld(float worldX, float worldZ)
        {
            int cx = (int)MathF.Floor((worldX - _originX) / _cellSize);
            int cy = (int)MathF.Floor((worldZ - _originZ) / _cellSize);
            return DepthCmAtCell(cx, cy);
        }

        /// <summary>월드 좌표 ↔ 셀. 셀을 직접 훑는 쪽이 원점·셀 크기를 다시 계산하지 않게 한다.</summary>
        public int CellXAtWorld(float worldX) => (int)MathF.Floor((worldX - _originX) / _cellSize);
        public int CellYAtWorld(float worldZ) => (int)MathF.Floor((worldZ - _originZ) / _cellSize);
        public float WorldXAtCell(int x) => _originX + (x + 0.5f) * _cellSize;
        public float WorldZAtCell(int y) => _originZ + (y + 0.5f) * _cellSize;

        /// <summary>
        /// 셀 하나의 깊이를 바꾸고 <b>실제로 적용된 양</b>을 돌려준다. <b>영수증의 기본 연산</b>이다
        /// (<see cref="SnowPlowLedger"/>).
        ///
        /// <see cref="ApplyStamp"/> 와 다른 점 셋:
        /// <list type="bullet">
        /// <item>사각형이 아니라 <b>셀 하나</b>다 — 모양 있는 힙은 셀마다 다른 값을 쓴다.</item>
        /// <item><b>부호 있는 실제 적용량</b>을 돌려준다. 0 과 <see cref="MaxDepthCm"/> 의 클램프가
        ///   삼킨 양이 호출자에게 보인다. 삼킨 양을 모르면 원장이 필드보다 많이 혹은 적게 빠지고,
        ///   그 차이가 곧 누출이다 — v7 이 -1.40 mL/step 으로 물린 자리가 정확히 이것이다.</item>
        /// <item>(tick, stampId) 멱등 검사를 하지 않는다. 힙은 한 스텝에 수천 셀을 쓰므로 스탬프
        ///   단위 중복 방지의 대상이 아니고, 중복 방지는 <b>한 스텝 전체</b>를 한 번만 돌리는
        ///   호출자가 한다.</item>
        /// </list>
        /// </summary>
        /// <returns>실제로 더해진 양(cm). 뺐으면 음수. 0 이면 아무 일도 없었다.</returns>
        public int ApplyCellDelta(int x, int y, int deltaCm)
        {
            if (deltaCm == 0) return 0;
            if (x < 0 || y < 0 || x >= _width || y >= _height) return 0;

            int i = y * _width + x;
            int before = _depthCm[i];
            int after = before + deltaCm;
            if (after < 0) after = 0;
            else if (after > _maxDepthCm) after = _maxDepthCm;

            int applied = after - before;
            if (applied == 0) return 0;

            _depthCm[i] = (byte)after;
            TotalDepthCm += applied;
            MergeDirty(x, y, x, y);
            return applied;
        }

        /// <summary>셀이 더 받을 수 있는 양(cm). 힙 방출이 천장에 부딪히는 자리를 미리 안다.</summary>
        public int HeadroomCmAtCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return 0;
            return _maxDepthCm - _depthCm[y * _width + x];
        }

        /// <summary>한 프레임의 업로드가 끝나면 부른다. 다음 스탬프부터 rect 를 다시 모은다.</summary>
        public void ClearDirty() => DirtyRect = (0, 0, 0, 0);

        /// <summary>
        /// 사각 패드가 덮은 셀의 깊이를 <paramref name="deltaCm"/> 만큼 바꾼다.
        /// 밀기는 음수, 이벤트 적설은 양수 — <b>같은 연산이다.</b>
        ///
        /// <paramref name="stampId"/> 는 스탬프를 낸 주체(도구·이벤트)를 구분하는 값이다.
        /// 같은 <c>(tick, stampId)</c> 를 다시 적용하면 <b>아무 일도 하지 않는다</b> — 재시뮬레이션이
        /// 같은 틱을 여러 번 재생해도 깊이가 한 번 적용한 것과 같아야 하기 때문이다.
        /// </summary>
        /// <returns>실제로 제거된 총량(cm·셀). 0 이면 연출도 뜨지 않아야 한다.</returns>
        public int ApplyStamp(int tick, int stampId, in SnowStampArea area, int deltaCm)
        {
            if (deltaCm == 0) return 0;
            if (WasApplied(tick, stampId)) return 0;
            MarkApplied(tick, stampId);

            // 패드의 월드 AABB → 셀 범위. 셀 중심이 패드 안인지는 아래에서 다시 본다.
            int minX = (int)MathF.Floor((area.MinX - _originX) / _cellSize);
            int maxX = (int)MathF.Floor((area.MaxX - _originX) / _cellSize);
            int minY = (int)MathF.Floor((area.MinZ - _originZ) / _cellSize);
            int maxY = (int)MathF.Floor((area.MaxZ - _originZ) / _cellSize);

            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX >= _width) maxX = _width - 1;
            if (maxY >= _height) maxY = _height - 1;
            if (minX > maxX || minY > maxY) return 0;

            int removed = 0;
            int touchedMinX = int.MaxValue, touchedMinY = int.MaxValue;
            int touchedMaxX = int.MinValue, touchedMaxY = int.MinValue;

            for (int y = minY; y <= maxY; y++)
            {
                float worldZ = _originZ + (y + 0.5f) * _cellSize;
                int row = y * _width;

                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = _originX + (x + 0.5f) * _cellSize;
                    if (!area.Contains(worldX, worldZ)) continue;

                    int before = _depthCm[row + x];
                    int after = before + deltaCm;
                    if (after < 0) after = 0;
                    else if (after > _maxDepthCm) after = _maxDepthCm;
                    if (after == before) continue;

                    _depthCm[row + x] = (byte)after;
                    TotalDepthCm += after - before;
                    if (after < before) removed += before - after;

                    if (x < touchedMinX) touchedMinX = x;
                    if (y < touchedMinY) touchedMinY = y;
                    if (x > touchedMaxX) touchedMaxX = x;
                    if (y > touchedMaxY) touchedMaxY = y;
                }
            }

            if (touchedMaxX >= touchedMinX) MergeDirty(touchedMinX, touchedMinY, touchedMaxX, touchedMaxY);
            return removed;
        }

        // ------------------------------------------------------------------
        // 동기화 — 블록 해시. 정상 상태 비용은 해상도가 아니라 불일치량에 비례한다(스펙 §3.4).
        // 해시 비교와 불일치 블록 재전송은 여기서 끝난다. 비어 있는 것은 이 페이로드를 실제로
        // 나르는 전송 계층뿐이고, 그래서 이 절 전체가 그래픽·넷코드 없이 검증된다.
        // ------------------------------------------------------------------

        /// <summary>블록 한 변의 셀 수. 2m 블록(12.5cm 셀에서 16칸)이 기준.</summary>
        public const int BlockCells = 16;

        public int BlockCountX => (_width + BlockCells - 1) / BlockCells;
        public int BlockCountY => (_height + BlockCells - 1) / BlockCells;

        /// <summary>블록 하나의 해시. 서버와 클라가 이 값만 비교해 갈라진 블록을 찾는다.</summary>
        public uint BlockHash(int bx, int by)
        {
            int x0 = bx * BlockCells, y0 = by * BlockCells;
            int x1 = Math.Min(x0 + BlockCells, _width);
            int y1 = Math.Min(y0 + BlockCells, _height);

            uint h = 2166136261u;                       // FNV-1a
            for (int y = y0; y < y1; y++)
            {
                int row = y * _width;
                for (int x = x0; x < x1; x++)
                {
                    h = (h ^ _depthCm[row + x]) * 16777619u;
                }
            }
            return h;
        }

        /// <summary>블록 하나를 직렬화한다(깊이 cm 그대로). 불일치 블록 재전송용.</summary>
        public int WriteBlock(int bx, int by, Span<byte> destination)
        {
            int x0 = bx * BlockCells, y0 = by * BlockCells;
            int x1 = Math.Min(x0 + BlockCells, _width);
            int y1 = Math.Min(y0 + BlockCells, _height);

            int written = 0;
            for (int y = y0; y < y1; y++)
            {
                int row = y * _width;
                for (int x = x0; x < x1; x++) destination[written++] = _depthCm[row + x];
            }
            return written;
        }

        public void ReadBlock(int bx, int by, ReadOnlySpan<byte> source)
        {
            int x0 = bx * BlockCells, y0 = by * BlockCells;
            int x1 = Math.Min(x0 + BlockCells, _width);
            int y1 = Math.Min(y0 + BlockCells, _height);

            int read = 0;
            long delta = 0;
            for (int y = y0; y < y1; y++)
            {
                int row = y * _width;
                for (int x = x0; x < x1; x++)
                {
                    byte next = source[read++];
                    delta += next - _depthCm[row + x];
                    _depthCm[row + x] = next;
                }
            }
            TotalDepthCm += delta;
            MergeDirty(x0, y0, x1 - 1, y1 - 1);
        }

        /// <summary>블록 개수. 해시 배열과 불일치 목록의 길이가 이것이다.</summary>
        public int BlockCount => BlockCountX * BlockCountY;

        /// <summary>
        /// 블록 하나가 담는 셀 수. 격자 폭·높이가 <see cref="BlockCells"/> 로 나눠지지 않으면 마지막
        /// 열·행이 작다. <b>페이로드에 길이를 쓰지 않는 이유가 이 함수다</b> — 양쪽이 격자 크기를
        /// 공유하므로 받는 쪽이 인덱스에서 되계산한다.
        /// </summary>
        public int BlockCellCount(int blockIndex)
        {
            if (blockIndex < 0 || blockIndex >= BlockCount)
                throw new ArgumentOutOfRangeException(nameof(blockIndex));

            int bx = blockIndex % BlockCountX;
            int by = blockIndex / BlockCountX;
            int w = Math.Min((bx + 1) * BlockCells, _width) - bx * BlockCells;
            int h = Math.Min((by + 1) * BlockCells, _height) - by * BlockCells;
            return w * h;
        }

        /// <summary>모든 블록의 해시. 서버가 동기화 주기마다 한 번 채운다.</summary>
        public void WriteBlockHashes(Span<uint> destination)
        {
            int count = BlockCount;
            if (destination.Length < count)
                throw new ArgumentException("해시 배열이 블록 수보다 짧다", nameof(destination));

            int bcx = BlockCountX;
            for (int i = 0; i < count; i++) destination[i] = BlockHash(i % bcx, i / bcx);
        }

        /// <summary>
        /// 원격 해시와 다른 블록의 인덱스를 모은다. <b>일치하는 블록은 4바이트 비교로 끝나므로</b>
        /// 정상 상태 비용이 격자 해상도가 아니라 불일치량에 비례한다.
        ///
        /// <paramref name="destination"/> 이 짧으면 거기까지만 담는다 — 대역폭 예산이 목록 길이다.
        /// 남은 블록은 다음 주기에 다시 불일치로 잡히므로 수렴이 늦어질 뿐 어긋난 채로 남지 않는다.
        /// </summary>
        /// <returns>채운 인덱스 수.</returns>
        public int CollectMismatchedBlocks(ReadOnlySpan<uint> remoteHashes, Span<int> destination)
        {
            int count = BlockCount;
            if (remoteHashes.Length != count)
                throw new ArgumentException("원격 해시 수가 블록 수와 다르다 — 격자 크기가 어긋났다",
                                            nameof(remoteHashes));

            int bcx = BlockCountX;
            int found = 0;
            for (int i = 0; i < count && found < destination.Length; i++)
            {
                if (remoteHashes[i] == BlockHash(i % bcx, i / bcx)) continue;
                destination[found++] = i;
            }
            return found;
        }

        /// <summary>블록 인덱스가 차지하는 페이로드 머리 크기(바이트).</summary>
        public const int BlockPayloadHeaderBytes = 2;

        /// <summary>블록 <paramref name="blockCount"/> 개를 담기에 충분한 버퍼 크기.</summary>
        public static int MaxBlockPayloadBytes(int blockCount) =>
            blockCount * (BlockPayloadHeaderBytes + BlockCells * BlockCells);

        /// <summary>
        /// 불일치 블록들을 페이로드 하나로 직렬화한다. 항목은 <c>[ushort 블록 인덱스][깊이 바이트]</c> 이고
        /// 길이는 쓰지 않는다(<see cref="BlockCellCount"/>).
        /// </summary>
        /// <returns>쓴 바이트 수.</returns>
        public int WriteBlocks(ReadOnlySpan<int> blockIndices, Span<byte> destination)
        {
            int count = BlockCount;
            if (count > ushort.MaxValue)
                throw new InvalidOperationException("블록 수가 ushort 인덱스를 넘었다 — 머리 크기를 늘려야 한다");

            int bcx = BlockCountX;
            int written = 0;
            for (int i = 0; i < blockIndices.Length; i++)
            {
                int index = blockIndices[i];
                if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(blockIndices));

                int cells = BlockCellCount(index);
                if (written + BlockPayloadHeaderBytes + cells > destination.Length)
                    throw new ArgumentException("페이로드 버퍼가 부족하다", nameof(destination));

                destination[written] = (byte)(index & 0xFF);
                destination[written + 1] = (byte)(index >> 8);
                written += BlockPayloadHeaderBytes;
                written += WriteBlock(index % bcx, index / bcx, destination.Slice(written, cells));
            }
            return written;
        }

        /// <summary>
        /// <see cref="WriteBlocks"/> 가 만든 페이로드를 적용한다. <see cref="TotalDepthCm"/> 는 블록마다
        /// 증분으로 정확히 따라가므로 전체 격자를 다시 훑지 않는다.
        /// </summary>
        /// <returns>적용한 블록 수.</returns>
        public int ReadBlocks(ReadOnlySpan<byte> payload)
        {
            int count = BlockCount;
            int bcx = BlockCountX;
            int read = 0;
            int applied = 0;

            while (read < payload.Length)
            {
                if (read + BlockPayloadHeaderBytes > payload.Length)
                    throw new ArgumentException("페이로드가 인덱스 중간에서 끊겼다", nameof(payload));

                int index = payload[read] | (payload[read + 1] << 8);
                if (index >= count)
                    throw new ArgumentException("페이로드의 블록 인덱스가 격자 밖이다", nameof(payload));
                read += BlockPayloadHeaderBytes;

                int cells = BlockCellCount(index);
                if (read + cells > payload.Length)
                    throw new ArgumentException("페이로드가 블록 중간에서 끊겼다", nameof(payload));

                ReadBlock(index % bcx, index / bcx, payload.Slice(read, cells));
                read += cells;
                applied++;
            }
            return applied;
        }

        /// <summary>깊이 배열 전체(조인 스냅샷). 압축은 전송 계층이 한다.</summary>
        public ReadOnlySpan<byte> Snapshot() => _depthCm;

        public void LoadSnapshot(ReadOnlySpan<byte> source)
        {
            if (source.Length != _depthCm.Length)
                throw new ArgumentException("스냅샷 크기가 격자와 다르다", nameof(source));
            source.CopyTo(_depthCm);

            long sum = 0;
            for (int i = 0; i < _depthCm.Length; i++) sum += _depthCm[i];
            TotalDepthCm = sum;
            MarkAllDirty();
        }

        // ------------------------------------------------------------------

        private bool WasApplied(int tick, int stampId)
        {
            int n = Math.Min(_appliedCount, AppliedHistory);
            for (int i = 0; i < n; i++)
            {
                if (_appliedTick[i] == tick && _appliedStamp[i] == stampId) return true;
            }
            return false;
        }

        private void MarkApplied(int tick, int stampId)
        {
            int slot = _appliedCount % AppliedHistory;
            _appliedTick[slot] = tick;
            _appliedStamp[slot] = stampId;
            _appliedCount++;
        }

        private void MarkAllDirty() => DirtyRect = (0, 0, _width, _height);

        private void MergeDirty(int minX, int minY, int maxX, int maxY)
        {
            var d = DirtyRect;
            if (d.w == 0 || d.h == 0)
            {
                DirtyRect = (minX, minY, maxX - minX + 1, maxY - minY + 1);
                return;
            }

            int x0 = Math.Min(d.x, minX);
            int y0 = Math.Min(d.y, minY);
            int x1 = Math.Max(d.x + d.w - 1, maxX);
            int y1 = Math.Max(d.y + d.h - 1, maxY);
            DirtyRect = (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }
    }

    /// <summary>
    /// 스탬프가 덮는 영역 — 표면에 누운 <b>방향 있는 사각형</b>. 구가 아닌 이유는 도구가 패드라서다
    /// (<c>BrushPad</c> 와 같은 근거). 세로(Y) 는 보지 않는다 — 필드가 수평 격자다.
    /// </summary>
    public readonly struct SnowStampArea
    {
        private readonly float _centerX;
        private readonly float _centerZ;
        private readonly float _forwardX;   // 정규화된 진행 방향 XZ
        private readonly float _forwardZ;
        private readonly float _halfLength; // 진행 방향 반길이
        private readonly float _halfWidth;  // 좌우 반폭

        public float MinX { get; }
        public float MaxX { get; }
        public float MinZ { get; }
        public float MaxZ { get; }

        public SnowStampArea(float centerX, float centerZ, float forwardX, float forwardZ,
                             float halfLength, float halfWidth)
        {
            float len = MathF.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
            if (len < 1e-5f) { forwardX = 0f; forwardZ = 1f; }
            else { forwardX /= len; forwardZ /= len; }

            _centerX = centerX;
            _centerZ = centerZ;
            _forwardX = forwardX;
            _forwardZ = forwardZ;
            _halfLength = MathF.Abs(halfLength);
            _halfWidth = MathF.Abs(halfWidth);

            // 회전한 사각형의 월드 AABB
            float extentX = MathF.Abs(_forwardX) * _halfLength + MathF.Abs(_forwardZ) * _halfWidth;
            float extentZ = MathF.Abs(_forwardZ) * _halfLength + MathF.Abs(_forwardX) * _halfWidth;
            MinX = centerX - extentX;
            MaxX = centerX + extentX;
            MinZ = centerZ - extentZ;
            MaxZ = centerZ + extentZ;
        }

        /// <summary>셀 중심이 사각형 안인가. 경계는 포함한다.</summary>
        public bool Contains(float worldX, float worldZ)
        {
            float dx = worldX - _centerX;
            float dz = worldZ - _centerZ;
            float along = dx * _forwardX + dz * _forwardZ;
            float across = -dx * _forwardZ + dz * _forwardX;
            return MathF.Abs(along) <= _halfLength && MathF.Abs(across) <= _halfWidth;
        }
    }
}

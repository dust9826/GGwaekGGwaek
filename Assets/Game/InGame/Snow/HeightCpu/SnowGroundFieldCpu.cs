namespace PPack
{
    /// <summary>
    /// <b>눈이 얹히는 바닥.</b> 셀마다 바닥 높이, 커버리지, 시작 적설 배율을 가진다.
    ///
    /// <para><b>불변이다.</b> 맵 빌드 때 한 번 굽고(<c>SnowGroundMap</c>) 런타임에는 읽기만 한다.
    /// 그래서 네트워크로 보낼 것이 없다 — 서버와 모든 클라이언트가 맵과 함께 같은 값을 갖는다.
    /// 가변인 것은 여전히 <see cref="SnowHeightFieldCpu"/> 의 깊이 하나뿐이고, 그것이 이 분리의
    /// 이유다(설계: <c>docs/specs/2026-08-23-snow-regions.md</c> §3).</para>
    ///
    /// <para><b>바닥을 <c>ushort</c> 로 두는 이유는 깊이와 같다</b> — 정수라서 피어 간 비트단위로
    /// 같고, <c>ushort[]</c> 가 R16 UNorm 텍스처와 같은 바이트라 업로드가 memcpy 다. 음수 높이를
    /// 담기 위해 <see cref="SnowFieldGeometry.OriginYM"/> 을 기준으로 <b>그 위의 mm</b> 를 담는다.</para>
    ///
    /// <para><b>"눈이 있을 수 있나" 는 용량 0 으로 표현한다.</b> 별도의 예외 경로가 아니라
    /// <see cref="SnowHeightFieldCpu.AddAt"/> 의 천장이 0 이 되는 것이고, 그래서 퇴적·이완·재적설이
    /// 각자 마스크를 기억하지 않아도 눈이 새어 들어가지 않는다.</para>
    ///
    /// 이 클래스는 <c>UnityEngine</c> 을 참조하지 않는다 — 데디 서버(<c>-nographics</c>)가 이것을
    /// 읽고 판정하기 때문이고, 그 규칙은 <see cref="SnowHeightFieldCpu"/> 와 같다.
    /// </summary>
    public sealed class SnowGroundFieldCpu
    {
        /// <summary>"눈이 가득" 인 커버리지. R8 텍스처로 그대로 올라가므로 255 다.</summary>
        public const byte SnowableValue = 255;

        public SnowFieldGeometry Geo { get; }

        /// <summary><see cref="SnowFieldGeometry.OriginYM"/> 위의 바닥 높이(mm). 렌더가 memcpy 한다.</summary>
        public ushort[] FloorMm { get; }

        /// <summary>
        /// 셀당 <b>커버리지</b> 0~255. <c>0</c> 은 눈이 불가능(용량 0)이고 <c>255</c> 는 가득이며,
        /// <b>그 사이가 가장자리 마감</b>이다 — 렌더가 이 값을 깊이에 곱해 눈을 바닥면으로 재운다.
        ///
        /// <para>이진 마스크가 아니라 커버리지인 이유는 <b>경계가 두 곳에 있으면 안 되기</b> 때문이다.
        /// 예전에는 권위(이진 마스크)와 렌더(사각형 거리 페이드)가 각자 경계를 갖고 있었고, 그러면
        /// 상자가 격자를 꽉 채울 때 렌더 쪽이 경계를 못 본다. 하나로 합치면 굽는 쪽이 노이즈로
        /// 경계를 흩뜨려도 렌더가 그대로 따라온다.</para>
        ///
        /// <para>R8 텍스처로 그대로 올라가고 <b>bilinear</b> 로 읽는다.</para>
        /// </summary>
        public byte[] Coverage { get; }

        /// <summary>
        /// 스테이지의 시작 적설 깊이에 곱할 셀별 배율. 255는 전량, 0은 처음에는 눈이 없다는 뜻이다.
        /// 커버리지와 달리 이후 재적설 용량을 막지 않으므로 도로가 다시 쌓이는 것을 허용한다.
        /// </summary>
        public byte[] InitialDepthScaleR8 { get; }

        public int MinFloorMm { get; }
        public int MaxFloorMm { get; }

        /// <summary>눈이 앉을 수 있는 셀 수. 굽기가 맵을 통째로 놓쳤는지 한 값으로 드러난다.</summary>
        public int SnowableCells { get; }

        public SnowGroundFieldCpu(SnowFieldGeometry geo, ushort[] floorMm, byte[] coverage)
            : this(geo, floorMm, coverage, BuildFullInitialDepthScale(geo, coverage)) { }

        public SnowGroundFieldCpu(SnowFieldGeometry geo, ushort[] floorMm, byte[] coverage,
                                  byte[] initialDepthScaleR8)
        {
            if (geo == null) throw new System.ArgumentNullException(nameof(geo));
            if (floorMm == null || floorMm.Length != geo.CellCount)
                throw new System.ArgumentException("바닥 배열 길이가 격자와 다르다", nameof(floorMm));
            if (coverage == null || coverage.Length != geo.CellCount)
                throw new System.ArgumentException("커버리지 길이가 격자와 다르다", nameof(coverage));
            if (initialDepthScaleR8 == null || initialDepthScaleR8.Length != geo.CellCount)
                throw new System.ArgumentException("시작 적설 배율 길이가 격자와 다르다", nameof(initialDepthScaleR8));

            Geo = geo;
            FloorMm = floorMm;
            Coverage = coverage;
            InitialDepthScaleR8 = initialDepthScaleR8;

            int min = int.MaxValue;
            int max = int.MinValue;
            int count = 0;
            for (int i = 0; i < floorMm.Length; i++)
            {
                if (coverage[i] == 0) continue;
                count++;
                int f = floorMm[i];
                if (f < min) min = f;
                if (f > max) max = f;
            }

            SnowableCells = count;
            MinFloorMm = count == 0 ? 0 : min;
            MaxFloorMm = count == 0 ? 0 : max;
        }

        private static byte[] BuildFullInitialDepthScale(SnowFieldGeometry geo, byte[] coverage)
        {
            if (geo == null) throw new System.ArgumentNullException(nameof(geo));
            if (coverage == null || coverage.Length != geo.CellCount)
                throw new System.ArgumentException("커버리지 길이가 격자와 다르다", nameof(coverage));

            var scale = new byte[geo.CellCount];
            for (int i = 0; i < scale.Length; i++)
                if (coverage[i] != 0) scale[i] = byte.MaxValue;
            return scale;
        }

        /// <summary>
        /// 평지 바닥. 바닥 0, 전 셀 커버리지 가득 — 가장자리 마감이 없다. 테스트와, 마감이 의미
        /// 없는 경로가 쓴다.
        /// </summary>
        public static SnowGroundFieldCpu Flat(SnowFieldGeometry geo)
        {
            var coverage = new byte[geo.CellCount];
            for (int i = 0; i < coverage.Length; i++) coverage[i] = SnowableValue;
            return new SnowGroundFieldCpu(geo, new ushort[geo.CellCount], coverage, coverage);
        }

        /// <summary>
        /// 사각형 안에 눈을 채우고 <b>가장자리를 흩뜨려 재운다</b>. 지면 시트와 눈 상자가 같은 함수를 쓴다.
        ///
        /// <para><paramref name="fadeM"/> 폭에 걸쳐 커버리지가 0 으로 떨어지고, 그 경계선 자체가
        /// <paramref name="jitterM"/> 만큼 <b>안쪽으로</b> 저주파 노이즈로 흔들린다. 직선으로 끊긴
        /// 눈은 저작물처럼 보이고, 흔들린 경계는 쌓인 눈처럼 보인다 — 노이즈의 목적이 그것뿐이다.</para>
        ///
        /// <para>⚠ <b>흔들림은 안쪽으로만 간다</b>(2026-08-24 정정). 전에는 바깥으로 최대
        /// <paramref name="jitterM"/> 만큼 넘쳤는데, 그러면 램프·슬래브에 맞춰 놓은 상자의 눈이
        /// <b>메시 모서리를 넘어 허공에 처마로 남는다</b>. 사각형이 곧 <b>눈의 최대 범위</b>여야
        /// 상자를 받치는 메시에 맞춰 놓는 것만으로 눈이 지오메트리 밖으로 안 나간다.</para>
        ///
        /// <para><b>커버리지가 권위이기도 하다</b>(0 이면 용량 0). 그래서 흩뜨린 경계가 곧 팔 수 있는
        /// 범위이고, 렌더와 권위가 갈릴 자리가 없다. ⚠ 대신 노이즈는 <b>모든 피어에서 같은 값</b>이어야
        /// 한다 — 정수 좌표에서만 유도하고 부동소수 보간은 격자 상수에만 쓴다.</para>
        /// </summary>
        public static SnowGroundFieldCpu FromRect(SnowFieldGeometry geo, ushort[] floorMm,
                                                  float minX, float minZ, float maxX, float maxZ,
                                                  float fadeM, float jitterM)
        {
            var coverage = new byte[geo.CellCount];
            float fade = fadeM > 1e-4f ? fadeM : 1e-4f;

            for (int cz = 0; cz < geo.ResZ; cz++)
            for (int cx = 0; cx < geo.ResX; cx++)
            {
                // <b>격자 테두리 한 칸은 언제나 0 이다.</b> 셰이더의 커버리지 탭은 clamp 샘플러라
                // 0 인 칸이 격자 안에 없으면 마지막 값을 패널 가장자리까지 끌고 나간다 — 사각형이
                // 격자와 딱 맞는 지면 시트가 그랬고, 마지막 칸의 커버리지 35/255 가 둥근 어깨를
                // 지나면 <b>30 cm 두께</b>라 시트가 바닥에 닿지 않고 절벽으로 끊겼다.
                if (cx == 0 || cz == 0 || cx == geo.ResX - 1 || cz == geo.ResZ - 1) continue;

                geo.CellCenterWorld(cx, cz, out float x, out float z);

                // 사각형까지의 부호 있는 거리. 안이 양수다.
                float inside = System.Math.Min(System.Math.Min(x - minX, maxX - x),
                                               System.Math.Min(z - minZ, maxZ - z));
                if (jitterM > 0f) inside -= jitterM * EdgeNoise01(cx, cz);

                float c = inside / fade;
                if (c <= 0f) continue;
                coverage[geo.CellIndex(cx, cz)] = c >= 1f ? SnowableValue : (byte)(c * 255f + 0.5f);
            }

            return new SnowGroundFieldCpu(geo, floorMm ?? new ushort[geo.CellCount], coverage);
        }

        /// <summary>경계를 흔드는 저주파 값 노이즈. 0~1.</summary>
        private static float EdgeNoise01(int cx, int cz)
        {
            // 1 m 격자(8 셀)에서 해시하고 셀 안에서 보간한다. 셀마다 새로 뽑으면 12.5 cm 짜리
            // 지저분한 톱니가 되고, 격자만 쓰면 1 m 계단이 된다.
            const int lattice = 8;
            int gx = FloorDiv(cx, lattice);
            int gz = FloorDiv(cz, lattice);
            float fx = Smooth((cx - gx * lattice) / (float)lattice);
            float fz = Smooth((cz - gz * lattice) / (float)lattice);

            float a = Hash01(gx, gz);
            float b = Hash01(gx + 1, gz);
            float c = Hash01(gx, gz + 1);
            float d = Hash01(gx + 1, gz + 1);
            return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fz;
        }

        private static int FloorDiv(int v, int n) => v >= 0 ? v / n : -(((-v) + n - 1) / n);

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static float Hash01(int x, int z)
        {
            uint h = (uint)(x + 4096) * 1597334677u ^ (uint)(z + 4096) * 3812015801u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0x00ffffffu) * (1f / 16777216f);
        }

        public bool IsSnowableAt(int cellIndex) => Coverage[cellIndex] != 0;

        public int InitialDepthAt(int cellIndex, int stageInitialDepthMm)
            => (stageInitialDepthMm * InitialDepthScaleR8[cellIndex] + 127) / 255;

        public bool IsSnowable(int cx, int cz) => Coverage[Geo.CellIndex(cx, cz)] != 0;

        public int FloorMmAt(int cellIndex) => FloorMm[cellIndex];

        /// <summary>셀 바닥의 월드 Y.</summary>
        public float FloorYAt(int cellIndex) => Geo.OriginYM + FloorMm[cellIndex] * 0.001f;

        /// <summary>
        /// 월드 XZ 의 바닥 월드 Y. 필드 밖은 <see cref="SnowFieldGeometry.OriginYM"/> 이다 —
        /// 눈도 없는 자리이므로 호출자가 이 값을 지면으로 쓸 일이 없다.
        /// </summary>
        public float FloorYAtWorld(float worldX, float worldZ)
        {
            if (!Geo.TryWorldToCell(worldX, worldZ, out int cx, out int cz)) return Geo.OriginYM;
            return FloorYAt(Geo.CellIndex(cx, cz));
        }
    }
}

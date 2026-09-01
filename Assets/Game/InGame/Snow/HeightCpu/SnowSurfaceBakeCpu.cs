using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// <b>렌더가 읽는 표면의 디테일 층 전부.</b> 권위 필드는 여기서 한 비트도 안 바뀐다 —
    /// 이 클래스는 필드를 <b>읽어서</b> 텍스처 둘을 굽고, 그 둘은 서버에 존재하지 않는다.
    ///
    /// 두 층을 굽는다:
    /// <list type="bullet">
    /// <item><b>lump</b> — 구 격자를 높이 기여로. 각진 슬래브를 둥근 눈으로 바꾼다.</item>
    /// <item><b>fillet</b> — 둥근 어깨. 필드를 저역통과한 것과의 <b>차이</b>를 부호 있는 값으로
    /// 굽는다. 블레이드가 밀어놓은 자리처럼 relax 가 아직 못 푼 날카로운 능선을, 권위 필드를
    /// 건드리지 않고 렌더에서만 둥글린다.</item>
    /// </list>
    ///
    /// <b>왜 여기냐.</b> 권위 필드를 스무딩하면 질량 보존이 깨지고 모든 피어가 그 비용을 낸다.
    /// 셰이더에서 블러하면 표면 함수가 픽셀당 수십 번 불리므로 탭이 9배가 된다 — v7 이 로브를
    /// 스텝마다 평가했다가 +9.31ms 를 낸 그 실수다. 굽는 것이 유일하게 옳은 자리다.
    ///
    /// (아래는 lump 층의 근거)
    /// <b>눈이 눈으로 읽히게 만드는 층.</b> 높이필드 레이마칭만으로는 눈이 각진 계단식 슬래브로
    /// 읽힌다 — v7 이 실측으로 확인하고, 그 해법으로 도달한 것이 이 구 격자다.
    ///
    /// <code>
    ///     lift(x,z) = 3x3 이웃의 max( sqrt(r² − d²) )     d = 해시 지터된 구 중심까지의 수평거리
    ///     gate(x,z) = saturate((깊이 − minSnow) / gateDepth)
    ///     표면      = 필드 + gate · lift
    /// </code>
    ///
    /// 세 성질이 이 형태를 성립시킨다. 하나라도 버리면 무너진다.
    /// <list type="number">
    /// <item><b>여전히 높이함수다.</b> 진짜 3D SDF 합집합이면 오버행이 생겨 <c>ray.y &lt; h</c> 판정과
    /// coarse-max 스킵과 SV_Depth 가 동시에 무너진다.</item>
    /// <item><b>상한이 정확히 +r 이다.</b> <c>lift ≤ r</c> 이 점별로 성립하고 plain max 라 오버슈트가
    /// 없다. 진폭이 무한한 노이즈로 형태를 넣으려던 v7 의 시도는 상한을 뭉개서 meanSteps 가
    /// 7.4 에서 39.5 로 터졌다.</item>
    /// <item><b>평가하지 말고 굽는다.</b> lift 는 필드의 순수 함수다. 픽셀당 수십 번 재계산할 이유가
    /// 없다 — v7 실측으로 스텝마다 해시 9개를 돌리면 +9.31 ms, 텍스처에 굽고 탭 하나면 +1.70 ms.</item>
    /// </list>
    ///
    /// <b>게이트 문턱은 긁기 바닥보다 충분히 위여야 한다.</b> v7 이 gateDepth 를 잔여물과 같게 뒀다가
    /// 잔여물이 거의 최대 진폭의 로브를 뒤집어썼고, 그러면 치운 차선이 안 치운 눈과 구별되지 않아
    /// 제설 읽힘이 통째로 죽었다.
    /// </summary>
    public sealed class SnowSurfaceBakeCpu
    {
        /// <summary>필드 대비 배율. 2 면 6.25 cm 텍셀 — 30 cm 로브가 4.8 텍셀이라 로브로 읽힌다.</summary>
        public const int Upsample = 2;

        private readonly SnowHeightFieldCpu _field;
        private readonly byte[] _lift;          // 0..255 == 0..RadiusM
        private readonly byte[] _fillet;        // 128 == 0, 0..255 == -FilletRangeM..+FilletRangeM
        private float[] _blurScratch;

        public int ResX { get; }
        public int ResZ { get; }
        public byte[] Lift => _lift;

        /// <summary>부호 있는 둥근 어깨. 128 이 0 이고 ±<see cref="FilletRangeM"/> 로 펼쳐진다.</summary>
        public byte[] Fillet => _fillet;

        /// <summary>fillet 인코딩의 반범위. 이보다 큰 보정은 잘린다.</summary>
        public float FilletRangeM { get; set; } = 0.35f;

        /// <summary>저역통과 반경, 텍셀 단위. 4 면 25 cm - v7 fillet 과 같은 폭이다.</summary>
        public int FilletRadiusTexels { get; set; } = 4;

        /// <summary>0 이면 fillet 끄기. 1 이면 저역통과한 표면을 그대로 쓴다.</summary>
        public float FilletStrength { get; set; } = 0.55f;

        /// <summary>구 반지름. <b>이 값이 그대로 coarse-max 상한에 더해져야 한다.</b></summary>
        public float RadiusM { get; set; } = 0.17f;

        /// <summary>구 격자 간격. 반지름보다 조금 촘촘해야 로브가 이어진다.</summary>
        public float SpacingM { get; set; } = 0.23f;

        /// <summary>격자에서 중심이 흔들리는 정도, 간격 대비. 0 이면 격자무늬가 보인다.</summary>
        public float Jitter { get; set; } = 0.85f;

        /// <summary>구마다 반지름이 흔들리는 정도. 0 이면 크기가 전부 같아서 격자가 읽힌다.</summary>
        public float RadiusVary { get; set; } = 0.45f;

        /// <summary>
        /// 평평한 처녀설에 남기는 로브 세기. 1 이면 세계 전체가 골프공이 된다.
        ///
        /// v7 의 게이트에는 <b>경사 항</b>이 있었고, 그것이 "작업된 눈에만 로브가 붙는다" 를 만든다.
        /// 갓 내린 눈은 매끈하고, 밀고 쌓고 무너진 눈이 덩어리진다 - 그 대비가 제설 읽힘의 절반이다.
        /// </summary>
        public float FlatAmount { get; set; } = 0.28f;

        /// <summary>이 깊이 아래는 로브가 없다. 긁기 바닥보다 충분히 위여야 한다.</summary>
        public float GateMinDepthM { get; set; } = 0.04f;

        /// <summary>게이트가 0 에서 1 로 오르는 깊이 폭.</summary>
        public float GateDepthM { get; set; } = 0.10f;

        public float TexelSizeM => SnowFieldGeometry.CellSizeM / Upsample;

        public SnowSurfaceBakeCpu(SnowHeightFieldCpu field)
        {
            _field = field;
            ResX = field.Geo.ResX * Upsample;
            ResZ = field.Geo.ResZ * Upsample;
            _lift = new byte[ResX * ResZ];
            _fillet = new byte[ResX * ResZ];
            for (int i = 0; i < _fillet.Length; i++) _fillet[i] = 128;
        }

        public void RebuildAll()
        {
            BakeTexelRect(0, 0, ResX - 1, ResZ - 1);
            BakeFilletRect(0, 0, ResX - 1, ResZ - 1);
        }

        /// <summary>
        /// 변한 청크만 다시 굽는다. <b>청크마다가 아니라 합친 사각형 하나로</b> 굽는다.
        ///
        /// 청크마다 부르면 여유(apron)가 겹쳐서 같은 텍셀을 여러 번 굽는다 - fillet 여유가
        /// 10 텍셀인데 청크가 32 텍셀이라 실측으로 6배쯤 중복이었고, 그것이 프레임의 79% 를
        /// 먹는 34 ms 였다. 합쳐서 한 번 굽는 것이 옳다.
        ///
        /// 흩어진 청크에서는 합친 사각형이 오히려 클 수 있으므로, 낭비가 심하면 청크별로 되돌아간다.
        /// </summary>
        public void RebuildChunks(IReadOnlyList<int> chunks)
        {
            if (chunks == null || chunks.Count == 0) return;
            var geo = _field.Geo;

            int cx0 = int.MaxValue, cz0 = int.MaxValue, cx1 = int.MinValue, cz1 = int.MinValue;
            for (int k = 0; k < chunks.Count; k++)
            {
                geo.ChunkCellBounds(chunks[k], out int a, out int b, out int c, out int d);
                if (a < cx0) cx0 = a;
                if (b < cz0) cz0 = b;
                if (c > cx1) cx1 = c;
                if (d > cz1) cz1 = d;
            }

            long unionArea = (long)(cx1 - cx0 + 1) * (cz1 - cz0 + 1);
            long chunkArea = (long)chunks.Count * SnowFieldGeometry.ChunkCells * SnowFieldGeometry.ChunkCells;

            if (unionArea <= chunkArea * 4)
            {
                BakeCellRect(cx0, cz0, cx1, cz1);
                return;
            }

            for (int k = 0; k < chunks.Count; k++)
            {
                geo.ChunkCellBounds(chunks[k], out int a, out int b, out int c, out int d);
                BakeCellRect(a, b, c, d);
            }
        }

        private void BakeCellRect(int cx0, int cz0, int cx1, int cz1)
        {
            int pad = (int)(RadiusM / TexelSizeM) + 2;
            BakeTexelRect(cx0 * Upsample - pad, cz0 * Upsample - pad,
                          (cx1 + 1) * Upsample - 1 + pad, (cz1 + 1) * Upsample - 1 + pad);

            // fillet 은 저역통과라 반경만큼 번진다. 2패스 분리 블러이므로 여유는 2배다.
            int fpad = FilletRadiusTexels * 2 + 2;
            BakeFilletRect(cx0 * Upsample - fpad, cz0 * Upsample - fpad,
                           (cx1 + 1) * Upsample - 1 + fpad, (cz1 + 1) * Upsample - 1 + fpad);
        }

        private void BakeTexelRect(int tx0, int tz0, int tx1, int tz1)
        {
            if (tx0 < 0) tx0 = 0;
            if (tz0 < 0) tz0 = 0;
            if (tx1 >= ResX) tx1 = ResX - 1;
            if (tz1 >= ResZ) tz1 = ResZ - 1;

            var geo = _field.Geo;
            float texel = TexelSizeM;
            float invSpacing = 1f / SpacingM;
            float r2 = RadiusM * RadiusM;
            float invRadius = 1f / RadiusM;
            float gateInv = 1f / (GateDepthM > 1e-4f ? GateDepthM : 1e-4f);

            for (int tz = tz0; tz <= tz1; tz++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                float wx = (tx + 0.5f) * texel;
                float wz = (tz + 0.5f) * texel;

                // 게이트: 얕은 눈에는 로브가 없다. 치운 차선이 치운 것으로 읽히려면 이것이 있어야 한다.
                int cx = tx / Upsample, cz = tz / Upsample;
                if (cx >= geo.ResX) cx = geo.ResX - 1;
                if (cz >= geo.ResZ) cz = geo.ResZ - 1;
                float depth = _field.HeightMm[geo.CellIndex(cx, cz)] * 1e-3f;

                float gate = (depth - GateMinDepthM) * gateInv;
                if (gate <= 0f) { _lift[tz * ResX + tx] = 0; continue; }
                if (gate > 1f) gate = 1f;

                // 경사 항. 평평하면 FlatAmount 까지 줄이고 작업된 눈에서 1 로 올린다.
                gate *= FlatAmount + (1f - FlatAmount) * SlopeTerm(cx, cz);

                // 3x3 이웃 구의 최대 기여. plain max 라 오버슈트가 없고 상한이 정확히 +r 이다.
                int gx = (int)System.Math.Floor(wx * invSpacing);
                int gz = (int)System.Math.Floor(wz * invSpacing);

                float best = 0f;
                for (int oz = -1; oz <= 1; oz++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int hx = gx + ox, hz = gz + oz;
                    Hash3(hx, hz, out float jx, out float jz, out float jr);
                    float cxw = (hx + 0.5f + (jx - 0.5f) * Jitter) * SpacingM;
                    float czw = (hz + 0.5f + (jz - 0.5f) * Jitter) * SpacingM;

                    // 반지름도 흔든다. 크기가 전부 같으면 지터를 줘도 격자가 읽힌다.
                    // 상한은 여전히 RadiusM 이다 - 위로 흔들지 않으므로 +r 이 깨지지 않는다.
                    float rr = RadiusM * (1f - RadiusVary * jr);
                    float rr2 = rr * rr;

                    float dx = wx - cxw, dz = wz - czw;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= rr2) continue;
                    float lift = (float)System.Math.Sqrt(rr2 - d2);
                    if (lift > best) best = lift;
                }

                float v = best * gate * invRadius;          // 0..1
                _lift[tz * ResX + tx] = (byte)(v * 255f + 0.5f);
            }
        }

        /// <summary>
        /// 저역통과한 표면과의 차이를 굽는다. 2패스 분리 박스 블러다.
        ///
        /// 결과는 <c>(1-s)·h + s·blur(h)</c> 이므로 h 와 blur 가 모두 0 이상이면 결과도 0 이상이다 —
        /// 지면 아래로 내려가지 않는다. 그리고 blur 는 반경 안 최대값을 넘을 수 없으므로,
        /// 상한은 <c>블록 최대 높이 + max(0, fillet)</c> 로 여전히 성립한다.
        /// </summary>
        private void BakeFilletRect(int tx0, int tz0, int tx1, int tz1)
        {
            if (FilletStrength <= 1e-4f || FilletRadiusTexels < 1)
            {
                for (int tz = System.Math.Max(0, tz0); tz <= System.Math.Min(ResZ - 1, tz1); tz++)
                for (int tx = System.Math.Max(0, tx0); tx <= System.Math.Min(ResX - 1, tx1); tx++)
                    _fillet[tz * ResX + tx] = 128;
                return;
            }

            int r = FilletRadiusTexels;
            int ax0 = tx0 - r, az0 = tz0 - r, ax1 = tx1 + r, az1 = tz1 + r;   // 1패스용 여유
            if (ax0 < 0) ax0 = 0;
            if (az0 < 0) az0 = 0;
            if (ax1 >= ResX) ax1 = ResX - 1;
            if (az1 >= ResZ) az1 = ResZ - 1;

            int w = ax1 - ax0 + 1, h = az1 - az0 + 1;
            int need = w * h;
            if (_blurScratch == null || _blurScratch.Length < need) _blurScratch = new float[need];

            var geo = _field.Geo;
            var hm = _field.HeightMm;

            // 1패스: x 방향. 텍셀은 자기를 품은 셀의 높이를 읽는다.
            for (int tz = az0; tz <= az1; tz++)
            {
                int cz = tz / Upsample;
                if (cz >= geo.ResZ) cz = geo.ResZ - 1;
                int row = cz * geo.ResX;
                for (int tx = ax0; tx <= ax1; tx++)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int sx = tx + k;
                        if (sx < 0 || sx >= ResX) continue;
                        int cx = sx / Upsample;
                        if (cx >= geo.ResX) cx = geo.ResX - 1;
                        sum += hm[row + cx];
                        n++;
                    }
                    _blurScratch[(tz - az0) * w + (tx - ax0)] = sum / n;
                }
            }

            // 2패스: z 방향, 그리고 차이를 인코딩한다.
            int ox0 = System.Math.Max(0, tx0), oz0 = System.Math.Max(0, tz0);
            int ox1 = System.Math.Min(ResX - 1, tx1), oz1 = System.Math.Min(ResZ - 1, tz1);
            float invRange = 1f / (FilletRangeM * 1000f);

            for (int tz = oz0; tz <= oz1; tz++)
            for (int tx = ox0; tx <= ox1; tx++)
            {
                float sum = 0f;
                int n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int sz = tz + k;
                    if (sz < az0 || sz > az1) continue;
                    sum += _blurScratch[(sz - az0) * w + (tx - ax0)];
                    n++;
                }
                float smoothMm = sum / n;

                int cx = tx / Upsample, cz = tz / Upsample;
                if (cx >= geo.ResX) cx = geo.ResX - 1;
                if (cz >= geo.ResZ) cz = geo.ResZ - 1;
                float rawMm = hm[geo.CellIndex(cx, cz)];

                float deltaMm = (smoothMm - rawMm) * FilletStrength;
                float e = deltaMm * invRange * 0.5f + 0.5f;      // 0..1
                if (e < 0f) e = 0f;
                else if (e > 1f) e = 1f;
                _fillet[tz * ResX + tx] = (byte)(e * 255f + 0.5f);
            }
        }

        /// <summary>
        /// 이웃과의 높이차를 0..1 로. 평평한 곳 0, 안식각에 가까운 곳 1.
        /// 이것이 "작업된 눈" 판정이다 - 밀고 쌓고 무너진 자리는 기울어져 있다.
        /// </summary>
        private float SlopeTerm(int cx, int cz)
        {
            var geo = _field.Geo;
            var h = _field.HeightMm;
            int c = h[geo.CellIndex(cx, cz)];
            int worst = 0;
            if (cx > 0)             worst = System.Math.Max(worst, System.Math.Abs(c - h[geo.CellIndex(cx - 1, cz)]));
            if (cx < geo.ResX - 1)  worst = System.Math.Max(worst, System.Math.Abs(c - h[geo.CellIndex(cx + 1, cz)]));
            if (cz > 0)             worst = System.Math.Max(worst, System.Math.Abs(c - h[geo.CellIndex(cx, cz - 1)]));
            if (cz < geo.ResZ - 1)  worst = System.Math.Max(worst, System.Math.Abs(c - h[geo.CellIndex(cx, cz + 1)]));
            float t = worst / 60f;                        // 60 mm 낙차면 완전히 작업된 것으로 본다
            return t > 1f ? 1f : t;
        }

        /// <summary>결정론적 해시. 프레임이나 시간에 의존하는 것이 하나도 없어야 피어가 안 갈라진다.</summary>
        private static void Hash3(int x, int z, out float a, out float b, out float c)
        {
            uint h = (uint)(x * 374761393) + (uint)(z * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            a = (h & 0x7FF) / 2047f;
            b = ((h >> 11) & 0x7FF) / 2047f;
            c = ((h >> 22) & 0x3FF) / 1023f;
        }
    }
}

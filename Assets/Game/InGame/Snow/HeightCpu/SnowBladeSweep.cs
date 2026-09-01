using System.Collections.Generic;

namespace PPack
{
    /// <summary>블레이드 선분의 한 자세. 오른쪽 벡터는 전방에서 파생되므로 저장하지 않는다.</summary>
    public struct SnowBladePose
    {
        public float CenterX;
        public float CenterZ;
        public float ForwardX;
        public float ForwardZ;

        public float RightX => ForwardZ;
        public float RightZ => -ForwardX;
    }

    /// <summary>
    /// 블레이드가 지나간 자리의 눈을 <b>전부</b> 걷어 바로 앞에 붓는다.
    ///
    /// 컷은 전량 제거다. 예산으로 깎는 스킨이 아니다 — v6 이 4 cm 스킨으로 깎았을 때 25 m³ 짜리
    /// 덩어리를 밀고도 세계는 1.4 cm 밖에 변하지 않았고, 지나온 자리가 치워진 것이 아니라 밟힌 것처럼
    /// 읽혔다. 제설차는 치운다.
    ///
    /// 퇴적은 <b>놓은 양</b>을 세어서 돌려준다. 계산한 양이 아니라. 천장에 걸리거나 밴드가 필드 밖으로
    /// 나가서 못 놓은 잔량이 있으면 그만큼이 장부 없이 사라지므로 호출자가 그것을 볼 수 있어야 한다.
    /// </summary>
    public static class SnowBladeSweep
    {
        [System.ThreadStatic] private static List<int> _band;
        [System.ThreadStatic] private static List<int> _weight;

        private static SnowBladePose Lerp(in SnowBladePose a, in SnowBladePose b, float t)
        {
            float fx = a.ForwardX + (b.ForwardX - a.ForwardX) * t;
            float fz = a.ForwardZ + (b.ForwardZ - a.ForwardZ) * t;
            float len = (float)System.Math.Sqrt(fx * fx + fz * fz);
            if (len < 1e-6f) { fx = b.ForwardX; fz = b.ForwardZ; len = 1f; }
            return new SnowBladePose
            {
                CenterX = a.CenterX + (b.CenterX - a.CenterX) * t,
                CenterZ = a.CenterZ + (b.CenterZ - a.CenterZ) * t,
                ForwardX = fx / len,
                ForwardZ = fz / len
            };
        }

        /// <summary>두 자세를 잇는 스윕이 닿을 수 있는 셀 사각형. 셀 하나만큼 여유를 준다.</summary>
        public static bool SweptCellRect(SnowFieldGeometry geo, in SnowBladePose prev, in SnowBladePose now,
                                         in SnowBladeShape shape,
                                         out int cx0, out int cz0, out int cx1, out int cz1)
        {
            float reach = shape.ReachM + SnowFieldGeometry.CellSizeM;

            float minX = System.Math.Min(prev.CenterX, now.CenterX) - reach;
            float maxX = System.Math.Max(prev.CenterX, now.CenterX) + reach;
            float minZ = System.Math.Min(prev.CenterZ, now.CenterZ) - reach;
            float maxZ = System.Math.Max(prev.CenterZ, now.CenterZ) + reach;
            return geo.TryWorldRectToCellRect(minX, minZ, maxX, maxZ, out cx0, out cz0, out cx1, out cz1);
        }

        /// <summary>
        /// 스윕 상자 안의 눈을 <paramref name="residueMm"/> 만 남기고 전부 걷어낸다.
        /// 걷어낸 총량(mm)을 돌려준다 — 필드에서 실제로 빠진 양과 같다.
        /// </summary>
        public static long Cut(SnowHeightFieldCpu field, in SnowBladePose prev, in SnowBladePose now,
                               in SnowBladeShape shape, int segments, int residueMm)
        {
            var geo = field.Geo;
            if (!SweptCellRect(geo, prev, now, shape, out int cx0, out int cz0, out int cx1, out int cz1)) return 0;
            if (segments < 1) segments = 1;

            long cut = 0;
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int ci = geo.CellIndex(cx, cz);
                int h = field.HeightMm[ci];
                if (h <= residueMm) continue;

                geo.CellCenterWorld(cx, cz, out float wx, out float wz);

                bool hit = false;
                for (int s = 0; s < segments && !hit; s++)
                {
                    float t = segments == 1 ? 1f : s / (float)(segments - 1);
                    var pose = Lerp(prev, now, t);
                    hit = shape.Contains(pose, wx, wz);
                }
                if (!hit) continue;

                cut += -field.AddAt(ci, -(h - residueMm));
                field.WakeChunkOfCell(cx, cz);
            }
            return cut;
        }

        /// <summary>
        /// 블레이드 바로 앞 밴드에 <paramref name="amountMm"/> 를 가중 분배한다.
        /// <b>놓지 못한 잔량</b>을 돌려준다 — 0 이 아니면 그만큼이 장부 없이 사라진 것이고, 버그다.
        /// </summary>
        /// <remarks>
        /// 밴드는 <b>가운데 직선 구간</b> 기준이다. 날개 쪽으로 가중을 몰아주지 않는다 —
        /// 한쪽으로 뱉게 만드는 것은 날개(형상)의 일이고, 여기에 보정값을 하나 더 두면
        /// 형상과 무관한 노브가 생겨서 둘이 서로를 상쇄하기 시작한다.
        ///
        /// <b>가중은 남은 여유 높이에 비례한다.</b> 그것이 "블레이드 앞이 무한정 솟지 않고
        /// 옆으로 퍼진다" 를 만드는 전부다 — 상한에 가까워진 셀은 거의 안 받고 낮은 셀이 대신
        /// 받으므로, 더미가 위로 가는 대신 넓어진다. 밴드 전체가 상한에 닿으면 밴드를 넓혀가며
        /// 마저 놓고, 그래도 못 놓으면 상한을 무시한다. <b>질량은 어떤 경우에도 사라지지 않는다.</b>
        /// </remarks>
        public static long Deposit(SnowHeightFieldCpu field, in SnowBladePose now, in SnowBladeShape shape,
                                   float bandDepthM, float sideMarginM, long amountMm,
                                   int capMm = 0, int spreadRings = 0)
        {
            if (amountMm <= 0) return 0;

            long left = amountMm;
            for (int ring = 0; ring <= spreadRings && left > 0; ring++)
            {
                // 링마다 밴드를 넓힌다. 옆으로도, 앞으로도.
                float grow = ring * SnowFieldGeometry.CellSizeM * 2f;
                left = DepositIntoBand(field, now, shape, bandDepthM + grow, sideMarginM + grow, left, capMm);
            }

            // 그래도 남으면 상한을 무시하고 놓는다. 보기 좋으라고 질량을 버릴 수는 없다.
            if (left > 0)
                left = DepositIntoBand(field, now, shape,
                                       bandDepthM + spreadRings * SnowFieldGeometry.CellSizeM * 2f,
                                       sideMarginM + spreadRings * SnowFieldGeometry.CellSizeM * 2f,
                                       left, 0);
            return left;
        }

        /// <summary>밴드 하나에 붓는다. 놓지 못한 잔량을 돌려준다.</summary>
        private static long DepositIntoBand(SnowHeightFieldCpu field, in SnowBladePose now,
                                            in SnowBladeShape shape, float bandDepthM, float sideMarginM,
                                            long amountMm, int capMm)
        {
            if (amountMm <= 0) return 0;
            var geo = field.Geo;

            if (_band == null) { _band = new List<int>(2048); _weight = new List<int>(2048); }
            _band.Clear();
            _weight.Clear();

            float halfW = shape.HalfWidthM + sideMarginM;
            float reach = halfW + shape.HalfDepthM + bandDepthM + SnowFieldGeometry.CellSizeM;
            if (!geo.TryWorldRectToCellRect(now.CenterX - reach, now.CenterZ - reach,
                                            now.CenterX + reach, now.CenterZ + reach,
                                            out int cx0, out int cz0, out int cx1, out int cz1)) return amountMm;

            long sumW = 0;
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                geo.CellCenterWorld(cx, cz, out float wx, out float wz);
                float dx = wx - now.CenterX;
                float dz = wz - now.CenterZ;

                float r = dx * now.RightX + dz * now.RightZ;
                if (r < -halfW || r > halfW) continue;

                float f = dx * now.ForwardX + dz * now.ForwardZ;
                if (f < shape.HalfDepthM || f > shape.HalfDepthM + bandDepthM) continue;

                // 블레이드 쪽이 무겁다. 밴드 끝은 거의 0 이 아니라 1 이어서, 밴드 전체가 항상 후보다.
                float t = (f - shape.HalfDepthM) / bandDepthM;
                int w = 1000 - (int)(999f * t);

                int ci = geo.CellIndex(cx, cz);
                if (capMm > 0)
                {
                    // 남은 여유에 비례한다. 상한에 닿은 셀은 아예 후보에서 빠진다.
                    int head = capMm - field.HeightMm[ci];
                    if (head <= 0) continue;
                    long scaled = (long)w * head / capMm;
                    w = scaled < 1 ? 1 : (int)scaled;
                }

                _band.Add(ci);
                _weight.Add(w);
                sumW += w;
            }

            if (_band.Count == 0 || sumW == 0) return amountMm;

            long placed = 0;
            for (int i = 0; i < _band.Count; i++)
            {
                int give = (int)(amountMm * _weight[i] / sumW);
                if (give <= 0) continue;
                if (capMm > 0)
                {
                    int head = capMm - field.HeightMm[_band[i]];
                    if (head <= 0) continue;
                    if (give > head) give = head;
                }
                placed += field.AddAt(_band[i], give);
            }

            // 정수 나눗셈의 나머지를 <b>1 mm 씩 돌아가며</b> 놓는다 (최대잉여법).
            //
            // 처음에는 남은 양을 통째로 첫 셀에 부었다. 나머지는 스텝당 최대 밴드 셀 수만큼,
            // 즉 150~300 mm 나 되고, 블레이드가 전진하면 그 셀이 한 칸씩 옮겨가며 스파이크를
            // 줄지어 남긴다 - 더미 면에 보이던 빗살 모양 세로 골이 그것이었다. 나머지는 작지만
            // <b>매 스텝 같은 방향으로 쌓이기 때문에</b> 체계적 편향이 되고, 그 점에서 v7 의
            // 질량 누수와 정확히 같은 종류의 실수다.
            long rest = amountMm - placed;
            while (rest > 0)
            {
                long before = rest;
                for (int i = 0; i < _band.Count && rest > 0; i++)
                {
                    if (capMm > 0 && field.HeightMm[_band[i]] >= capMm) continue;
                    rest -= field.AddAt(_band[i], 1);
                }
                if (rest == before) break;                  // 밴드 전체가 상한이나 천장에 걸렸다
            }

            for (int i = 0; i < _band.Count; i++)
            {
                int ci = _band[i];
                field.WakeChunkOfCell(ci % geo.ResX, ci / geo.ResX);
            }
            return rest;
        }

        /// <summary>
        /// 현재 자세의 블레이드 <b>형상 전체</b>를 relax 의 벽으로. 날개가 있으면 날개도 벽이다 —
        /// 컷과 배리어가 같은 세그먼트를 쓰므로 <b>그려진 실루엣이 그대로 충돌면</b>이다.
        ///
        /// <b>스윕 합집합이 아니라 현재 자세여야 한다</b> — 합집합은 뒤로 끌리고, 그러면 치운 차선이
        /// 영원히 눈을 못 받아서 나중에 둔덕이 무너져 들어오는 경로가 막힌다.
        /// </summary>
        public static SnowRelaxBarrier MakeBarrier(in SnowBladePose now, in SnowBladeShape shape)
        {
            int n = shape.SegmentCount;
            var b = new SnowRelaxBarrier { Active = true, SegmentCount = n };
            b.S0 = shape.Segment(0, now);
            if (n > 1) b.S1 = shape.Segment(1, now);
            if (n > 2) b.S2 = shape.Segment(2, now);
            return b;
        }
    }
}

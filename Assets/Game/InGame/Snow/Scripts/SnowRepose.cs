using System;

namespace PPack
{
    /// <summary>
    /// 안식각 정착. 순수 C# 이고 <c>UnityEngine</c> 을 참조하지 않는다 — 데디 서버에서 그대로 돈다.
    /// AnyTest v7 은 이것을 컴퓨트 커널로 갖고 있었고, 그것이 GPU 를 쓸 수 없는 이 프로젝트에서
    /// 재구현이 필요했던 세 곳 중 하나다.
    ///
    /// <b>격자 전체를 훑지 않는다.</b> 비용이 격자 크기가 아니라 <b>최근에 밀린 면적</b>에 비례해야
    /// 한다는 것은 이 폴더가 이미 신선도 감쇠에서 물린 교훈이다(<c>AGENTS.md</c> 함정 2: 12.5cm 에서
    /// 프레임당 262k 였던 것이 6.25cm 에서 100만이 됐다). 그래서 <b>활동 흔적</b>의 링을 유지하고,
    /// 그 합집합 창 안에서만 돈다.
    ///
    /// <b>창은 무흐름 벽이다.</b> 이 방식이 질량을 보존하는 근거는 짝마다 정확히 반대칭이라는 것뿐이다.
    /// 창 경계를 걸치는 짝을 한쪽에서만 평가하면 유입만 있고 유출이 없어 <b>눈이 만들어진다.</b>
    /// 그래서 이웃이 창 밖이면 그 짝을 <b>통째로</b> 건너뛴다.
    ///
    /// <b>정수 격자에서 반대칭을 얻는 방법이 GPU 판과 다르다.</b> v7 은 셀마다 8이웃을 돌며 양쪽이
    /// 같은 실수식을 독립적으로 평가해 반대칭을 얻었다(Jacobi). 1바이트 정수 격자에서 그렇게 하면
    /// 두 쪽의 반올림이 갈라져 반대칭이 깨진다. 그래서 여기서는 <b>짝을 한 번만 방문</b>하고
    /// (각 셀의 E·NE·N·NW 네 방향만 본다) <b>정수 하나</b>를 한쪽에서 빼 다른 쪽에 더한다.
    /// 그 정수가 양쪽 클램프를 통과하도록 미리 자르므로 <c>ΔField = 0</c> 이 <b>항등</b>이다.
    /// </summary>
    public sealed class SnowRepose
    {
        // 흔적 링의 길이. 2.5초 창에 50Hz 면 125 칸이므로 1024 는 넉넉하다 — 넉넉한 쪽이
        // 오래된 칸을 조용히 덮어써서 창이 갑자기 좁아지는 것보다 낫다.
        private const int TrailCapacity = 1024;

        private readonly int[] _trailMinX = new int[TrailCapacity];
        private readonly int[] _trailMinY = new int[TrailCapacity];
        private readonly int[] _trailMaxX = new int[TrailCapacity];
        private readonly int[] _trailMaxY = new int[TrailCapacity];
        private readonly float[] _trailTime = new float[TrailCapacity];
        private int _trailHead;
        private int _trailCount;

        /// <summary>직전 <see cref="Run"/> 이 실제로 돈 셀 수. CPU 비용의 유일한 정직한 지표다.</summary>
        public int WindowCells { get; private set; }

        /// <summary>직전 <see cref="Run"/> 이 실제로 옮긴 짝의 수. 0 이면 전부 평형이다.</summary>
        public int Flows { get; private set; }

        /// <summary>창이 <c>maxWindowCells</c> 로 잘렸는가. 참이면 정착이 뒤처지고 있다는 뜻이다.</summary>
        public bool WindowClipped { get; private set; }

        public void Clear()
        {
            _trailHead = 0;
            _trailCount = 0;
            WindowCells = 0;
            Flows = 0;
            WindowClipped = false;
        }

        /// <summary>이번 스텝에 건드린 셀 범위를 흔적에 남긴다. 자르기·방출·내려놓기가 각각 부른다.</summary>
        public void Touch(int minX, int minY, int maxX, int maxY, float clockSeconds)
        {
            if (maxX < minX || maxY < minY) return;

            _trailMinX[_trailHead] = minX;
            _trailMinY[_trailHead] = minY;
            _trailMaxX[_trailHead] = maxX;
            _trailMaxY[_trailHead] = maxY;
            _trailTime[_trailHead] = clockSeconds;

            _trailHead = (_trailHead + 1) % TrailCapacity;
            if (_trailCount < TrailCapacity) _trailCount++;
        }

        /// <summary>
        /// 안식각으로 정착시킨다. 짝마다 정수 하나를 옮기고, 그 정수가 양쪽 클램프를 통과하도록
        /// 미리 자르므로 <b>필드 합이 정확히 보존된다.</b>
        ///
        /// <paramref name="guardDeltaCm"/> 는 <b>양쪽 모두 영수증이 있는</b> 짝에만 쓰는 더 급한
        /// 한계다. 이것이 없으면 relax 가 힙의 앞면을 먹는다: 65° 대 55° 안식각의 초과 기울기는
        /// 12.5cm 셀에서 8.9cm 이고, 방출기가 다시 세우는 속도와 같은 속도로 무너져 더미가
        /// 쌓이지 못하고 소반으로 흘러내린다(v7 실측). 판정이 <b>짝에 대해 대칭</b>이라 반대칭은
        /// 그대로다 — 언제 흐르는지만 바뀌고 흐름이 균형인지는 바뀌지 않는다.
        ///
        /// <b>측면은 얼지 않는다.</b> 측면은 안식각으로 방출되므로 어느 한계에서도 옮길 초과가 없다.
        /// 실제로 정착이 필요한 것은 <b>측면 벽·소반·내려놓은 더미</b>이고, 그것들은 영수증이 없어서
        /// 안식각 한계를 받아 정상적으로 무너진다. 가드는 <b>일부러 평형에서 벗어난 두 면</b>만 지킨다.
        /// </summary>
        /// <param name="ratePermille">
        /// 짝마다 초과 기울기의 몇 천분율을 옮길지. v7 의 <c>rate · 0.5</c> 에 대응한다(0.22 → 110).
        /// </param>
        public void Run(SnowField field, SnowPlowLedger ledger,
                        float clockSeconds, float trailSeconds, int padCells, int maxWindowCells,
                        int iterations, int ratePermille,
                        int maxDeltaCm, int maxDeltaDiagCm,
                        int guardDeltaCm, int guardDeltaDiagCm, bool guardEnabled)
        {
            WindowCells = 0;
            Flows = 0;
            WindowClipped = false;

            if (field is null || iterations <= 0) return;
            if (!Window(field, clockSeconds, trailSeconds, padCells,
                        out int x0, out int y0, out int x1, out int y1)) return;

            long cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
            if (maxWindowCells > 0 && cells > maxWindowCells)
            {
                // 창을 중심에서 잘라 낸다. 잘린 밖은 이번 스텝에 정착하지 않고 다음 스텝의
                // 흔적에 그대로 남아 있으므로 잃어버리는 것이 아니라 <b>미뤄지는</b> 것이다.
                WindowClipped = true;

                int side = (int)MathF.Sqrt(maxWindowCells);
                int cx = (x0 + x1) / 2;
                int cy = (y0 + y1) / 2;
                int half = Math.Max(1, side / 2);

                x0 = Math.Max(x0, cx - half);
                x1 = Math.Min(x1, cx + half);
                y0 = Math.Max(y0, cy - half);
                y1 = Math.Min(y1, cy + half);
            }

            WindowCells = (x1 - x0 + 1) * (y1 - y0 + 1);

            bool guard = guardEnabled && ledger is not null && ledger.HasReceipt;
            if (guardDeltaCm < maxDeltaCm) guardDeltaCm = maxDeltaCm;
            if (guardDeltaDiagCm < maxDeltaDiagCm) guardDeltaDiagCm = maxDeltaDiagCm;

            int maxDepth = field.MaxDepthCm;

            for (int it = 0; it < iterations; it++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        // 각 짝을 한 번만 본다 — 이 셀의 E·NE·N·NW 네 방향. 나머지 네 방향은
                        // 상대 셀이 자기 차례에 본다. 그래서 짝마다 정수 하나가 오간다.
                        bool inner = guard && ledger.ReceiptCmAtCell(x, y) > 0;

                        Pair(field, ledger, x, y, x + 1, y, false, inner, guard,
                             maxDeltaCm, maxDeltaDiagCm, guardDeltaCm, guardDeltaDiagCm,
                             ratePermille, maxDepth, x0, y0, x1, y1);
                        Pair(field, ledger, x, y, x + 1, y + 1, true, inner, guard,
                             maxDeltaCm, maxDeltaDiagCm, guardDeltaCm, guardDeltaDiagCm,
                             ratePermille, maxDepth, x0, y0, x1, y1);
                        Pair(field, ledger, x, y, x, y + 1, false, inner, guard,
                             maxDeltaCm, maxDeltaDiagCm, guardDeltaCm, guardDeltaDiagCm,
                             ratePermille, maxDepth, x0, y0, x1, y1);
                        Pair(field, ledger, x, y, x - 1, y + 1, true, inner, guard,
                             maxDeltaCm, maxDeltaDiagCm, guardDeltaCm, guardDeltaDiagCm,
                             ratePermille, maxDepth, x0, y0, x1, y1);
                    }
                }
            }
        }

        /// <summary>
        /// 짝 하나. 높은 쪽에서 <c>move</c> 를 빼 낮은 쪽에 더한다.
        ///
        /// <b>1cm 사역대</b>가 양자의 대가다. <c>move</c> 는 정수라 초과가 2cm 미만이면 흐르지
        /// 않는다 — 12.5cm 셀에서 그것은 안식각 위로 4.6° 의 여유다. 대신 초과가 2cm 이상이면
        /// 반드시 1 이상 흐르므로(반올림이 0 을 주면 1 로 올린다) 사역대가 <b>비율에 비례해
        /// 커지지 않는다.</b> 그냥 잘라 버리면 기본 rate 에서 사역대가 10cm(38°)가 된다.
        ///
        /// <c>move ≤ excess/2</c> 라 새 차이가 <c>diff - 2·move ≥ limit ≥ 0</c> 이다. 부호가 절대
        /// 뒤집히지 않으므로 <b>진동이 구조적으로 불가능</b>하다.
        /// </summary>
        private void Pair(SnowField field, SnowPlowLedger ledger, int ax, int ay, int bx, int by,
                          bool diagonal, bool innerA, bool guard,
                          int maxDeltaCm, int maxDeltaDiagCm, int guardDeltaCm, int guardDeltaDiagCm,
                          int ratePermille, int maxDepthCm,
                          int x0, int y0, int x1, int y1)
        {
            // 무흐름 벽. 창 밖 이웃은 짝을 통째로 건너뛴다.
            if (bx < x0 || bx > x1 || by < y0 || by > y1) return;

            int ha = field.DepthCmAtCell(ax, ay);
            int hb = field.DepthCmAtCell(bx, by);

            int diff = ha - hb;
            int abs = diff >= 0 ? diff : -diff;

            bool both = innerA && guard && ledger.ReceiptCmAtCell(bx, by) > 0;
            int limit = both
                ? (diagonal ? guardDeltaDiagCm : guardDeltaCm)
                : (diagonal ? maxDeltaDiagCm : maxDeltaCm);

            int excess = abs - limit;
            if (excess < 2) return;

            int move = (excess * ratePermille + 500) / 1000;
            if (move < 1) move = 1;
            if (move * 2 > excess) move = excess / 2;

            int hiX, hiY, loX, loY, hiH, loH;
            if (diff > 0) { hiX = ax; hiY = ay; loX = bx; loY = by; hiH = ha; loH = hb; }
            else { hiX = bx; hiY = by; loX = ax; loY = ay; hiH = hb; loH = ha; }

            // 양쪽 클램프를 미리 통과시킨다. 그래야 두 ApplyCellDelta 가 <b>같은 정수</b>를 쓰고
            // 필드 합이 정확히 0 만큼 바뀐다. 이 순서를 뒤집으면 클램프가 한쪽만 물어 눈이
            // 만들어지거나 사라진다.
            if (move > hiH) move = hiH;
            int headroom = maxDepthCm - loH;
            if (move > headroom) move = headroom;
            if (move <= 0) return;

            field.ApplyCellDelta(hiX, hiY, -move);
            field.ApplyCellDelta(loX, loY, move);
            Flows++;
        }

        /// <summary>
        /// 흔적 링에서 <paramref name="trailSeconds"/> 안의 칸들을 합집합한다. 링이 시간 순서라
        /// 뒤에서부터 걸어 첫 만료 칸에서 멈춘다.
        /// </summary>
        private bool Window(SnowField field, float clockSeconds, float trailSeconds, int padCells,
                            out int x0, out int y0, out int x1, out int y1)
        {
            x0 = y0 = x1 = y1 = 0;
            if (_trailCount == 0) return false;

            float cutoff = clockSeconds - trailSeconds;
            bool any = false;

            for (int i = 0; i < _trailCount; i++)
            {
                int idx = (_trailHead - 1 - i + TrailCapacity * 2) % TrailCapacity;
                if (_trailTime[idx] < cutoff) break;

                if (!any)
                {
                    x0 = _trailMinX[idx]; y0 = _trailMinY[idx];
                    x1 = _trailMaxX[idx]; y1 = _trailMaxY[idx];
                    any = true;
                    continue;
                }

                if (_trailMinX[idx] < x0) x0 = _trailMinX[idx];
                if (_trailMinY[idx] < y0) y0 = _trailMinY[idx];
                if (_trailMaxX[idx] > x1) x1 = _trailMaxX[idx];
                if (_trailMaxY[idx] > y1) y1 = _trailMaxY[idx];
            }

            if (!any) return false;

            x0 -= padCells; y0 -= padCells;
            x1 += padCells; y1 += padCells;

            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 >= field.Width) x1 = field.Width - 1;
            if (y1 >= field.Height) y1 = field.Height - 1;

            return x1 >= x0 && y1 >= y0;
        }
    }
}

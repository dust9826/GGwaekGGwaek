using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// 정수만으로 계산하는 스윕. <b>명령 복제의 급소가 여기다</b> —
    /// `docs/specs/2026-08-21-snow-quadtree-commands.md` 3절.
    ///
    /// <para>명령(원인)을 복제하려면 같은 명령이 <b>모든 피어에서 같은 셀 집합</b>을 골라야 한다.
    /// 현행 <see cref="SnowBladeSweep.Cut"/> 는 float 기하로 셀을 고르므로, Mac 클라이언트와 Linux
    /// 서버가 경계 셀에서 갈릴 수 있다. 갈리면 그 자리가 영구히 어긋나고(정정 채널이 덮지만 그러면
    /// 명령의 이점이 사라진다) 설계가 무너진다.</para>
    ///
    /// <para><b>왜 되는가:</b> 담기 판정은 OBB 투영 두 개다 — 점을 오른쪽·전방 축에 투영해서 반폭·
    /// 반깊이와 비교. 나눗셈도 초월함수도 없다. 좌표를 mm 정수로, 축을 Q15 고정소수로 두면
    /// <c>long</c> 곱셈과 비교만 남는다.</para>
    ///
    /// <para>초월함수는 둘뿐이고 둘 다 피할 수 있다:
    /// <list type="bullet">
    /// <item>자세 정규화의 <c>sqrt</c> → <see cref="Isqrt"/> (정수, 결정적)</item>
    /// <item>날개 각도의 <c>cos/sin</c> → 형상마다 상수다. 명령에 고정소수로 실어 보낸다</item>
    /// </list></para>
    ///
    /// <para>이완(relax)은 여기 없다 — 각 피어가 스스로 돌리고 갈리는 것을 이미 허용한다.
    /// 명령이 정해야 하는 것은 <b>절삭이 닿는 셀 집합</b>뿐이다.</para>
    /// </summary>
    public static class SnowSweepInt
    {
        /// <summary>
        /// 고정소수 한 칸. 축 성분은 −ONE..ONE 이다.
        ///
        /// <para><b>Q14 인 이유는 와이어다.</b> 처음 Q15(32768)로 두었더니 명령이 축을
        /// <c>short</c> 로 싣지 못했다 — <c>short</c> 최대가 32767 이라 단위벡터 하나가 그대로
        /// 넘친다(컴파일 에러로 걸렸다). Q14 면 16384 라 부호 있는 16 비트에 여유가 남고,
        /// 정밀도는 1/16384 ≈ 0.006% 로 셀 크기(125 mm) 앞에서 무의미하다.</para>
        /// </summary>
        public const int One = 1 << 14;

        /// <summary>셀 한 변(mm). <see cref="SnowFieldGeometry.CellSizeMm"/> 와 같아야 한다.</summary>
        public const int CellMm = SnowFieldGeometry.CellSizeMm;

        /// <summary>한 자세. 중심은 mm 정수, 전방은 Q15 단위벡터다.</summary>
        public struct PoseI
        {
            public int CenterXMm;
            public int CenterZMm;
            public int FwdX;
            public int FwdZ;

            /// <summary>오른쪽은 전방에서 파생한다 — <see cref="SnowBladePose"/> 와 같은 규약.</summary>
            public int RightX => FwdZ;
            public int RightZ => -FwdX;
        }

        /// <summary>가운데 구간만. 날개는 이 스파이크의 범위 밖이다(명령에 세그먼트를 여럿 실으면 된다).</summary>
        public struct ShapeI
        {
            public int HalfWidthMm;
            public int HalfDepthMm;
        }

        /// <summary>
        /// 정수 제곱근(내림). <c>System.Math.Sqrt</c> 를 쓰지 않는 이유는 <b>결정성</b>이다 —
        /// 이 함수는 어느 플랫폼에서도 같은 값을 준다.
        /// </summary>
        public static long Isqrt(long v)
        {
            if (v <= 0) return 0;

            long x = v;
            long y = (x + 1) / 2;
            while (y < x)
            {
                x = y;
                y = (x + v / x) / 2;
            }
            return x;
        }

        /// <summary>Q15 단위벡터로 정규화한다. 길이가 0 이면 +X 를 준다.</summary>
        public static void Normalize(int x, int z, out int nx, out int nz)
        {
            long len = Isqrt((long)x * x + (long)z * z);
            if (len == 0) { nx = One; nz = 0; return; }

            nx = (int)((long)x * One / len);
            nz = (int)((long)z * One / len);
        }

        /// <summary>
        /// 점이 이 자세의 OBB 안인가. <b>정수 전용</b> — 나눗셈도 초월함수도 없다.
        ///
        /// <para>축이 Q15 이므로 투영값도 Q15 배 스케일이다. 그래서 반폭도 같은 배로 올려서 비교한다.</para>
        /// </summary>
        public static bool Contains(in PoseI pose, in ShapeI shape, int pxMm, int pzMm)
        {
            long dx = pxMm - pose.CenterXMm;
            long dz = pzMm - pose.CenterZMm;

            long across = dx * pose.RightX + dz * pose.RightZ;   // Q15 mm
            if (across < 0) across = -across;
            if (across > (long)shape.HalfWidthMm * One) return false;

            long along = dx * pose.FwdX + dz * pose.FwdZ;
            if (along < 0) along = -along;
            return along <= (long)shape.HalfDepthMm * One;
        }

        /// <summary>
        /// 두 자세 사이를 <paramref name="t"/> (0..One) 로 보간한다. 전방은 다시 정규화한다.
        /// </summary>
        public static PoseI Lerp(in PoseI a, in PoseI b, int t)
        {
            int fx = a.FwdX + (int)(((long)(b.FwdX - a.FwdX) * t) / One);
            int fz = a.FwdZ + (int)(((long)(b.FwdZ - a.FwdZ) * t) / One);
            Normalize(fx, fz, out int nx, out int nz);

            return new PoseI
            {
                CenterXMm = a.CenterXMm + (int)(((long)(b.CenterXMm - a.CenterXMm) * t) / One),
                CenterZMm = a.CenterZMm + (int)(((long)(b.CenterZMm - a.CenterZMm) * t) / One),
                FwdX = nx,
                FwdZ = nz,
            };
        }

        /// <summary>
        /// 스윕이 닿는 셀을 모은다. <b>필드를 건드리지 않는다</b> — 이 스파이크가 재는 것은
        /// "같은 명령이 같은 셀 집합을 주는가" 이고, 높이를 바꾸는 것은 그 다음 문제다.
        /// </summary>
        /// <param name="cells">셀 색인, <b>오름차순</b>. 순서가 고정이어야 피어 간 비교가 된다.</param>
        public static void CollectCells(SnowFieldGeometry geo, in PoseI prev, in PoseI now,
                                        in ShapeI shape, int segments, List<int> cells)
        {
            cells.Clear();
            if (segments < 1) segments = 1;

            // 스윕 AABB. reach 는 정수 제곱근으로 구한다.
            long reachMm = Isqrt((long)shape.HalfWidthMm * shape.HalfWidthMm
                                 + (long)shape.HalfDepthMm * shape.HalfDepthMm) + CellMm;

            int minX = (prev.CenterXMm < now.CenterXMm ? prev.CenterXMm : now.CenterXMm) - (int)reachMm;
            int maxX = (prev.CenterXMm > now.CenterXMm ? prev.CenterXMm : now.CenterXMm) + (int)reachMm;
            int minZ = (prev.CenterZMm < now.CenterZMm ? prev.CenterZMm : now.CenterZMm) - (int)reachMm;
            int maxZ = (prev.CenterZMm > now.CenterZMm ? prev.CenterZMm : now.CenterZMm) + (int)reachMm;

            int originXMm = (int)System.Math.Round(geo.OriginXM * 1000.0);
            int originZMm = (int)System.Math.Round(geo.OriginZM * 1000.0);

            int cx0 = FloorDiv(minX - originXMm, CellMm);
            int cx1 = FloorDiv(maxX - originXMm, CellMm);
            int cz0 = FloorDiv(minZ - originZMm, CellMm);
            int cz1 = FloorDiv(maxZ - originZMm, CellMm);

            if (cx0 < 0) cx0 = 0;
            if (cz0 < 0) cz0 = 0;
            if (cx1 >= geo.ResX) cx1 = geo.ResX - 1;
            if (cz1 >= geo.ResZ) cz1 = geo.ResZ - 1;
            if (cx0 > cx1 || cz0 > cz1) return;

            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                // 셀 중심. 반 칸이 62.5 mm 라 정수로 안 떨어지므로 <b>두 배 격자</b>에서 센다.
                int px2 = originXMm * 2 + cx * CellMm * 2 + CellMm;
                int pz2 = originZMm * 2 + cz * CellMm * 2 + CellMm;

                bool hit = false;
                for (int s = 0; s < segments && !hit; s++)
                {
                    int t = segments == 1 ? One : (int)((long)s * One / (segments - 1));
                    PoseI pose = Lerp(prev, now, t);

                    // 두 배 격자에 맞춰 자세도 두 배로 올린다 - 스케일이 양쪽에 같으면 판정은 같다.
                    var pose2 = new PoseI
                    {
                        CenterXMm = pose.CenterXMm * 2,
                        CenterZMm = pose.CenterZMm * 2,
                        FwdX = pose.FwdX,
                        FwdZ = pose.FwdZ,
                    };
                    var shape2 = new ShapeI
                    {
                        HalfWidthMm = shape.HalfWidthMm * 2,
                        HalfDepthMm = shape.HalfDepthMm * 2,
                    };

                    hit = Contains(pose2, shape2, px2, pz2);
                }

                if (hit) cells.Add(geo.CellIndex(cx, cz));
            }
        }

        /// <summary>바닥 방향 나눗셈. C# 의 / 는 0 쪽으로 자르므로 음수에서 한 줄이 어긋난다.</summary>
        public static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }
}

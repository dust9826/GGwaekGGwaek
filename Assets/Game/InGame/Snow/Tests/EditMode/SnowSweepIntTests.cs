using System.Collections.Generic;
using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// 명령 복제의 급소 실험 — <b>정수 스윕이 float 스윕과 같은 셀을 고르는가.</b>
    /// `docs/specs/2026-08-21-snow-quadtree-commands.md` 3·5절.
    ///
    /// <para>여기서 재는 것은 "완전히 같다" 가 아니다. 경계 셀은 반올림 때문에 갈릴 수 있고, 그
    /// 정도는 허용된다 — 중요한 것은 <b>정수 쪽이 스스로 항상 같은 답을 준다</b>(결정성)는 것과,
    /// float 쪽과의 차이가 <b>경계 한 줄</b>에 머문다는 것이다. 차이가 면적으로 벌어지면 두 경로가
    /// 다른 모양을 깎는다는 뜻이므로 이 방향을 접어야 한다.</para>
    /// </summary>
    public sealed class SnowSweepIntTests
    {
        private static SnowFieldGeometry Geo() => new SnowFieldGeometry(24f, 24f, 0f, 0f);

        private static SnowSweepInt.PoseI PoseI(float xM, float zM, float fx, float fz)
        {
            SnowSweepInt.Normalize((int)(fx * SnowSweepInt.One), (int)(fz * SnowSweepInt.One),
                                   out int nx, out int nz);
            return new SnowSweepInt.PoseI
            {
                CenterXMm = (int)System.Math.Round(xM * 1000.0),
                CenterZMm = (int)System.Math.Round(zM * 1000.0),
                FwdX = nx,
                FwdZ = nz,
            };
        }

        private static SnowBladePose PoseF(float xM, float zM, float fx, float fz)
        {
            float len = (float)System.Math.Sqrt(fx * fx + fz * fz);
            return new SnowBladePose { CenterX = xM, CenterZ = zM, ForwardX = fx / len, ForwardZ = fz / len };
        }

        /// <summary>float 경로가 실제로 건드린 셀. 필드를 깎아 보고 바뀐 셀을 읽는다.</summary>
        private static List<int> FloatCells(SnowFieldGeometry geo,
                                            in SnowBladePose prev, in SnowBladePose now,
                                            in SnowBladeShape shape, int segments)
        {
            var field = new SnowHeightFieldCpu(geo, 300);
            field.BeginStep();
            SnowBladeSweep.Cut(field, prev, now, shape, segments, 0);

            var cells = new List<int>(field.ChangedCells);
            cells.Sort();
            return cells;
        }

        [Test]
        public void Isqrt_IsExactOnPerfectSquaresAndFloorsOtherwise()
        {
            Assert.AreEqual(0, SnowSweepInt.Isqrt(0));
            Assert.AreEqual(1, SnowSweepInt.Isqrt(1));
            Assert.AreEqual(3, SnowSweepInt.Isqrt(9));
            Assert.AreEqual(3, SnowSweepInt.Isqrt(15));
            Assert.AreEqual(1000, SnowSweepInt.Isqrt(1_000_000));
            Assert.AreEqual(46340, SnowSweepInt.Isqrt(2_147_395_600));
        }

        [Test]
        public void Normalize_KeepsUnitLengthWithinOneQuantum()
        {
            foreach (var (x, z) in new[] { (1, 0), (0, 1), (1, 1), (-3, 7), (100, -250), (-9999, -1) })
            {
                SnowSweepInt.Normalize(x * 1000, z * 1000, out int nx, out int nz);
                long len2 = (long)nx * nx + (long)nz * nz;
                long one2 = (long)SnowSweepInt.One * SnowSweepInt.One;

                // Q15 반올림 오차라 1/32768 안쪽이어야 한다.
                double ratio = (double)len2 / one2;
                Assert.That(ratio, Is.EqualTo(1.0).Within(0.001), $"({x},{z}) 정규화 길이 {ratio}");
            }
        }

        /// <summary>
        /// <b>결정성.</b> 같은 입력을 여러 번 돌려 같은 답이 나오는가. float 과 달리 이것이
        /// 플랫폼을 넘어서도 성립한다는 것이 이 경로를 쓰는 이유다.
        /// </summary>
        [Test]
        public void CollectCells_IsRepeatable()
        {
            var geo = Geo();
            var shape = new SnowSweepInt.ShapeI { HalfWidthMm = 1150, HalfDepthMm = 175 };
            var a = new List<int>();
            var b = new List<int>();

            SnowSweepInt.CollectCells(geo, PoseI(4f, 12f, 1f, 0f), PoseI(9f, 12.4f, 1f, 0.1f), shape, 8, a);
            SnowSweepInt.CollectCells(geo, PoseI(4f, 12f, 1f, 0f), PoseI(9f, 12.4f, 1f, 0.1f), shape, 8, b);

            Assert.Greater(a.Count, 0, "닿은 셀이 있어야 한다");
            Assert.AreEqual(a, b);
        }

        /// <summary>
        /// <b>급소.</b> 정수와 float 이 고르는 셀이 얼마나 다른가. 차이가 경계 한 줄에 머물러야 한다.
        ///
        /// <para>판정 기준: 대칭차가 합집합의 <b>10% 이하</b>. 블레이드 폭 2.3 m 스윕에서 경계 셀은
        /// 둘레에 한 줄이고 면적 대비 그 정도다. 넘으면 두 경로가 다른 모양을 깎는다는 뜻이다.</para>
        /// </summary>
        [Test]
        public void CollectCells_MatchesFloatSweepExceptOnTheBoundary()
        {
            var geo = Geo();
            var shapeF = new SnowBladeShape
            {
                HalfWidthM = 1.15f,
                HalfDepthM = 0.175f,
                Profile = SnowBladeProfileKind.Straight,
                WingLengthM = 0f,
            };
            var shapeI = new SnowSweepInt.ShapeI { HalfWidthMm = 1150, HalfDepthMm = 175 };

            var cases = new (float px, float pz, float nx, float nz, float fx, float fz)[]
            {
                (4f, 12f, 9f, 12f, 1f, 0f),
                (4f, 4f, 4f, 10f, 0f, 1f),
                (6f, 6f, 11f, 11f, 1f, 1f),
                (12f, 3f, 7f, 8f, -1f, 1f),
            };

            foreach (var c in cases)
            {
                var fCells = FloatCells(geo, PoseF(c.px, c.pz, c.fx, c.fz), PoseF(c.nx, c.nz, c.fx, c.fz),
                                        shapeF, 8);

                var iCells = new List<int>();
                SnowSweepInt.CollectCells(geo, PoseI(c.px, c.pz, c.fx, c.fz), PoseI(c.nx, c.nz, c.fx, c.fz),
                                          shapeI, 8, iCells);

                var setF = new HashSet<int>(fCells);
                var setI = new HashSet<int>(iCells);

                var union = new HashSet<int>(setF);
                union.UnionWith(setI);

                var diff = new HashSet<int>(setF);
                diff.SymmetricExceptWith(setI);

                double ratio = union.Count == 0 ? 0.0 : (double)diff.Count / union.Count;

                TestContext.WriteLine($"[정수스윕] ({c.px},{c.pz})->({c.nx},{c.nz}) " +
                                      $"float={setF.Count} int={setI.Count} 차이={diff.Count} " +
                                      $"비율={ratio:P1}");

                Assert.Greater(setF.Count, 0, "float 경로가 아무것도 안 깎았다 - 케이스가 틀렸다");
                Assert.That(ratio, Is.LessThanOrEqualTo(0.10),
                            $"정수와 float 이 고르는 셀이 경계를 넘어 갈린다 - {ratio:P1}");
            }
        }
    }
}

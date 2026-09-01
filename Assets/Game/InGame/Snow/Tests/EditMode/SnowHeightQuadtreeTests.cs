using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// 실험 2 — <b>쿼드트리 스냅샷이 실제로 접히는가.</b>
    /// `docs/specs/2026-08-21-snow-quadtree-commands.md` 5절 2번.
    ///
    /// <para>늦게 참가하는 피어에게 필드를 밀어 보내는 비용이 이 스파이크의 두 번째 정당화다.
    /// 64 m 맵이 262,144 셀 = 512 KB 이고 512 m 맵이면 32 MB 다. 쿼드트리가 접히지 않으면 그
    /// 정당화가 사라진다.</para>
    ///
    /// <para>세 국면을 만든다: 처녀설 / 몇 줄 밀린 상태 / 많이 밀린 상태. 실제 플레이 시간을
    /// 흉내내는 것이 아니라 <b>깎인 면적의 비율</b>을 바꾸는 것이다 — 접힘을 정하는 것은 시간이
    /// 아니라 균일한 영역의 크기다.</para>
    /// </summary>
    public sealed class SnowHeightQuadtreeTests
    {
        private static SnowFieldGeometry Geo() => new SnowFieldGeometry(64f, 64f, 0f, 0f);

        private static SnowHeightFieldCpu Field(int depthMm = 300) => new SnowHeightFieldCpu(Geo(), depthMm);

        private static (int bytes, int leaves, int internals) Snapshot(SnowHeightFieldCpu field)
        {
            var tree = new SnowHeightQuadtree(field.Geo);
            byte[] bytes = tree.Serialize(field);
            return (bytes.Length, tree.LeafCount, tree.InternalCount);
        }

        /// <summary>처녀설은 루트 하나다. 접힘의 상한이 여기 있다.</summary>
        [Test]
        public void VirginSnow_FoldsToASingleLeaf()
        {
            var field = Field();
            var tree = new SnowHeightQuadtree(field.Geo);
            byte[] bytes = tree.Serialize(field);

            Assert.AreEqual(1, tree.LeafCount, "균일한 필드는 잎 하나여야 한다");
            Assert.AreEqual(0, tree.InternalCount);
            Assert.AreEqual(3, bytes.Length, "태그 1 비트 + 높이 2 바이트");

            TestContext.WriteLine($"[스냅샷] 처녀설 {bytes.Length} B / 평평 {tree.FlatBytes:N0} B " +
                                  $"= {(double)tree.FlatBytes / bytes.Length:N0} 배 압축");
        }

        /// <summary>
        /// <b>본론.</b> 밀린 면적이 늘어날 때 스냅샷이 어떻게 커지는가.
        ///
        /// <para>판정은 "몇 배" 가 아니라 <b>많이 밀린 상태에서도 평평한 배열보다 작은가</b> 다.
        /// 그것이 아니면 늦게 참가 비용이 나아지지 않는다.</para>
        /// </summary>
        [Test]
        public void PlowedField_StaysSmallerThanTheFlatArray()
        {
            var shape = new SnowBladeShape
            {
                HalfWidthM = 1.15f,
                HalfDepthM = 0.175f,
                Profile = SnowBladeProfileKind.Straight,
                WingLengthM = 0f,
            };

            var geo = Geo();
            int flat = geo.CellCount * 2;

            // 줄 수를 늘려 가며 깎인 면적을 키운다.
            foreach (int lanes in new[] { 1, 4, 16, 48 })
            {
                var field = Field();
                for (int i = 0; i < lanes; i++)
                {
                    float z = 1.5f + i * (62f / lanes);
                    var prev = new SnowBladePose { CenterX = 1f,  CenterZ = z, ForwardX = 1f, ForwardZ = 0f };
                    var now  = new SnowBladePose { CenterX = 63f, CenterZ = z, ForwardX = 1f, ForwardZ = 0f };
                    SnowBladeSweep.Cut(field, prev, now, shape, 8, 0);
                }

                var (bytes, leaves, internals) = Snapshot(field);
                double ratio = (double)flat / bytes;

                TestContext.WriteLine($"[스냅샷] {lanes,2} 줄 · {bytes,9:N0} B · 잎 {leaves,8:N0} · " +
                                      $"내부 {internals,8:N0} · 평평 대비 {ratio,7:N1} 배");

                Assert.Less(bytes, flat,
                    $"{lanes} 줄에서 쿼드트리가 평평한 배열보다 크다 - 접힘이 값을 못 한다");
            }

            TestContext.WriteLine($"[스냅샷] 평평한 배열 = {flat:N0} B ({geo.CellCount:N0} 셀)");
        }

        /// <summary>
        /// 완전히 깎인 필드도 하나로 접힌다 — 처녀설과 대칭이다. 청소가 끝난 판이 가장 싸다는
        /// 뜻이고, 그것이 이 표현이 게임과 맞는 이유다.
        /// </summary>
        [Test]
        public void FullyClearedField_FoldsBackToOneLeaf()
        {
            var field = Field();
            for (int i = 0; i < field.Geo.CellCount; i++) field.AddAt(i, -300);

            var tree = new SnowHeightQuadtree(field.Geo);
            byte[] bytes = tree.Serialize(field);

            Assert.AreEqual(1, tree.LeafCount);
            TestContext.WriteLine($"[스냅샷] 전부 깎임 {bytes.Length} B");
        }
    }
}

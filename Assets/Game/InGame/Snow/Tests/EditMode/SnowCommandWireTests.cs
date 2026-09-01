using System.Collections.Generic;
using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// 실험 3 — <b>명령 와이어의 대역폭과 수렴.</b>
    /// `docs/specs/2026-08-21-snow-quadtree-commands.md` 5절 3번.
    ///
    /// <para>두 가지를 잰다. 하나는 <b>바이트</b> — 같은 밀기를 명령으로 보낼 때와 셀 델타로 보낼
    /// 때의 비. 다른 하나는 <b>수렴</b> — 명령만 받은 피어의 필드가 권위와 같아지는가. 둘 중
    /// 하나만 좋아도 의미가 없다: 대역폭이 줄어도 갈라지면 못 쓰고, 같아져도 안 줄면 쓸 이유가 없다.</para>
    /// </summary>
    public sealed class SnowCommandWireTests
    {
        /// <summary>현행 와이어의 셀 하나 비용. <c>(색인 4 B, 높이 2 B)</c> — 폴더 문서 기준.</summary>
        private const int CellDeltaBytes = 6;

        private static SnowFieldGeometry Geo() => new SnowFieldGeometry(64f, 64f, 0f, 0f);

        [Test]
        public void Wire_RoundTripsExactly()
        {
            var sent = new List<SnowCommand>
            {
                new SnowCommand
                {
                    Tick = 12345, Kind = ESnowCommandKind.BladeCut, Actor = 7,
                    PrevXMm = -4000, PrevZMm = 12000, NowXMm = 9000, NowZMm = 12400,
                    FwdX = 32767, FwdZ = -1234, Param = 1150,
                },
                new SnowCommand
                {
                    Tick = 12346, Kind = ESnowCommandKind.BallBurst, Actor = 65535,
                    PrevXMm = 1, PrevZMm = -1, NowXMm = 0, NowZMm = 0,
                    FwdX = -32768, FwdZ = 0, Param = -7,
                },
            };

            byte[] bytes = SnowCommandWire.Write(sent);
            Assert.AreEqual(sent.Count * SnowCommandWire.Stride, bytes.Length);

            var got = new List<SnowCommand>();
            SnowCommandWire.Read(bytes, got);

            Assert.AreEqual(sent.Count, got.Count);
            for (int i = 0; i < sent.Count; i++)
            {
                Assert.AreEqual(sent[i].Tick, got[i].Tick);
                Assert.AreEqual(sent[i].Kind, got[i].Kind);
                Assert.AreEqual(sent[i].Actor, got[i].Actor);
                Assert.AreEqual(sent[i].PrevXMm, got[i].PrevXMm);
                Assert.AreEqual(sent[i].PrevZMm, got[i].PrevZMm);
                Assert.AreEqual(sent[i].NowXMm, got[i].NowXMm);
                Assert.AreEqual(sent[i].NowZMm, got[i].NowZMm);
                Assert.AreEqual(sent[i].FwdX, got[i].FwdX);
                Assert.AreEqual(sent[i].FwdZ, got[i].FwdZ);
                Assert.AreEqual(sent[i].Param, got[i].Param);
            }
        }

        /// <summary>
        /// <b>본론.</b> 차량 하나가 한 줄을 미는 동안, 명령 바이트와 셀 델타 바이트를 나란히 센다.
        ///
        /// <para>그리고 명령만 받은 두 번째 필드가 권위와 <b>셀 단위로 같아지는지</b> 확인한다 —
        /// 이완은 양쪽 다 돌리지 않는다(설계상 각 피어가 알아서 하는 것이고, 그래서 비교에서 뺀다).</para>
        /// </summary>
        [Test]
        public void Commands_AreFarSmallerAndStillConverge()
        {
            var geo = Geo();
            var authority = new SnowHeightFieldCpu(geo, 300);
            var replica = new SnowHeightFieldCpu(geo, 300);

            const int halfWidthMm = 1150;
            var shapeI = new SnowSweepInt.ShapeI { HalfWidthMm = halfWidthMm, HalfDepthMm = 175 };

            var commands = new List<SnowCommand>();
            var scratch = new List<int>();
            long cellDeltaCells = 0;

            // 62 m 를 0.25 m 씩 민다 - 60 Hz 에 4 m/s 로 달리는 차량의 한 틱 이동과 비슷하다.
            int steps = 248;
            for (int i = 0; i < steps; i++)
            {
                int prevXMm = 1000 + i * 250;
                int nowXMm = prevXMm + 250;

                var prev = new SnowSweepInt.PoseI { CenterXMm = prevXMm, CenterZMm = 12000, FwdX = SnowSweepInt.One, FwdZ = 0 };
                var now  = new SnowSweepInt.PoseI { CenterXMm = nowXMm,  CenterZMm = 12000, FwdX = SnowSweepInt.One, FwdZ = 0 };

                // 권위: 자기 격자에 적용하고, 현행 방식이라면 보낼 셀 수를 센다.
                SnowSweepInt.CollectCells(geo, prev, now, shapeI, 8, scratch);
                int changed = 0;
                foreach (int ci in scratch)
                {
                    int h = authority.HeightMm[ci];
                    if (h <= 0) continue;
                    authority.AddAt(ci, -h);
                    changed++;
                }
                cellDeltaCells += changed;

                commands.Add(new SnowCommand
                {
                    Tick = (uint)i,
                    Kind = ESnowCommandKind.BladeCut,
                    Actor = 1,
                    PrevXMm = prevXMm, PrevZMm = 12000,
                    NowXMm = nowXMm, NowZMm = 12000,
                    FwdX = (short)SnowSweepInt.One, FwdZ = 0,
                    Param = halfWidthMm,
                });
            }

            byte[] wire = SnowCommandWire.Write(commands);
            long cellDeltaBytes = cellDeltaCells * CellDeltaBytes;

            // 복제: 명령만 받아 적용한다.
            var got = new List<SnowCommand>();
            SnowCommandWire.Read(wire, got);
            foreach (SnowCommand c in got) SnowCommandWire.Apply(replica, c, scratch);

            int mismatched = 0;
            for (int ci = 0; ci < geo.CellCount; ci++)
                if (authority.HeightMm[ci] != replica.HeightMm[ci]) mismatched++;

            double ratio = (double)cellDeltaBytes / wire.Length;

            TestContext.WriteLine($"[명령] 스텝 {steps} · 명령 {wire.Length:N0} B " +
                                  $"({SnowCommandWire.Stride} B x {commands.Count}) · " +
                                  $"셀 델타 {cellDeltaBytes:N0} B (셀 {cellDeltaCells:N0}) · " +
                                  $"{ratio:N1} 배 적다");
            TestContext.WriteLine($"[명령] 어긋난 셀 {mismatched} / {geo.CellCount:N0} · " +
                                  $"권위 총량 {authority.TotalHeightMm:N0} · 복제 {replica.TotalHeightMm:N0}");

            Assert.AreEqual(0, mismatched, "명령만 받은 필드가 권위와 다르다 - 수렴하지 않는다");
            Assert.AreEqual(authority.TotalHeightMm, replica.TotalHeightMm);
            Assert.Greater(ratio, 3.0, "명령이 셀 델타보다 3 배 이상 작아야 쓸 이유가 있다");
        }
    }
}

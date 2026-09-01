using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 바닥(경사·눈 가능 마스크)이 들어온 뒤에도 <b>깊이만 있던 시절의 계약이 그대로인지</b>와,
    /// 새로 생긴 계약 둘 — 마스크가 꺼진 셀은 용량 0 이라는 것과, 이완이 지형 낙차를 흐름으로
    /// 오해하지 않는다는 것 — 을 검사한다.
    /// </summary>
    public sealed class SnowGroundFieldTests
    {
        private static SnowRelaxBarrier NoBarrier => default;
        private static SnowMaterialCpu Mat => SnowMaterialCpu.Default;

        private static List<int> AllChunks(SnowFieldGeometry g)
        {
            var l = new List<int>(g.ChunkCount);
            for (int i = 0; i < g.ChunkCount; i++) l.Add(i);
            return l;
        }

        private static SnowFieldGeometry Geo(float sizeM, float originYM = 0f)
            => new SnowFieldGeometry(sizeM, sizeM, 0f, 0f, originYM);

        /// <summary>x 방향으로 셀당 <paramref name="stepMm"/> 씩 올라가는 바닥. 전 셀 눈 가능.</summary>
        private static SnowGroundFieldCpu Ramp(SnowFieldGeometry geo, int stepMm)
        {
            var floor = new ushort[geo.CellCount];
            var mask = new byte[geo.CellCount];
            for (int cz = 0; cz < geo.ResZ; cz++)
            for (int cx = 0; cx < geo.ResX; cx++)
            {
                int i = geo.CellIndex(cx, cz);
                floor[i] = (ushort)(cx * stepMm);
                mask[i] = SnowGroundFieldCpu.SnowableValue;
            }

            return new SnowGroundFieldCpu(geo, floor, mask);
        }

        [Test]
        public void Flat_MatchesTheNoGroundBehaviourExactly()
        {
            var geo = Geo(4f);
            var withoutGround = new SnowHeightFieldCpu(geo, 600);
            var withFlat = new SnowHeightFieldCpu(Geo(4f), 600, SnowGroundFieldCpu.Flat(Geo(4f)));

            Assert.AreEqual(withoutGround.TotalHeightMm, withFlat.TotalHeightMm,
                            "평지 바닥은 바닥이 없는 것과 같은 총량이어야 한다");
            Assert.AreEqual((long)geo.CellCount * 600, withFlat.TotalHeightMm);
        }

        [Test]
        public void InitialDepth_SkipsCellsWhereSnowIsImpossible()
        {
            var geo = Geo(4f);
            var mask = new byte[geo.CellCount];
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = i % 2 == 0 ? SnowGroundFieldCpu.SnowableValue : (byte)0;
            }

            var ground = new SnowGroundFieldCpu(geo, new ushort[geo.CellCount], mask);
            var field = new SnowHeightFieldCpu(geo, 600, ground);

            Assert.AreEqual((long)ground.SnowableCells * 600, field.TotalHeightMm,
                            "눈이 불가능한 셀에는 초기 적설이 실리지 않아야 한다");
            Assert.AreEqual(field.TotalHeightMm, field.RecomputeTotalHeightMm(),
                            "증분 원장이 실제 배열과 같아야 한다");
        }

        [Test]
        public void AddAt_RefusesSnowOnCellsWhereSnowIsImpossible()
        {
            var geo = Geo(4f);
            var mask = new byte[geo.CellCount];
            for (int i = 0; i < mask.Length; i++) mask[i] = SnowGroundFieldCpu.SnowableValue;
            int blocked = geo.CellIndex(8, 8);
            mask[blocked] = 0;

            var field = new SnowHeightFieldCpu(geo, 0, new SnowGroundFieldCpu(geo, new ushort[geo.CellCount], mask));

            Assert.AreEqual(0, field.AddAt(blocked, 500), "용량 0 이면 한 밀리미터도 반영되지 않아야 한다");
            Assert.AreEqual(0, field.GetAt(blocked));
            Assert.AreEqual(0, field.TotalHeightMm, "거절된 양이 원장에 실리면 안 된다");

            field.Set(8, 8, 1234);
            Assert.AreEqual(0, field.GetAt(blocked), "직접 대입도 용량을 넘지 못한다");
            Assert.AreEqual(500, field.Add(9, 8, 500), "이웃 셀은 정상이어야 한다");
        }

        /// <summary>
        /// <b>이것이 경사 눈의 핵심 계약이다.</b> 램프 위에 균일하게 깔린 눈은 지형이 기울었다는
        /// 이유만으로 흘러내리지 않는다. 이완이 깊이 대신 표면 높이만 보도록 고치면 이 검사가
        /// 깨지고, 그래서 허용 낙차에 지형 낙차를 더한다(스펙 §5).
        /// </summary>
        [Test]
        public void Relax_LeavesUniformSnowOnASlopeAlone()
        {
            var geo = Geo(4f);
            // 셀당 125 mm — 45° 램프. 안식각 낙차(178 mm)보다 작지만 그 자체로 흐름을 만들면 안 된다.
            var field = new SnowHeightFieldCpu(geo, 600, Ramp(geo, 125));
            long before = field.TotalHeightMm;
            var chunks = AllChunks(geo);

            long moved = 0;
            for (int i = 0; i < 32; i++)
            {
                moved += SnowReposeRelax.Iterate(field, chunks, NoBarrier, Mat, out long clamped);
                Assert.AreEqual(0, clamped, $"반복 {i} 에서 {clamped} mm 가 잘려나갔다");
            }

            Assert.AreEqual(0, moved, "경사 위의 균일한 눈은 움직이지 않아야 한다");
            Assert.AreEqual(before, field.TotalHeightMm);
        }

        /// <summary>
        /// 지형이 아무리 급해도 눈은 그 낙차를 타고 흘러내리지 않는다 — 램프 위 눈이 옆의 낮은
        /// 지면으로 새면 램프가 세션 내내 자기 눈을 잃는다(스펙 §4 "구역을 벗어난 눈은 안 떨어진다").
        /// </summary>
        [Test]
        public void Relax_DoesNotPourSnowOffACliffInTheFloor()
        {
            var geo = Geo(4f);
            var floor = new ushort[geo.CellCount];
            var mask = new byte[geo.CellCount];
            for (int cz = 0; cz < geo.ResZ; cz++)
            for (int cx = 0; cx < geo.ResX; cx++)
            {
                int i = geo.CellIndex(cx, cz);
                floor[i] = cx >= 16 ? (ushort)3000 : (ushort)0;   // 3 m 단
                mask[i] = SnowGroundFieldCpu.SnowableValue;
            }

            var field = new SnowHeightFieldCpu(geo, 600, new SnowGroundFieldCpu(geo, floor, mask));
            var chunks = AllChunks(geo);
            for (int i = 0; i < 64; i++) SnowReposeRelax.Iterate(field, chunks, NoBarrier, Mat, out _);

            Assert.AreEqual(600, field.Get(16, 8), "단 위 첫 셀이 아래로 쏟아지면 안 된다");
            Assert.AreEqual(600, field.Get(15, 8), "단 아래 셀도 위에서 받은 것이 없어야 한다");
        }

        [Test]
        public void Relax_StillFlattensAPileOnFlatFloor()
        {
            var geo = Geo(4f);
            var field = new SnowHeightFieldCpu(geo, 0, SnowGroundFieldCpu.Flat(geo));
            field.Set(16, 16, 20000);
            var chunks = AllChunks(geo);
            for (int i = 0; i < 512; i++) SnowReposeRelax.Iterate(field, chunks, NoBarrier, Mat, out _);

            Assert.Less(field.Get(16, 16), 20000, "평지의 기둥은 여전히 무너져야 한다");
            Assert.Greater(field.Get(17, 16), 0, "옆 셀이 받았어야 한다");
        }

        [Test]
        public void Relax_NeverFlowsIntoCellsWhereSnowIsImpossible()
        {
            var geo = Geo(4f);
            var mask = new byte[geo.CellCount];
            for (int i = 0; i < mask.Length; i++) mask[i] = SnowGroundFieldCpu.SnowableValue;
            mask[geo.CellIndex(17, 16)] = 0;

            var field = new SnowHeightFieldCpu(geo, 0, new SnowGroundFieldCpu(geo, new ushort[geo.CellCount], mask));
            field.Set(16, 16, 20000);
            long before = field.TotalHeightMm;

            var chunks = AllChunks(geo);
            for (int i = 0; i < 512; i++)
            {
                SnowReposeRelax.Iterate(field, chunks, NoBarrier, Mat, out long clamped);
                Assert.AreEqual(0, clamped, "막힌 셀로 흘려보내면 그만큼이 잘려 원장에서 사라진다");
            }

            Assert.AreEqual(0, field.Get(17, 16), "눈이 불가능한 셀은 받지 않는다");
            Assert.AreEqual(before, field.TotalHeightMm, "그래도 총량은 정확히 보존된다");
        }

        [Test]
        public void GroundMap_RoundTripsThroughTheAssetPayload()
        {
            var map = ScriptableObject.CreateInstance<SnowGroundMap>();
            try
            {
                // 범위의 주인은 맵이다 — 굽기도 맵의 원점·크기로 격자를 세우므로 테스트도 같게 한다.
                var probe = new SnowFieldGeometry(map.SizeMeters.x, map.SizeMeters.y,
                                                 map.OriginXZ.x, map.OriginXZ.y);
                var floor = new ushort[probe.CellCount];
                var mask = new byte[probe.CellCount];
                var initialDepth = new byte[probe.CellCount];
                for (int i = 0; i < floor.Length; i++)
                {
                    floor[i] = (ushort)(i % 4096);
                    mask[i] = i % 3 == 0 ? (byte)0 : SnowGroundFieldCpu.SnowableValue;
                    initialDepth[i] = mask[i] == 0 ? (byte)0 : (byte)(i % 2 == 0 ? 89 : 255);
                }

                map.WriteBake(probe.ResX, probe.ResZ, 2.5f, floor, mask, initialDepth, "__TEST__");
                Assert.IsTrue(map.IsBaked);

                SnowFieldGeometry geo = map.BuildGeometry();
                Assert.AreEqual(2.5f, geo.OriginYM, 1e-4f, "격자가 맵의 바닥 기준 Y 를 물려받아야 한다");
                Assert.IsTrue(map.TryBuildField(geo, out SnowGroundFieldCpu ground, out string error), error);

                int floorMismatch = 0;
                int maskMismatch = 0;
                int initialDepthMismatch = 0;
                for (int i = 0; i < floor.Length; i++)
                {
                    if (floor[i] != ground.FloorMm[i]) floorMismatch++;
                    if ((mask[i] != 0) != ground.IsSnowableAt(i)) maskMismatch++;
                    if (initialDepth[i] != ground.InitialDepthScaleR8[i]) initialDepthMismatch++;
                }

                Assert.AreEqual(0, floorMismatch, "바닥이 에셋 왕복에서 바뀌었다");
                Assert.AreEqual(0, maskMismatch, "마스크가 에셋 왕복에서 바뀌었다");
                Assert.AreEqual(0, initialDepthMismatch, "시작 적설 배율이 에셋 왕복에서 바뀌었다");

                int probeCell = geo.CellIndex(1, 0);
                Assert.AreEqual(2.5f + floor[probeCell] * 0.001f, ground.FloorYAt(probeCell), 1e-4f,
                                "바닥 월드 Y 는 기준 Y + mm 다");

                // 격자가 어긋나면 조용히 평지로 돌아가지 않고 거절해야 한다.
                Assert.IsFalse(map.TryBuildField(Geo(8f, 2.5f), out _, out string mismatch));
                Assert.IsNotEmpty(mismatch);
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void InitialDepthScale_SetsRoadDepthWithoutBlockingLaterSnow()
        {
            SnowFieldGeometry geo = Geo(1f);
            var floor = new ushort[geo.CellCount];
            var coverage = new byte[geo.CellCount];
            var initial = new byte[geo.CellCount];
            for (int i = 0; i < geo.CellCount; i++)
            {
                coverage[i] = byte.MaxValue;
                initial[i] = byte.MaxValue;
            }

            int roadCell = geo.CellIndex(2, 2);
            initial[roadCell] = 85;
            var ground = new SnowGroundFieldCpu(geo, floor, coverage, initial);
            var field = new SnowHeightFieldCpu(geo, 300, ground);

            Assert.AreEqual(100, field.Get(2, 2), "도로는 300 mm의 1/3 깊이로 시작해야 한다");
            Assert.AreEqual(SnowHeightFieldCpu.MaxHeightMm, field.CapacityAt(roadCell),
                            "시작 눈이 얕아도 재적설 용량은 막지 않는다");
            Assert.AreEqual(50, field.AddAt(roadCell, 50), "도로에도 이후 눈이 다시 쌓여야 한다");
        }
    }
}

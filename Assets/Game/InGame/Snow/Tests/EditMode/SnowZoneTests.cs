using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 눈 상자의 계약. 핵심은 둘이다 — <b>격자가 상자 로컬</b>이라 회전이 인덱스를 바꾸지 않는 것과,
    /// <b>눈 경계가 저작한 사각형과 정확히 같은</b> 것(청크 배수 올림이 새어 나오지 않는다).
    /// </summary>
    public sealed class SnowZoneTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
        }

        private SnowZone MakeZone(Vector2 sizeXZ, Vector3 pos, Quaternion rot,
                                  float heightM = 2f, int depthMm = 600)
        {
            _go = new GameObject("__TEST__SnowZone");
            _go.transform.SetPositionAndRotation(pos, rot);

            SnowZone zone = _go.AddComponent<SnowZone>();
            Set(zone, "_sizeXZ", sizeXZ);
            Set(zone, "_heightM", heightM);
            Set(zone, "_initialDepthMm", depthMm);
            zone.EnsureBuilt();
            return zone;
        }

        private static void Set(object target, string field, object value)
        {
            System.Reflection.FieldInfo info = target.GetType().GetField(
                field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(info, $"{field} 필드가 없다 — 개명했으면 이 테스트도 따라가야 한다");
            info.SetValue(target, value);
        }

        /// <summary>
        /// 격자는 상자보다 <b>넓고</b>(커버리지 0 인 테두리가 있어야 한다) 상자 중심에 <b>대칭</b>이다.
        /// 요청 크기로 원점을 잡으면 청크 올림분이 한쪽에만 붙어 격자와 메시가 상자에서 밀린다.
        /// </summary>
        [Test]
        public void Grid_IsPaddedAndCentredOnTheBox()
        {
            SnowZone zone = MakeZone(new Vector2(6f, 10f), Vector3.zero, Quaternion.identity);
            SnowFieldGeometry geo = zone.Field.Geo;

            float w = geo.ResX * SnowFieldGeometry.CellSizeM;
            float d = geo.ResZ * SnowFieldGeometry.CellSizeM;

            Assert.GreaterOrEqual(w, 6f + SnowFieldGeometry.CellSizeM * 2f, "커버리지 0 인 테두리가 없다");
            Assert.GreaterOrEqual(d, 10f + SnowFieldGeometry.CellSizeM * 2f);
            Assert.AreEqual(-w * 0.5f, geo.OriginXM, 1e-4f, "격자가 상자 중심에 대칭이 아니다");
            Assert.AreEqual(-d * 0.5f, geo.OriginZM, 1e-4f);
        }

        /// <summary>
        /// <b>이것이 기울어진 램프가 되는 이유다.</b> 같은 로컬 점은 상자를 어떻게 돌려도 같은 셀이다 —
        /// 격자가 월드 XZ 였다면 회전할 때마다 인덱스가 미끄러진다.
        /// </summary>
        [Test]
        public void Rotation_DoesNotMoveCells()
        {
            var local = new Vector3(1.3f, 0f, -2.7f);

            SnowZone flat = MakeZone(new Vector2(6f, 10f), new Vector3(5f, 1f, -2f), Quaternion.identity);
            Vector3 flatLocal = flat.ToLocal(flat.ToWorld(local));
            Assert.IsTrue(flat.Field.Geo.TryWorldToCell(flatLocal.x, flatLocal.z,
                                                        out int fx, out int fz));
            Object.DestroyImmediate(_go);
            _go = null;

            SnowZone tilted = MakeZone(new Vector2(6f, 10f), new Vector3(-11f, 4f, 7f),
                                       Quaternion.Euler(45f, 30f, 0f));
            Vector3 tiltedLocal = tilted.ToLocal(tilted.ToWorld(local));
            Assert.IsTrue(tilted.Field.Geo.TryWorldToCell(tiltedLocal.x, tiltedLocal.z,
                                                          out int tx, out int tz));

            Assert.AreEqual(fx, tx, "회전이 셀 X 를 옮겼다");
            Assert.AreEqual(fz, tz, "회전이 셀 Z 를 옮겼다");
        }

        /// <summary>
        /// 커버리지가 <b>권위와 마감을 동시에</b> 정의한다 — 안쪽은 가득, 가장자리에서 0 으로
        /// 떨어지고, <b>상자 밖은 무조건 0</b> 이다. 흔들림은 안쪽으로만 가므로 상자가 곧 눈의 최대
        /// 범위이고, 그래서 상자를 받치는 메시에 맞춰 놓으면 눈이 메시 밖으로 나가지 않는다.
        /// </summary>
        [Test]
        public void Coverage_IsFullInsideAndFadesInsideTheBoxEdge()
        {
            SnowZone zone = MakeZone(new Vector2(5f, 5f), Vector3.zero, Quaternion.identity);
            SnowFieldGeometry geo = zone.Field.Geo;
            SnowGroundFieldCpu ground = zone.Field.Ground;
            Assert.IsNotNull(ground, "커버리지가 있어야 한다");

            byte At(float x, float z)
            {
                Assert.IsTrue(geo.TryWorldToCell(x, z, out int cx, out int cz), $"({x},{z}) 가 격자 밖이다");
                return ground.Coverage[geo.CellIndex(cx, cz)];
            }

            // 사각형 반폭 2.5 m · 페이드 0.45 m · 흔들림 0.3 m(안쪽으로).
            Assert.AreEqual(SnowGroundFieldCpu.SnowableValue, At(0f, 0f), "안쪽은 가득이어야 한다");
            Assert.AreEqual(SnowGroundFieldCpu.SnowableValue, At(1.7f, 0f), "페이드+흔들림 띠 안쪽은 가득이다");
            Assert.Less(At(2.3f, 0f), SnowGroundFieldCpu.SnowableValue, "가장자리는 재워져야 한다");
            Assert.AreEqual(0, At(2.55f, 0f), "상자 밖에는 눈이 없어야 한다");

            // 초기 적설은 커버리지가 0 이 아닌 셀만 받는다.
            Assert.AreEqual((long)ground.SnowableCells * 600, zone.TotalHeightMm);
            Assert.Less(ground.SnowableCells, geo.CellCount, "가장자리 밖이 하나도 안 꺼졌다");
        }

        [Test]
        public void Contains_UsesTheBoxNotTheWorldAxes()
        {
            SnowZone zone = MakeZone(new Vector2(6f, 10f), new Vector3(0f, 3f, 0f),
                                     Quaternion.Euler(45f, 0f, 0f), heightM: 1.5f);

            // 상자 바닥면 위 0.5 m — 로컬 좌표로 넣어야 하고, 월드로는 기울어진 점이다.
            Assert.IsTrue(zone.Contains(zone.ToWorld(new Vector3(0f, 0.5f, 0f))));
            Assert.IsTrue(zone.Contains(zone.ToWorld(new Vector3(2.9f, 0f, 4.9f))));

            Assert.IsFalse(zone.Contains(zone.ToWorld(new Vector3(3.2f, 0f, 0f))), "XZ 밖");
            Assert.IsFalse(zone.Contains(zone.ToWorld(new Vector3(0f, 2.0f, 0f))), "높이 밖");
            Assert.IsFalse(zone.Contains(zone.ToWorld(new Vector3(0f, -0.5f, 0f))), "바닥 여유 밖");

            // 회전을 무시하고 월드 축으로 판정하면 통과해 버리는 점.
            Assert.IsFalse(zone.Contains(new Vector3(0f, 3.5f, 4.9f)),
                           "월드 축 기준으로 판정하면 안 된다");
        }

        [Test]
        public void Surface_IsFloorPlusDepthInTheBoxFrame()
        {
            SnowZone zone = MakeZone(new Vector2(6f, 10f), new Vector3(0f, 5f, 0f),
                                     Quaternion.Euler(30f, 0f, 0f));

            Vector3 at = zone.ToWorld(new Vector3(0.5f, 0.2f, -1f));
            Assert.IsTrue(zone.TrySurfaceLocalY(at, out float localY, out float depth));
            Assert.AreEqual(0.6f, depth, 1e-4f, "초기 적설 600 mm");
            Assert.AreEqual(0.6f, localY, 1e-4f, "바닥이 평면이므로 표면은 깊이와 같다");

            Assert.IsTrue(zone.TrySurfaceWorldY(at, out float worldY, out _));
            Vector3 expected = zone.ToWorld(new Vector3(0.5f, 0.6f, -1f));
            Assert.AreEqual(expected.y, worldY, 1e-3f, "표면의 월드 Y 는 상자 회전을 타야 한다");
        }

        [Test]
        public void StableId_IsAHierarchyPath()
        {
            SnowZone zone = MakeZone(new Vector2(6f, 10f), Vector3.zero, Quaternion.identity);
            var parent = new GameObject("__TEST__Parent");
            try
            {
                Assert.AreEqual("/__TEST__SnowZone", zone.StableId);

                zone.transform.SetParent(parent.transform, true);
                Assert.AreEqual("/__TEST__Parent/__TEST__SnowZone", zone.StableId,
                                "복제가 인덱스로 상자를 가리키므로 키는 이름이 아니라 경로여야 한다");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void OversizedBox_RefusesToBuild()
        {
            // 상한 16,384 셀 = 한 변 128 셀(16 m) 남짓. 40 x 40 m 는 102,400 셀이다.
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("상자로 다룰 크기가"));
            SnowZone zone = MakeZone(new Vector2(40f, 40f), Vector3.zero, Quaternion.identity);
            Assert.IsNull(zone.Field, "상한을 넘으면 격자를 만들지 않는다 — 지면 시트가 할 일이다");
        }
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <b>바닥을 굽는 도구.</b> 열려 있는 씬의 콜라이더에 셀마다 레이를 한 발 쏴서
    /// <see cref="SnowGroundMap"/> 을 채운다.
    ///
    /// <para><b>바닥과 마스크가 같은 한 패스에서 나온다.</b> 스펙 §3 이 "터레인 유도와 손칠은 두
    /// 방식이 아니라 하나의 데이터에 대한 두 저작 경로" 라고 한 것의 첫 절반이고, 유도 규칙은 셋뿐이다
    /// — Ground/Road가 없으면 눈 없음, Obstacle이 그 위를 덮으면 눈 없음, 경사각이 한계를 넘으면
    /// 눈 없음이다. Road는 같은 패스에서 시작 적설 배율까지 기록한다.</para>
    ///
    /// <para><b>분류 표식이 없는 콜라이더와 동적 리지드바디는 굽지 않는다.</b> 물은 표식을 붙이지 않아
    /// Ground도 Obstacle도 아닌 빈 영역으로 남긴다.</para>
    ///
    /// <para>CLI 에서 부를 수 있게 <see cref="Bake(SnowGroundMap, out string)"/> 를 public static 으로
    /// 둔다 — 메뉴는 그 얇은 껍데기다.</para>
    /// </summary>
    public static class SnowGroundBake
    {
        private const int MaxHitsPerCell = 16;

        [MenuItem("Tools/PPack/Snow/Bake Ground Map (selected asset)")]
        public static void BakeSelected()
        {
            var map = Selection.activeObject as SnowGroundMap;
            if (map == null)
            {
                Debug.LogError("SnowGroundBake: 프로젝트 창에서 SnowGroundMap 에셋을 골라야 한다.");
                return;
            }

            if (Bake(map, out string report)) Debug.Log($"SnowGroundBake: {report}");
            else Debug.LogError($"SnowGroundBake: {report}");
        }

        /// <summary>
        /// 열린 씬에서 굽는다. 성공하면 에셋을 저장하고 요약을 돌려준다.
        /// </summary>
        public static bool Bake(SnowGroundMap map, out string report)
        {
            if (map == null)
            {
                report = "맵이 null 이다";
                return false;
            }

            // 굽기는 격자의 XZ 해상도만 쓴다 — 바닥 기준 Y 는 굽기 결과로 정해지므로 여기서는 0 이다.
            var probeGeo = new SnowFieldGeometry(map.SizeMeters.x, map.SizeMeters.y,
                                                 map.OriginXZ.x, map.OriginXZ.y);
            int resX = probeGeo.ResX;
            int resZ = probeGeo.ResZ;
            int cells = resX * resZ;
            UnityEngine.SceneManagement.Scene bakeScene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            var hitY = new float[cells];
            var snowable = new byte[cells];
            var initialDepth = new byte[cells];
            var hits = new RaycastHit[MaxHitsPerCell];
            var markerCache = new System.Collections.Generic.Dictionary<Collider, SnowBakeSurface>();

            int layerMask = map.ProbeLayers.value;
            float cosLimit = Mathf.Cos(map.MaxSlopeDeg * Mathf.Deg2Rad);
            float topY = map.ProbeTopYM;
            float length = map.ProbeLengthM;

            int hitCells = 0;
            int tooSteep = 0;
            int obstacleCells = 0;
            int roadCells = 0;
            int dynamicSkipped = 0;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            // 에디트 모드에서는 트랜스폼 변경이 물리 씬에 자동 반영되지 않을 수 있다.
            Physics.SyncTransforms();

            try
            {
                for (int cz = 0; cz < resZ; cz++)
                {
                    if ((cz & 31) == 0)
                    {
                        EditorUtility.DisplayProgressBar("Snow Ground Bake",
                            $"{map.name} — {cz}/{resZ}", cz / (float)resZ);
                    }

                    for (int cx = 0; cx < resX; cx++)
                    {
                        probeGeo.CellCenterWorld(cx, cz, out float wx, out float wz);
                        int count = Physics.RaycastNonAlloc(new Vector3(wx, topY, wz), Vector3.down,
                                                            hits, length, layerMask,
                                                            QueryTriggerInteraction.Ignore);

                        int groundHit = -1;
                        float groundDist = float.MaxValue;
                        SnowBakeSurface groundMarker = null;
                        float obstacleDist = float.MaxValue;
                        bool skippedDynamicHere = false;

                        for (int h = 0; h < count; h++)
                        {
                            Rigidbody body = hits[h].collider.attachedRigidbody;
                            if (body != null && !body.isKinematic)
                            {
                                skippedDynamicHere = true;
                                continue;
                            }

                            Collider collider = hits[h].collider;
                            if (collider.gameObject.scene != bakeScene) continue;
                            if (!markerCache.TryGetValue(collider, out SnowBakeSurface marker))
                            {
                                marker = collider.GetComponentInParent<SnowBakeSurface>();
                                markerCache.Add(collider, marker);
                            }
                            if (marker == null || marker.Surface == ESnowBakeSurface.Ignore) continue;

                            if (marker.Surface == ESnowBakeSurface.Obstacle)
                            {
                                if (hits[h].distance < obstacleDist) obstacleDist = hits[h].distance;
                                continue;
                            }

                            if (hits[h].distance >= groundDist) continue;
                            groundDist = hits[h].distance;
                            groundHit = h;
                            groundMarker = marker;
                        }

                        if (skippedDynamicHere) dynamicSkipped++;

                        int ci = probeGeo.CellIndex(cx, cz);
                        if (groundHit < 0)
                        {
                            hitY[ci] = 0f;
                            snowable[ci] = 0;
                            continue;
                        }

                        float y = hits[groundHit].point.y;
                        hitY[ci] = y;
                        hitCells++;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;

                        // 위를 향한 성분으로 경사를 판정한다. 뒤집힌 면(아래를 보는 법선)은 자연히 탈락한다.
                        if (obstacleDist < groundDist)
                        {
                            obstacleCells++;
                            continue;
                        }

                        if (hits[groundHit].normal.y < cosLimit)
                        {
                            snowable[ci] = 0;
                            tooSteep++;
                            continue;
                        }

                        snowable[ci] = SnowGroundFieldCpu.SnowableValue;
                        initialDepth[ci] = groundMarker.InitialDepthScaleR8;
                        if (groundMarker.Surface == ESnowBakeSurface.Road) roadCells++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (hitCells == 0)
            {
                report = $"{map.name}: 아무 콜라이더도 못 맞혔다 — 범위({map.OriginXZ}, {map.SizeMeters}) 와 " +
                         $"레이 시작 Y({topY}) 를 확인할 것";
                return false;
            }

            // 바닥 기준 Y 는 실제로 맞은 가장 낮은 바닥에서 1 mm 내림으로 잡는다. 음수 mm 가 생기지
            // 않는 가장 싼 방법이고, ushort 창(65.5 m)이 가장 높은 바닥까지 덮는지도 여기서 검사한다.
            float originY = Mathf.Floor(minY * 1000f) * 0.001f;
            double spanMm = (maxY - originY) * 1000.0;
            if (spanMm > SnowHeightFieldCpu.MaxHeightMm)
            {
                report = $"{map.name}: 바닥 높이차 {spanMm / 1000.0:F1} m 가 ushort 창(65.5 m)을 넘는다";
                return false;
            }

            var floorMm = new ushort[cells];
            for (int i = 0; i < cells; i++)
            {
                if (snowable[i] == 0 && hitY[i] == 0f) continue;
                int mm = Mathf.RoundToInt((hitY[i] - originY) * 1000f);
                if (mm < 0) mm = 0;
                else if (mm > SnowHeightFieldCpu.MaxHeightMm) mm = SnowHeightFieldCpu.MaxHeightMm;
                floorMm[i] = (ushort)mm;
            }

            string sceneName = bakeScene.name;
            map.WriteBake(resX, resZ, originY, floorMm, snowable, initialDepth, sceneName);

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();

            report = $"{map.name}: {resX}x{resZ} 셀 · 맞음 {Ratio(hitCells, cells)} · 눈 가능 " +
                     $"{Ratio(map.SnowableCells, cells)} · 급경사 탈락 {tooSteep} · 동적 무시 {dynamicSkipped} · " +
                     $"도로 {roadCells} · 장애물 {obstacleCells} · " +
                     $"바닥 기준 Y {originY:F3} · 바닥 {map.MinFloorMm}~{map.MaxFloorMm} mm · 씬 {sceneName}";
            return true;
        }

        private static string Ratio(int n, int total) => $"{n}({n * 100f / total:F1}%)";
    }
}
#endif

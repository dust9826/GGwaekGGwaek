using System;
using System.Collections.Generic;
using System.Linq;
using PPack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack.InGame.Editor
{
    internal static class RoadFirstWorldDistrictRecomposer
    {
        private const string ScenePath = "Assets/Game/InGame/Map/WinterVillage/Scenes/WinterVillage_RoadFirstWorld.unity";
        private const string DistrictRootName = "Districts_LowPolyWinter_NoVendorLand";
        private const string MarkerName = "__RoadFirstVillageRecomposition_v2";
        private const string LargeRockScaleMarkerName = "__RoadFirstLargeRockScale_v1";
        private const string MenuPath = "PPack/Map/Road First World/Recompose Villages Into Individual Props";
        private const string PolishMenuPath = "PPack/Map/Road First World/Polish Individual Placement And Ski Slope";
        private const string CleanupMenuPath = "PPack/Map/Road First World/Clean Props From Road Surface";
        private const string ApplyInteractionPropsMenuPath =
            "PPack/Map/Road First World/Apply Interactive Barrel And Hydrant Prefabs";
        private const string ConceptSmokePrefabPath = "Assets/Game/InGame/Map/WinterVillage/Prefabs/VFX/PF_WinterChimneySmoke.prefab";
        private const string RollingBarrelPrefabPath =
            "Assets/Game/InGame/Interaction/Barrel/Prefabs/PF_RollingBarrel.prefab";
        private const string BreakableHydrantPrefabPath =
            "Assets/Game/InGame/Interaction/Hydrant/Prefabs/PF_BreakableHydrant.prefab";

        private readonly struct RoadSample
        {
            public readonly Vector3 Position;
            public readonly float ProtectedRadius;
            public readonly Vector3 Lateral;

            public RoadSample(Vector3 position, float protectedRadius, Vector3 lateral)
            {
                Position = position;
                ProtectedRadius = protectedRadius;
                Lateral = lateral;
            }
        }

        [MenuItem(MenuPath)]
        public static void Recompose()
        {
            Scene scene = RequireRoadFirstScene();
            Terrain terrain = Terrain.activeTerrain;
            GameObject districtRoot = GameObject.Find(DistrictRootName);
            if (terrain == null || districtRoot == null)
                throw new InvalidOperationException("Road First World terrain or district root was not found.");
            if (GameObject.Find(MarkerName) != null)
                throw new InvalidOperationException("The individual-prop village recomposition has already been applied.");

            Undo.RegisterFullObjectHierarchyUndo(districtRoot, "Recompose Road First World villages");
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Regrade Road First World village terrain");

            var buildings = new List<Transform>();
            var groundItems = new List<Transform>();
            var natureItems = new List<Transform>();
            var sourceSmokeAnchors = new Dictionary<Transform, Vector3>();
            int unpackedDistricts = 0;

            foreach (Transform district in districtRoot.transform.Cast<Transform>().ToArray())
            {
                if (PrefabUtility.IsPartOfPrefabInstance(district.gameObject))
                {
                    GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(district.gameObject);
                    if (instanceRoot == district.gameObject)
                    {
                        PrefabUtility.UnpackPrefabInstance(
                            district.gameObject,
                            PrefabUnpackMode.Completely,
                            InteractionMode.AutomatedAction);
                        unpackedDistricts++;
                    }
                }

                Transform buildingRoot = CreateCategory(district, "Buildings_Individual");
                Transform natureRoot = CreateCategory(district, "Nature_Individual");
                Transform propRoot = CreateCategory(district, "Props_Individual");
                Transform overheadRoot = CreateCategory(district, "Overhead_KeepRelative");
                Transform referenceRoot = CreateCategory(district, "ReferenceOnly_Disabled");
                referenceRoot.gameObject.SetActive(false);

                Transform[] originalItems = district.Cast<Transform>()
                    .Where(item => item != buildingRoot && item != natureRoot && item != propRoot && item != overheadRoot && item != referenceRoot)
                    .ToArray();

                CaptureAndRemoveStaticSmoke(originalItems, sourceSmokeAnchors);

                foreach (Transform item in originalItems)
                {
                    if (item == null) continue;
                    string lower = item.name.ToLowerInvariant();
                    if (IsVendorSurfaceLeftover(lower))
                    {
                        Undo.DestroyObjectImmediate(item.gameObject);
                        continue;
                    }
                    if (IsReferenceLand(item, lower))
                    {
                        item.SetParent(referenceRoot, true);
                        continue;
                    }

                    if (IsHouse(lower))
                    {
                        item.SetParent(buildingRoot, true);
                        item.localScale *= GetHouseScale(lower);
                        buildings.Add(item);
                        continue;
                    }

                    if (IsOverhead(lower))
                    {
                        item.SetParent(overheadRoot, true);
                        continue;
                    }

                    if (IsNature(lower))
                    {
                        item.SetParent(natureRoot, true);
                        natureItems.Add(item);
                        groundItems.Add(item);
                        continue;
                    }

                    item.SetParent(propRoot, true);
                    if (ShouldSnapToTerrain(lower)) groundItems.Add(item);
                }
            }

            MoveSkiLodgeToSlopeBase(buildings);
            ResolveBuildingOverlaps(buildings);

            TerrainData terrainData = terrain.terrainData;
            int resolution = terrainData.heightmapResolution;
            float[,] heights = terrainData.GetHeights(0, 0, resolution, resolution);
            List<RoadSample> roadSamples = BuildRoadSamples();
            BuildContinuousSkiSlope(terrain, heights, roadSamples);

            foreach (Transform building in buildings)
            {
                Bounds bounds = CalculateBounds(building.gameObject);
                float baseHeight = SampleWorldHeight(terrain, heights, bounds.center.x, bounds.center.z);
                StampFlatBuildingPad(terrain, heights, bounds, baseHeight, roadSamples);
            }

            terrainData.SetHeights(0, 0, heights);
            terrain.Flush();
            EditorUtility.SetDirty(terrainData);

            int movedBuildings = 0;
            foreach (Transform building in buildings)
            {
                if (SnapItemToTerrain(building, terrain, 0.025f)) movedBuildings++;
            }

            RepelNatureFromBuildings(natureItems, buildings);
            int groundedItems = 0;
            foreach (Transform item in groundItems)
            {
                string lower = item.name.ToLowerInvariant();
                float sink = lower.Contains("stone") || lower.Contains("rock") || lower.Contains("snowdrift") ? -0.06f : 0.015f;
                if (SnapItemToTerrain(item, terrain, sink)) groundedItems++;
            }

            int smokeCount = AddConceptChimneySmoke(scene, buildings, sourceSmokeAnchors);

            var marker = new GameObject(MarkerName);
            marker.transform.SetParent(districtRoot.transform.parent, false);
            marker.tag = "EditorOnly";

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();

            Debug.Log(
                "[RoadFirstWorldRecomposer] Unpacked " + unpackedDistricts +
                " village instances, enlarged/repositioned " + buildings.Count +
                " houses, grounded " + groundedItems +
                " individual props, added " + smokeCount +
                " ConceptMap chimney-smoke systems, and built a continuous ski slope. Building vertical corrections=" + movedBuildings + ".");
        }

        [MenuItem(PolishMenuPath)]
        public static void PolishRecomposition()
        {
            Scene scene = RequireRoadFirstScene();
            Terrain terrain = Terrain.activeTerrain;
            GameObject districtRoot = GameObject.Find(DistrictRootName);
            if (terrain == null || districtRoot == null || GameObject.Find(MarkerName) == null)
                throw new InvalidOperationException("Apply the individual-prop village recomposition before polishing it.");

            var buildings = new List<Transform>();
            var natureItems = new List<Transform>();
            var groundProps = new List<Transform>();
            int removedVendorSurfaces = 0;
            int removedUnnaturalBoulders = 0;
            foreach (Transform district in districtRoot.transform)
            {
                Transform buildingRoot = district.Find("Buildings_Individual");
                Transform natureRoot = district.Find("Nature_Individual");
                if (buildingRoot != null) buildings.AddRange(buildingRoot.Cast<Transform>());
                if (natureRoot != null)
                {
                    foreach (Transform nature in natureRoot.Cast<Transform>().ToArray())
                    {
                        if (district.name.Contains("08_NorthPoleCamp") &&
                            nature.name.StartsWith("stone winter large", StringComparison.OrdinalIgnoreCase))
                        {
                            Undo.DestroyObjectImmediate(nature.gameObject);
                            removedUnnaturalBoulders++;
                        }
                        else
                        {
                            natureItems.Add(nature);
                        }
                    }
                }
                Transform propRoot = district.Find("Props_Individual");
                if (propRoot == null) continue;
                foreach (Transform prop in propRoot.Cast<Transform>().ToArray())
                {
                    string lower = prop.name.ToLowerInvariant();
                    if (IsVendorSurfaceLeftover(lower))
                    {
                        Undo.DestroyObjectImmediate(prop.gameObject);
                        removedVendorSurfaces++;
                    }
                    else if (ShouldSnapToTerrain(lower))
                    {
                        groundProps.Add(prop);
                    }
                }
            }

            Undo.RegisterFullObjectHierarchyUndo(districtRoot, "Polish Road First World individual placement");
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Polish Road First World ski terrain");
            List<RoadSample> roadSamples = BuildRoadSamples();
            int scaledLargeRocks = 0;
            if (GameObject.Find(LargeRockScaleMarkerName) == null)
            {
                scaledLargeRocks = ScaleOversizedDistrictRocks(natureItems);
                var scaleMarker = new GameObject(LargeRockScaleMarkerName);
                scaleMarker.transform.SetParent(districtRoot.transform.parent, false);
                scaleMarker.tag = "EditorOnly";
            }
            int shiftedBuildings = MoveItemsOffRoad(buildings, roadSamples, 0.8f, 0.82f);
            ResolveBuildingOverlaps(buildings);

            TerrainData data = terrain.terrainData;
            float[,] heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            BuildContinuousSkiSlope(terrain, heights, roadSamples);
            foreach (Transform building in buildings)
            {
                Bounds bounds = CalculateBounds(building.gameObject);
                float baseHeight = SampleWorldHeight(terrain, heights, bounds.center.x, bounds.center.z);
                StampFlatBuildingPad(terrain, heights, bounds, baseHeight, roadSamples);
            }
            data.SetHeights(0, 0, heights);
            terrain.Flush();
            EditorUtility.SetDirty(data);

            foreach (Transform building in buildings) SnapItemToTerrain(building, terrain, 0.025f);
            MoveSkiScenicRocks(natureItems);
            int shiftedNature = MoveItemsOffRoad(natureItems, roadSamples, 0.6f, 0.58f);
            int shiftedProps = MoveItemsOffRoad(groundProps, roadSamples, 0.32f, 0.5f);
            RepelNatureFromBuildings(natureItems, buildings);
            foreach (Transform nature in natureItems)
            {
                string lower = nature.name.ToLowerInvariant();
                float sink = lower.Contains("stone") || lower.Contains("rock") || lower.Contains("snowdrift") ? -0.06f : 0.015f;
                SnapItemToTerrain(nature, terrain, sink);
            }
            foreach (Transform prop in groundProps) SnapItemToTerrain(prop, terrain, 0.015f);
            int remainingPropIntrusions = CountRoadIntrusions(groundProps, roadSamples, 0.32f, 0.5f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            Debug.Log("[RoadFirstWorldRecomposer] Polished road clearance for " + shiftedBuildings +
                      " houses, " + shiftedNature + " nature props, and " + shiftedProps +
                      " roadside props; remaining prop intrusions=" + remainingPropIntrusions +
                      ". Removed " + removedVendorSurfaces +
                      " vendor surface leftovers and " + removedUnnaturalBoulders +
                      " obstructive North Pole boulders, scaled " + scaledLargeRocks +
                      " oversized boulders, and reshaped the ski hill into a narrower continuous piste.");
        }

        [MenuItem(CleanupMenuPath)]
        public static void CleanPropsFromRoadSurface()
        {
            Scene scene = RequireRoadFirstScene();
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
                throw new InvalidOperationException("Road First World terrain was not found.");

            List<RoadSample> roadSamples = BuildRoadSamples();
            List<Transform> candidates = CollectSceneRoadsideProps(scene);
            List<Transform> routeGuides = candidates.Where(IsRouteGuide).ToList();
            int initialIntrusions = CountRoadIntrusions(candidates, roadSamples, 0.32f, 0.5f, false);
            int initialRouteGuideIntrusions = CountRoadIntrusions(routeGuides, roadSamples, 1.45f, 0.5f, false);
            foreach (Transform candidate in candidates)
                Undo.RegisterCompleteObjectUndo(candidate, "Clear Road First World road surface");

            int shifted = MoveItemsOffRoad(candidates, roadSamples, 0.32f, 0.5f);
            int shiftedRouteGuides = MoveItemsOffRoad(routeGuides, roadSamples, 1.45f, 0.5f);
            int resolvedRouteGuideOverlaps = ResolveRouteGuideOverlaps(routeGuides, candidates, roadSamples);
            foreach (Transform candidate in candidates)
            {
                string lower = candidate.name.ToLowerInvariant();
                float sink = lower.Contains("stone") || lower.Contains("rock") || lower.Contains("snowdrift")
                    ? -0.06f
                    : 0.015f;
                SnapItemToTerrain(candidate, terrain, sink);
            }

            int remainingIntrusions = CountRoadIntrusions(candidates, roadSamples, 0.32f, 0.5f, true);
            int remainingRouteGuideIntrusions = CountRoadIntrusions(routeGuides, roadSamples, 1.45f, 0.5f, true);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();

            Debug.Log(
                "[RoadFirstWorldRecomposer] Audited " + candidates.Count +
                " scene-wide roadside props. Initial road intrusions=" + initialIntrusions +
                ", moved=" + shifted + ", remaining=" + remainingIntrusions +
                "; route guides=" + routeGuides.Count +
                ", initial route-guide intrusions=" + initialRouteGuideIntrusions +
                ", moved farther=" + shiftedRouteGuides +
                ", resolved guide/prop overlaps=" + resolvedRouteGuideOverlaps +
                ", remaining route-guide intrusions=" + remainingRouteGuideIntrusions + ".");
        }

        [MenuItem(ApplyInteractionPropsMenuPath)]
        public static void ApplyInteractiveRoadsideProps()
        {
            Scene scene = RequireRoadFirstScene();
            GameObject barrelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RollingBarrelPrefabPath);
            GameObject hydrantPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BreakableHydrantPrefabPath);
            if (barrelPrefab == null || hydrantPrefab == null)
                throw new InvalidOperationException("Interactive barrel or hydrant prefab is missing.");

            Transform[] sceneTransforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            Transform[] legacyBarrels = sceneTransforms
                .Where(item => IsLegacyLooseProp(item, "barrel"))
                .OrderBy(GetHierarchyPath, StringComparer.Ordinal)
                .ToArray();
            Transform[] legacyHydrants = sceneTransforms
                .Where(item => IsLegacyLooseProp(item, "hydrant"))
                .OrderBy(GetHierarchyPath, StringComparer.Ordinal)
                .ToArray();

            int barrelIndex = CountSceneComponents<RollingBarrel>(scene);
            foreach (Transform source in legacyBarrels)
            {
                barrelIndex++;
                ReplaceLooseProp(source, barrelPrefab, scene, $"InteractiveBarrel_{barrelIndex:00}");
            }

            int hydrantIndex = CountSceneComponents<BreakableHydrant>(scene);
            foreach (Transform source in legacyHydrants)
            {
                hydrantIndex++;
                ReplaceLooseProp(source, hydrantPrefab, scene, $"InteractiveHydrant_{hydrantIndex:00}");
            }

            Physics.SyncTransforms();
            int barrelCount = CountSceneComponents<RollingBarrel>(scene);
            int hydrantCount = CountSceneComponents<BreakableHydrant>(scene);
            if (barrelCount < 3 || hydrantCount < 1)
                throw new InvalidOperationException(
                    $"Interactive roadside prop placement is incomplete: barrels={barrelCount}, hydrants={hydrantCount}.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[RoadFirstWorldRecomposer] Applied interactive roadside prefabs: barrels={barrelCount}, " +
                $"hydrants={hydrantCount}; replaced legacy barrels={legacyBarrels.Length}, " +
                $"legacy hydrants={legacyHydrants.Length}.");
        }

        private static bool IsLegacyLooseProp(Transform item, string baseName)
        {
            if (item == null || PrefabUtility.IsPartOfPrefabInstance(item.gameObject)) return false;
            string name = item.name.Trim();
            return name.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(baseName + " (", StringComparison.OrdinalIgnoreCase);
        }

        private static void ReplaceLooseProp(Transform source, GameObject prefab, Scene scene, string name)
        {
            Transform parent = source.parent;
            int siblingIndex = source.GetSiblingIndex();
            Bounds sourceBounds = CalculateBounds(source.gameObject);
            Vector3 sourcePosition = source.position;
            float sourceYaw = source.eulerAngles.y;

            GameObject replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            replacement.name = name;
            replacement.transform.SetParent(parent, true);
            replacement.transform.SetPositionAndRotation(
                sourcePosition, Quaternion.Euler(0f, sourceYaw, 0f));
            replacement.transform.localScale = Vector3.one;

            Bounds replacementBounds = CalculateBounds(replacement);
            replacement.transform.position += Vector3.up * (sourceBounds.min.y - replacementBounds.min.y);
            replacement.transform.SetSiblingIndex(siblingIndex);
            Undo.RegisterCreatedObjectUndo(replacement, "Apply interactive roadside prop");
            Undo.DestroyObjectImmediate(source.gameObject);
        }

        private static int CountSceneComponents<T>(Scene scene) where T : Component
        {
            int count = 0;
            foreach (T component in UnityEngine.Object.FindObjectsByType<T>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (component.gameObject.scene == scene) count++;
            return count;
        }

        private static bool IsRouteGuide(Transform item)
        {
            string lower = item.name.ToLowerInvariant();
            return lower.Contains("lamp") ||
                   lower.Contains("lantern") ||
                   lower.Contains("street light") ||
                   lower.Contains("streetlight") ||
                   lower.Contains("sign") ||
                   lower.Contains("hydrant");
        }

        private static int ResolveRouteGuideOverlaps(
            List<Transform> routeGuides,
            List<Transform> candidates,
            List<RoadSample> roads)
        {
            int movedCount = 0;
            Dictionary<Transform, Bounds> boundsCache = candidates.ToDictionary(
                candidate => candidate,
                candidate => CalculateBounds(candidate.gameObject));
            foreach (Transform guide in routeGuides)
            {
                Bounds guideBounds = boundsCache[guide];
                if (!HasRoadsideOverlap(guide, guideBounds, candidates, boundsCache)) continue;

                RoadSample nearest = roads
                    .OrderBy(sample => HorizontalSqrDistance(guideBounds.center, sample.Position))
                    .First();
                Vector3 tangent = new(nearest.Lateral.z, 0f, -nearest.Lateral.x);
                Vector3 originalPosition = guide.position;
                bool placed = false;
                for (int step = 1; step <= 10 && !placed; step++)
                {
                    float distance = step * 0.65f;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 offset = tangent * (distance * sign);
                        Bounds proposedBounds = guideBounds;
                        proposedBounds.center += offset;
                        if (!IsRoadClear(proposedBounds, roads, 1.45f, 0.5f) ||
                            HasRoadsideOverlap(guide, proposedBounds, candidates, boundsCache))
                            continue;

                        guide.position = originalPosition + offset;
                        boundsCache[guide] = proposedBounds;
                        movedCount++;
                        placed = true;
                        break;
                    }
                }
            }
            return movedCount;
        }

        private static bool HasRoadsideOverlap(
            Transform subject,
            Bounds subjectBounds,
            List<Transform> candidates,
            IReadOnlyDictionary<Transform, Bounds> boundsCache)
        {
            float subjectRadius = RoadsideFootprintRadius(subjectBounds);
            foreach (Transform other in candidates)
            {
                if (other == subject) continue;
                Bounds otherBounds = boundsCache[other];
                float required = subjectRadius + RoadsideFootprintRadius(otherBounds) + 0.35f;
                if (HorizontalSqrDistance(subjectBounds.center, otherBounds.center) < required * required)
                    return true;
            }
            return false;
        }

        private static float RoadsideFootprintRadius(Bounds bounds)
        {
            return Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.45f, 0.2f, 1.1f);
        }

        private static bool IsRoadClear(
            Bounds bounds,
            List<RoadSample> roads,
            float clearance,
            float footprintMultiplier)
        {
            float footprint = Mathf.Min(bounds.extents.x, bounds.extents.z) * footprintMultiplier;
            return roads.All(road =>
                HorizontalSqrDistance(bounds.center, road.Position) >=
                Mathf.Pow(road.ProtectedRadius + footprint + clearance, 2f));
        }

        private static void CaptureAndRemoveStaticSmoke(
            Transform[] originalItems,
            Dictionary<Transform, Vector3> sourceSmokeAnchors)
        {
            Transform[] districtHouses = originalItems
                .Where(item => item != null && IsHouse(item.name.ToLowerInvariant()))
                .ToArray();
            foreach (Transform smoke in originalItems.Where(item =>
                         item != null && item.name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                Transform nearestHouse = districtHouses
                    .OrderBy(house => HorizontalSqrDistance(
                        CalculateBounds(house.gameObject).center,
                        smoke.position))
                    .FirstOrDefault();
                if (nearestHouse != null)
                {
                    Vector3 localAnchor = nearestHouse.InverseTransformPoint(smoke.position);
                    if (!sourceSmokeAnchors.TryGetValue(nearestHouse, out Vector3 previous) || localAnchor.y < previous.y)
                        sourceSmokeAnchors[nearestHouse] = localAnchor;
                }
                Undo.DestroyObjectImmediate(smoke.gameObject);
            }
        }

        private static int AddConceptChimneySmoke(
            Scene scene,
            List<Transform> buildings,
            Dictionary<Transform, Vector3> sourceSmokeAnchors)
        {
            GameObject smokePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ConceptSmokePrefabPath);
            if (smokePrefab == null)
                throw new InvalidOperationException("ConceptMap chimney-smoke prefab was not found at " + ConceptSmokePrefabPath);

            int count = 0;
            foreach (Transform building in buildings)
            {
                string lower = building.name.ToLowerInvariant();
                if (lower.Contains("igloo") || lower.Contains("fairground")) continue;

                Vector3 anchor = sourceSmokeAnchors.TryGetValue(building, out Vector3 sourceLocal)
                    ? building.TransformPoint(sourceLocal)
                    : FindHeuristicChimneyAnchor(building);
                var smoke = (GameObject)PrefabUtility.InstantiatePrefab(smokePrefab, scene);
                smoke.name = "ConceptChimneySmoke";
                smoke.transform.SetPositionAndRotation(anchor, Quaternion.identity);
                smoke.transform.localScale = Vector3.one * 0.9f;
                smoke.transform.SetParent(building, true);
                count++;
            }
            return count;
        }

        private static Vector3 FindHeuristicChimneyAnchor(Transform building)
        {
            Bounds bounds = CalculateBounds(building.gameObject);
            int checksum = 0;
            for (int i = 0; i < building.name.Length; i++) checksum += building.name[i];
            float side = (checksum & 1) == 0 ? -0.22f : 0.22f;
            Vector3 anchor = bounds.center +
                             building.right * (bounds.size.x * side) +
                             building.forward * (bounds.size.z * 0.08f);
            anchor.y = bounds.max.y + 0.1f;
            return anchor;
        }

        private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static Transform CreateCategory(Transform district, string name)
        {
            var category = new GameObject(name).transform;
            category.SetParent(district, false);
            return category;
        }

        private static bool IsReferenceLand(Transform item, string lower)
        {
            return !item.gameObject.activeInHierarchy && lower.StartsWith("land ", StringComparison.Ordinal);
        }

        private static bool IsHouse(string lower)
        {
            return lower.Contains("house");
        }

        private static bool IsVendorSurfaceLeftover(string lower)
        {
            return lower.StartsWith("water location", StringComparison.Ordinal) ||
                   lower == "road" ||
                   lower == "road broken" ||
                   lower == "pedestrian road";
        }

        private static float GetHouseScale(string lower)
        {
            if (lower.Contains("ski house")) return 1.18f;
            if (lower.Contains("country winter house")) return 1.22f;
            if (lower.Contains("igloo house")) return 1.24f;
            if (lower.Contains("fairground")) return 1.34f;
            return 1.30f;
        }

        private static bool IsNature(string lower)
        {
            return lower.Contains("tree") ||
                   lower.Contains("stone") ||
                   lower.Contains("rock") ||
                   lower.Contains("snowdrift");
        }

        private static bool IsOverhead(string lower)
        {
            return lower.Contains("smoke") ||
                   lower.Contains("garland") ||
                   lower.Contains("wire") ||
                   lower.Contains("chairlift") ||
                   lower.Contains("electric pole") ||
                   lower.Contains("ski lift");
        }

        private static bool ShouldSnapToTerrain(string lower)
        {
            return !lower.Contains("bridge") &&
                   !lower.Contains("pier") &&
                   !lower.Contains("ice stalactite") &&
                   !lower.Contains("reflection");
        }

        private static void MoveSkiLodgeToSlopeBase(List<Transform> buildings)
        {
            Transform skiLodge = buildings.FirstOrDefault(item =>
                item.name.IndexOf("ski house", StringComparison.OrdinalIgnoreCase) >= 0);
            if (skiLodge == null) return;

            Vector3 position = skiLodge.position;
            position.x = 34.5f;
            position.z = 91f;
            skiLodge.position = position;
        }

        private static void ResolveBuildingOverlaps(List<Transform> buildings)
        {
            for (int pass = 0; pass < 5; pass++)
            {
                bool movedAny = false;
                for (int i = 0; i < buildings.Count; i++)
                {
                    Bounds a = CalculateBounds(buildings[i].gameObject);
                    for (int j = i + 1; j < buildings.Count; j++)
                    {
                        if (buildings[i].parent.parent != buildings[j].parent.parent) continue;
                        Bounds b = CalculateBounds(buildings[j].gameObject);
                        float overlapX = a.extents.x + b.extents.x + 1.5f - Mathf.Abs(a.center.x - b.center.x);
                        float overlapZ = a.extents.z + b.extents.z + 1.5f - Mathf.Abs(a.center.z - b.center.z);
                        if (overlapX <= 0f || overlapZ <= 0f) continue;

                        Vector3 shift;
                        if (overlapX < overlapZ)
                        {
                            float sign = b.center.x >= a.center.x ? 1f : -1f;
                            shift = new Vector3(sign * overlapX, 0f, 0f);
                        }
                        else
                        {
                            float sign = b.center.z >= a.center.z ? 1f : -1f;
                            shift = new Vector3(0f, 0f, sign * overlapZ);
                        }
                        buildings[j].position += shift;
                        movedAny = true;
                    }
                }
                if (!movedAny) break;
            }
        }

        private static List<RoadSample> BuildRoadSamples()
        {
            var result = new List<RoadSample>();
            foreach (TerrainRoadPath road in UnityEngine.Object.FindObjectsByType<TerrainRoadPath>())
            {
                var points = new List<Vector3>();
                road.GetWorldCenterPoints(points, 0.85f);
                float protectedRadius = road.Width * 0.5f + 0.65f;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 previous = points[Mathf.Max(0, i - 1)];
                    Vector3 next = points[Mathf.Min(points.Count - 1, i + 1)];
                    Vector3 tangent = next - previous;
                    tangent.y = 0f;
                    if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.forward;
                    tangent.Normalize();
                    Vector3 lateral = new(-tangent.z, 0f, tangent.x);
                    result.Add(new RoadSample(points[i], protectedRadius, lateral));
                }
            }
            return result;
        }

        private static void BuildContinuousSkiSlope(Terrain terrain, float[,] heights, List<RoadSample> roads)
        {
            TerrainData data = terrain.terrainData;
            int resolution = data.heightmapResolution;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            int minX = WorldToHeightIndex(24f, origin.x, size.x, resolution);
            int maxX = WorldToHeightIndex(67f, origin.x, size.x, resolution);
            int minZ = WorldToHeightIndex(62f, origin.z, size.z, resolution);
            int maxZ = WorldToHeightIndex(101f, origin.z, size.z, resolution);

            for (int z = minZ; z <= maxZ; z++)
            {
                float worldZ = HeightIndexToWorld(z, origin.z, size.z, resolution);
                float climb = Mathf.Clamp01(Mathf.InverseLerp(99f, 65f, worldZ));
                float desiredHeight = Mathf.Lerp(19.15f, 27.2f, climb);
                float centerX = Mathf.Lerp(46f, 50.5f, climb);
                float zEdge = Mathf.Min(
                    Smooth01(Mathf.InverseLerp(101f, 96f, worldZ)),
                    Smooth01(Mathf.InverseLerp(61f, 66f, worldZ)));
                float leftBase = origin.y + heights[z, minX] * size.y;
                float rightBase = origin.y + heights[z, maxX] * size.y;
                float surroundingHeight = (leftBase + rightBase) * 0.5f;

                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = HeightIndexToWorld(x, origin.x, size.x, resolution);
                    float lateralDistance = Mathf.Abs(worldX - centerX);
                    float ridgeBlend = 1f - Smooth01(Mathf.InverseLerp(5.5f, 20.5f, lateralDistance));
                    if (zEdge <= 0f || IsRoadProtected(worldX, worldZ, roads)) continue;

                    float currentHeight = origin.y + heights[z, x] * size.y;
                    float targetHeight = Mathf.Lerp(surroundingHeight, desiredHeight, ridgeBlend);
                    float finalHeight = Mathf.Lerp(currentHeight, targetHeight, zEdge);
                    heights[z, x] = Mathf.Clamp01((finalHeight - origin.y) / size.y);
                }
            }
        }

        private static int MoveItemsOffRoad(
            List<Transform> items,
            List<RoadSample> roads,
            float clearance,
            float footprintMultiplier)
        {
            int movedCount = 0;
            foreach (Transform item in items)
            {
                bool moved = false;
                for (int pass = 0; pass < 8; pass++)
                {
                    Bounds bounds = CalculateBounds(item.gameObject);
                    float footprint = Mathf.Min(bounds.extents.x, bounds.extents.z) * footprintMultiplier;
                    int nearestIndex = -1;
                    float deepestIntrusion = 0f;
                    for (int i = 0; i < roads.Count; i++)
                    {
                        float distance = Mathf.Sqrt(HorizontalSqrDistance(bounds.center, roads[i].Position));
                        float required = roads[i].ProtectedRadius + footprint + clearance;
                        float intrusion = required - distance;
                        if (intrusion <= deepestIntrusion) continue;
                        deepestIntrusion = intrusion;
                        nearestIndex = i;
                    }
                    if (nearestIndex < 0) break;

                    RoadSample nearest = roads[nearestIndex];
                    Vector3 direction = bounds.center - nearest.Position;
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 0.01f)
                    {
                        Transform district = item.parent != null ? item.parent.parent : null;
                        Vector3 districtDirection = district != null ? bounds.center - district.position : Vector3.zero;
                        float sign = Vector3.Dot(districtDirection, nearest.Lateral) >= 0f ? 1f : -1f;
                        direction = nearest.Lateral * sign;
                    }
                    if (direction.sqrMagnitude < 0.01f) direction = nearest.Lateral;
                    direction.Normalize();
                    item.position += direction * (deepestIntrusion + 0.03f);
                    moved = true;
                }

                if (moved) movedCount++;
            }
            return movedCount;
        }

        private static int CountRoadIntrusions(
            List<Transform> items,
            List<RoadSample> roads,
            float clearance,
            float footprintMultiplier,
            bool logDetails = true)
        {
            int count = 0;
            foreach (Transform item in items)
            {
                Bounds bounds = CalculateBounds(item.gameObject);
                float footprint = Mathf.Min(bounds.extents.x, bounds.extents.z) * footprintMultiplier;
                float deepestIntrusion = 0f;
                foreach (RoadSample road in roads)
                {
                    float distance = Mathf.Sqrt(HorizontalSqrDistance(bounds.center, road.Position));
                    deepestIntrusion = Mathf.Max(
                        deepestIntrusion,
                        road.ProtectedRadius + footprint + clearance - distance - 0.025f);
                }
                if (deepestIntrusion <= 0f) continue;

                count++;
                if (logDetails)
                {
                    Debug.LogWarning(
                        "[RoadFirstWorldRecomposer] Road intrusion remains for " + GetHierarchyPath(item) +
                        " at " + bounds.center.ToString("F2") + ", depth=" + deepestIntrusion.ToString("F2") + "m.");
                }
            }
            return count;
        }

        private static List<Transform> CollectSceneRoadsideProps(Scene scene)
        {
            var candidates = new HashSet<Transform>();
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            foreach (Transform item in sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!item.gameObject.activeInHierarchy || IsExcludedRoadsideHierarchy(item)) continue;
                string lower = item.name.ToLowerInvariant();
                if (!IsRoadsidePropName(lower)) continue;

                GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(item.gameObject);
                Transform placement = prefabRoot != null ? prefabRoot.transform : item;
                if (placement == null || IsExcludedRoadsideHierarchy(placement)) continue;
                if (placement.GetComponentsInChildren<Renderer>(false).All(renderer => renderer is ParticleSystemRenderer))
                    continue;
                candidates.Add(placement);
            }
            return candidates.OrderBy(GetHierarchyPath, StringComparer.Ordinal).ToList();
        }

        private static bool IsRoadsidePropName(string lower)
        {
            return lower.Contains("lamp") ||
                   lower.Contains("lantern") ||
                   lower.Contains("street light") ||
                   lower.Contains("streetlight") ||
                   lower.Contains("sign") ||
                   lower.Contains("tree") ||
                   lower.Contains("stone") ||
                   lower.Contains("rock") ||
                   lower.Contains("boulder") ||
                   lower.Contains("snowdrift") ||
                   lower.Contains("snowman") ||
                   lower.Contains("sled") ||
                   lower.Contains("sleigh") ||
                   lower.Contains("bench") ||
                   lower.Contains("barrel") ||
                   lower.Contains("trash") ||
                   lower.Contains("hydrant") ||
                   lower.Contains("crate") ||
                   lower.Contains("gift") ||
                   lower.Contains("present") ||
                   lower.Contains("shovel");
        }

        private static bool IsExcludedRoadsideHierarchy(Transform item)
        {
            if (item.GetComponentInParent<TerrainRoadPath>() != null) return true;
            for (Transform current = item; current != null; current = current.parent)
            {
                string lower = current.name.ToLowerInvariant();
                if (lower.Contains("easyroads") ||
                    lower.Contains("editableterrainroadnetwork") ||
                    lower.Contains("buildings_individual") ||
                    lower.Contains("overhead_keeprelative") ||
                    lower.Contains("riverandbridge") ||
                    lower.Contains("gameplay") ||
                    lower.Contains("playervehicle") ||
                    lower.Contains("chairlift") ||
                    lower.Contains("electric pole") ||
                    lower.Contains("wire"))
                    return true;
            }
            return false;
        }

        private static string GetHierarchyPath(Transform item)
        {
            var names = new Stack<string>();
            for (Transform current = item; current != null; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names);
        }

        private static void MoveSkiScenicRocks(List<Transform> natureItems)
        {
            foreach (Transform item in natureItems)
            {
                Transform district = item.parent != null ? item.parent.parent : null;
                if (district == null || district.name.IndexOf("09_SkiResort", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Vector3 position = item.position;
                if (item.name.Equals("stone winter large", StringComparison.OrdinalIgnoreCase))
                {
                    position.x = 59f;
                    position.z = 82f;
                    item.position = position;
                }
                else if (item.name.Equals("stone winter large 2", StringComparison.OrdinalIgnoreCase))
                {
                    position.x = 58f;
                    position.z = 69f;
                    item.position = position;
                }
            }
        }

        private static int ScaleOversizedDistrictRocks(List<Transform> natureItems)
        {
            int count = 0;
            foreach (Transform item in natureItems)
            {
                Transform district = item.parent != null ? item.parent.parent : null;
                if (district == null ||
                    (!district.name.Contains("08_NorthPoleCamp") && !district.name.Contains("09_SkiResort")))
                    continue;
                if (!item.name.StartsWith("stone winter large", StringComparison.OrdinalIgnoreCase)) continue;
                item.localScale *= 0.68f;
                count++;
            }
            return count;
        }

        private static void StampFlatBuildingPad(
            Terrain terrain,
            float[,] heights,
            Bounds bounds,
            float baseHeight,
            List<RoadSample> roads)
        {
            TerrainData data = terrain.terrainData;
            int resolution = data.heightmapResolution;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            float innerX = Mathf.Max(1f, bounds.extents.x + 0.55f);
            float innerZ = Mathf.Max(1f, bounds.extents.z + 0.55f);
            const float falloff = 4.5f;
            int minX = WorldToHeightIndex(bounds.center.x - innerX - falloff, origin.x, size.x, resolution);
            int maxX = WorldToHeightIndex(bounds.center.x + innerX + falloff, origin.x, size.x, resolution);
            int minZ = WorldToHeightIndex(bounds.center.z - innerZ - falloff, origin.z, size.z, resolution);
            int maxZ = WorldToHeightIndex(bounds.center.z + innerZ + falloff, origin.z, size.z, resolution);

            for (int z = minZ; z <= maxZ; z++)
            {
                float worldZ = HeightIndexToWorld(z, origin.z, size.z, resolution);
                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = HeightIndexToWorld(x, origin.x, size.x, resolution);
                    float dx = Mathf.Max(0f, Mathf.Abs(worldX - bounds.center.x) - innerX);
                    float dz = Mathf.Max(0f, Mathf.Abs(worldZ - bounds.center.z) - innerZ);
                    float blend = 1f - Smooth01(Mathf.Sqrt(dx * dx + dz * dz) / falloff);
                    if (blend <= 0f || IsRoadProtected(worldX, worldZ, roads)) continue;

                    float currentHeight = origin.y + heights[z, x] * size.y;
                    float finalHeight = Mathf.Lerp(currentHeight, baseHeight, blend);
                    heights[z, x] = Mathf.Clamp01((finalHeight - origin.y) / size.y);
                }
            }
        }

        private static bool IsRoadProtected(float worldX, float worldZ, List<RoadSample> roads)
        {
            for (int i = 0; i < roads.Count; i++)
            {
                float dx = worldX - roads[i].Position.x;
                float dz = worldZ - roads[i].Position.z;
                float radius = roads[i].ProtectedRadius;
                if (dx * dx + dz * dz <= radius * radius) return true;
            }
            return false;
        }

        private static void RepelNatureFromBuildings(List<Transform> natureItems, List<Transform> buildings)
        {
            foreach (Transform nature in natureItems)
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    Bounds itemBounds = CalculateBounds(nature.gameObject);
                    bool moved = false;
                    foreach (Transform building in buildings)
                    {
                        if (nature.parent.parent != building.parent.parent) continue;
                        Bounds buildingBounds = CalculateBounds(building.gameObject);
                        float clearance = nature.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ? 1.8f : 0.65f;
                        float dx = itemBounds.center.x - buildingBounds.center.x;
                        float dz = itemBounds.center.z - buildingBounds.center.z;
                        float overlapX = itemBounds.extents.x + buildingBounds.extents.x + clearance - Mathf.Abs(dx);
                        float overlapZ = itemBounds.extents.z + buildingBounds.extents.z + clearance - Mathf.Abs(dz);
                        if (overlapX <= 0f || overlapZ <= 0f) continue;

                        if (overlapX < overlapZ)
                            nature.position += new Vector3((dx >= 0f ? 1f : -1f) * overlapX, 0f, 0f);
                        else
                            nature.position += new Vector3(0f, 0f, (dz >= 0f ? 1f : -1f) * overlapZ);
                        moved = true;
                        break;
                    }
                    if (!moved) break;
                }
            }
        }

        private static bool SnapItemToTerrain(Transform item, Terrain terrain, float verticalOffset)
        {
            Bounds bounds = CalculateBounds(item.gameObject);
            float ground = terrain.SampleHeight(bounds.center) + terrain.transform.position.y;
            float delta = ground + verticalOffset - bounds.min.y;
            if (Mathf.Abs(delta) <= 0.025f) return false;
            item.position += Vector3.up * delta;
            return true;
        }

        private static float SampleWorldHeight(Terrain terrain, float[,] heights, float worldX, float worldZ)
        {
            TerrainData data = terrain.terrainData;
            int resolution = data.heightmapResolution;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            float fx = Mathf.Clamp01((worldX - origin.x) / size.x) * (resolution - 1);
            float fz = Mathf.Clamp01((worldZ - origin.z) / size.z) * (resolution - 1);
            int x0 = Mathf.FloorToInt(fx);
            int z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, resolution - 1);
            int z1 = Mathf.Min(z0 + 1, resolution - 1);
            float tx = fx - x0;
            float tz = fz - z0;
            float h0 = Mathf.Lerp(heights[z0, x0], heights[z0, x1], tx);
            float h1 = Mathf.Lerp(heights[z1, x0], heights[z1, x1], tx);
            return origin.y + Mathf.Lerp(h0, h1, tz) * size.y;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false)
                .Where(renderer => !(renderer is ParticleSystemRenderer))
                .ToArray();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one * 0.2f);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static int WorldToHeightIndex(float world, float origin, float size, int resolution)
        {
            return Mathf.Clamp(Mathf.FloorToInt((world - origin) / size * (resolution - 1)), 0, resolution - 1);
        }

        private static float HeightIndexToWorld(int index, float origin, float size, int resolution)
        {
            return origin + index / (float)(resolution - 1) * size;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static Scene RequireRoadFirstScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
                throw new InvalidOperationException("Open WinterVillage_RoadFirstWorld before running this command.");
            return scene;
        }
    }
}

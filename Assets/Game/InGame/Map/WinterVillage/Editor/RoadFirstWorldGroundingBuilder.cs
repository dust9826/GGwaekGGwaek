using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack.InGame.Editor
{
    internal static class RoadFirstWorldGroundingBuilder
    {
        private const string ScenePath = "Assets/Game/InGame/Map/WinterVillage/Scenes/WinterVillage_RoadFirstWorld.unity";
        private const string DistrictRootName = "Districts_LowPolyWinter_NoVendorLand";
        private const string GroundMenuPath = "PPack/Map/Road First World/Ground All Districts To Reference Terrain";
        private const string CaptureMenuPath = "PPack/Map/Road First World/Capture District Grounding QA";
        private const string PreviewRoot = "Assets/Game/InGame/Map/WinterVillage/Preview/RoadFirstWorld/GroundingQA";

        private sealed class GroundingResult
        {
            public string DistrictName;
            public int SampleCount;
            public float MaximumRaise;
            public float MaximumLower;
        }

        [MenuItem(GroundMenuPath)]
        public static void GroundAllDistricts()
        {
            Scene scene = RequireRoadFirstScene();
            Terrain terrain = Terrain.activeTerrain;
            GameObject districtRoot = GameObject.Find(DistrictRootName);
            if (terrain == null || districtRoot == null)
            {
                throw new InvalidOperationException("Road First World terrain or district root was not found.");
            }

            TerrainData terrainData = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(terrainData, "Ground Road First World districts");
            float[,] heights = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);
            var results = new List<GroundingResult>();

            foreach (Transform district in districtRoot.transform)
            {
                MeshFilter referenceLand = district.GetComponentsInChildren<MeshFilter>(true)
                    .FirstOrDefault(filter =>
                        !filter.gameObject.activeInHierarchy &&
                        filter.sharedMesh != null &&
                        filter.name.StartsWith("land ", StringComparison.OrdinalIgnoreCase));
                if (referenceLand == null)
                {
                    Debug.LogWarning("[RoadFirstWorldGrounding] No inactive reference land found for " + district.name);
                    continue;
                }

                results.Add(BlendReferenceLandIntoTerrain(terrain, referenceLand, heights, district.name));
            }

            terrainData.SetHeights(0, 0, heights);
            terrain.Flush();
            EditorUtility.SetDirty(terrainData);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            string summary = string.Join("; ", results.Select(result =>
                result.DistrictName + " samples=" + result.SampleCount +
                " raise=" + result.MaximumRaise.ToString("F2") + "m" +
                " lower=" + result.MaximumLower.ToString("F2") + "m"));
            Debug.Log("[RoadFirstWorldGrounding] Grounded " + results.Count + " districts against their inactive vendor-land height references. " + summary);
        }

        [MenuItem(CaptureMenuPath)]
        public static void CaptureDistrictGroundingQa()
        {
            Scene scene = RequireRoadFirstScene();
            Terrain terrain = Terrain.activeTerrain;
            GameObject districtRoot = GameObject.Find(DistrictRootName);
            Camera camera = GameObject.Find("ConceptOverviewCamera")?.GetComponent<Camera>();
            if (terrain == null || districtRoot == null || camera == null)
            {
                throw new InvalidOperationException("Road First World terrain, districts, or overview camera was not found.");
            }

            EnsureFolder(PreviewRoot);
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            bool originalOrthographic = camera.orthographic;
            float originalFieldOfView = camera.fieldOfView;
            ParticleSystem[] conceptSmoke = districtRoot.GetComponentsInChildren<ParticleSystem>(true)
                .Where(system => system.name == "ConceptChimneySmoke")
                .ToArray();

            try
            {
                foreach (ParticleSystem smoke in conceptSmoke)
                    smoke.Simulate(6f, true, true, false);
                CaptureCameraPng(camera, PreviewRoot + "/00_GroundedOverview.png");
                camera.orthographic = false;
                camera.fieldOfView = 43f;
                int index = 1;
                foreach (Transform district in districtRoot.transform.Cast<Transform>().OrderBy(item => item.name, StringComparer.Ordinal))
                {
                    Bounds bounds = CalculateActiveRendererBounds(district.gameObject);
                    float ground = terrain.SampleHeight(district.position) + terrain.transform.position.y;
                    Vector3 target = new Vector3(bounds.center.x, ground + 2.2f, bounds.center.z);
                    Vector3 forwardOffset = district.rotation * new Vector3(-28f, 0f, -31f);
                    camera.transform.position = target + forwardOffset + Vector3.up * 10.5f;
                    camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
                    CaptureCameraPng(camera, PreviewRoot + "/" + index.ToString("00") + "_" + district.name + "_LowAngle.png");

                    if (district.name.IndexOf("SkiResort", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Vector3 reverseOffset = district.rotation * new Vector3(29f, 0f, 30f);
                        camera.transform.position = target + reverseOffset + Vector3.up * 11.5f;
                        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
                        CaptureCameraPng(camera, PreviewRoot + "/" + index.ToString("00") + "_" + district.name + "_ReverseLowAngle.png");
                    }
                    index++;
                }
            }
            finally
            {
                foreach (ParticleSystem smoke in conceptSmoke)
                    smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                camera.transform.position = originalPosition;
                camera.transform.rotation = originalRotation;
                camera.orthographic = originalOrthographic;
                camera.fieldOfView = originalFieldOfView;
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.Refresh();
            }

            Debug.Log("[RoadFirstWorldGrounding] Captured the grounded overview, nine district low-angle checks, and a reverse ski-resort view at " + PreviewRoot);
        }

        private static GroundingResult BlendReferenceLandIntoTerrain(
            Terrain terrain,
            MeshFilter referenceLand,
            float[,] heights,
            string districtName)
        {
            TerrainData data = terrain.terrainData;
            int resolution = data.heightmapResolution;
            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = data.size;

            var sampler = new GameObject("__RoadFirstReferenceLandSampler");
            sampler.hideFlags = HideFlags.HideAndDontSave;
            sampler.transform.SetPositionAndRotation(referenceLand.transform.position, referenceLand.transform.rotation);
            sampler.transform.localScale = referenceLand.transform.lossyScale;
            MeshCollider collider = sampler.AddComponent<MeshCollider>();
            collider.sharedMesh = referenceLand.sharedMesh;
            Physics.SyncTransforms();

            var result = new GroundingResult { DistrictName = districtName };
            try
            {
                Bounds bounds = collider.bounds;
                int minX = WorldToHeightIndex(bounds.min.x, terrainPosition.x, terrainSize.x, resolution);
                int maxX = WorldToHeightIndex(bounds.max.x, terrainPosition.x, terrainSize.x, resolution);
                int minZ = WorldToHeightIndex(bounds.min.z, terrainPosition.z, terrainSize.z, resolution);
                int maxZ = WorldToHeightIndex(bounds.max.z, terrainPosition.z, terrainSize.z, resolution);
                float rayStart = bounds.max.y + 8f;
                float rayLength = bounds.size.y + 20f;

                for (int z = minZ; z <= maxZ; z++)
                {
                    float worldZ = HeightIndexToWorld(z, terrainPosition.z, terrainSize.z, resolution);
                    for (int x = minX; x <= maxX; x++)
                    {
                        float worldX = HeightIndexToWorld(x, terrainPosition.x, terrainSize.x, resolution);
                        var ray = new Ray(new Vector3(worldX, rayStart, worldZ), Vector3.down);
                        if (!collider.Raycast(ray, out RaycastHit hit, rayLength) || hit.normal.y < 0.22f)
                        {
                            continue;
                        }

                        float normalizedX = Mathf.Abs((worldX - bounds.center.x) / Mathf.Max(0.01f, bounds.extents.x));
                        float normalizedZ = Mathf.Abs((worldZ - bounds.center.z) / Mathf.Max(0.01f, bounds.extents.z));
                        float edgeDistance = Mathf.Max(normalizedX, normalizedZ);
                        float blend = 1f - Smooth01(Mathf.InverseLerp(0.74f, 0.985f, edgeDistance));
                        if (blend <= 0f)
                        {
                            continue;
                        }

                        float currentWorldHeight = terrainPosition.y + heights[z, x] * terrainSize.y;
                        float desiredWorldHeight = Mathf.Clamp(hit.point.y, terrainPosition.y, terrainPosition.y + terrainSize.y);
                        float delta = desiredWorldHeight - currentWorldHeight;
                        float blendedWorldHeight = Mathf.Lerp(currentWorldHeight, desiredWorldHeight, blend);
                        heights[z, x] = Mathf.Clamp01((blendedWorldHeight - terrainPosition.y) / terrainSize.y);
                        result.SampleCount++;
                        result.MaximumRaise = Mathf.Max(result.MaximumRaise, delta);
                        result.MaximumLower = Mathf.Max(result.MaximumLower, -delta);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sampler);
            }

            return result;
        }

        private static Bounds CalculateActiveRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false)
                .Where(renderer => !(renderer is ParticleSystemRenderer))
                .ToArray();
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        private static int WorldToHeightIndex(float world, float terrainOrigin, float terrainSize, int resolution)
        {
            return Mathf.Clamp(Mathf.FloorToInt((world - terrainOrigin) / terrainSize * (resolution - 1)), 0, resolution - 1);
        }

        private static float HeightIndexToWorld(int index, float terrainOrigin, float terrainSize, int resolution)
        {
            return terrainOrigin + index / (float)(resolution - 1) * terrainSize;
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
            {
                throw new InvalidOperationException("Open WinterVillage_RoadFirstWorld before running this command.");
            }
            return scene;
        }

        private static void CaptureCameraPng(Camera camera, string assetPath)
        {
            const int width = 1600;
            const int height = 900;
            RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                var screenshot = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                screenshot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                screenshot.Apply(false, false);
                string absolutePath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(absolutePath, screenshot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(screenshot);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}

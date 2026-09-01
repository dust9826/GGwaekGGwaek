using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    public sealed class PlacementToolTests
    {
        private const string FixturePrefabPath = "Assets/Game/InGame/Map/Editor/LevelDesign/Tests/EditMode/__TEST__PlacementFixture.prefab";

        private GameObject _fixturePrefab;

        [SetUp]
        public void SetUp()
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _fixturePrefab = PrefabUtility.SaveAsPrefabAsset(source, FixturePrefabPath);
            Object.DestroyImmediate(source);
            Assert.That(_fixturePrefab, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            PlacementTool.SelectPrefab(null);
            AssetDatabase.DeleteAsset(FixturePrefabPath);
        }

        [Test]
        public void PlacePrefab_KeepsConnectionAndSupportsUndo()
        {
            Vector3 position = new Vector3(3f, 2f, -4f);

            GameObject instance = PlacementTool.PlacePrefab(_fixturePrefab, position);

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.transform.position, Is.EqualTo(position));
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(instance), Is.EqualTo(PrefabInstanceStatus.Connected));

            Undo.PerformUndo();
            Assert.That(instance == null, Is.True);
        }

        [Test]
        public void PlacePrefab_AppliesPreviewRotationToInstance()
        {
            Vector3 position = new Vector3(3f, 2f, -4f);
            Quaternion rotation = PlacementTool.GetPlacementRotation(_fixturePrefab, 75f);

            GameObject instance = PlacementTool.PlacePrefab(
                _fixturePrefab,
                position,
                rotation,
                SceneManager.GetActiveScene());

            Assert.That(instance, Is.Not.Null);
            Assert.That(Quaternion.Angle(instance.transform.rotation, rotation), Is.LessThan(0.01f));
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(instance), Is.EqualTo(PrefabInstanceStatus.Connected));

            Undo.PerformUndo();
            Assert.That(instance == null, Is.True);
        }

        [Test]
        public void PlacePrefabWithSidewalk_CreatesEasyRoadAndUndoesTogether()
        {
            TerrainData terrainData = new TerrainData
            {
                heightmapResolution = 33,
                alphamapResolution = 32,
                size = new Vector3(20f, 10f, 20f)
            };
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.transform.position = new Vector3(4300f, 0f, 4300f);
            GameObject instance = null;
            TerrainRoadPath sidewalk = null;

            try
            {
                List<Vector3> previewPath = new()
                {
                    new Vector3(4305f, 0.03f, 4304f),
                    new Vector3(4305f, 0.03f, 4310f)
                };

                instance = PlacementTool.PlacePrefabWithSidewalk(
                    _fixturePrefab,
                    new Vector3(4305f, 0f, 4304f),
                    Quaternion.identity,
                    SceneManager.GetActiveScene(),
                    previewPath,
                    1.4f,
                    out sidewalk);

                Assert.That(instance, Is.Not.Null);
                Assert.That(sidewalk, Is.Not.Null);
                Assert.That(sidewalk.name, Is.EqualTo($"Sidewalk_From_{instance.name}"));
                Assert.That(sidewalk.Width, Is.EqualTo(1.4f).Within(0.001f));
                Assert.That(sidewalk.HasEditableSpline, Is.True);
                Assert.That(EasyRoadAuthoring.IsEasyRoadSource(sidewalk), Is.True);
                EasyRoadSource source = sidewalk.GetComponent<EasyRoadSource>();
                Assert.That(source, Is.Not.Null);
                Assert.That(source.Template, Is.EqualTo(EasyRoadBuilderPreferences.Template));

                Undo.PerformUndo();
                Assert.That(instance == null, Is.True);
                Assert.That(sidewalk == null, Is.True);
            }
            finally
            {
                if (sidewalk != null) Object.DestroyImmediate(sidewalk.gameObject);
                if (instance != null) Object.DestroyImmediate(instance);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
            }
        }

        [Test]
        public void TerrainFlattenPlan_UsesPreviewRotationForFootprintAxes()
        {
            TerrainData terrainData = new TerrainData
            {
                heightmapResolution = 33,
                size = new Vector3(32f, 10f, 32f)
            };
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            try
            {
                Quaternion rotation = PlacementTool.GetPlacementRotation(_fixturePrefab, 90f);
                bool planned = TerrainFlattenPlan.TryCreate(
                    terrainObject.GetComponent<Terrain>(),
                    _fixturePrefab,
                    new Vector3(16f, 2f, 16f),
                    rotation,
                    out TerrainFlattenPlan plan);

                Assert.That(planned, Is.True);
                Assert.That(Mathf.Abs(Vector3.Dot(plan.Right, Vector3.forward)), Is.GreaterThan(0.99f));
                Assert.That(Mathf.Abs(Vector3.Dot(plan.Forward, Vector3.right)), Is.GreaterThan(0.99f));
            }
            finally
            {
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
            }
        }

        [Test]
        public void PlacementPreview_DoesNotCreateSceneObjects()
        {
            Scene testScene = SceneManager.GetActiveScene();
            int rootCountBefore = testScene.rootCount;
            bool dirtyBefore = testScene.isDirty;

            using (PlacementPreview preview = new PlacementPreview())
            {
                preview.SetPrefab(_fixturePrefab);
                Assert.That(preview.MeshCount, Is.GreaterThan(0));
            }

            Assert.That(testScene.rootCount, Is.EqualTo(rootCountBefore));
            Assert.That(testScene.isDirty, Is.EqualTo(dirtyBefore));
        }

        [Test]
        public void SceneRaycaster_HitsColliderAndIgnoresTriggers()
        {
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                surface.transform.position = Vector3.zero;
                Collider surfaceCollider = surface.GetComponent<Collider>();
                Assert.That(surfaceCollider, Is.Not.Null);
                Physics.SyncTransforms();

                bool hitSurface = SceneRaycaster.TryGetSurfaceHit(
                    new Ray(Vector3.up * 5f, Vector3.down),
                    out RaycastHit hit);

                Assert.That(hitSurface, Is.True);
                Assert.That(hit.collider.gameObject, Is.EqualTo(surface));

                surfaceCollider.isTrigger = true;
                Physics.SyncTransforms();
                bool hitTrigger = SceneRaycaster.TryGetSurfaceHit(
                    new Ray(Vector3.up * 5f, Vector3.down),
                    out _);

                Assert.That(hitTrigger, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(surface);
            }
        }

        [Test]
        public void PlacePrefabAndFlattenTerrain_ChangesHeightAndUndoesAsOneAction()
        {
            TerrainData terrainData = new TerrainData
            {
                heightmapResolution = 33,
                size = new Vector3(32f, 10f, 32f)
            };
            float[,] slope = new float[33, 33];
            for (int z = 0; z < 33; z++)
            {
                for (int x = 0; x < 33; x++)
                {
                    slope[z, x] = x / 32f * 0.6f;
                }
            }
            terrainData.SetHeights(0, 0, slope);

            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            try
            {
                Terrain terrain = terrainObject.GetComponent<Terrain>();
                float originalCenterHeight = terrainData.GetHeight(16, 16);
                Vector3 hitPoint = new Vector3(16f, 2f, 16f);

                bool planned = TerrainFlattenPlan.TryCreate(
                    terrain,
                    _fixturePrefab,
                    hitPoint,
                    out TerrainFlattenPlan plan);

                Assert.That(planned, Is.True);
                Assert.That(plan, Is.Not.Null);
                Assert.That(plan.MaximumAdjustment, Is.GreaterThan(0f));
                Assert.That(terrainData.GetHeight(16, 16), Is.EqualTo(originalCenterHeight).Within(0.001f));

                Scene testScene = SceneManager.GetActiveScene();
                int rootCountBeforePreview = testScene.rootCount;
                bool dirtyBeforePreview = testScene.isDirty;
                using (TerrainFlattenPreview preview = new TerrainFlattenPreview())
                {
                    preview.SetPlan(plan);
                }
                Assert.That(testScene.rootCount, Is.EqualTo(rootCountBeforePreview));
                Assert.That(testScene.isDirty, Is.EqualTo(dirtyBeforePreview));
                Assert.That(terrainData.GetHeight(16, 16), Is.EqualTo(originalCenterHeight).Within(0.001f));

                GameObject instance = PlacementTool.PlacePrefabAndFlattenTerrain(
                    _fixturePrefab,
                    plan,
                    SceneManager.GetActiveScene());

                Assert.That(instance, Is.Not.Null);
                Assert.That(terrainData.GetHeight(16, 16), Is.EqualTo(hitPoint.y).Within(0.05f));

                Undo.PerformUndo();
                terrainData.SyncHeightmap();
                Assert.That(instance == null, Is.True);
                Assert.That(terrainData.GetHeight(16, 16), Is.EqualTo(originalCenterHeight).Within(0.05f));
            }
            finally
            {
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
            }
        }
    }
}

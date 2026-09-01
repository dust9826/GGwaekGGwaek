using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.TestTools.Utils;

namespace PPack
{
    public sealed class RoadBuilderTests
    {
        [Test]
        public void TerrainRoad_CreatesMetadataWithoutMeshAndUndoesAsOneAction()
        {
            TerrainData terrainData = CreateTerrainData(32, 20f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.name = "__TEST__Terrain";
            terrainObject.transform.position = new Vector3(4100f, 0f, 4100f);
            List<Vector3> points = new()
            {
                new Vector3(4100f, 3f, 4100f),
                new Vector3(4100f, 3.2f, 4105f),
                new Vector3(4103f, 3.4f, 4110f)
            };

            try
            {
                TerrainRoadPath road = RoadBuilderTool.CreateRoad(
                    points,
                    3f,
                    "__TEST__Road",
                    SceneManager.GetActiveScene());

                Assert.That(road, Is.Not.Null);
                Assert.That(road.Width, Is.EqualTo(3f).Within(0.001f));
                Assert.That(road.HasEditableSpline, Is.True);
                Assert.That(road.GetComponent<SplineContainer>(), Is.Not.Null);
                Assert.That(road.GetComponent<MeshFilter>(), Is.Null);
                Assert.That(road.GetComponent<MeshRenderer>(), Is.Null);
                Assert.That(road.GetComponent<MeshCollider>(), Is.Null);

                Undo.PerformUndo();
                Assert.That(road == null, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
            }
        }

        [Test]
        public void RoadConnection_SnapsToMiddleOfExistingRoad()
        {
            TerrainData terrainData = CreateTerrainData(32, 20f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            TerrainLayer layer = new TerrainLayer();
            TerrainLayer borderLayer = new TerrainLayer();
            GameObject roadObject = new GameObject("__TEST__SnapRoad");
            TerrainRoadPath road = roadObject.AddComponent<TerrainRoadPath>();
            road.Configure(
                new[]
                {
                    new Vector3(2f, 0f, 10f),
                    new Vector3(12f, 0f, 10f)
                },
                4f,
                0.1f,
                0.55f,
                0.08f,
                terrain,
                layer,
                borderLayer);

            try
            {
                bool snapped = RoadConnectionUtility.TrySnapToRoad(
                    new Vector3(7f, 0f, 11f),
                    null,
                    out RoadSnapResult result);

                Assert.That(snapped, Is.True);
                Assert.That(result.TargetRoad, Is.EqualTo(road));
                Assert.That(result.Point.x, Is.EqualTo(7f).Within(0.01f));
                Assert.That(result.Point.z, Is.EqualTo(10f).Within(0.01f));
                Assert.That(result.TargetWidth, Is.EqualTo(4f).Within(0.001f));
                Assert.That(result.IsEndpoint, Is.False);
                Assert.That(Mathf.Abs(Vector3.Dot(result.Tangent, Vector3.right)), Is.GreaterThan(0.99f));
            }
            finally
            {
                Object.DestroyImmediate(roadObject);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
                Object.DestroyImmediate(layer);
                Object.DestroyImmediate(borderLayer);
            }
        }

        [Test]
        public void RoadConnection_UsesRoadEdgeInsteadOfOnlyCenterLine()
        {
            TerrainData terrainData = CreateTerrainData(32, 20f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.transform.position = new Vector3(4100f, 0f, 4100f);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            TerrainLayer layer = new();
            TerrainLayer borderLayer = new();
            GameObject roadObject = new("__TEST__WideSnapRoad");
            TerrainRoadPath road = roadObject.AddComponent<TerrainRoadPath>();
            road.Configure(
                new[] { new Vector3(4102f, 0f, 4110f), new Vector3(4112f, 0f, 4110f) },
                8f,
                0.1f,
                0.55f,
                0.08f,
                terrain,
                layer,
                borderLayer);

            try
            {
                bool nearEdge = RoadConnectionUtility.TrySnapToRoad(
                    new Vector3(4107f, 0f, 4115.5f),
                    null,
                    out RoadSnapResult result);
                bool beyondMargin = RoadConnectionUtility.TrySnapToRoad(
                    new Vector3(4107f, 0f, 4116f),
                    null,
                    out RoadSnapResult _);

                Assert.That(nearEdge, Is.True);
                Assert.That(result.TargetRoad, Is.EqualTo(road));
                Assert.That(result.Point.z, Is.EqualTo(4110f).Within(0.01f));
                Assert.That(beyondMargin, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(roadObject);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
                Object.DestroyImmediate(layer);
                Object.DestroyImmediate(borderLayer);
            }
        }

        [Test]
        public void EasyRoadConnection_TJunctionUsesWidthScaledOverlapInsideTargetEdge()
        {
            TerrainData terrainData = CreateTerrainData(32, 20f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            TerrainLayer layer = new();
            TerrainLayer borderLayer = new();
            GameObject roadObject = new("__TEST__ConnectionTarget");
            TerrainRoadPath targetRoad = roadObject.AddComponent<TerrainRoadPath>();
            targetRoad.Configure(
                new[] { new Vector3(2f, 0f, 10f), new Vector3(12f, 0f, 10f) },
                4f,
                0.1f,
                0.55f,
                0.08f,
                terrain,
                layer,
                borderLayer);
            List<Vector3> targetBefore = new();
            targetRoad.GetWorldControlPoints(targetBefore);
            List<Vector3> newRoad = new()
            {
                new Vector3(7f, 0f, 10f),
                new Vector3(7f, 0f, 15f)
            };
            RoadSnapResult connection = new(
                new Vector3(7f, 0f, 10f),
                targetRoad,
                Vector3.right,
                false);

            try
            {
                List<Vector3> connected = EasyRoadAuthoring.BuildConnectedControlPoints(
                    newRoad,
                    connection,
                    null);
                List<Vector3> targetAfter = new();
                targetRoad.GetWorldControlPoints(targetAfter);

                Assert.That(newRoad[0].z, Is.EqualTo(10f).Within(0.001f));
                Assert.That(connected[0].z, Is.EqualTo(11.28f).Within(0.001f));
                Assert.That(connected.Count, Is.EqualTo(2));
                Assert.That(connected[1].z, Is.GreaterThan(connected[0].z));
                CollectionAssert.AreEqual(targetBefore, targetAfter);
            }
            finally
            {
                Object.DestroyImmediate(roadObject);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
                Object.DestroyImmediate(layer);
                Object.DestroyImmediate(borderLayer);
            }
        }

        [Test]
        public void EasyRoadConnection_EndToEndUsesWidthScaledOverlap()
        {
            TerrainData terrainData = CreateTerrainData(32, 20f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            TerrainLayer layer = new();
            TerrainLayer borderLayer = new();
            GameObject roadObject = new("__TEST__EndpointTarget");
            TerrainRoadPath targetRoad = roadObject.AddComponent<TerrainRoadPath>();
            targetRoad.Configure(
                new[] { new Vector3(2f, 0f, 10f), new Vector3(12f, 0f, 10f) },
                4f,
                0.1f,
                0.55f,
                0.08f,
                terrain,
                layer,
                borderLayer);
            RoadSnapResult connection = new(
                new Vector3(12f, 0f, 10f),
                targetRoad,
                Vector3.right,
                true);

            try
            {
                List<Vector3> connected = EasyRoadAuthoring.BuildConnectedControlPoints(
                    new[]
                    {
                        new Vector3(12f, 0f, 10f),
                        new Vector3(16f, 0f, 10f)
                    },
                    connection,
                    null);

                Assert.That(connected[0].x, Is.EqualTo(11.28f).Within(0.001f));
                Assert.That(connected[1].x, Is.GreaterThan(connected[0].x));
            }
            finally
            {
                Object.DestroyImmediate(roadObject);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
                Object.DestroyImmediate(layer);
                Object.DestroyImmediate(borderLayer);
            }
        }

        [Test]
        public void RoadEndpointConnector_BlendsBothRoadsIntoOneContinuousCorner()
        {
            List<Vector3> horizontal = new()
            {
                new Vector3(-8f, 2f, 0f),
                new Vector3(-4f, 2f, 0f),
                new Vector3(-0.4f, 2f, 0f)
            };
            List<Vector3> vertical = new()
            {
                new Vector3(0f, 2.2f, 8f),
                new Vector3(0f, 2.2f, 4f),
                new Vector3(0f, 2.2f, 0.5f)
            };

            bool connected = RoadEndpointConnector.TryBuildSmoothedConnection(
                horizontal,
                4f,
                vertical,
                4f,
                out List<Vector3> horizontalResult,
                out List<Vector3> verticalResult,
                out RoadEndpointConnector.RoadEnd horizontalEnd,
                out RoadEndpointConnector.RoadEnd verticalEnd,
                out string error);

            Assert.That(connected, Is.True, error);
            Assert.That(horizontalEnd, Is.EqualTo(RoadEndpointConnector.RoadEnd.End));
            Assert.That(verticalEnd, Is.EqualTo(RoadEndpointConnector.RoadEnd.End));
            Assert.That(horizontalResult[^1], Is.EqualTo(verticalResult[^1]).Using(Vector3ComparerWithEqualsOperator.Instance));

            Vector3 horizontalOutward = (horizontalResult[^1] - horizontalResult[^2]).normalized;
            Vector3 verticalOutward = (verticalResult[^1] - verticalResult[^2]).normalized;
            Assert.That(Vector3.Dot(horizontalOutward, verticalOutward), Is.LessThan(-0.99f));
            Assert.That(horizontalResult[0], Is.EqualTo(horizontal[0]).Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(verticalResult[0], Is.EqualTo(vertical[0]).Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [Test]
        public void RoadEndpointConnector_RejectsTwoBranchesOpeningToSameSide()
        {
            List<Vector3> upper = new()
            {
                Vector3.zero,
                new Vector3(5f, 0f, 1f),
                new Vector3(10f, 0f, 2f)
            };
            List<Vector3> lower = new()
            {
                Vector3.zero,
                new Vector3(5f, 0f, -1f),
                new Vector3(10f, 0f, -2f)
            };

            bool connected = RoadEndpointConnector.TryBuildSmoothedConnection(
                upper,
                4f,
                lower,
                4f,
                out _,
                out _,
                out _,
                out _,
                out string error);

            Assert.That(connected, Is.False);
            StringAssert.Contains("branch", error.ToLowerInvariant());
        }

        [Test]
        public void TerrainPainter_IntersectionsUnionAndDarkBorderStayNormalized()
        {
            TerrainData terrainData = CreateTerrainData(64, 20f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            TerrainLayer baseLayer = new TerrainLayer();
            TerrainLayer roadLayer = new TerrainLayer();
            TerrainLayer borderLayer = new TerrainLayer();
            terrainData.terrainLayers = new[] { baseLayer };
            float[,,] baseWeights = new float[terrainData.alphamapHeight, terrainData.alphamapWidth, 1];
            for (int z = 0; z < terrainData.alphamapHeight; z++)
                for (int x = 0; x < terrainData.alphamapWidth; x++)
                    baseWeights[z, x, 0] = 1f;
            terrainData.SetAlphamaps(0, 0, baseWeights);
            GameObject horizontalObject = new("__TEST__HorizontalRoad");
            GameObject verticalObject = new("__TEST__VerticalRoad");
            TerrainRoadPath horizontal = horizontalObject.AddComponent<TerrainRoadPath>();
            TerrainRoadPath vertical = verticalObject.AddComponent<TerrainRoadPath>();
            horizontal.Configure(
                new[] { new Vector3(2f, 0f, 10f), new Vector3(18f, 0f, 10f) },
                4f,
                0.1f,
                0.75f,
                0.08f,
                terrain,
                roadLayer,
                borderLayer);
            vertical.Configure(
                new[] { new Vector3(10f, 0f, 2f), new Vector3(10f, 0f, 18f) },
                4f,
                0.1f,
                0.75f,
                0.08f,
                terrain,
                roadLayer,
                borderLayer);

            try
            {
                Assert.That(TerrainRoadPainter.RebuildTerrainRoads(
                    terrain,
                    roadLayer,
                    borderLayer,
                    new[] { horizontal, vertical },
                    "__TEST__ Global Road Union",
                    false), Is.True);

                float[,,] weights = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);
                int center = Mathf.RoundToInt(10f / 20f * (terrainData.alphamapWidth - 1));
                int borderX = Mathf.RoundToInt(4f / 20f * (terrainData.alphamapWidth - 1));
                int borderZ = Mathf.RoundToInt(12.5f / 20f * (terrainData.alphamapHeight - 1));
                Assert.That(weights[center, center, 2], Is.GreaterThan(0.99f));
                Assert.That(weights[center, center, 1], Is.LessThan(0.01f));
                Assert.That(weights[borderZ, borderX, 1], Is.GreaterThan(0.9f));
                Assert.That(weights[borderZ, borderX, 2], Is.LessThan(0.05f));
                Assert.That(weights[center, center, 0] + weights[center, center, 1] + weights[center, center, 2], Is.EqualTo(1f).Within(0.001f));
                Assert.That(weights[borderZ, borderX, 0] + weights[borderZ, borderX, 1] + weights[borderZ, borderX, 2], Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(horizontalObject);
                Object.DestroyImmediate(verticalObject);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
                Object.DestroyImmediate(baseLayer);
                Object.DestroyImmediate(roadLayer);
                Object.DestroyImmediate(borderLayer);
            }
        }

        [Test]
        public void TerrainRoad_SplineIsEditableSourceAndSamplesSmoothCenterLine()
        {
            TerrainData terrainData = CreateTerrainData(32, 20f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            TerrainLayer roadLayer = new();
            TerrainLayer borderLayer = new();
            GameObject roadObject = new("__TEST__SplineRoad");
            TerrainRoadPath road = roadObject.AddComponent<TerrainRoadPath>();

            try
            {
                road.Configure(
                    new[]
                    {
                        new Vector3(1f, 1f, 1f),
                        new Vector3(10f, 2f, 6f),
                        new Vector3(18f, 3f, 18f)
                    },
                    4f,
                    0.1f,
                    0.75f,
                    0.08f,
                    terrain,
                    roadLayer,
                    borderLayer);

                List<Vector3> controls = new();
                List<Vector3> samples = new();
                road.GetWorldControlPoints(controls);
                road.GetWorldCenterPoints(samples, 0.5f);

                Assert.That(road.GetComponent<SplineContainer>(), Is.Not.Null);
                Assert.That(road.SplineContainer.Spline.Count, Is.EqualTo(3));
                Assert.That(controls.Count, Is.EqualTo(3));
                Assert.That(samples.Count, Is.GreaterThan(controls.Count));
            }
            finally
            {
                Object.DestroyImmediate(roadObject);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
                Object.DestroyImmediate(roadLayer);
                Object.DestroyImmediate(borderLayer);
            }
        }

        [Test]
        public void TerrainGrader_StartsFromBaselineAndLeavesOutsideShoulderUntouched()
        {
            TerrainData terrainData = CreateTerrainData(32, 20f);
            int resolution = terrainData.heightmapResolution;
            float[,] original = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                    original[z, x] = 0.2f + ((x + z) % 2 == 0 ? 0.08f : 0f);
            }
            terrainData.SetHeights(0, 0, original);

            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            TerrainLayer roadLayer = new();
            TerrainLayer borderLayer = new();
            TerrainRoadBaseline baseline = ScriptableObject.CreateInstance<TerrainRoadBaseline>();
            baseline.Capture(terrainData);
            GameObject roadObject = new("__TEST__GradedRoad");
            TerrainRoadPath road = roadObject.AddComponent<TerrainRoadPath>();
            road.Configure(
                new[] { new Vector3(2f, 5f, 10f), new Vector3(18f, 5f, 10f) },
                4f,
                0.1f,
                0.75f,
                0.08f,
                terrain,
                roadLayer,
                borderLayer);

            try
            {
                Assert.That(TerrainRoadGrader.GradeTerrain(
                    terrain,
                    baseline,
                    new[] { road },
                    12f,
                    2f,
                    "__TEST__ Grade Road",
                    false), Is.True);

                float[,] graded = terrainData.GetHeights(0, 0, resolution, resolution);
                int center = Mathf.RoundToInt(0.5f * (resolution - 1));
                int outside = Mathf.RoundToInt(0.05f * (resolution - 1));
                float expectedCenter = (5f - RoadSurfaceSampler.SurfaceOffset) / terrainData.size.y;
                Assert.That(graded[center, center], Is.EqualTo(expectedCenter).Within(0.02f));
                Assert.That(graded[outside, center], Is.EqualTo(original[outside, center]).Within(0.0001f));

                Assert.That(baseline.TryCopyHeights(out float[,] restored), Is.True);
                Assert.That(restored[center, center], Is.EqualTo(original[center, center]).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(roadObject);
                Object.DestroyImmediate(baseline);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
                Object.DestroyImmediate(roadLayer);
                Object.DestroyImmediate(borderLayer);
            }
        }

        [Test]
        public void EntranceWorldData_UsesHouseTransformAndDoorWidthScale()
        {
            GameObject house = new GameObject("__TEST__House");
            try
            {
                house.transform.SetPositionAndRotation(new Vector3(3f, 1f, 5f), Quaternion.Euler(0f, 90f, 0f));
                house.transform.localScale = new Vector3(2f, 1f, 2f);
                RoadEntranceProfile profile = new(
                    null,
                    new Vector3(0f, 0f, 2f),
                    Vector3.forward,
                    1.4f,
                    false);

                RoadEntranceWorldData world = new(house, profile);

                Assert.That(world.Position, Is.EqualTo(new Vector3(7f, 1f, 5f)).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(world.Forward, Is.EqualTo(Vector3.right).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(world.DoorWidth, Is.EqualTo(2.8f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(house);
            }
        }

        [Test]
        public void HouseSidewalkPreview_BuildsFromDoorToNearestRoadEdge()
        {
            TerrainData terrainData = CreateTerrainData(32, 20f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            TerrainLayer layer = new();
            TerrainLayer borderLayer = new();
            GameObject roadObject = new("__TEST__HousePreviewRoad");
            TerrainRoadPath road = roadObject.AddComponent<TerrainRoadPath>();
            road.Configure(
                new[] { new Vector3(2f, 0f, 10f), new Vector3(12f, 0f, 10f) },
                4f,
                0.1f,
                0.55f,
                0.08f,
                terrain,
                layer,
                borderLayer);
            RoadEntranceProfile profile = new(
                null,
                Vector3.zero,
                Vector3.forward,
                1.4f,
                false);

            try
            {
                bool built = HouseSidewalkPreview.TryBuildPath(
                    Matrix4x4.TRS(new Vector3(7f, 0f, 4f), Quaternion.identity, Vector3.one),
                    profile,
                    out List<Vector3> path,
                    out float doorWidth);

                Assert.That(built, Is.True);
                Assert.That(path.Count, Is.GreaterThan(2));
                Assert.That(path[0].z, Is.EqualTo(4f).Within(0.05f));
                Assert.That(path[^1].z, Is.EqualTo(8f).Within(0.08f));
                Assert.That(doorWidth, Is.EqualTo(1.4f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(roadObject);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
                Object.DestroyImmediate(layer);
                Object.DestroyImmediate(borderLayer);
            }
        }

        [Test]
        public void HouseSidewalkPreview_EasyRoadControlsRemoveDensePreviewSamples()
        {
            List<Vector3> sampled = new();
            for (int z = 0; z <= 8; z++) sampled.Add(new Vector3(4f, 0f, z));

            List<Vector3> controls = HouseSidewalkPreview.BuildEasyRoadControlPoints(sampled);

            Assert.That(controls.Count, Is.EqualTo(3));
            Assert.That(controls[0], Is.EqualTo(sampled[0]));
            Assert.That(controls[^1], Is.EqualTo(sampled[^1]));
            for (int i = 1; i < controls.Count; i++)
                Assert.That(Vector3.Distance(controls[i - 1], controls[i]), Is.GreaterThanOrEqualTo(2f));
        }

        [Test]
        public void RoadPreview_DoesNotCreateSceneObjectsOrDirtyScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            int rootCount = scene.rootCount;
            bool dirty = scene.isDirty;

            using (RoadPreview preview = new RoadPreview())
            {
                preview.Set(
                    new[] { Vector3.zero, Vector3.forward * 4f },
                    2f,
                    null);
                Assert.That(preview.VertexCount, Is.EqualTo(4));
            }

            Assert.That(scene.rootCount, Is.EqualTo(rootCount));
            Assert.That(scene.isDirty, Is.EqualTo(dirty));
        }

        [Test]
        public void RoadRibbon_UsesUpwardNormalsAndWidthPreservingMiterAtCorner()
        {
            Mesh mesh = RoadPath.BuildRibbonMesh(
                new[]
                {
                    Vector3.zero,
                    Vector3.forward * 5f,
                    Vector3.forward * 5f + Vector3.right * 5f
                },
                4f,
                "__TEST__Miter");

            try
            {
                Assert.That(mesh.normals[0].y, Is.GreaterThan(0.9f));
                Vector3 corner = Vector3.forward * 5f;
                float miterLength = Vector3.Distance(mesh.vertices[2], corner);
                Assert.That(miterLength, Is.EqualTo(Mathf.Sqrt(8f)).Within(0.02f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RoadRibbon_JunctionCapCoversConnectedEndpoint()
        {
            Mesh mesh = RoadPath.BuildRibbonMesh(
                new[] { Vector3.zero, Vector3.forward * 5f },
                2f,
                "__TEST__Junction",
                0f,
                3f);

            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(4 + 21));
                Assert.That(mesh.bounds.max.x, Is.GreaterThanOrEqualTo(2.99f));
                Assert.That(mesh.normals[^1].y, Is.GreaterThan(0.9f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void RoadRibbon_SelfTJunctionReceivesAutomaticCap()
        {
            Mesh mesh = RoadPath.BuildRibbonMesh(
                new[]
                {
                    new Vector3(-5f, 0f, 0f),
                    new Vector3(5f, 0f, 0f),
                    new Vector3(0f, 0f, -5f),
                    Vector3.zero
                },
                2f,
                "__TEST__SelfJunction");

            try
            {
                int ribbonVertexCount = 4 * 2;
                Assert.That(mesh.vertexCount, Is.EqualTo(ribbonVertexCount + 21));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void EasyRoadTemplate_DefaultsMatchWinterVillageRoadRecipe()
        {
            EasyRoadTemplate template = ScriptableObject.CreateInstance<EasyRoadTemplate>();
            try
            {
                Assert.That(template.RoadTypeName, Is.EqualTo("PPack Packed Snow Village Road"));
                Assert.That(template.DefaultWidth, Is.EqualTo(4.8f).Within(0.001f));
                Assert.That(template.SurfaceOffset, Is.EqualTo(0.08f).Within(0.001f));
                Assert.That(template.MeshCollider, Is.True);
                Assert.That(template.SnapToTerrain, Is.True);
                Assert.That(template.TerrainDeformation, Is.False);
                Assert.That(template.MaximumGrade, Is.EqualTo(10.5f).Within(0.001f));
                Assert.That(template.MinimumShoulder, Is.EqualTo(6f).Within(0.001f));
                Assert.That(template.MaximumShoulder, Is.EqualTo(22f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(template);
            }
        }

        [Test]
        public void EasyRoadSource_IsSeparatedFromTerrainPaintRoadCollection()
        {
            TerrainData terrainData = CreateTerrainData(32, 20f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            TerrainLayer roadLayer = new();
            TerrainLayer borderLayer = new();
            GameObject root = new(EasyRoadAuthoring.SourceRootName);
            GameObject easyRoadObject = new("__TEST__EasyRoadSource");
            easyRoadObject.transform.SetParent(root.transform);
            TerrainRoadPath easyRoad = easyRoadObject.AddComponent<TerrainRoadPath>();
            easyRoad.Configure(
                new[] { Vector3.zero, Vector3.forward * 5f },
                4.8f,
                0.1f,
                0.75f,
                0.08f,
                terrain,
                roadLayer,
                borderLayer);
            EasyRoadSource source = easyRoadObject.AddComponent<EasyRoadSource>();
            source.Configure(null, "ER___TEST__EasyRoadSource");

            try
            {
                Assert.That(EasyRoadAuthoring.IsEasyRoadSource(easyRoad), Is.True);
                CollectionAssert.DoesNotContain(
                    TerrainRoadAuthoring.CollectTerrainPaths(terrain),
                    easyRoad);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(terrainData);
                Object.DestroyImmediate(roadLayer);
                Object.DestroyImmediate(borderLayer);
            }
        }

        [Test]
        public void EditableRootWithoutEasyRoadSource_DoesNotOwnExistingRoad()
        {
            GameObject root = new(EasyRoadAuthoring.SourceRootName);
            GameObject existingRoadObject = new("__TEST__ExistingRoad");
            existingRoadObject.transform.SetParent(root.transform);
            TerrainRoadPath existingRoad = existingRoadObject.AddComponent<TerrainRoadPath>();

            try
            {
                Assert.That(EasyRoadAuthoring.IsEasyRoadSource(existingRoad), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static TerrainData CreateTerrainData(int alphamapResolution, float size)
        {
            TerrainData data = new TerrainData
            {
                heightmapResolution = 33,
                alphamapResolution = alphamapResolution,
                size = new Vector3(size, 10f, size)
            };
            return data;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PPack
{
    public static class InteractivePropAssetBuilder
    {
        public const string DumpsterPrefabPath =
            "Assets/Game/InGame/Interaction/Dumpster/Prefabs/PF_HingedDumpster.prefab";
        public const string BarrelPrefabPath =
            "Assets/Game/InGame/Interaction/Barrel/Prefabs/PF_RollingBarrel.prefab";
        public const string HydrantPrefabPath =
            "Assets/Game/InGame/Interaction/Hydrant/Prefabs/PF_BreakableHydrant.prefab";
        public const string TestScenePath =
            "Assets/Game/InGame/Interaction/Tests/Interaction_DynamicProps_Test.unity";

        private const string VendorBarrelPath =
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Barrels/barrel.prefab";
        private const string VendorHydrantPath =
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/hydrant.prefab";
        private const string VendorHydrantModelPath =
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Models/Environment/hydrant.fbx";
        private const string PenguinPrefabPath =
            "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab";
        private const string MaterialFolder = "Assets/Game/InGame/Interaction/Materials";
        private const string ParticleTextureFolder = "Assets/Game/InGame/Interaction/Textures";
        private const string HydrantMeshFolder = "Assets/Game/InGame/Interaction/Hydrant/Meshes";
        private const string HydrantBodyMeshPath = HydrantMeshFolder + "/MESH_Hydrant_BrokenBody.asset";
        private const string HydrantCapMeshPath = HydrantMeshFolder + "/MESH_Hydrant_FlyingCap.asset";
        private const string WaterStreakTexturePath = ParticleTextureFolder + "/T_WaterStreak.asset";
        private const string WaterDropletTexturePath = ParticleTextureFolder + "/T_WaterDroplet.asset";
        private const string WaterSplashTexturePath = ParticleTextureFolder + "/T_WaterSplashFlipbook.asset";
        private const string BuilderMarker = "InteractivePropAssetBuilder:8";

        [InitializeOnLoadMethod]
        private static void QueueBuildIfMissing()
        {
            if (AssetsExist()) return;
            EditorApplication.delayCall += BuildIfMissing;
        }

        private static bool AssetsExist()
        {
            bool filesExist = AssetDatabase.LoadAssetAtPath<GameObject>(DumpsterPrefabPath) != null &&
                   AssetDatabase.LoadAssetAtPath<GameObject>(BarrelPrefabPath) != null &&
                   AssetDatabase.LoadAssetAtPath<GameObject>(HydrantPrefabPath) != null &&
                   AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) != null;
            if (!filesExist) return false;

            AssetImporter sceneImporter = AssetImporter.GetAtPath(TestScenePath);
            return sceneImporter != null && sceneImporter.userData == BuilderMarker;
        }

        private static void BuildIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += BuildIfMissing;
                return;
            }
            if (!AssetsExist()) BuildAll();
        }

        [MenuItem("PPack/Interaction/Build Dynamic Props Test Scene")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Play Mode 중에는 Dynamic Props 에셋을 다시 만들지 않는다.");
                return;
            }

            AssetDatabase.Refresh();

            Material green = GetOrCreateMaterial("M_Prop_DumpsterGreen", new Color(0.08f, 0.28f, 0.18f), 0.12f, 0f);
            Material lid = GetOrCreateMaterial("M_Prop_DumpsterLid", new Color(0.10f, 0.23f, 0.36f), 0.2f, 0.05f);
            Material dark = GetOrCreateMaterial("M_Prop_DarkInterior", new Color(0.025f, 0.04f, 0.045f), 0.04f, 0f);
            Material metal = GetOrCreateMaterial("M_Prop_Metal", new Color(0.20f, 0.25f, 0.27f), 0.36f, 0.72f);
            Material ice = GetOrCreateMaterial("M_Prop_Ice", new Color(0.36f, 0.88f, 1f), 0.62f, 0.05f);
            Texture2D waterStreakTexture = GetOrCreateWaterTexture(WaterStreakTexturePath, WaterTextureKind.Streak);
            Texture2D waterDropletTexture = GetOrCreateWaterTexture(WaterDropletTexturePath, WaterTextureKind.Droplet);
            Texture2D waterSplashTexture = GetOrCreateWaterTexture(WaterSplashTexturePath, WaterTextureKind.SplashFlipbook);
            Material waterCore = GetOrCreateParticleMaterial(
                "M_Prop_WaterJet", new Color(0.76f, 0.97f, 1f, 0.96f), waterStreakTexture);
            Material waterDroplet = GetOrCreateParticleMaterial(
                "M_Prop_WaterDroplet", new Color(0.46f, 0.88f, 1f, 0.86f), waterDropletTexture);
            Material waterMist = GetOrCreateParticleMaterial(
                "M_Prop_WaterMist", new Color(0.78f, 0.95f, 1f, 0.52f), waterSplashTexture);
            Material floor = GetOrCreateMaterial("M_Prop_TestFloor", new Color(0.20f, 0.29f, 0.34f), 0.08f, 0f);
            Material snow = GetOrCreateMaterial("M_Prop_TestSnow", new Color(0.92f, 0.98f, 1f), 0.22f, 0f);

            BuildDumpsterPrefab(green, lid, dark);
            BuildBarrelPrefab(metal);
            BuildHydrantPrefab(ice, waterCore, waterDroplet, waterMist);
            BuildTestScene(floor, snow);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            AssetImporter sceneImporter = AssetImporter.GetAtPath(TestScenePath);
            if (sceneImporter != null)
            {
                sceneImporter.userData = BuilderMarker;
                sceneImporter.SaveAndReimport();
            }
            Debug.Log($"Dynamic interaction props and test scene built: {TestScenePath}");
        }

        [MenuItem("PPack/Interaction/Open Dynamic Props Test Scene")]
        private static void OpenTestScene()
        {
            if (!AssetsExist()) BuildAll();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        }

        private static void BuildDumpsterPrefab(Material green, Material lidMaterial, Material dark)
        {
            var root = new GameObject("PF_HingedDumpster");
            try
            {
                CreateBox(root.transform, "Bottom", new Vector3(0f, 0.12f, 0f), new Vector3(2.4f, 0.24f, 1.4f), green);
                CreateBox(root.transform, "Front", new Vector3(0f, 0.72f, -0.65f), new Vector3(2.4f, 1.2f, 0.12f), green);
                CreateBox(root.transform, "Back", new Vector3(0f, 0.72f, 0.65f), new Vector3(2.4f, 1.2f, 0.12f), green);
                CreateBox(root.transform, "Side_Left", new Vector3(-1.14f, 0.72f, 0f), new Vector3(0.12f, 1.2f, 1.2f), green);
                CreateBox(root.transform, "Side_Right", new Vector3(1.14f, 0.72f, 0f), new Vector3(0.12f, 1.2f, 1.2f), green);
                CreateBox(root.transform, "Interior", new Vector3(0f, 0.28f, 0f), new Vector3(2.12f, 0.08f, 1.08f), dark);

                AddBoxCollider(root, new Vector3(0f, 0.12f, 0f), new Vector3(2.4f, 0.24f, 1.4f));
                AddBoxCollider(root, new Vector3(0f, 0.72f, -0.65f), new Vector3(2.4f, 1.2f, 0.12f));
                AddBoxCollider(root, new Vector3(0f, 0.72f, 0.65f), new Vector3(2.4f, 1.2f, 0.12f));
                AddBoxCollider(root, new Vector3(-1.14f, 0.72f, 0f), new Vector3(0.12f, 1.2f, 1.2f));
                AddBoxCollider(root, new Vector3(1.14f, 0.72f, 0f), new Vector3(0.12f, 1.2f, 1.2f));

                Transform leftPivot = CreateLid(root.transform, "Lid_Left", -0.6f, lidMaterial);
                Transform rightPivot = CreateLid(root.transform, "Lid_Right", 0.6f, lidMaterial);
                root.AddComponent<DumpsterLidController>().Configure(leftPivot, rightPivot);

                SavePrefab(root, DumpsterPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Transform CreateLid(Transform parent, string name, float x, Material material)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = new Vector3(x, 1.36f, 0.66f);

            GameObject panel = CreateBox(
                pivot.transform,
                "Panel",
                new Vector3(0f, 0f, -0.66f),
                new Vector3(1.14f, 0.10f, 1.32f),
                material);
            var panelCollider = panel.AddComponent<BoxCollider>();
            panelCollider.size = Vector3.one;
            Rigidbody body = pivot.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            return pivot.transform;
        }

        private static void BuildBarrelPrefab(Material metal)
        {
            var root = new GameObject("PF_RollingBarrel");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 48f;
                body.linearDamping = 0.08f;
                body.angularDamping = 0.035f;
                body.maxAngularVelocity = 35f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.center = new Vector3(0f, 0.45f, 0f);
                capsule.radius = 0.41f;
                capsule.height = 0.90f;

                GameObject visual = InstantiateVendorVisual(VendorBarrelPath, root.transform, "Visual");
                if (visual == null)
                {
                    visual = CreateCylinder(root.transform, "Visual", new Vector3(0f, 0.45f, 0f), new Vector3(0.82f, 0.45f, 0.82f), metal);
                }

                root.AddComponent<RollingBarrel>();
                SavePrefab(root, BarrelPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildHydrantPrefab(
            Material ice,
            Material waterCore,
            Material waterDroplet,
            Material waterMist)
        {
            ModelImporter modelImporter = AssetImporter.GetAtPath(VendorHydrantModelPath) as ModelImporter;
            if (modelImporter == null)
                throw new InvalidOperationException($"원본 소화전 모델 임포터를 찾을 수 없다: {VendorHydrantModelPath}");
            bool restoreReadWriteDisabled = !modelImporter.isReadable;
            if (restoreReadWriteDisabled)
            {
                modelImporter.isReadable = true;
                modelImporter.SaveAndReimport();
            }

            var root = new GameObject("PF_BreakableHydrant");
            try
            {
                Rigidbody rootBody = root.AddComponent<Rigidbody>();
                rootBody.isKinematic = true;
                var rootCollider = root.AddComponent<CapsuleCollider>();
                rootCollider.center = new Vector3(0f, 0.52f, 0f);
                rootCollider.radius = 0.33f;
                rootCollider.height = 1.05f;

                GameObject intact = InstantiateVendorVisual(VendorHydrantPath, root.transform, "IntactVisual");
                if (intact == null)
                    throw new InvalidOperationException($"원본 소화전 프리팹을 찾을 수 없다: {VendorHydrantPath}");

                CreateOriginalHydrantBreakParts(
                    intact,
                    root.transform,
                    out GameObject brokenBase,
                    out Rigidbody topBody,
                    out float openingHeight);

                ParticleSystem waterJet = CreateWaterJet(
                    root.transform,
                    openingHeight,
                    waterCore,
                    waterDroplet,
                    waterMist);
                Transform iceGrowth = CreateIceGrowth(root.transform, ice);

                root.AddComponent<BreakableHydrant>().Configure(
                    intact,
                    brokenBase,
                    topBody,
                    waterJet,
                    iceGrowth);
                SavePrefab(root, HydrantPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (restoreReadWriteDisabled)
                {
                    modelImporter = AssetImporter.GetAtPath(VendorHydrantModelPath) as ModelImporter;
                    if (modelImporter != null)
                    {
                        modelImporter.isReadable = false;
                        modelImporter.SaveAndReimport();
                    }
                }
            }
        }

        private static void CreateOriginalHydrantBreakParts(
            GameObject intact,
            Transform parent,
            out GameObject brokenBase,
            out Rigidbody brokenTopBody,
            out float openingHeight)
        {
            MeshFilter sourceFilter = intact.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer sourceRenderer = intact.GetComponentInChildren<MeshRenderer>(true);
            if (sourceFilter == null || sourceRenderer == null || sourceFilter.sharedMesh == null)
                throw new InvalidOperationException("원본 소화전에서 MeshFilter/MeshRenderer를 찾을 수 없다");

            Mesh source = sourceFilter.sharedMesh;
            float cutHeight = source.bounds.min.y + source.bounds.size.y * 0.72f;
            Mesh bodyGenerated = CreateHydrantMeshSection(source, cutHeight, false, Vector3.zero, "Hydrant_BrokenBody");
            Mesh capUncentered = CreateHydrantMeshSection(source, cutHeight, true, Vector3.zero, "Hydrant_FlyingCap_Source");
            Vector3 capCenter = capUncentered.bounds.center;
            UnityEngine.Object.DestroyImmediate(capUncentered);
            Mesh capGenerated = CreateHydrantMeshSection(source, cutHeight, true, capCenter, "Hydrant_FlyingCap");

            EnsureAssetFolder(HydrantMeshFolder);
            Mesh bodyMesh = SaveOrReplaceMesh(bodyGenerated, HydrantBodyMeshPath);
            Mesh capMesh = SaveOrReplaceMesh(capGenerated, HydrantCapMeshPath);

            brokenBase = new GameObject("BrokenBase_OriginalMesh");
            brokenBase.transform.SetParent(parent, false);
            CopyRelativeTransform(sourceFilter.transform, intact.transform, brokenBase.transform);
            brokenBase.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            brokenBase.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
            var baseCollider = brokenBase.AddComponent<BoxCollider>();
            baseCollider.center = bodyMesh.bounds.center;
            baseCollider.size = bodyMesh.bounds.size;

            var brokenTop = new GameObject("BrokenTop_OriginalCap");
            brokenTop.transform.SetParent(parent, false);
            CopyRelativeTransform(sourceFilter.transform, intact.transform, brokenTop.transform);
            brokenTop.transform.localPosition += sourceFilter.transform.TransformVector(capCenter);
            brokenTop.AddComponent<MeshFilter>().sharedMesh = capMesh;
            brokenTop.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
            var topCollider = brokenTop.AddComponent<BoxCollider>();
            topCollider.center = capMesh.bounds.center;
            topCollider.size = capMesh.bounds.size;
            brokenTopBody = brokenTop.AddComponent<Rigidbody>();
            brokenTopBody.mass = 5.5f;
            brokenTopBody.isKinematic = true;
            brokenTopBody.interpolation = RigidbodyInterpolation.Interpolate;

            openingHeight = cutHeight;
        }

        private static Mesh CreateHydrantMeshSection(
            Mesh source,
            float cutHeight,
            bool keepTop,
            Vector3 vertexOffset,
            string name)
        {
            Vector3[] sourceVertices = source.vertices;
            var vertices = new Vector3[sourceVertices.Length];
            for (int i = 0; i < sourceVertices.Length; i++) vertices[i] = sourceVertices[i] - vertexOffset;

            var mesh = new Mesh { name = name, indexFormat = source.indexFormat };
            mesh.vertices = vertices;
            if (source.normals.Length == source.vertexCount) mesh.normals = source.normals;
            if (source.tangents.Length == source.vertexCount) mesh.tangents = source.tangents;
            if (source.colors.Length == source.vertexCount) mesh.colors = source.colors;
            if (source.uv.Length == source.vertexCount) mesh.uv = source.uv;
            if (source.uv2.Length == source.vertexCount) mesh.uv2 = source.uv2;
            mesh.subMeshCount = source.subMeshCount;

            int keptTriangles = 0;
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                int[] triangles = source.GetTriangles(subMesh);
                var kept = new List<int>(triangles.Length);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    float centroidY = (sourceVertices[triangles[i]].y +
                                       sourceVertices[triangles[i + 1]].y +
                                       sourceVertices[triangles[i + 2]].y) / 3f;
                    if ((centroidY >= cutHeight) != keepTop) continue;
                    kept.Add(triangles[i]);
                    kept.Add(triangles[i + 1]);
                    kept.Add(triangles[i + 2]);
                    keptTriangles++;
                }
                mesh.SetTriangles(kept, subMesh, false);
            }

            if (keptTriangles == 0)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw new InvalidOperationException($"원본 소화전 메시 분리 결과가 비었다: {name}");
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private static void CopyRelativeTransform(Transform source, Transform sourceRoot, Transform destination)
        {
            destination.localPosition = sourceRoot.InverseTransformPoint(source.position);
            destination.localRotation = Quaternion.Inverse(sourceRoot.rotation) * source.rotation;
            Vector3 rootScale = sourceRoot.lossyScale;
            Vector3 sourceScale = source.lossyScale;
            destination.localScale = new Vector3(
                sourceScale.x / Mathf.Max(0.0001f, rootScale.x),
                sourceScale.y / Mathf.Max(0.0001f, rootScale.y),
                sourceScale.z / Mathf.Max(0.0001f, rootScale.z));
        }

        private static ParticleSystem CreateWaterJet(
            Transform parent,
            float openingHeight,
            Material coreMaterial,
            Material dropletMaterial,
            Material mistMaterial)
        {
            var waterObject = new GameObject("WaterJet");
            waterObject.transform.SetParent(parent, false);
            waterObject.transform.localPosition = new Vector3(0f, openingHeight, 0f);
            waterObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var particles = waterObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.duration = 4f;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.72f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6.2f, 8.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.095f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.90f, 0.99f, 1f, 1f),
                new Color(0.44f, 0.86f, 1f, 0.92f));
            main.gravityModifier = 1.08f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 700;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 125f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 3.5f;
            shape.radius = 0.045f;
            ConfigureWaterLifetime(particles, 0.92f);
            ConfigureWaterCollision(particles, 0.38f, 0.04f);
            ParticleSystem.TrailModule trails = particles.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 0.30f;
            trails.lifetime = 0.09f;
            trails.dieWithParticles = false;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = coreMaterial;
            renderer.trailMaterial = coreMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.085f;
            renderer.lengthScale = 2.2f;

            ParticleSystem droplets = CreateWaterDroplets(waterObject.transform, dropletMaterial, mistMaterial);
            ParticleSystem.SubEmittersModule subEmitters = droplets.subEmitters;
            subEmitters.enabled = true;
            ParticleSystem splash = droplets.transform.Find("CollisionSplash").GetComponent<ParticleSystem>();
            subEmitters.AddSubEmitter(
                splash,
                ParticleSystemSubEmitterType.Collision,
                ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize,
                0.52f);

            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            waterObject.SetActive(false);
            return particles;
        }

        private static ParticleSystem CreateWaterDroplets(
            Transform parent,
            Material dropletMaterial,
            Material mistMaterial)
        {
            var dropletObject = new GameObject("BallisticDroplets");
            dropletObject.transform.SetParent(parent, false);
            var droplets = dropletObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = droplets.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4.2f, 7.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.58f, 0.93f, 1f, 0.94f),
                new Color(0.22f, 0.70f, 1f, 0.74f));
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 900;
            ParticleSystem.EmissionModule emission = droplets.emission;
            emission.rateOverTime = 150f;
            ParticleSystem.ShapeModule shape = droplets.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 13f;
            shape.radius = 0.07f;
            ConfigureWaterLifetime(droplets, 0.64f);
            ConfigureWaterCollision(droplets, 0.48f, 0.08f);
            ParticleSystem.NoiseModule noise = droplets.noise;
            noise.enabled = true;
            noise.strength = 0.14f;
            noise.frequency = 0.72f;
            noise.scrollSpeed = 0.18f;
            ParticleSystemRenderer renderer = droplets.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = dropletMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            var splashObject = new GameObject("CollisionSplash");
            splashObject.transform.SetParent(dropletObject.transform, false);
            var splash = splashObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule splashMain = splash.main;
            splashMain.loop = false;
            splashMain.playOnAwake = false;
            splashMain.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.38f);
            splashMain.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 1.8f);
            splashMain.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.10f);
            splashMain.startColor = new Color(0.64f, 0.93f, 1f, 0.52f);
            splashMain.gravityModifier = 0.52f;
            splashMain.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule splashEmission = splash.emission;
            splashEmission.rateOverTime = 0f;
            splashEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 7, 13) });
            ParticleSystem.ShapeModule splashShape = splash.shape;
            splashShape.shapeType = ParticleSystemShapeType.Hemisphere;
            splashShape.radius = 0.04f;
            ConfigureWaterLifetime(splash, 0.35f);
            ParticleSystem.TextureSheetAnimationModule splashSheet = splash.textureSheetAnimation;
            splashSheet.enabled = true;
            splashSheet.mode = ParticleSystemAnimationMode.Grid;
            splashSheet.numTilesX = 2;
            splashSheet.numTilesY = 2;
            splashSheet.animation = ParticleSystemAnimationType.WholeSheet;
            splashSheet.frameOverTime = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            ParticleSystemRenderer splashRenderer = splash.GetComponent<ParticleSystemRenderer>();
            splashRenderer.sharedMaterial = mistMaterial;
            splashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            return droplets;
        }

        private static void ConfigureWaterCollision(ParticleSystem system, float dampen, float bounce)
        {
            ParticleSystem.CollisionModule collision = system.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = dampen;
            collision.bounce = bounce;
            collision.lifetimeLoss = 0.18f;
            collision.radiusScale = 0.35f;
        }

        private static void ConfigureWaterLifetime(ParticleSystem system, float endAlpha)
        {
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.45f, 0.86f, 1f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(endAlpha, 0.72f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.12f, 1f),
                new Keyframe(1f, 0.42f)));
        }

        private static Mesh SaveOrReplaceMesh(Mesh generated, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            string parent = path.Substring(0, split);
            string name = path.Substring(split + 1);
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static Transform CreateIceGrowth(Transform parent, Material ice)
        {
            var root = new GameObject("IceGrowth");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            root.transform.localScale = Vector3.one;
            CreateCylinder(root.transform, "IceColumn", new Vector3(0f, 0.65f, 0f), new Vector3(0.48f, 0.65f, 0.48f), ice);
            GameObject shardA = CreateBox(root.transform, "IceShard_A", new Vector3(-0.28f, 0.42f, 0.08f), new Vector3(0.22f, 0.82f, 0.22f), ice);
            shardA.transform.localRotation = Quaternion.Euler(0f, 0f, -13f);
            GameObject shardB = CreateBox(root.transform, "IceShard_B", new Vector3(0.30f, 0.34f, -0.04f), new Vector3(0.18f, 0.62f, 0.18f), ice);
            shardB.transform.localRotation = Quaternion.Euler(8f, 0f, 17f);
            CreateCylinder(root.transform, "FrozenPool", new Vector3(0f, 0.025f, 0f), new Vector3(1.15f, 0.035f, 0.9f), ice);
            root.SetActive(false);
            return root.transform;
        }

        private static void BuildTestScene(Material floor, Material snow)
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.45f, 0.58f, 0.68f);
                RenderSettings.ambientEquatorColor = new Color(0.20f, 0.28f, 0.34f);
                RenderSettings.ambientGroundColor = new Color(0.08f, 0.11f, 0.14f);

                GameObject floorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floorObject.name = "TestGround";
                floorObject.transform.position = new Vector3(0f, -0.25f, 1f);
                floorObject.transform.localScale = new Vector3(18f, 0.5f, 12f);
                floorObject.GetComponent<Renderer>().sharedMaterial = floor;

                CreateBoundary(new Vector3(-8.8f, 0.45f, 1f), new Vector3(0.3f, 1.4f, 12f), floor);
                CreateBoundary(new Vector3(8.8f, 0.45f, 1f), new Vector3(0.3f, 1.4f, 12f), floor);
                CreateBoundary(new Vector3(0f, 0.45f, 6.8f), new Vector3(18f, 1.4f, 0.3f), floor);

                DumpsterLidController dumpster = InstantiatePrefab<DumpsterLidController>(
                    DumpsterPrefabPath, new Vector3(-4.3f, 0f, 1f), Quaternion.identity);
                RollingBarrel barrel = InstantiatePrefab<RollingBarrel>(
                    BarrelPrefabPath, new Vector3(-1.0f, 0f, 1f), Quaternion.identity);
                BreakableHydrant hydrant = InstantiatePrefab<BreakableHydrant>(
                    HydrantPrefabPath, new Vector3(4.2f, 0f, 1.2f), Quaternion.identity);

                GameObject impactBallObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                impactBallObject.name = "HydrantImpactBall";
                impactBallObject.transform.position = new Vector3(4.2f, 0.55f, -4.2f);
                impactBallObject.transform.localScale = Vector3.one * 0.9f;
                impactBallObject.GetComponent<Renderer>().sharedMaterial = snow;
                Rigidbody impactBall = impactBallObject.AddComponent<Rigidbody>();
                impactBall.mass = 25f;
                impactBall.interpolation = RigidbodyInterpolation.Interpolate;
                impactBall.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                var controllerObject = new GameObject("DynamicPropsTestController");
                controllerObject.AddComponent<DynamicPropsTestController>().Configure(
                    dumpster, barrel, hydrant, impactBall);

                CreatePlayablePenguin();

                CreateLabel("1  HINGED DUMPSTER", new Vector3(-4.3f, 2.55f, 1f));
                CreateLabel("2  ROLLING BARREL", new Vector3(-1.0f, 2.15f, 1f));
                CreateLabel("3  BREAK + WATER + ICE", new Vector3(4.2f, 2.35f, 1.2f));

                var lightObject = new GameObject("Directional Light");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(0.90f, 0.96f, 1f);
                light.intensity = 1.25f;
                light.shadows = LightShadows.Soft;
                lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

                if (!EditorSceneManager.SaveScene(scene, TestScenePath))
                    throw new InvalidOperationException($"테스트 씬 저장 실패: {TestScenePath}");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
            }
        }

        private static void CreatePlayablePenguin()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PenguinPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"현재 펭귄 프리팹을 찾을 수 없다: {PenguinPrefabPath}");

            GameObject penguin = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            penguin.name = "PlayablePenguin_CurrentPF";
            penguin.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -3.4f), Quaternion.identity);

            Rigidbody body = penguin.GetComponent<Rigidbody>();
            if (body == null)
                throw new InvalidOperationException($"{PenguinPrefabPath} 루트에 Rigidbody가 없다");
            body.isKinematic = false;
            body.useGravity = true;
        }

        private static T InstantiatePrefab<T>(string path, Vector3 position, Quaternion rotation)
            where T : Component
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException($"프리팹을 찾을 수 없다: {path}");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(position, rotation);
            T component = instance.GetComponent<T>();
            if (component == null) throw new InvalidOperationException($"{path}에 {typeof(T).Name}이 없다");
            return component;
        }

        private static GameObject InstantiateVendorVisual(string path, Transform parent, string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            return instance;
        }

        private static void CreateBoundary(Vector3 position, Vector3 scale, Material material)
        {
            GameObject boundary = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boundary.name = "Boundary";
            boundary.transform.position = position;
            boundary.transform.localScale = scale;
            boundary.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void CreateLabel(string text, Vector3 position)
        {
            var labelObject = new GameObject(text);
            labelObject.transform.position = position;
            labelObject.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 48;
            label.characterSize = 0.06f;
            label.color = new Color(0.92f, 0.98f, 1f);
        }

        private static GameObject CreateBox(
            Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            return CreatePrimitive(parent, name, PrimitiveType.Cube, localPosition, localScale, material);
        }

        private static GameObject CreateCylinder(
            Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            return CreatePrimitive(parent, name, PrimitiveType.Cylinder, localPosition, localScale, material);
        }

        private static GameObject CreateSphere(
            Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            return CreatePrimitive(parent, name, PrimitiveType.Sphere, localPosition, localScale, material);
        }

        private static GameObject CreatePrimitive(
            Transform parent,
            string name,
            PrimitiveType type,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }

        private static void AddBoxCollider(GameObject target, Vector3 center, Vector3 size)
        {
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }

        private static void SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (prefab == null) throw new InvalidOperationException($"프리팹 저장 실패: {path}");
        }

        private static Material GetOrCreateMaterial(
            string name, Color color, float smoothness, float metallic)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit 셰이더를 찾을 수 없다");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        private enum WaterTextureKind
        {
            Streak,
            Droplet,
            SplashFlipbook
        }

        private static Texture2D GetOrCreateWaterTexture(string path, WaterTextureKind kind)
        {
            EnsureAssetFolder(ParticleTextureFolder);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int size = kind == WaterTextureKind.SplashFlipbook ? 128 : 64;
            if (texture == null)
            {
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(path),
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                AssetDatabase.CreateAsset(texture, path);
            }

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    float alpha = kind switch
                    {
                        WaterTextureKind.Streak => EvaluateStreakAlpha(uv),
                        WaterTextureKind.Droplet => EvaluateDropletAlpha(uv),
                        _ => EvaluateSplashFlipbookAlpha(uv)
                    };
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.Reinitialize(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            EditorUtility.SetDirty(texture);
            return texture;
        }

        private static float EvaluateStreakAlpha(Vector2 uv)
        {
            Vector2 p = (uv - Vector2.one * 0.5f) * 2f;
            float taperedWidth = Mathf.Lerp(0.18f, 0.58f, Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(p.y)), 0.42f));
            float edge = 1f - Mathf.SmoothStep(taperedWidth * 0.62f, taperedWidth, Mathf.Abs(p.x));
            float ends = 1f - Mathf.SmoothStep(0.72f, 1f, Mathf.Abs(p.y));
            float flowingDetail = Mathf.Lerp(0.74f, 1f, Mathf.PerlinNoise(uv.x * 4.7f, uv.y * 8.3f));
            return Mathf.Clamp01(edge * ends * flowingDetail);
        }

        private static float EvaluateDropletAlpha(Vector2 uv)
        {
            Vector2 p = (uv - new Vector2(0.5f, 0.43f)) * 2f;
            float y01 = Mathf.Clamp01((p.y + 0.9f) * 0.56f);
            float width = Mathf.Lerp(0.52f, 0.08f, Mathf.Pow(y01, 1.35f));
            float body = 1f - Mathf.SmoothStep(width * 0.72f, width, Mathf.Abs(p.x));
            float bottom = 1f - Mathf.SmoothStep(0.62f, 0.88f, -p.y);
            float top = 1f - Mathf.SmoothStep(0.82f, 1f, p.y);
            return Mathf.Clamp01(body * bottom * top);
        }

        private static float EvaluateSplashFlipbookAlpha(Vector2 uv)
        {
            int cellX = Mathf.Min(1, Mathf.FloorToInt(uv.x * 2f));
            int cellY = Mathf.Min(1, Mathf.FloorToInt(uv.y * 2f));
            int frame = cellY * 2 + cellX;
            Vector2 p = new Vector2(Mathf.Repeat(uv.x * 2f, 1f), Mathf.Repeat(uv.y * 2f, 1f));
            p = (p - Vector2.one * 0.5f) * 2f;

            float progress = frame / 3f;
            float centerRadius = Mathf.Lerp(0.22f, 0.08f, progress);
            float alpha = 1f - Mathf.SmoothStep(centerRadius * 0.55f, centerRadius, p.magnitude);
            int rayCount = 7;
            for (int i = 0; i < rayCount; i++)
            {
                float angle = (i / (float)rayCount) * Mathf.PI * 2f + frame * 0.31f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float start = Mathf.Lerp(0.04f, 0.15f, progress);
                float length = Mathf.Lerp(0.48f, 0.88f, progress) * (0.82f + 0.18f * Mathf.Sin(i * 2.17f));
                float distance = DistanceToSegment(p, direction * start, direction * length);
                float rayWidth = Mathf.Lerp(0.095f, 0.035f, progress);
                alpha = Mathf.Max(alpha, 1f - Mathf.SmoothStep(rayWidth * 0.48f, rayWidth, distance));
            }
            return Mathf.Clamp01(alpha * (1f - progress * 0.24f));
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 line = end - start;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, line) / Mathf.Max(0.0001f, line.sqrMagnitude));
            return Vector2.Distance(point, start + line * t);
        }

        private static Material GetOrCreateParticleMaterial(string name, Color color, Texture2D texture)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("URP 파티클 셰이더를 찾을 수 없다");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}

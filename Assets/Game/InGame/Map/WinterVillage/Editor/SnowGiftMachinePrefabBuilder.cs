#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;

namespace PPack.InGame.Map.WinterVillage.Editor
{
    /// <summary>Builds the bright low-poly snow-to-gift machine from project-owned meshes and materials.</summary>
    public static class SnowGiftMachinePrefabBuilder
    {
        private const string RootFolder = "Assets/Game/InGame/Map/WinterVillage";
        private const string PrefabFolder = "Assets/Game/InGame/Delivery/Prefabs/GiftProductionFlow";
        private const string MaterialFolder = RootFolder + "/Materials/SnowGiftMachine";
        private const string GeneratedFolder = RootFolder + "/Generated/SnowGiftMachine";
        private const string PrefabPath = PrefabFolder + "/PF_SnowGiftMachine.prefab";
        private const string GiftPrefabPath = "Assets/Game/InGame/Delivery/Prefabs/PF_GiftBox_Variable.prefab";
        private const string SnowballPrefabPath = "Assets/Game/InGame/Snow/Resources/PF_SnowBall.prefab";
        private const string TestSceneFolder = RootFolder + "/Tests";
        private const string TestScenePath = TestSceneFolder + "/SnowGiftMachine_Feel_Test.unity";
        private const string VacuumVfxPrefabPath = "Assets/Game/InGame/Vacuum/VFX/PF_TornadoVFXPreview.prefab";
        private const string VacuumVfxAssetPath = "Assets/Game/InGame/Vacuum/VFX/TornadoVFX.vfx";
        private const string AirflowTexturePath = "Assets/Game/Sandbox/VfxTest/Textures/T_Vortex_Wisp.png";
        private const string VacuumSuctionAudioPath = "Assets/Game/InGame/sound/vaccum1/vacuum_pop_01_low_quiet.wav";
        private const string GiftOutputAudioPath = "Assets/CelerisLab/CompleteUISFX/basic_interactions_and_navigation/buttons/magic_button/magic_button_01.wav";
        private const string ObsoletePreviewMaterialPath = MaterialFolder + "/M_SGM_PreviewSnow.mat";
        private const string PreviewPath = "/tmp/PPack_SnowGiftMachinePreview.png";
        private const int PreviewLayer = 31;

        private static readonly Color BodySky = new Color(0.34f, 0.79f, 0.78f, 1f);
        private static readonly Color Coral = new Color(1.00f, 0.43f, 0.57f, 1f);
        private static readonly Color Cream = new Color(1.00f, 0.92f, 0.72f, 1f);
        private static readonly Color HoseBlueGray = new Color(0.18f, 0.29f, 0.38f, 1f);
        private static readonly Color IntakeGlow = new Color(0.46f, 0.92f, 1.00f, 1f);
        private static readonly Color Lavender = new Color(0.67f, 0.55f, 0.95f, 1f);

        [MenuItem("PPack/Map/Winter Village/Build Snow Gift Machine")]
        public static void Build()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(GeneratedFolder);
            if (AssetDatabase.LoadMainAssetAtPath(ObsoletePreviewMaterialPath) != null)
                AssetDatabase.DeleteAsset(ObsoletePreviewMaterialPath);

            Material body = Material("M_SGM_BodySky", BodySky, 0f, 0.48f);
            Material coral = Material("M_SGM_Coral", Coral, 0f, 0.42f);
            Material cream = Material("M_SGM_Cream", Cream, 0f, 0.36f);
            Material hose = Material("M_SGM_HoseBlueGray", HoseBlueGray, 0.08f, 0.28f);
            Material glow = Material("M_SGM_IntakeGlow", IntakeGlow, 0f, 0.50f, true, IntakeGlow * 2.1f);
            Material lavender = Material("M_SGM_Lavender", Lavender, 0f, 0.44f);
            Material particle = ParticleMaterial("M_SGM_GiftBurstParticle");
            Texture2D airflowTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AirflowTexturePath);
            if (airflowTexture == null) throw new InvalidOperationException("Airflow texture is missing: " + AirflowTexturePath);
            Material airflowParticle = ParticleMaterial("M_SGM_AirflowParticle", airflowTexture);

            Mesh intakeShell = GetOrCreateIntakeShell();
            Mesh burstDiamond = GetOrCreateBurstDiamond();
            GameObject giftPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GiftPrefabPath);
            GameObject snowballPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SnowballPrefabPath);
            AudioClip suctionClip = AssetDatabase.LoadAssetAtPath<AudioClip>(VacuumSuctionAudioPath);
            AudioClip giftOutputClip = AssetDatabase.LoadAssetAtPath<AudioClip>(GiftOutputAudioPath);
            if (giftPrefabAsset == null) throw new InvalidOperationException("Gift prefab is missing: " + GiftPrefabPath);
            if (snowballPrefabAsset == null) throw new InvalidOperationException("Snowball prefab is missing: " + SnowballPrefabPath);
            if (suctionClip == null) throw new InvalidOperationException("Existing vacuum suction audio is missing: " + VacuumSuctionAudioPath);
            if (giftOutputClip == null) throw new InvalidOperationException("Gift output audio is missing: " + GiftOutputAudioPath);

            GameObject root = new GameObject("PF_SnowGiftMachine");
            try
            {
                Transform motionRoot = Child(root.transform, "MachineMotionRoot");
                AddBody(motionRoot, body, coral, cream, hose, lavender);
                Transform intakeAnchor = AddIntake(motionRoot, intakeShell, body, coral, hose, glow);
                Transform outputAnchor = AddOutput(motionRoot, coral, cream, glow);
                AudioSource suctionAudio = AddSpatialAudioSource(intakeAnchor, "SuctionAudio", 2.2f, 20f);
                AudioSource giftOutputAudio = AddSpatialAudioSource(outputAnchor, "GiftOutputAudio", 1.8f, 18f);
                ParticleSystem[] giftBurstVfx = AddGiftBurstVfx(outputAnchor, particle, burstDiamond);
                VisualEffect suctionVfx = AddVacuumVfx(motionRoot);
                ParticleSystem[] airflowVfx = AddSuctionAirflowVfx(intakeAnchor, airflowParticle,
                    out ParticleSystem powerOnVfx, out ParticleSystem powerOffVfx);
                Transform intakeVisual = AddIntakeVisual(root.transform, snowballPrefabAsset,
                    out MeshFilter intakeVisualFilter, out MeshRenderer intakeVisualRenderer);
                Transform giftPopDriver = AddGiftPopDriver(outputAnchor, giftPrefabAsset);
                MMF_Player intakeFeedback = AddIntakeFeedback(root.transform, intakeVisual, intakeAnchor);
                MMF_Player digestFeedback = AddDigestFeedback(root.transform, motionRoot);
                MMF_Player giftPopFeedback = AddGiftPopFeedback(root.transform, giftPopDriver);

                BoxCollider bodyCollider = root.AddComponent<BoxCollider>();
                bodyCollider.center = new Vector3(0f, 2.05f, 0.28f);
                bodyCollider.size = new Vector3(4.45f, 4.10f, 3.95f);

                SnowGiftMachinePresentation presentation = root.AddComponent<SnowGiftMachinePresentation>();
                presentation.Configure(intakeAnchor, outputAnchor, suctionVfx);
                presentation.ConfigureFeel(
                    motionRoot,
                    intakeVisual,
                    intakeVisualFilter,
                    intakeVisualRenderer,
                    intakeFeedback,
                    digestFeedback,
                    giftPopDriver,
                    giftPopFeedback,
                    giftPrefabAsset.GetComponent<Gift>(),
                    giftBurstVfx);
                presentation.ConfigureSuctionFeedback(airflowVfx, powerOnVfx, powerOffVfx);
                presentation.ConfigureAudio(suctionAudio, suctionClip, giftOutputAudio, giftOutputClip);
                AddSuctionTrigger(root.transform, presentation);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SnowGiftMachinePrefabBuilder] Built " + PrefabPath);
        }

        [MenuItem("PPack/Map/Winter Village/Validate Snow Gift Machine")]
        public static void Validate()
        {
            Build();
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (asset == null) throw new InvalidOperationException("Snow gift machine prefab is missing.");

            SnowGiftMachinePresentation presentation = asset.GetComponent<SnowGiftMachinePresentation>();
            if (presentation == null) throw new InvalidOperationException("SnowGiftMachinePresentation is missing.");
            if (presentation.IntakeAnchor == null || presentation.GiftOutputAnchor == null)
                throw new InvalidOperationException("Gameplay anchors are not wired.");
            if (presentation.SuctionVfx == null) throw new InvalidOperationException("Vacuum VFX is not wired.");
            if (AssetDatabase.GetAssetPath(presentation.SuctionVfx.visualEffectAsset) != VacuumVfxAssetPath)
                throw new InvalidOperationException("The prefab is not using the existing vacuum VFX asset.");
            if (presentation.SuctionVfx.gameObject.activeSelf)
                throw new InvalidOperationException("Suction VFX must remain off until a snowball enters the trigger.");
            if (presentation.MachineMotionRoot == null || presentation.IntakeVisual == null ||
                presentation.IntakeFeedback == null || presentation.DigestFeedback == null ||
                presentation.GiftPopDriver == null || presentation.GiftPopFeedback == null ||
                presentation.GiftPrefab == null || presentation.GiftBurstVfx == null ||
                presentation.GiftBurstVfx.Length < 3)
                throw new InvalidOperationException("Feel drivers or gift output are not wired.");
            if (presentation.AirflowVfx == null || presentation.AirflowVfx.Length < 2 ||
                presentation.PowerOnVfx == null || presentation.PowerOffVfx == null)
                throw new InvalidOperationException("Suction on, airflow, or off feedback is not wired.");
            if (presentation.SuctionAudioSource == null || presentation.GiftOutputAudioSource == null ||
                AssetDatabase.GetAssetPath(presentation.SuctionClip) != VacuumSuctionAudioPath ||
                AssetDatabase.GetAssetPath(presentation.GiftOutputClip) != GiftOutputAudioPath)
                throw new InvalidOperationException("Snow intake or gift output audio is not wired to the selected project SFX.");
            if (presentation.SuctionAudioSource.playOnAwake || presentation.SuctionAudioSource.loop ||
                presentation.GiftOutputAudioSource.playOnAwake || presentation.GiftOutputAudioSource.loop ||
                presentation.SuctionAudioSource.spatialBlend < 0.99f ||
                presentation.GiftOutputAudioSource.spatialBlend < 0.99f)
                throw new InvalidOperationException("Machine audio must be non-looping, 3D, and silent on awake.");
            if (presentation.IntakeFeedback.FeedbacksList.Count < 3 ||
                presentation.DigestFeedback.FeedbacksList.Count < 3 ||
                presentation.GiftPopFeedback.FeedbacksList.Count < 3)
                throw new InvalidOperationException("The three-stage Feel sequence is incomplete.");
            foreach (ParticleSystem particles in presentation.GiftBurstVfx)
            {
                if (particles == null || particles.main.loop || particles.main.playOnAwake ||
                    particles.emission.burstCount == 0)
                    throw new InvalidOperationException("Gift burst VFX must be a wired one-shot burst.");
            }
            int vortexRibbonCount = 0;
            foreach (ParticleSystem airflow in presentation.AirflowVfx)
            {
                if (airflow == null || !airflow.main.loop || airflow.main.playOnAwake ||
                    airflow.emission.rateOverTime.constantMax <= 0f)
                    throw new InvalidOperationException("Suction airflow must be a wired looping stream.");
                ParticleSystemRenderer airflowRenderer = airflow.GetComponent<ParticleSystemRenderer>();
                Texture airflowTexture = airflowRenderer != null && airflowRenderer.sharedMaterial != null
                    ? airflowRenderer.sharedMaterial.GetTexture("_BaseMap")
                    : null;
                if (airflowRenderer == null || airflowRenderer.renderMode != ParticleSystemRenderMode.Billboard ||
                    AssetDatabase.GetAssetPath(airflowTexture) != AirflowTexturePath)
                    throw new InvalidOperationException("Airflow must use the soft wisp texture instead of a square particle.");
                if (airflow.trails.enabled) vortexRibbonCount++;
            }
            if (vortexRibbonCount < 2 ||
                asset.transform.Find("MachineMotionRoot/Intake/IntakeAnchor/SuctionStateVFX/VortexCoreGlow") == null)
                throw new InvalidOperationException("Layered vortex ribbons or the intake core glow are missing.");
            if (presentation.PowerOnVfx.main.loop || presentation.PowerOffVfx.main.loop ||
                presentation.PowerOnVfx.emission.burstCount == 0 ||
                presentation.PowerOffVfx.emission.burstCount == 0)
                throw new InvalidOperationException("Suction power-state bursts are not configured as one-shots.");

            SnowGiftMachineSuctionTrigger trigger = asset.GetComponentInChildren<SnowGiftMachineSuctionTrigger>(true);
            BoxCollider triggerCollider = trigger != null ? trigger.GetComponent<BoxCollider>() : null;
            if (trigger == null || triggerCollider == null || !triggerCollider.isTrigger)
                throw new InvalidOperationException("Snowball intake trigger is missing or not configured as a trigger.");
            if (triggerCollider.size.x > 2.55f || triggerCollider.size.y > 2.55f || triggerCollider.size.z > 1.15f)
                throw new InvalidOperationException("The intake trigger extends beyond the front opening.");

            Transform intakeAnchor = presentation.IntakeAnchor;
            if (!trigger.IsWithinFrontIntake(intakeAnchor.TransformPoint(new Vector3(0f, 0f, -0.55f))))
                throw new InvalidOperationException("A snowball centered in front of the intake is not accepted.");
            if (trigger.IsWithinFrontIntake(intakeAnchor.TransformPoint(new Vector3(1.42f, 0f, -0.35f))) ||
                trigger.IsWithinFrontIntake(intakeAnchor.TransformPoint(new Vector3(0f, 0f, 0.35f))))
                throw new InvalidOperationException("The intake incorrectly accepts snowballs from the side or rear.");

            MeshRenderer bodyRenderer = FindRenderer(asset.transform, "MachineMotionRoot/Visual/BodyShell");
            if (bodyRenderer == null || !Approximately(bodyRenderer.sharedMaterial.color, BodySky))
                throw new InvalidOperationException("Bright single-color body material is not applied.");
            if (asset.transform.Find("MachineMotionRoot/Intake/SupportLeftFoot") != null ||
                asset.transform.Find("MachineMotionRoot/Intake/SupportRightFoot") != null)
                throw new InvalidOperationException("Obsolete intake feet are still present.");
            MeshFilter intakeFilter = asset.transform.Find("MachineMotionRoot/Intake/ContinuousIntakeShell")
                ?.GetComponent<MeshFilter>();
            if (intakeFilter == null || intakeFilter.sharedMesh == null || intakeFilter.sharedMesh.subMeshCount != 3)
                throw new InvalidOperationException("Continuous intake shell is missing or incomplete.");
            Transform frontGiftBadge = asset.transform.Find("MachineMotionRoot/Visual/FrontGiftBadge");
            if (frontGiftBadge == null || frontGiftBadge.Find("GiftBox") == null)
                throw new InvalidOperationException("Centered front gift badge is missing.");
            if (asset.transform.Find("MachineMotionRoot/Visual/FaceAndGiftBadge") != null)
                throw new InvalidOperationException("Obsolete machine eyes are still present.");
            if (asset.transform.Find("MachineMotionRoot/Visual/LowerAccentBand") != null ||
                asset.transform.Find("MachineMotionRoot/Visual/Indicator_1") != null ||
                asset.transform.Find("MachineMotionRoot/Visual/Indicator_2") != null ||
                asset.transform.Find("MachineMotionRoot/Visual/Indicator_3") != null)
                throw new InvalidOperationException("Obsolete lower accent bar or signal lights are still present.");
            Transform sidePictogram = asset.transform.Find("MachineMotionRoot/Visual/SideGiftPictogram");
            Transform sideGiftBox = sidePictogram != null ? sidePictogram.Find("GiftIcon/GiftBox") : null;
            if (sidePictogram == null || sideGiftBox == null || sidePictogram.Find("SnowballIcon") != null ||
                Mathf.Abs(sideGiftBox.localPosition.x) > 0.001f)
                throw new InvalidOperationException("The side panel must contain only one centered gift pictogram.");

            Scene validationScene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, validationScene);
                SnowGiftMachinePresentation instancePresentation = instance.GetComponent<SnowGiftMachinePresentation>();
                instancePresentation.BeginSuction();
                if (!instancePresentation.IsSuctionActive || !instancePresentation.SuctionVfx.gameObject.activeSelf)
                    throw new InvalidOperationException("Suction VFX did not activate.");
                foreach (ParticleSystem airflow in instancePresentation.AirflowVfx)
                    if (!airflow.isPlaying || !airflow.isEmitting)
                        throw new InvalidOperationException("Airflow did not start with the machine.");
                if (!instancePresentation.PowerOnVfx.isPlaying)
                    throw new InvalidOperationException("Power-on feedback did not play.");
                instancePresentation.EndSuction();
                if (instancePresentation.IsSuctionActive || instancePresentation.SuctionVfx.gameObject.activeSelf)
                    throw new InvalidOperationException("Suction VFX did not stop cleanly.");
                foreach (ParticleSystem airflow in instancePresentation.AirflowVfx)
                    if (airflow.isEmitting)
                        throw new InvalidOperationException("Airflow kept emitting after power-off.");
                if (!instancePresentation.PowerOffVfx.isPlaying)
                    throw new InvalidOperationException("Power-off feedback did not play.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(validationScene);
            }

            Debug.Log("[SnowGiftMachinePrefabBuilder] Validation passed: intake shrink, digest squash, gift pop, and reused vacuum VFX.");
        }

        [MenuItem("PPack/Map/Winter Village/Capture Snow Gift Machine Preview")]
        public static void CapturePreview()
        {
            Build();
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            AmbientMode previousAmbientMode = RenderSettings.ambientMode;
            Color previousAmbientLight = RenderSettings.ambientLight;
            Material groundMaterial = null;
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.26f, 0.36f, 0.52f);

                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                GameObject machine = (GameObject)PrefabUtility.InstantiatePrefab(asset, previewScene);
                SetLayerRecursively(machine, PreviewLayer);
                SnowGiftMachinePresentation previewPresentation = machine.GetComponent<SnowGiftMachinePresentation>();
                previewPresentation.BeginSuction();
                foreach (ParticleSystem airflow in previewPresentation.AirflowVfx)
                    airflow.Simulate(0.56f, true, false, true);
                previewPresentation.PowerOnVfx.Simulate(0.28f, true, false, true);

                Shader groundShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                groundMaterial = new Material(groundShader) { name = "M_SGM_PreviewSnow_Temporary" };
                if (groundMaterial.HasProperty("_BaseColor"))
                    groundMaterial.SetColor("_BaseColor", new Color(0.72f, 0.86f, 1f));
                if (groundMaterial.HasProperty("_Smoothness")) groundMaterial.SetFloat("_Smoothness", 0.20f);
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "PreviewSnowGround";
                ground.transform.position = new Vector3(0f, -0.28f, -0.7f);
                ground.transform.localScale = new Vector3(18f, 0.5f, 18f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                SceneManager.MoveGameObjectToScene(ground, previewScene);
                SetLayerRecursively(ground, PreviewLayer);

                AddPreviewLight(previewScene, "PreviewKey", new Color(0.80f, 0.90f, 1f), 1.35f, Quaternion.Euler(48f, -34f, 0f));
                AddPreviewLight(previewScene, "PreviewWarmFill", new Color(1f, 0.55f, 0.35f), 0.42f, Quaternion.Euler(32f, 145f, 0f));

                GameObject cameraObject = new GameObject("PreviewCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 38f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.035f, 0.075f, 0.16f);
                camera.cullingMask = 1 << PreviewLayer;
                camera.transform.position = new Vector3(9.8f, 7.4f, -13.2f);
                camera.transform.LookAt(new Vector3(0f, 2.0f, -1.1f));
                Capture(camera, PreviewPath, 1400, 900);
            }
            finally
            {
                EditorSceneManager.CloseScene(previewScene, true);
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                if (groundMaterial != null) UnityEngine.Object.DestroyImmediate(groundMaterial);
            }

            Debug.Log("[SnowGiftMachinePrefabBuilder] Preview captured at " + PreviewPath);
        }

        [MenuItem("PPack/Map/Winter Village/Build Snow Gift Machine Feel Test Scene")]
        public static void BuildFeelTestScene()
        {
            Build();
            EnsureFolder(TestSceneFolder);
            Scene testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                GameObject machinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                GameObject snowballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowballPrefabPath);
                GameObject machine = (GameObject)PrefabUtility.InstantiatePrefab(machinePrefab, testScene);
                machine.name = "SnowGiftMachine_TestSubject";

                GameObject snowball = (GameObject)PrefabUtility.InstantiatePrefab(snowballPrefab, testScene);
                snowball.name = "Snowball_TestInput";
                snowball.transform.position = new Vector3(0f, 0.45f, -8.25f);

                GameObject driverObject = new GameObject("FeelTestDriver");
                SceneManager.MoveGameObjectToScene(driverObject, testScene);
                SnowGiftMachineFeelTestDriver driver = driverObject.AddComponent<SnowGiftMachineFeelTestDriver>();
                driver.Configure(machine.transform, snowball.GetComponent<SnowBallCarrier>(), 1.2f);

                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "TestSnowGround";
                ground.transform.position = new Vector3(0f, -0.28f, -0.5f);
                ground.transform.localScale = new Vector3(18f, 0.5f, 18f);
                ground.GetComponent<Renderer>().sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_SGM_Cream.mat");
                SceneManager.MoveGameObjectToScene(ground, testScene);

                GameObject lightObject = new GameObject("TestDirectionalLight");
                SceneManager.MoveGameObjectToScene(lightObject, testScene);
                lightObject.transform.rotation = Quaternion.Euler(48f, -34f, 0f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(0.82f, 0.91f, 1f);
                light.intensity = 1.25f;
                light.shadows = LightShadows.Soft;

                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                SceneManager.MoveGameObjectToScene(cameraObject, testScene);
                Camera camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                camera.fieldOfView = 38f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.035f, 0.075f, 0.16f);
                camera.transform.position = new Vector3(12.8f, 7.0f, -5.2f);
                camera.transform.LookAt(new Vector3(0f, 2.2f, -0.1f));

                if (!EditorSceneManager.SaveScene(testScene, TestScenePath))
                    throw new InvalidOperationException("Failed to save Feel test scene: " + TestScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(testScene, true);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[SnowGiftMachinePrefabBuilder] Feel test scene built at " + TestScenePath);
        }

        [MenuItem("PPack/Map/Winter Village/Open Snow Gift Machine Feel Test Scene")]
        public static void OpenFeelTestScene()
        {
            if (!File.Exists(TestScenePath)) BuildFeelTestScene();
            EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        }

        [MenuItem("PPack/Map/Winter Village/Capture Running Snow Gift Machine Feel Frame")]
        public static void CaptureRunningFeelFrame()
        {
            Camera camera = Camera.main;
            if (!Application.isPlaying || camera == null)
            {
                Debug.LogWarning("[SnowGiftMachinePrefabBuilder] Open the Feel test scene and enter Play Mode first.");
                return;
            }

            string path = "/tmp/PPack_SnowGiftMachineFeel_" + Time.frameCount + ".png";
            Capture(camera, path, 1400, 900);
            Debug.Log("[SnowGiftMachinePrefabBuilder] Runtime Feel frame captured at " + path);
        }

        private static Transform AddIntakeVisual(
            Transform root,
            GameObject snowballPrefab,
            out MeshFilter meshFilter,
            out MeshRenderer meshRenderer)
        {
            Transform runtimeVisuals = Child(root, "RuntimeVisuals");
            Transform driver = Child(runtimeVisuals, "IntakeSnowVisual");
            meshFilter = driver.gameObject.AddComponent<MeshFilter>();
            meshRenderer = driver.gameObject.AddComponent<MeshRenderer>();

            MeshFilter sourceFilter = snowballPrefab.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = snowballPrefab.GetComponent<MeshRenderer>();
            if (sourceFilter == null || sourceRenderer == null)
                throw new InvalidOperationException("Snowball visual source is incomplete.");

            meshFilter.sharedMesh = sourceFilter.sharedMesh;
            meshRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            meshRenderer.enabled = false;
            return driver;
        }

        private static Transform AddGiftPopDriver(Transform outputAnchor, GameObject giftPrefab)
        {
            Transform driver = Child(outputAnchor, "GiftPopDriver");
            driver.localScale = Vector3.one * 0.01f;

            GameObject preview = (GameObject)PrefabUtility.InstantiatePrefab(giftPrefab);
            preview.name = "GiftPreviewVisual";
            preview.transform.SetParent(driver, false);
            preview.transform.localPosition = Vector3.zero;
            preview.transform.localRotation = Quaternion.identity;

            Gift gift = preview.GetComponent<Gift>();
            if (gift != null) gift.enabled = false;
            foreach (Collider target in preview.GetComponentsInChildren<Collider>(true)) target.enabled = false;
            foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true))
                UnityEngine.Object.DestroyImmediate(body);
            return driver;
        }

        private static MMF_Player AddIntakeFeedback(Transform root, Transform intakeVisual, Transform intakeAnchor)
        {
            Transform holder = Child(root, "Feel_IntakeSnow");
            MMF_Player player = holder.gameObject.AddComponent<MMF_Player>();
            player.RestoreInitialValuesOnDisable = false;
            player.CanPlayWhileAlreadyPlaying = false;

            player.AddFeedback(new MMF_Position
            {
                AnimatePositionTarget = intakeVisual.gameObject,
                Mode = MMF_Position.Modes.ToDestination,
                Space = MMF_Position.Spaces.World,
                MovementMode = MMF_Position.MovementModes.Duration,
                AnimatePositionDuration = 0.72f,
                AnimatePositionTween = Tween(
                    new Keyframe(0f, 0f, 0f, 0.2f),
                    new Keyframe(0.55f, 0.48f, 1.0f, 1.0f),
                    new Keyframe(1f, 1f, 2.4f, 0f)),
                DestinationPositionTransform = intakeAnchor,
                DeterminePositionsOnPlay = true,
                RelativePosition = false,
                AllowAdditivePlays = false
            });

            player.AddFeedback(new MMF_Scale
            {
                AnimateScaleTarget = intakeVisual,
                Mode = MMF_Scale.Modes.ToDestination,
                MovementMode = MMF_Scale.MovementModes.Duration,
                AnimateScaleDuration = 0.72f,
                DestinationScale = Vector3.one * 0.035f,
                DetermineScaleOnPlay = true,
                AnimateScaleTweenX = Tween(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.35f, 0.12f),
                    new Keyframe(0.72f, 0.60f),
                    new Keyframe(1f, 1f)),
                UniformScaling = true,
                AllowAdditivePlays = false
            });

            player.AddFeedback(new MMF_Rotation
            {
                AnimateRotationTarget = intakeVisual,
                Mode = MMF_Rotation.Modes.Additive,
                RotationSpace = Space.Self,
                AnimateRotationDuration = 0.72f,
                RemapCurveZero = 0f,
                RemapCurveOne = 230f,
                AnimateX = true,
                AnimateY = true,
                AnimateZ = false,
                AnimateRotationTweenX = Tween(new Keyframe(0f, 0f), new Keyframe(1f, 0.55f)),
                AnimateRotationTweenY = Tween(new Keyframe(0f, 0f), new Keyframe(1f, 1f)),
                DetermineRotationOnPlay = true,
                AllowAdditivePlays = false
            });
            return player;
        }

        private static MMF_Player AddDigestFeedback(Transform root, Transform motionRoot)
        {
            Transform holder = Child(root, "Feel_MachineDigest");
            MMF_Player player = holder.gameObject.AddComponent<MMF_Player>();
            player.RestoreInitialValuesOnDisable = false;
            player.CanPlayWhileAlreadyPlaying = false;
            MMFeedbackTiming timing = new MMFeedbackTiming { InitialDelay = 0.48f };
            AnimationCurve gulp = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.18f, 1f),
                new Keyframe(0.42f, -0.34f),
                new Keyframe(0.64f, 0.28f),
                new Keyframe(0.82f, -0.10f),
                new Keyframe(1f, 0f));

            player.AddFeedback(new MMF_SquashAndStretch
            {
                Timing = timing,
                SquashAndStretchTarget = motionRoot,
                Mode = MMF_SquashAndStretch.Modes.Absolute,
                Axis = MMF_SquashAndStretch.PossibleAxis.YtoXZ,
                AnimateScaleDuration = 0.66f,
                RemapCurveZero = 1f,
                RemapCurveOne = 0.82f,
                AnimateCurve = gulp,
                DetermineScaleOnPlay = true,
                AllowAdditivePlays = false
            });

            player.AddFeedback(new MMF_Position
            {
                Timing = new MMFeedbackTiming { InitialDelay = 0.48f },
                AnimatePositionTarget = motionRoot.gameObject,
                Mode = MMF_Position.Modes.AtoB,
                Space = MMF_Position.Spaces.Local,
                MovementMode = MMF_Position.MovementModes.Duration,
                AnimatePositionDuration = 0.66f,
                InitialPosition = Vector3.zero,
                DestinationPosition = new Vector3(0f, 0.14f, 0f),
                AnimatePositionTween = new MMTweenType(gulp),
                RelativePosition = true,
                DeterminePositionsOnPlay = true,
                AllowAdditivePlays = false
            });

            player.AddFeedback(new MMF_Rotation
            {
                Timing = new MMFeedbackTiming { InitialDelay = 0.48f },
                AnimateRotationTarget = motionRoot,
                Mode = MMF_Rotation.Modes.Additive,
                RotationSpace = Space.Self,
                AnimateRotationDuration = 0.66f,
                RemapCurveZero = 0f,
                RemapCurveOne = 3.2f,
                AnimateX = false,
                AnimateY = false,
                AnimateZ = true,
                AnimateRotationTweenZ = new MMTweenType(gulp),
                DetermineRotationOnPlay = true,
                AllowAdditivePlays = false
            });
            return player;
        }

        private static MMF_Player AddGiftPopFeedback(Transform root, Transform giftPopDriver)
        {
            Transform holder = Child(root, "Feel_GiftPop");
            MMF_Player player = holder.gameObject.AddComponent<MMF_Player>();
            player.RestoreInitialValuesOnDisable = false;
            player.CanPlayWhileAlreadyPlaying = false;
            MMFeedbackTiming timing = new MMFeedbackTiming { InitialDelay = 1.00f };

            player.AddFeedback(new MMF_Position
            {
                Timing = timing,
                AnimatePositionTarget = giftPopDriver.gameObject,
                Mode = MMF_Position.Modes.AtoB,
                Space = MMF_Position.Spaces.Local,
                MovementMode = MMF_Position.MovementModes.Duration,
                AnimatePositionDuration = 0.62f,
                InitialPosition = Vector3.zero,
                DestinationPosition = new Vector3(0f, 1.18f, 1.10f),
                AnimatePositionTween = Tween(
                    new Keyframe(0f, 0f, 0f, 2.8f),
                    new Keyframe(0.68f, 1.08f, 0.7f, 0.7f),
                    new Keyframe(1f, 1f, -0.2f, 0f)),
                RelativePosition = true,
                DeterminePositionsOnPlay = true,
                AllowAdditivePlays = false
            });

            MMTweenType popScale = Tween(
                new Keyframe(0f, 0f, 0f, 3.6f),
                new Keyframe(0.48f, 1.16f, 0.6f, 0.6f),
                new Keyframe(0.73f, 0.92f, -0.3f, -0.3f),
                new Keyframe(1f, 1f, 0.2f, 0f));
            player.AddFeedback(new MMF_Scale
            {
                Timing = new MMFeedbackTiming { InitialDelay = 1.00f },
                AnimateScaleTarget = giftPopDriver,
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = 0.62f,
                RemapCurveZero = 0.03f,
                RemapCurveOne = 1f,
                UniformScaling = true,
                AnimateScaleTweenX = popScale,
                AnimateScaleTweenY = popScale,
                AnimateScaleTweenZ = popScale,
                DetermineScaleOnPlay = false,
                AllowAdditivePlays = false
            });

            player.AddFeedback(new MMF_Rotation
            {
                Timing = new MMFeedbackTiming { InitialDelay = 1.00f },
                AnimateRotationTarget = giftPopDriver,
                Mode = MMF_Rotation.Modes.Additive,
                RotationSpace = Space.Self,
                AnimateRotationDuration = 0.62f,
                RemapCurveZero = 0f,
                RemapCurveOne = 16f,
                AnimateX = false,
                AnimateY = true,
                AnimateZ = true,
                AnimateRotationTweenY = Tween(new Keyframe(0f, 0f), new Keyframe(1f, 1f)),
                AnimateRotationTweenZ = Tween(
                    new Keyframe(0f, -0.35f),
                    new Keyframe(0.45f, 1f),
                    new Keyframe(1f, 0f)),
                DetermineRotationOnPlay = true,
                AllowAdditivePlays = false
            });
            return player;
        }

        private static MMTweenType Tween(params Keyframe[] keys) =>
            new MMTweenType(new AnimationCurve(keys));

        private static void AddBody(Transform root, Material body, Material coral, Material cream, Material hose,
            Material lavender)
        {
            Transform visual = Child(root, "Visual");
            Cube(visual, "BodyShell", new Vector3(0f, 2.05f, 0.30f), new Vector3(4.20f, 3.70f, 3.75f), body);
            Cube(visual, "Base", new Vector3(0f, 0.30f, 0.32f), new Vector3(4.55f, 0.52f, 4.05f), body);
            Cube(visual, "TopCap", new Vector3(0f, 4.02f, 0.30f), new Vector3(4.45f, 0.25f, 3.95f), coral);

            foreach (float x in new[] { -2.02f, 2.02f })
            foreach (float z in new[] { -1.48f, 2.08f })
            {
                string suffix = (x < 0f ? "L" : "R") + (z < 0f ? "F" : "B");
                Cube(visual, "CornerPost_" + suffix, new Vector3(x, 2.05f, z), new Vector3(0.34f, 3.85f, 0.34f), coral);
                Cube(visual, "CornerBlockTop_" + suffix, new Vector3(x, 3.80f, z), new Vector3(0.62f, 0.55f, 0.62f), coral);
                Cube(visual, "CornerBlockBottom_" + suffix, new Vector3(x, 0.42f, z), new Vector3(0.62f, 0.55f, 0.62f), coral);
            }

            Cube(visual, "FrontIntakePlate", new Vector3(0f, 1.57f, -1.63f), new Vector3(1.82f, 1.82f, 0.16f), cream);
            AddSideGiftPictogram(visual, cream, coral, lavender);
            AddFrontGiftBadge(visual, cream, coral);

            for (int i = 0; i < 5; i++)
            {
                Cylinder(visual, "HoseRib_" + (i + 1).ToString("00"), new Vector3(0f, 1.57f, -1.82f - i * 0.23f),
                    new Vector3(0.78f - i * 0.035f, 0.14f, 0.78f - i * 0.035f), hose, Quaternion.Euler(90f, 0f, 0f));
            }
        }

        private static Transform AddIntake(Transform root, Mesh intakeShell,
            Material body, Material coral, Material hose, Material glow)
        {
            Transform intake = Child(root, "Intake");
            intake.localPosition = new Vector3(0f, 1.57f, -4.02f);

            MeshPart(intake, "ContinuousIntakeShell", intakeShell, Vector3.zero, Quaternion.identity, Vector3.one,
                new[] { body, hose, coral });
            Cylinder(intake, "InnerGlow", new Vector3(0f, 0f, 1.05f), new Vector3(0.50f, 0.04f, 0.50f), glow, Quaternion.Euler(90f, 0f, 0f));

            Cube(intake, "SupportLeft", new Vector3(-1.23f, -1.35f, -0.56f), new Vector3(0.45f, 0.42f, 0.70f), coral,
                Quaternion.Euler(0f, 0f, -12f));
            Cube(intake, "SupportRight", new Vector3(1.23f, -1.35f, -0.56f), new Vector3(0.45f, 0.42f, 0.70f), coral,
                Quaternion.Euler(0f, 0f, 12f));

            return Empty(intake, "IntakeAnchor", new Vector3(0f, 0f, -1.20f), Quaternion.identity);
        }

        private static Transform AddOutput(Transform root, Material coral, Material cream, Material glow)
        {
            Transform output = Child(root, "GiftOutput");
            Cube(output, "ChuteBase", new Vector3(0f, 4.28f, 1.22f), new Vector3(1.82f, 0.48f, 1.42f), coral);
            Cube(output, "ChuteInner", new Vector3(0f, 4.48f, 1.22f), new Vector3(1.35f, 0.16f, 0.96f), glow);
            Cube(output, "RearLip", new Vector3(0f, 4.58f, 1.82f), new Vector3(1.82f, 0.34f, 0.22f), cream);
            return Empty(output, "GiftOutputAnchor", new Vector3(0f, 4.82f, 1.22f), Quaternion.identity);
        }

        private static ParticleSystem[] AddGiftBurstVfx(Transform outputAnchor, Material particleMaterial, Mesh particleMesh)
        {
            Transform root = Child(outputAnchor, "GiftBurstVFX");
            root.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            return new[]
            {
                CreateGiftParticles(root, "ConfettiPop", 20, 0.72f, 1.05f, 3.8f, 5.6f, 0.12f, 0.23f,
                    0.42f, Coral, Lavender, 48f, particleMaterial, particleMesh),
                CreateGiftParticles(root, "CreamSparkles", 14, 0.48f, 0.82f, 2.4f, 4.2f, 0.08f, 0.16f,
                    0.10f, Cream, IntakeGlow, 58f, particleMaterial, particleMesh),
                CreateGiftParticles(root, "MintPuffs", 9, 0.38f, 0.62f, 1.3f, 2.5f, 0.18f, 0.31f,
                    0f, BodySky, Cream, 68f, particleMaterial, particleMesh)
            };
        }

        private static ParticleSystem[] AddSuctionAirflowVfx(
            Transform intakeAnchor,
            Material material,
            out ParticleSystem powerOnVfx,
            out ParticleSystem powerOffVfx)
        {
            Transform root = Child(intakeAnchor, "SuctionStateVFX");
            ParticleSystem[] airflow =
            {
                CreateAirflowStream(root, "WideAirflow", new Vector3(0f, 0f, -2.55f),
                    1.28f, 14f, 3.8f, 5.0f, 0.54f, 0.76f, 0.52f, 0.88f,
                    new Color(0.72f, 0.92f, 1f, 0.24f), material),
                CreateAirflowStream(root, "CoreAirflow", new Vector3(0f, 0f, -2.15f),
                    0.62f, 9f, 4.7f, 6.0f, 0.42f, 0.62f, 0.38f, 0.68f,
                    new Color(0.90f, 0.97f, 1f, 0.32f), material),
                CreateVortexRibbonStream(root, "OuterVortexRibbons", -2.45f,
                    1.38f, 9f, 4.1f, 4.8f, -0.82f,
                    new Color(0.38f, 0.78f, 1f, 0.28f), material),
                CreateVortexRibbonStream(root, "InnerVortexRibbons", -1.85f,
                    0.86f, 12f, 4.8f, 6.2f, -0.62f,
                    new Color(0.72f, 0.94f, 1f, 0.42f), material),
                CreateVortexCore(root, material)
            };

            powerOnVfx = CreateSuctionStateBurst(root, "PowerOnGust",
                new Vector3(0f, 0f, -1.62f), Quaternion.identity, 18, 3.7f, 5.2f,
                0.30f, 0.48f, 0.42f, 0.76f, new Color(0.70f, 0.94f, 1f, 0.45f),
                24f, true, material);
            powerOffVfx = CreateSuctionStateBurst(root, "PowerOffPuff",
                new Vector3(0f, 0f, -0.18f), Quaternion.Euler(0f, 180f, 0f), 12, 1.2f, 2.2f,
                0.34f, 0.58f, 0.48f, 0.86f, new Color(0.90f, 0.95f, 1f, 0.32f),
                42f, false, material);
            return airflow;
        }

        private static ParticleSystem CreateVortexRibbonStream(
            Transform parent,
            string name,
            float localZ,
            float radius,
            float rate,
            float forwardSpeed,
            float orbitSpeed,
            float radialPull,
            Color color,
            Material material)
        {
            GameObject go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, localZ);
            ParticleSystem particles = go.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.58f, 0.82f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.44f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            main.maxParticles = Mathf.CeilToInt(rate) + 12;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.42f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.z = forwardSpeed;
            velocity.orbitalZ = orbitSpeed;
            velocity.radial = radialPull;

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = 0.16f;
            noise.strengthY = 0.16f;
            noise.strengthZ = 0.05f;
            noise.frequency = 0.48f;
            noise.scrollSpeed = 0.34f;

            ConfigureSuctionParticleFade(particles, 0.92f);
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.28f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.75f, 0.66f),
                new Keyframe(1f, 0.08f)));

            ParticleSystem.TrailModule trails = particles.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.42f);
            trails.minVertexDistance = 0.035f;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.worldSpace = false;
            trails.dieWithParticles = false;
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.52f),
                new Keyframe(0.38f, 1f),
                new Keyframe(1f, 0f)));

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.trailMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static ParticleSystem CreateVortexCore(Transform parent, Material material)
        {
            GameObject go = new GameObject("VortexCoreGlow", typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.30f);
            ParticleSystem particles = go.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.20f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.92f, 1.42f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.72f, 0.94f, 1f, 0.56f);
            main.maxParticles = 12;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 12f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            ConfigureSuctionParticleFade(particles, 0.95f);
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.12f),
                new Keyframe(0.18f, 1f),
                new Keyframe(1f, 0.18f)));
            ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-4.2f, 4.2f);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static ParticleSystem CreateAirflowStream(
            Transform parent,
            string name,
            Vector3 localPosition,
            float radius,
            float rate,
            float minSpeed,
            float maxSpeed,
            float minLifetime,
            float maxLifetime,
            float minSize,
            float maxSize,
            Color color,
            Material material)
        {
            GameObject go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            ParticleSystem particles = go.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            main.maxParticles = Mathf.CeilToInt(rate * maxLifetime) + 12;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = radius;
            shape.radiusThickness = 1f;

            ConfigureSuctionParticleFade(particles, 0.62f);
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.18f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.72f, 0.72f),
                new Keyframe(1f, 0.18f)));

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = 0.24f;
            noise.strengthY = 0.24f;
            noise.strengthZ = 0.06f;
            noise.frequency = 0.58f;
            noise.scrollSpeed = 0.38f;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(-0.72f, 0.72f);

            ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.1f, 1.1f);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static ParticleSystem CreateSuctionStateBurst(
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation,
            short count,
            float minSpeed,
            float maxSpeed,
            float minLifetime,
            float maxLifetime,
            float minSize,
            float maxSize,
            Color color,
            float coneAngle,
            bool stretched,
            Material material)
        {
            GameObject go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            ParticleSystem particles = go.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.65f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            main.maxParticles = count + 4;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = stretched ? 0.82f : 0.38f;
            shape.radiusThickness = 1f;

            ConfigureSuctionParticleFade(particles, stretched ? 0.48f : 0.78f);
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, stretched ? 0.18f : 0.35f),
                new Keyframe(0.18f, 1f),
                new Keyframe(1f, stretched ? 0.20f : 1.42f)));

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static void ConfigureSuctionParticleFade(ParticleSystem particles, float peakAlpha)
        {
            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peakAlpha, 0.14f),
                    new GradientAlphaKey(peakAlpha, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                }
            });
        }

        private static ParticleSystem CreateGiftParticles(
            Transform parent,
            string name,
            short count,
            float minLifetime,
            float maxLifetime,
            float minSpeed,
            float maxSpeed,
            float minSize,
            float maxSize,
            float gravity,
            Color colorA,
            Color colorB,
            float coneAngle,
            Material material,
            Mesh mesh)
        {
            GameObject go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            ParticleSystem particles = go.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = 1.1f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.gravityModifier = gravity;
            main.maxParticles = count + 4;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.08f;

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(1f, 0.58f),
                    new GradientAlphaKey(0f, 1f)
                }
            });

            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.15f),
                new Keyframe(0.16f, 1.20f),
                new Keyframe(0.72f, 0.92f),
                new Keyframe(1f, 0f)));

            ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-4.5f, 4.5f);

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.24f;
            noise.frequency = 0.55f;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static VisualEffect AddVacuumVfx(Transform root)
        {
            GameObject vfxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VacuumVfxPrefabPath);
            if (vfxPrefab == null) throw new InvalidOperationException("Existing vacuum VFX prefab is missing: " + VacuumVfxPrefabPath);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(vfxPrefab);
            instance.name = "SuctionVFX_ExistingVacuum";
            instance.transform.SetParent(root, false);
            instance.transform.localPosition = new Vector3(0f, 1.57f, -4.42f);
            instance.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            instance.transform.localScale = Vector3.one * 0.78f;

            VisualEffect effect = instance.GetComponent<VisualEffect>();
            if (effect == null) throw new InvalidOperationException("Existing vacuum VFX prefab has no VisualEffect component.");
            effect.SetVector3("Scale", new Vector3(0.42f, 0.86f, 0.42f));
            effect.SetVector4("TornadoCoreColor", new Vector4(0.78f, 0.94f, 1.0f, 0.82f));
            effect.SetVector4("TornadoLayer1Color", new Vector4(0.28f, 0.76f, 1.0f, 0.68f));
            effect.SetVector3("TopRingPosition", new Vector3(0f, 2.45f, 0f));
            instance.SetActive(false);
            return effect;
        }

        private static void AddSuctionTrigger(Transform root, SnowGiftMachinePresentation presentation)
        {
            Transform trigger = Child(root, "Gameplay/IntakeTrigger");
            trigger.localPosition = new Vector3(0f, 1.57f, -5.78f);
            BoxCollider collider = trigger.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(2.50f, 2.50f, 1.12f);
            SnowGiftMachineSuctionTrigger relay = trigger.gameObject.AddComponent<SnowGiftMachineSuctionTrigger>();
            relay.Configure(presentation, 1.18f, 1.25f, 0.06f);
        }

        private static AudioSource AddSpatialAudioSource(
            Transform parent,
            string name,
            float minDistance,
            float maxDistance)
        {
            Transform audioRoot = Child(parent, name);
            AudioSource source = audioRoot.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            return source;
        }

        private static void AddSideGiftPictogram(Transform parent, Material cream,
            Material coral, Material lavender)
        {
            Transform mark = Child(parent, "SideGiftPictogram");
            mark.localPosition = new Vector3(2.13f, 2.20f, 0.35f);
            mark.localRotation = Quaternion.Euler(0f, 90f, 0f);

            Transform gift = Child(mark, "GiftIcon");
            Cube(gift, "GiftBox", new Vector3(0f, -0.05f, 0.02f), new Vector3(0.86f, 0.68f, 0.10f), coral);
            Cube(gift, "GiftLid", new Vector3(0f, 0.33f, 0.05f), new Vector3(0.98f, 0.18f, 0.12f), coral);
            Cube(gift, "RibbonVertical", new Vector3(0f, -0.01f, 0.10f), new Vector3(0.14f, 0.80f, 0.06f), cream);
            Cube(gift, "RibbonHorizontal", new Vector3(0f, 0.04f, 0.11f), new Vector3(0.90f, 0.12f, 0.06f), cream);
            Sphere(gift, "BowLeft", new Vector3(-0.17f, 0.50f, 0.11f), new Vector3(0.26f, 0.16f, 0.055f), lavender);
            Sphere(gift, "BowRight", new Vector3(0.17f, 0.50f, 0.11f), new Vector3(0.26f, 0.16f, 0.055f), lavender);
            Sphere(gift, "BowKnot", new Vector3(0f, 0.46f, 0.15f), new Vector3(0.14f, 0.14f, 0.06f), cream);
        }

        private static void AddFrontGiftBadge(Transform parent, Material cream, Material coral)
        {
            Transform badge = Child(parent, "FrontGiftBadge");
            badge.localPosition = new Vector3(0f, 3.54f, -1.78f);
            Cube(badge, "BadgePlate", Vector3.zero, new Vector3(1.08f, 0.68f, 0.10f), cream);
            Cube(badge, "GiftBox", new Vector3(0f, -0.05f, -0.08f), new Vector3(0.52f, 0.34f, 0.07f), coral);
            Cube(badge, "GiftLid", new Vector3(0f, 0.15f, -0.09f), new Vector3(0.62f, 0.10f, 0.07f), coral);
            Cube(badge, "RibbonVertical", new Vector3(0f, -0.01f, -0.13f), new Vector3(0.09f, 0.40f, 0.04f), cream);
            Sphere(badge, "BowLeft", new Vector3(-0.12f, 0.25f, -0.12f), new Vector3(0.14f, 0.09f, 0.04f), coral);
            Sphere(badge, "BowRight", new Vector3(0.12f, 0.25f, -0.12f), new Vector3(0.14f, 0.09f, 0.04f), coral);
        }

        private static Mesh GetOrCreateIntakeShell()
        {
            const string name = "MSH_SGM_ContinuousIntakeShell";
            string path = GeneratedFolder + "/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }

            const int segments = 24;
            const float frontZ = -1.16f;
            const float backZ = 1.22f;
            var vertices = new List<Vector3>(segments * 4);
            var outerTriangles = new List<int>(segments * 6);
            var innerTriangles = new List<int>(segments * 6);
            var rimTriangles = new List<int>(segments * 12);
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float x = Mathf.Cos(angle);
                float y = Mathf.Sin(angle);
                vertices.Add(new Vector3(x * 1.68f, y * 1.68f, frontZ));
                vertices.Add(new Vector3(x * 0.80f, y * 0.80f, backZ));
                vertices.Add(new Vector3(x * 1.24f, y * 1.24f, frontZ));
                vertices.Add(new Vector3(x * 0.50f, y * 0.50f, backZ));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int a = i * 4;
                int b = next * 4;
                AddQuad(outerTriangles, a, b, a + 1, b + 1, false);
                AddQuad(innerTriangles, a + 2, a + 3, b + 2, b + 3, false);
                AddQuad(rimTriangles, a, a + 2, b, b + 2, false);
                AddQuad(rimTriangles, a + 1, b + 1, a + 3, b + 3, false);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(outerTriangles, 0);
            mesh.SetTriangles(innerTriangles, 1);
            mesh.SetTriangles(rimTriangles, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Mesh GetOrCreateBurstDiamond()
        {
            const string name = "MSH_SGM_GiftBurstDiamond";
            string path = GeneratedFolder + "/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }

            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(0f, 0.68f, 0f), new Vector3(0f, -0.68f, 0f),
                new Vector3(0.44f, 0f, 0f), new Vector3(-0.44f, 0f, 0f),
                new Vector3(0f, 0f, 0.24f), new Vector3(0f, 0f, -0.24f)
            };
            mesh.triangles = new[]
            {
                0, 4, 2, 0, 3, 4, 0, 5, 3, 0, 2, 5,
                1, 2, 4, 1, 4, 3, 1, 3, 5, 1, 5, 2
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Mesh GetOrCreateFrustum(string name, float frontRadius, float backRadius, float frontZ, float backZ, bool inward)
        {
            string path = GeneratedFolder + "/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }

            const int segments = 12;
            var vertices = new List<Vector3>(segments * 2);
            var triangles = new List<int>(segments * 6);
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float x = Mathf.Cos(angle);
                float y = Mathf.Sin(angle);
                vertices.Add(new Vector3(x * frontRadius, y * frontRadius, frontZ));
                vertices.Add(new Vector3(x * backRadius, y * backRadius, backZ));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int a = i * 2;
                int b = next * 2;
                int c = a + 1;
                int d = b + 1;
                if (inward)
                {
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
                else
                {
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Mesh GetOrCreateHollowRing(string name, float outerRadius, float innerRadius, float depth)
        {
            string path = GeneratedFolder + "/" + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }

            const int segments = 12;
            float halfDepth = depth * 0.5f;
            var vertices = new List<Vector3>(segments * 4);
            var triangles = new List<int>(segments * 24);
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                float x = Mathf.Cos(angle);
                float y = Mathf.Sin(angle);
                vertices.Add(new Vector3(x * outerRadius, y * outerRadius, -halfDepth));
                vertices.Add(new Vector3(x * outerRadius, y * outerRadius, halfDepth));
                vertices.Add(new Vector3(x * innerRadius, y * innerRadius, -halfDepth));
                vertices.Add(new Vector3(x * innerRadius, y * innerRadius, halfDepth));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int a = i * 4;
                int b = next * 4;
                AddQuad(triangles, a, b, a + 1, b + 1, false);
                AddQuad(triangles, a + 2, a + 3, b + 2, b + 3, false);
                AddQuad(triangles, a, a + 2, b, b + 2, true);
                AddQuad(triangles, a + 1, b + 1, a + 3, b + 3, true);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void AddQuad(List<int> triangles, int a, int b, int c, int d, bool flip)
        {
            if (flip)
            {
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
            else
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
        }

        private static Material Material(string name, Color color, float metallic, float smoothness, bool emission = false, Color emissionColor = default)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emissionColor);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ParticleMaterial(string name, Texture texture = null)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                            Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", texture != null ? 2f : 0f);
            if (texture != null)
            {
                if (material.HasProperty("_SrcBlend"))
                    material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetFloat("_DstBlend", (float)BlendMode.One);
                if (material.HasProperty("_SrcBlendAlpha"))
                    material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                if (material.HasProperty("_DstBlendAlpha"))
                    material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform Child(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Transform Empty(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.transform.localRotation = rotation;
            return child.transform;
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material, Quaternion rotation = default)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material material, Quaternion rotation)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject Sphere(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void MeshPart(Transform parent, string name, Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void MeshPart(Transform parent, string name, Mesh mesh, Vector3 position, Quaternion rotation,
            Vector3 scale, Material[] materials)
        {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = materials;
        }

        private static MeshRenderer FindRenderer(Transform root, string path)
        {
            Transform child = root.Find(path);
            return child != null ? child.GetComponent<MeshRenderer>() : null;
        }

        private static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f &&
                   Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }

        private static void AddPreviewLight(Scene scene, string name, Color color, float intensity, Quaternion rotation)
        {
            GameObject go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = rotation;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
            light.cullingMask = 1 << PreviewLayer;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void Capture(Camera camera, string path, int width, int height)
        {
            RenderTexture texture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = texture;
                RenderTexture.active = texture;
                camera.Render();
                Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(texture);
            }
        }
    }
}
#endif

using System;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using MoreMountains.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    public static class PenguinTutorialSceneBuilder
    {
        public const string ScenePath = "Assets/Game/InGame/Tutorial/Scenes/PenguinTutorial.unity";

        private const string PenguinPrefabPath = "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab";
        private const string SnowGiftMachinePrefabPath =
            "Assets/Game/InGame/Delivery/Prefabs/GiftProductionFlow/PF_SnowGiftMachine.prefab";
        private const string GiftDeliveryTerminalPrefabPath =
            "Assets/Game/InGame/Delivery/Prefabs/GiftProductionFlow/GiftDeliveryTerminal.prefab";
        private const string WarehousePrefabPath =
            "Assets/Game/InGame/Delivery/Prefabs/GiftProductionFlow/PF_SnowballWarehouse.prefab";
        private const string PanelSettingsPath = "Assets/Game/InGame/UI/StageHUD/StageHUDPanelSettings.asset";
        private const string TutorialPanelSettingsPath =
            "Assets/Game/InGame/Tutorial/UI/PenguinTutorialPanelSettings.asset";
        private const string UxmlPath = "Assets/Game/InGame/Tutorial/UI/PenguinTutorial.uxml";
        private const string CutsceneUxmlPath = "Assets/Game/InGame/Tutorial/UI/TutorialComicCutscene.uxml";
        private const string KoreanFontPath = "Assets/Game/InGame/UI/MissionHUD/Fonts/NotoSansKR-Variable.ttf";
        private const string EnglishDisplayFontPath = "Assets/Game/Core/UI/Fonts/LilitaOne-Regular.ttf";
        private const string TutorialMusicPath =
            "Assets/Game/InGame/Tutorial/Audio/TutorialWinterStory_Loop.mp3";
        private const string CutsceneRevealSfxPath =
            "Assets/CelerisLab/CompleteUISFX/basic_interactions_and_navigation/button_click_02.wav";
        private const string ComicBubbleSfxPath =
            "Assets/Feel/NiceVibrations/HapticSamples/ApplicationUX/Pop1.wav";
        private const string ComicMagicSfxPath =
            "Assets/CelerisLab/CompleteUISFX/basic_interactions_and_navigation/buttons/magic_button/magic_button_03.wav";
        private const string ComicWhooshSfxPath =
            "Assets/CelerisLab/CompleteUISFX/basic_interactions_and_navigation/menu_open_fast_04.wav";

        private static readonly string[] CutsceneCardPaths =
        {
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_01_SnowMachineLeft_GoldenCelV6SnowCentered.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_01_SnowMachineRight_GoldenCelV4Matched.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_02_FairiesLeft_GoldenCelV4Matched.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_02_FairiesRight_GoldenCelV4Matched.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_03_SantaLeft_GoldenCelV4Matched.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_03_SantaRight_GoldenCelV4Matched.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_04_GiftLeft_GoldenCelV4Matched.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_04_GiftRight_GoldenCelV4Matched.png"
        };

        private static readonly string[] DeliveryStoryCardPaths =
        {
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_05_ParcelSendLeft_GoldenCelV2OutdoorVillage.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_05_ParcelSendRight_GoldenCelV2OutdoorVillage.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_06_RemotePickupLeft_GoldenCel.png",
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_06_RemoteDeliveryRight_GoldenCel.png"
        };

        private static readonly TutorialComicCutscene.ComicDialogueLine[] OpeningComicDialogueLines =
        {
            Dialogue("펭귄", "좋아, 눈을 잔뜩 모았어!", 7f, 11f, true,
                TutorialComicCutscene.ComicDialogueStyle.Penguin),
            Dialogue("펭귄", "이 호스에 넣으면 되겠지?", 64f, 11f, false,
                TutorialComicCutscene.ComicDialogueStyle.Penguin),
            Dialogue("요정", "차갑게 다듬고…", 7f, 11f, true,
                TutorialComicCutscene.ComicDialogueStyle.Fairy),
            Dialogue("요정", "선물 모양을 만들어요!", 64f, 11f, false,
                TutorialComicCutscene.ComicDialogueStyle.Fairy),
            Dialogue("산타", "이제 따뜻한 마법을 살짝!", 7f, 11f, true,
                TutorialComicCutscene.ComicDialogueStyle.Santa),
            Dialogue(string.Empty, "반짝—!", 66f, 14f, false,
                TutorialComicCutscene.ComicDialogueStyle.MagicEffect),
            Dialogue("산타", "예쁘게 포장하면…", 7f, 12f, true,
                TutorialComicCutscene.ComicDialogueStyle.Santa),
            Dialogue("펭귄", "선물 완성!", 65f, 13f, false,
                TutorialComicCutscene.ComicDialogueStyle.Penguin)
        };

        private static readonly TutorialComicCutscene.ComicDialogueLine[] DeliveryComicDialogueLines =
        {
            Dialogue("펭귄", "우편기에 밀어 넣고…", 7f, 11f, true,
                TutorialComicCutscene.ComicDialogueStyle.Penguin),
            Dialogue(string.Empty, "슈우웅—!", 65f, 14f, false,
                TutorialComicCutscene.ComicDialogueStyle.WhooshEffect),
            Dialogue("펭귄", "창고에 도착했어!", 7f, 11f, true,
                TutorialComicCutscene.ComicDialogueStyle.Penguin),
            Dialogue("펭귄", "이웃집까지 배달 완료!", 64f, 11f, false,
                TutorialComicCutscene.ComicDialogueStyle.Penguin)
        };

        private const string SnowMaterialPath = "Assets/Game/InGame/Tutorial/Materials/M_TutorialSnowGround.mat";
        private const string EdgeMaterialPath = "Assets/Game/InGame/Tutorial/Materials/M_TutorialEdge.mat";
        private const string CyanMaterialPath = "Assets/Game/InGame/Tutorial/Materials/M_TutorialCyan.mat";
        private const string GreenMaterialPath = "Assets/Game/InGame/Tutorial/Materials/M_TutorialGreen.mat";
        private const string OrangeMaterialPath = "Assets/Game/InGame/Tutorial/Materials/M_TutorialOrange.mat";
        private const string IndoorWallMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialIndoorWall.mat";
        private const string IndoorColumnMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialIndoorColumn.mat";
        private const string IndoorWindowMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialIndoorWindow.mat";
        private const string GoalParticleMaterialPath = "Assets/Game/InGame/Tutorial/VFX/M_TutorialGoalParticle.mat";
        private const string GoalParticleTexturePath = "Assets/Game/InGame/Tutorial/VFX/T_TutorialGoalStar.asset";
        private const string GuideArrowMaterialPath = "Assets/Game/InGame/Tutorial/VFX/M_TutorialGuideArrow.mat";
        private const string GuideArrowTexturePath = "Assets/Game/InGame/Tutorial/VFX/T_TutorialGuideArrow.asset";
        private const string BarrierMaterialPath = "Assets/Game/InGame/Tutorial/VFX/M_TutorialQuestBarrier.mat";
        private const string BarrierTexturePath = "Assets/Game/InGame/Tutorial/VFX/T_TutorialQuestBarrierGrid.asset";
        private const string BarrierParticleMaterialPath =
            "Assets/Game/InGame/Tutorial/VFX/M_TutorialQuestBarrierSnowflake.mat";
        private const string BarrierParticleTexturePath =
            "Assets/Game/InGame/Tutorial/VFX/T_TutorialQuestBarrierSnowflake.asset";
        private const string CandyRedMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialCandyRed.mat";
        private const string CandyCreamMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialCandyCream.mat";
        private const string RoomSignKeyPlateMaterialPath =
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Materials/M_RoomSign_KeyPlate.mat";
        private const string RoomSignTitleTextMaterialPath =
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Materials/M_RoomSign_TitleText.mat";
        private const string RoomSignKeyTextMaterialPath =
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Materials/M_RoomSign_KeyText.mat";

        private static readonly string[] RoomSignIconPaths =
        {
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Walk.png",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Run.png",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Slide.png",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Snowball.png",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Machine.png",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Delivery.png"
        };

        private static readonly string[] RoomSignIconMaterialPaths =
        {
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Materials/M_RoomSign_Walk.mat",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Materials/M_RoomSign_Run.mat",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Materials/M_RoomSign_Slide.mat",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Materials/M_RoomSign_Snowball.mat",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Materials/M_RoomSign_Machine.mat",
            "Assets/Game/InGame/Tutorial/UI/RoomSigns/Materials/M_RoomSign_Delivery.mat"
        };

        private static readonly Vector3 PlayerSpawn = new Vector3(-20f, 0.6f, -13f);
        private static readonly Vector3 WalkTarget = new Vector3(-15f, 0f, -10f);
        private static readonly Vector3 RunTarget = new Vector3(-3f, 0f, -11f);
        private static readonly Vector3 SlideTarget = new Vector3(10f, 0f, -4f);
        private static readonly Vector3 SnowballSpawn = new Vector3(15f, 0.25f, 8.5f);
        private static readonly Vector3 SnowGiftMachinePosition = new Vector3(2f, 0f, 10.5f);
        private static readonly Vector3 SnowGiftMachineEuler = new Vector3(0f, -90f, 0f);
        private static readonly Vector3 GiftRoomSenderPosition = new Vector3(-10.5f, 0.13f, 6.5f);
        private static readonly Vector3 GiftRoomWarehousePosition = new Vector3(-19f, 0.1f, 13.5f);
        private static readonly Vector3 NeighborDeliveryPosition = new Vector3(-12.3f, 0.04f, 14.0f);
        // The gift is pushed through the terminal's local -Z entrance. Face that entrance
        // toward the open north side of the gift room, away from both the east gate and south divider.
        private static readonly Vector3 DeliveryTerminalEuler = new Vector3(0f, 180f, 0f);

        private static readonly Vector2 WalkRoomCenter = new Vector2(-16f, -11f);
        private static readonly Vector2 WalkRoomSize = new Vector2(16f, 14f);
        private static readonly Vector2 RunRoomCenter = new Vector2(0f, -11f);
        private static readonly Vector2 RunRoomSize = new Vector2(16f, 14f);
        private static readonly Vector2 SlideRoomCenter = new Vector2(16f, -4f);
        private static readonly Vector2 SlideRoomSize = new Vector2(16f, 28f);

        private static TutorialComicCutscene.ComicDialogueLine Dialogue(string speaker, string text,
            float xPercent, float yPercent, bool tailPointsRight,
            TutorialComicCutscene.ComicDialogueStyle style)
        {
            return new TutorialComicCutscene.ComicDialogueLine(speaker, text,
                new Vector2(xPercent, yPercent), tailPointsRight, style);
        }

        [InitializeOnLoadMethod]
        private static void QueueFirstBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            EditorApplication.delayCall += BuildIfMissing;
        }

        private static void BuildIfMissing()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += BuildIfMissing;
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
        }

        [MenuItem("PPack/Tutorial/Build Penguin Tutorial Scene")]
        public static void Build()
        {
            EnsureFolders();

            Scene currentScene = SceneManager.GetActiveScene();
            bool buildInSingleScene = currentScene.IsValid() &&
                                      (currentScene.path == ScenePath || string.IsNullOrEmpty(currentScene.path));

            GameObject penguinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PenguinPrefabPath);
            GameObject snowGiftMachinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowGiftMachinePrefabPath);
            GameObject giftDeliveryTerminalPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(GiftDeliveryTerminalPrefabPath);
            GameObject warehousePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WarehousePrefabPath);
            VisualTreeAsset tutorialUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            VisualTreeAsset cutsceneUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CutsceneUxmlPath);
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Font koreanFont = AssetDatabase.LoadAssetAtPath<Font>(KoreanFontPath);
            Font englishDisplayFont = AssetDatabase.LoadAssetAtPath<Font>(EnglishDisplayFontPath);
            AudioClip tutorialMusic = AssetDatabase.LoadAssetAtPath<AudioClip>(TutorialMusicPath);
            AudioClip cutsceneRevealSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(CutsceneRevealSfxPath);
            AudioClip comicBubbleSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(ComicBubbleSfxPath);
            AudioClip comicMagicSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(ComicMagicSfxPath);
            AudioClip comicWhooshSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(ComicWhooshSfxPath);
            var cutsceneCards = new Texture2D[CutsceneCardPaths.Length];
            for (int index = 0; index < CutsceneCardPaths.Length; index++)
                cutsceneCards[index] = AssetDatabase.LoadAssetAtPath<Texture2D>(CutsceneCardPaths[index]);
            var deliveryStoryCards = new Texture2D[DeliveryStoryCardPaths.Length];
            for (int index = 0; index < DeliveryStoryCardPaths.Length; index++)
                deliveryStoryCards[index] = AssetDatabase.LoadAssetAtPath<Texture2D>(DeliveryStoryCardPaths[index]);
            var roomSignIcons = new Texture2D[RoomSignIconPaths.Length];
            var roomSignIconMaterials = new Material[RoomSignIconPaths.Length];
            for (int index = 0; index < RoomSignIconPaths.Length; index++)
            {
                roomSignIcons[index] = AssetDatabase.LoadAssetAtPath<Texture2D>(RoomSignIconPaths[index]);
                if (roomSignIcons[index] == null)
                    throw new InvalidOperationException($"튜토리얼 구간 표지판 아이콘을 찾을 수 없다: {RoomSignIconPaths[index]}");
                roomSignIconMaterials[index] = GetOrCreateRoomSignIconMaterial(
                    roomSignIcons[index], RoomSignIconMaterialPaths[index], $"M_RoomSign_{index + 1:00}");
            }
            if (penguinPrefab == null) throw new InvalidOperationException($"펭귄 프리팹이 없다: {PenguinPrefabPath}");
            if (snowGiftMachinePrefab == null)
                throw new InvalidOperationException($"눈 선물 기계 프리팹이 없다: {SnowGiftMachinePrefabPath}");
            if (giftDeliveryTerminalPrefab == null ||
                giftDeliveryTerminalPrefab.GetComponent<GiftDeliveryTerminal>() == null)
                throw new InvalidOperationException($"선물 배송 단말기 프리팹이 없다: {GiftDeliveryTerminalPrefabPath}");
            if (warehousePrefab == null || warehousePrefab.GetComponent<SnowballWarehouseStorage>() == null)
                throw new InvalidOperationException($"창고 프리팹 또는 저장 컴포넌트가 없다: {WarehousePrefabPath}");
            if (tutorialUxml == null || cutsceneUxml == null || panelSettings == null || koreanFont == null ||
                englishDisplayFont == null)
                throw new InvalidOperationException("튜토리얼 UI 또는 한글 폰트 에셋을 찾을 수 없다.");
            PanelSettings tutorialPanelSettings = GetOrCreateTutorialPanelSettings(panelSettings);
            if (tutorialMusic == null)
                throw new InvalidOperationException($"튜토리얼 음악을 찾을 수 없다: {TutorialMusicPath}");
            if (cutsceneRevealSfx == null)
                throw new InvalidOperationException($"컷신 등장 효과음을 찾을 수 없다: {CutsceneRevealSfxPath}");
            if (comicBubbleSfx == null || comicMagicSfx == null || comicWhooshSfx == null)
                throw new InvalidOperationException("컷신 말풍선 효과음 중 하나 이상을 찾을 수 없다.");
            for (int index = 0; index < cutsceneCards.Length; index++)
                if (cutsceneCards[index] == null)
                    throw new InvalidOperationException($"튜토리얼 만화 카드를 찾을 수 없다: {CutsceneCardPaths[index]}");
            for (int index = 0; index < deliveryStoryCards.Length; index++)
                if (deliveryStoryCards[index] == null)
                    throw new InvalidOperationException($"배송 이야기 만화 카드를 찾을 수 없다: {DeliveryStoryCardPaths[index]}");

            Material snow = GetOrCreateMaterial(SnowMaterialPath, "M_TutorialSnowGround", new Color(0.69f, 0.83f, 0.91f), 0.12f);
            Material edge = GetOrCreateMaterial(EdgeMaterialPath, "M_TutorialEdge", new Color(0.055f, 0.12f, 0.20f), 0.28f);
            Material cyan = GetOrCreateMaterial(CyanMaterialPath, "M_TutorialCyan", new Color(0.18f, 0.82f, 0.96f), 0.22f, true);
            Material green = GetOrCreateMaterial(GreenMaterialPath, "M_TutorialGreen", new Color(0.22f, 0.82f, 0.46f), 0.18f, true);
            Material orange = GetOrCreateMaterial(OrangeMaterialPath, "M_TutorialOrange", new Color(1f, 0.58f, 0.16f), 0.18f, true);
            Material indoorWall = GetOrCreateMaterial(IndoorWallMaterialPath, "M_TutorialIndoorWall",
                new Color(0.08f, 0.30f, 0.38f), 0.24f);
            Material indoorColumn = GetOrCreateMaterial(IndoorColumnMaterialPath, "M_TutorialIndoorColumn",
                new Color(0.76f, 0.19f, 0.43f), 0.22f);
            Material indoorWindow = GetOrCreateMaterial(IndoorWindowMaterialPath, "M_TutorialIndoorWindow",
                new Color(0.30f, 0.72f, 0.88f), 0.26f, true);
            Material roomSignKeyPlate = GetOrCreateRoomSignKeyPlateMaterial();
            Material roomSignTitleText = GetOrCreateRoomSignTextMaterial(englishDisplayFont,
                RoomSignTitleTextMaterialPath, "M_RoomSign_TitleText", Color.white);
            Material roomSignKeyText = GetOrCreateRoomSignTextMaterial(englishDisplayFont,
                RoomSignKeyTextMaterialPath, "M_RoomSign_KeyText", new Color(0.035f, 0.09f, 0.16f));
            Texture2D goalParticleTexture = GetOrCreateGoalParticleTexture();
            Material goalParticleMaterial = GetOrCreateParticleMaterial(goalParticleTexture);
            Texture2D guideArrowTexture = GetOrCreateGuideArrowTexture();
            Material guideArrowMaterial = GetOrCreateGuideMaterial(guideArrowTexture);
            Texture2D barrierTexture = GetOrCreateBarrierTexture();
            Material barrierMaterial = GetOrCreateBarrierMaterial(barrierTexture);
            Texture2D barrierParticleTexture = GetOrCreateBarrierSnowflakeTexture();
            Material barrierParticleMaterial = GetOrCreateBarrierParticleMaterial(barrierParticleTexture);

            Scene originalScene = buildInSingleScene ? default : SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                buildInSingleScene ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            // NewSceneMode.Single 전환은 Unity 6 beta에서 직전에 로드한 UI 에셋의 네이티브
            // 참조를 무효화할 수 있다. 새 씬이 활성화된 뒤 다시 로드해 UIDocument에 영속 참조를 넣는다.
            tutorialUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            cutsceneUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CutsceneUxmlPath);
            panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            tutorialPanelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(TutorialPanelSettingsPath);
            if (tutorialUxml == null || cutsceneUxml == null || panelSettings == null || tutorialPanelSettings == null)
                throw new InvalidOperationException("새 씬 전환 후 튜토리얼 UI 에셋을 다시 로드하지 못했다.");

            var root = new GameObject("PenguinTutorial");

            BuildGround(root.transform, snow, edge);
            BuildIndoorHall(root.transform, indoorWall, indoorColumn, indoorWindow, edge, snow, green, orange);
            BuildCourse(root.transform, snow, edge, cyan, green, orange, barrierParticleMaterial,
                barrierMaterial, koreanFont, englishDisplayFont, roomSignKeyPlate,
                roomSignTitleText, roomSignKeyText, roomSignIconMaterials);
            Light[] stageLights = BuildLighting(root.transform, edge, orange);

            SnowCpuStage snowStage = BuildSnowStage(root.transform);
            GameObject penguin = PrefabUtility.InstantiatePrefab(penguinPrefab, scene) as GameObject;
            if (penguin == null) throw new InvalidOperationException("펭귄 프리팹 인스턴스 생성 실패");
            penguin.name = "TutorialPenguin";
            penguin.transform.SetPositionAndRotation(PlayerSpawn, Quaternion.Euler(0f, 70f, 0f));

            PenguinInputReader input = penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = penguin.GetComponent<PenguinLocomotion>();
            PenguinSnowball snowball = penguin.GetComponent<PenguinSnowball>();
            if (input == null || locomotion == null || snowball == null)
                throw new InvalidOperationException("PF_Penguin에 튜토리얼에 필요한 실제 게임 컴포넌트가 없다.");
            SetSerialized(locomotion, "_snowCpuStage", snowStage);
            SetSerialized(snowball, "_stage", snowStage);
            PenguinCameraOrbit cameraOrbit = penguin.GetComponentInChildren<PenguinCameraOrbit>(true);
            if (cameraOrbit != null)
            {
                SetSerialized(cameraOrbit, "_initialPitch", 22f);
                SetSerialized(cameraOrbit, "_distanceLow", 4f);
                SetSerialized(cameraOrbit, "_heightLow", 0.4f);
                Vector3 cameraRigPosition = cameraOrbit.transform.localPosition;
                cameraRigPosition.y = 1.95f;
                cameraOrbit.transform.localPosition = cameraRigPosition;
            }

            GameObject machineObject = PrefabUtility.InstantiatePrefab(snowGiftMachinePrefab, scene) as GameObject;
            if (machineObject == null) throw new InvalidOperationException("눈 선물 기계 프리팹 인스턴스 생성 실패");
            machineObject.name = "TutorialSnowGiftMachine";
            machineObject.transform.SetParent(root.transform, true);
            machineObject.transform.SetPositionAndRotation(
                SnowGiftMachinePosition, Quaternion.Euler(SnowGiftMachineEuler));
            SnowGiftMachinePresentation snowGiftMachine = machineObject.GetComponent<SnowGiftMachinePresentation>();
            if (snowGiftMachine == null)
                throw new InvalidOperationException("PF_SnowGiftMachine에 SnowGiftMachinePresentation이 없다.");
            snowGiftMachine.ConfigureSnowDeliveryConversion(snowStage);
            EditorUtility.SetDirty(snowGiftMachine);

            // Legacy object names are retained because the TutorialPlay and delivery-flow
            // builders locate these source objects by name before adapting the copied scene.
            GiftDeliveryTerminal machineDeliveryTerminal = InstantiateDeliveryTerminal(
                giftDeliveryTerminalPrefab,
                scene,
                root.transform.Find("TrainingCampus/Room_Gift"),
                "GiftDeliveryTerminal_SnowMachine",
                GiftRoomSenderPosition);
            Transform giftRoom = root.transform.Find("TrainingCampus/Room_Gift");
            GameObject warehouse = PrefabUtility.InstantiatePrefab(warehousePrefab, scene) as GameObject;
            if (warehouse == null) throw new InvalidOperationException("튜토리얼 창고 인스턴스 생성 실패");
            warehouse.name = "TutorialWarehouse";
            warehouse.transform.SetParent(giftRoom, true);
            warehouse.transform.SetPositionAndRotation(GiftRoomWarehousePosition, Quaternion.identity);
            SnowballWarehouseStorage warehouseStorage = warehouse.GetComponent<SnowballWarehouseStorage>();
            GiftDropZone houseDeliveryZone = giftRoom.GetComponentInChildren<GiftDropZone>(true);
            if (houseDeliveryZone == null)
                throw new InvalidOperationException("튜토리얼 옆 배송처 납품 구역 생성 실패");

            GiftDeliveryTerminal warehouseEndpoint = InstantiateDeliveryTerminal(
                giftDeliveryTerminalPrefab,
                scene,
                warehouse.transform,
                "GiftDeliveryTerminal_Tutorial",
                GiftRoomWarehousePosition + new Vector3(0f, 0.13f, 0.5f));
            HideDeliveryEndpoint(warehouseEndpoint);

            Transform marker = BuildMarker(root.transform, koreanFont, goalParticleMaterial,
                guideArrowMaterial, guideArrowTexture);
            marker.gameObject.SetActive(false);
            ParticleSystem snowballFailureVfx = BuildSnowballFailureVfx(root.transform, goalParticleMaterial);
            PenguinTutorialHud hud = BuildHud(root.transform, tutorialUxml, tutorialPanelSettings);
            TutorialComicCutscene openingCutscene =
                BuildComicCutscene(root.transform, "OpeningComicCutscene", 100, cutsceneUxml,
                    panelSettings, cutsceneCards, OpeningComicDialogueLines, cutsceneRevealSfx,
                    comicBubbleSfx, comicMagicSfx, comicWhooshSfx);
            TutorialComicCutscene deliveryStoryCutscene =
                BuildComicCutscene(root.transform, "DeliveryStoryComicCutscene", 110, cutsceneUxml,
                    panelSettings, deliveryStoryCards, DeliveryComicDialogueLines, cutsceneRevealSfx,
                    comicBubbleSfx, comicMagicSfx, comicWhooshSfx);
            BuildTutorialMusic(root.transform, tutorialMusic);

            var directorObject = new GameObject("TutorialDirector");
            directorObject.transform.SetParent(root.transform);
            PenguinTutorialDirector director = directorObject.AddComponent<PenguinTutorialDirector>();
            SetSerialized(director, "_player", penguin.transform);
            SetSerialized(director, "_input", input);
            SetSerialized(director, "_locomotion", locomotion);
            SetSerialized(director, "_snowballControl", snowball);
            SetSerialized(director, "_snowStage", snowStage);
            SetSerialized(director, "_snowGiftMachine", snowGiftMachine);
            SetSerialized(director, "_giftDeliveryTerminal", machineDeliveryTerminal);
            SetSerialized(director, "_warehouseStorage", warehouseStorage);
            SetSerialized(director, "_warehouseDeliveryTerminal", warehouseEndpoint);
            SetSerialized(director, "_houseDeliveryZone", houseDeliveryZone);
            SetSerialized(director, "_worldMarker", marker);
            SetSerialized(director, "_hud", hud);
            SetSerialized(director, "_openingCutscene", openingCutscene);
            SetSerialized(director, "_deliveryStoryCutscene", deliveryStoryCutscene);
            SetSerialized(director, "_cameraOrbit", cameraOrbit);
            SetSerialized(director, "_walkTarget", WalkTarget);
            SetSerialized(director, "_runTarget", RunTarget);
            SetSerialized(director, "_slideTarget", SlideTarget);
            SetSerialized(director, "_snowballSpawn", SnowballSpawn);
            SetSerialized(director, "_walkRoomCenter", WalkRoomCenter);
            SetSerialized(director, "_walkRoomSize", WalkRoomSize);
            SetSerialized(director, "_runRoomCenter", RunRoomCenter);
            SetSerialized(director, "_runRoomSize", RunRoomSize);
            SetSerialized(director, "_slideRoomCenter", SlideRoomCenter);
            SetSerialized(director, "_slideRoomSize", SlideRoomSize);
            SetSerialized(director, "_useWorldMarker", false);
            SetSerialized(director, "_useQuestCycle", true);
            SetSerialized(director, "_returnToMainMenuOnComplete", true);
            SetSerialized(director, "_snowballFailureVfx", snowballFailureVfx);
            SetObjectArray(director, "_stageLights", stageLights);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.28f, 0.34f, 0.43f);
            RenderSettings.fog = false;

            // UIDocument 활성화 초기화가 끝난 뒤 마지막으로 참조를 다시 기록한다.
            // 생성 직후 한 번만 설정하면 Unity 6 beta가 활성화 과정에서 null로 되돌릴 수 있다.
            ConfigureUiDocument(hud.GetComponent<UIDocument>(), tutorialPanelSettings, tutorialUxml, 0);
            ConfigureUiDocument(openingCutscene.GetComponent<UIDocument>(), panelSettings, cutsceneUxml, 100);
            ConfigureUiDocument(deliveryStoryCutscene.GetComponent<UIDocument>(), panelSettings, cutsceneUxml, 110);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"튜토리얼 씬 저장 실패: {ScenePath}");
            EnsureBuildSettingsScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Penguin tutorial scene built: {ScenePath}");

            if (originalScene.IsValid() && originalScene.isLoaded)
            {
                SceneManager.SetActiveScene(originalScene);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("PPack/Tutorial/Apply Comic Dialogue To Current Scene")]
        public static void ApplyComicDialogueToCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
                throw new InvalidOperationException($"튜토리얼 씬을 연 뒤 실행해야 한다: {ScenePath}");

            AudioClip bubbleSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(ComicBubbleSfxPath);
            AudioClip magicSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(ComicMagicSfxPath);
            AudioClip whooshSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(ComicWhooshSfxPath);
            if (bubbleSfx == null || magicSfx == null || whooshSfx == null)
                throw new InvalidOperationException("컷신 말풍선 효과음 중 하나 이상을 찾을 수 없다.");

            TutorialComicCutscene[] cutscenes = Array.Empty<TutorialComicCutscene>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                TutorialComicCutscene[] found = root.GetComponentsInChildren<TutorialComicCutscene>(true);
                if (found.Length > 0)
                {
                    var merged = new TutorialComicCutscene[cutscenes.Length + found.Length];
                    Array.Copy(cutscenes, merged, cutscenes.Length);
                    Array.Copy(found, 0, merged, cutscenes.Length, found.Length);
                    cutscenes = merged;
                }
            }

            TutorialComicCutscene opening = Array.Find(cutscenes, item => item.name == "OpeningComicCutscene");
            TutorialComicCutscene delivery = Array.Find(cutscenes, item => item.name == "DeliveryStoryComicCutscene");
            if (opening == null || delivery == null)
                throw new InvalidOperationException("현재 씬에서 두 TutorialComicCutscene을 모두 찾지 못했다.");

            ConfigureExistingComicDialogue(opening, OpeningComicDialogueLines, bubbleSfx, magicSfx, whooshSfx);
            ConfigureExistingComicDialogue(delivery, DeliveryComicDialogueLines, bubbleSfx, magicSfx, whooshSfx);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"튜토리얼 씬 저장 실패: {ScenePath}");
            AssetDatabase.SaveAssets();
            Debug.Log("Tutorial comic dialogue and FEEL speech bubbles applied to the current scene.");
        }

        private static void ConfigureExistingComicDialogue(TutorialComicCutscene cutscene,
            TutorialComicCutscene.ComicDialogueLine[] dialogueLines, AudioClip bubbleSfx,
            AudioClip magicSfx, AudioClip whooshSfx)
        {
            cutscene.ConfigureDialogue(dialogueLines);
            SetSerialized(cutscene, "_showStartPrompt", false);
            SetSerialized(cutscene, "_bubbleRevealSfx", bubbleSfx);
            SetSerialized(cutscene, "_magicRevealSfx", magicSfx);
            SetSerialized(cutscene, "_whooshRevealSfx", whooshSfx);
            SetSerialized(cutscene, "_bubbleRevealSfxVolume", 0.34f);

            Transform parent = cutscene.transform;
            MMF_Player leftBubbleReveal = ReplaceComicBubbleRevealFeedback(parent, false);
            MMF_Player rightBubbleReveal = ReplaceComicBubbleRevealFeedback(parent, true);
            SetSerialized(cutscene, "_leftBubbleRevealFeedbacks", leftBubbleReveal);
            SetSerialized(cutscene, "_rightBubbleRevealFeedbacks", rightBubbleReveal);
            EditorUtility.SetDirty(cutscene);
        }

        private static MMF_Player ReplaceComicBubbleRevealFeedback(Transform parent, bool isRight)
        {
            string objectName = isRight ? "Feel_RightBubbleReveal" : "Feel_LeftBubbleReveal";
            Transform existing = parent.Find(objectName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            return BuildComicBubbleRevealFeedback(parent, isRight);
        }

        private static GiftDeliveryTerminal InstantiateDeliveryTerminal(
            GameObject prefab,
            Scene scene,
            Transform parent,
            string name,
            Vector3 position)
        {
            if (parent == null) throw new InvalidOperationException($"배송 단말기 부모가 없다: {name}");
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null) throw new InvalidOperationException($"배송 단말기 생성 실패: {name}");
            instance.name = name;
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(DeliveryTerminalEuler));
            return instance.GetComponent<GiftDeliveryTerminal>();
        }

        private static void HideDeliveryEndpoint(GiftDeliveryTerminal endpoint)
        {
            foreach (Renderer renderer in endpoint.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Collider collider in endpoint.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (AudioSource source in endpoint.GetComponentsInChildren<AudioSource>(true)) source.mute = true;
        }

        private static void BuildGround(Transform parent, Material snow, Material edge)
        {
            CreateCube(parent, "Foundation", new Vector3(0f, -0.48f, 0f), new Vector3(50f, 0.7f, 38f), edge, true);
            CreateCube(parent, "SnowGround", new Vector3(0f, -0.08f, 0f), new Vector3(48f, 0.16f, 36f), snow, true);

            BuildWallZ(parent, "OuterWall_West", -23.5f, 0f, 35f, edge, snow);
            BuildWallZ(parent, "OuterWall_East", 23.5f, 0f, 35f, edge, snow);
            BuildWallX(parent, "OuterWall_North", 0f, 17.5f, 47f, edge, snow);
            BuildWallX(parent, "OuterWall_South_Left", -23.5f, -17.5f, 1f, edge, snow);
            BuildWallX(parent, "OuterWall_South_Right", 3.5f, -17.5f, 41f, edge, snow);
        }

        private static void BuildIndoorHall(Transform parent, Material wall, Material column,
            Material window, Material edge, Material snow, Material green, Material orange)
        {
            var hall = new GameObject("IndoorHall");
            hall.transform.SetParent(parent);

            Transform shell = Child(hall.transform, "WorkshopWalls");
            CreateCube(shell, "Wall_West", new Vector3(-24.25f, 4.35f, 0f),
                new Vector3(0.75f, 8.7f, 38f), wall, true);
            CreateCube(shell, "Wall_East", new Vector3(24.25f, 4.35f, 0f),
                new Vector3(0.75f, 8.7f, 38f), wall, true);
            CreateCube(shell, "Wall_North", new Vector3(0f, 4.35f, 18.25f),
                new Vector3(49f, 8.7f, 0.75f), wall, true);
            CreateCube(shell, "Wall_South_Left", new Vector3(-23.25f, 4.35f, -18.25f),
                new Vector3(1.5f, 8.7f, 0.75f), wall, true);
            CreateCube(shell, "Wall_South_Right", new Vector3(3.25f, 4.35f, -18.25f),
                new Vector3(41.5f, 8.7f, 0.75f), wall, true);
            CreateCube(shell, "EntranceHeader", new Vector3(-20f, 7.45f, -18.25f),
                new Vector3(7f, 2.5f, 0.75f), wall, true);

            Transform columns = Child(hall.transform, "WorkshopColumns");
            float[] columnZ = { -15f, -7.5f, 0f, 7.5f, 15f };
            foreach (float z in columnZ)
            {
                CreateCube(columns, $"Column_W_{z:0.0}", new Vector3(-23.55f, 4.25f, z),
                    new Vector3(0.9f, 8.5f, 0.9f), column, false);
                CreateCube(columns, $"Column_E_{z:0.0}", new Vector3(23.55f, 4.25f, z),
                    new Vector3(0.9f, 8.5f, 0.9f), column, false);
            }
            float[] columnX = { -16f, -8f, 0f, 8f, 16f };
            foreach (float x in columnX)
            {
                CreateCube(columns, $"Column_N_{x:0.0}", new Vector3(x, 4.25f, 17.55f),
                    new Vector3(0.9f, 8.5f, 0.9f), column, false);
                if (Mathf.Abs(x + 20f) > 4f)
                    CreateCube(columns, $"Column_S_{x:0.0}", new Vector3(x, 4.25f, -17.55f),
                        new Vector3(0.9f, 8.5f, 0.9f), column, false);
            }

            Transform windows = Child(hall.transform, "WorkshopWindows");
            BuildWindowX(windows, "Window_North_Left", new Vector3(-14f, 6.15f, 17.82f), window, edge);
            BuildWindowX(windows, "Window_North_Center", new Vector3(0f, 6.15f, 17.82f), window, edge);
            BuildWindowX(windows, "Window_North_Right", new Vector3(14f, 6.15f, 17.82f), window, edge);
            BuildWindowZ(windows, "Window_West_South", new Vector3(-23.82f, 6.15f, -9f), window, edge);
            BuildWindowZ(windows, "Window_West_North", new Vector3(-23.82f, 6.15f, 8f), window, edge);
            BuildWindowZ(windows, "Window_East_South", new Vector3(23.82f, 6.15f, -9f), window, edge);
            BuildWindowZ(windows, "Window_East_North", new Vector3(23.82f, 6.15f, 8f), window, edge);

            Transform ceiling = Child(hall.transform, "WorkshopCeiling");
            CreateCube(ceiling, "CeilingSlab", new Vector3(0f, 9.35f, 0f),
                new Vector3(49f, 0.45f, 38f), edge, false);
            for (int index = 0; index < 5; index++)
            {
                float z = -15f + index * 7.5f;
                CreateCube(ceiling, $"RoofTruss_{index:00}", new Vector3(0f, 8.55f, z),
                    new Vector3(48f, 0.34f, 0.42f), edge, false);
                CreateCube(ceiling, $"RoofTrussAccent_{index:00}_L", new Vector3(-12f, 8.10f, z),
                    new Vector3(23f, 0.18f, 0.30f), column, false).transform.rotation =
                    Quaternion.Euler(0f, 0f, -3.2f);
                CreateCube(ceiling, $"RoofTrussAccent_{index:00}_R", new Vector3(12f, 8.10f, z),
                    new Vector3(23f, 0.18f, 0.30f), column, false).transform.rotation =
                    Quaternion.Euler(0f, 0f, 3.2f);
            }

            Transform props = Child(hall.transform, "WorkshopProps");
            BuildWallPipe(props, "Pipe_West", new Vector3(-23.4f, 3.1f, 0f), 20f, column);
            BuildCrateStack(props, "Crates_NorthWest", new Vector3(-21.0f, 0f, 15.5f), green, orange, edge);
            BuildCrateStack(props, "Crates_SouthEast", new Vector3(21.0f, 0f, -15.5f), orange, green, edge);
            CreateCube(props, "EntranceSnowMat", new Vector3(-20f, 0.04f, -15.7f),
                new Vector3(6.5f, 0.06f, 3.0f), snow, false);
        }

        private static void BuildCourse(Transform parent, Material snow, Material edge, Material cyan,
            Material green, Material orange, Material goalParticleMaterial, Material barrierMaterial,
            Font koreanFont, Font englishDisplayFont, Material roomSignKeyPlate,
            Material roomSignTitleText, Material roomSignKeyText, Material[] roomSignIconMaterials)
        {
            var course = new GameObject("TrainingCampus");
            course.transform.SetParent(parent);

            BuildRoomDividers(course.transform, edge, snow);
            BuildWalkRoom(course.transform, cyan, snow, edge);
            BuildRunRoom(course.transform, green);
            BuildSlideRoom(course.transform, orange);
            BuildSnowballRoom(course.transform, snow, cyan);
            BuildSnowMachineRoom(course.transform, cyan, orange);
            BuildGiftRoom(course.transform, green, orange, edge, koreanFont);

            BuildGateX(course.transform, "Gate_WalkToRun", new Vector3(-8f, 0f, -11f), 6f,
                green, snow, edge, goalParticleMaterial, barrierMaterial, 0);
            BuildGateX(course.transform, "Gate_RunToSlide", new Vector3(8f, 0f, -11f), 6f,
                orange, snow, edge, goalParticleMaterial, barrierMaterial, 1);
            BuildGateZ(course.transform, "Gate_SlideToSnowball", new Vector3(16f, 0f, 4f), 6f,
                cyan, snow, edge, goalParticleMaterial, barrierMaterial, 2);
            BuildGateX(course.transform, "Gate_SnowballToMachine", new Vector3(8f, 0f, 11f), 6f,
                green, snow, edge, goalParticleMaterial, barrierMaterial, 3);
            BuildGateX(course.transform, "Gate_MachineToGift", new Vector3(-8f, 0f, 11f), 6f,
                green, snow, edge, goalParticleMaterial, barrierMaterial, 4);
            BuildRoomIdentitySigns(course.transform, englishDisplayFont, edge, roomSignKeyPlate,
                roomSignTitleText, roomSignKeyText, cyan, green, orange,
                roomSignIconMaterials);
        }

        private static void BuildRoomDividers(Transform parent, Material edge, Material snow)
        {
            var walk = new GameObject("Room_Walk");
            walk.transform.SetParent(parent);
            BuildWallZ(walk.transform, "Divider_East_Lower", -8f, -16f, 4f, edge, snow);
            BuildWallZ(walk.transform, "Divider_East_Upper", -8f, -6f, 4f, edge, snow);
            BuildWallX(walk.transform, "Divider_North", -16f, -4f, 16f, edge, snow);

            var run = new GameObject("Room_Run");
            run.transform.SetParent(parent);
            BuildWallX(run.transform, "Divider_North", 0f, -4f, 16f, edge, snow);

            var slide = new GameObject("Room_Slide");
            slide.transform.SetParent(parent);
            BuildWallZ(slide.transform, "Divider_West_Lower", 8f, -16f, 4f, edge, snow);
            BuildWallZ(slide.transform, "Divider_West_Upper", 8f, -2f, 12f, edge, snow);
            BuildWallX(slide.transform, "Divider_North_Left", 10.5f, 4f, 5f, edge, snow);
            BuildWallX(slide.transform, "Divider_North_Right", 21.5f, 4f, 5f, edge, snow);

            var snowball = new GameObject("Room_Snowball");
            snowball.transform.SetParent(parent);
            BuildWallZ(snowball.transform, "Divider_West_Lower", 8f, 6f, 4f, edge, snow);
            BuildWallZ(snowball.transform, "Divider_West_Upper", 8f, 16f, 4f, edge, snow);

            var gift = new GameObject("Room_Gift");
            gift.transform.SetParent(parent);
            BuildWallX(gift.transform, "Divider_South", -16f, 4f, 16f, edge, snow);
            BuildWallZ(gift.transform, "Divider_East_Lower", -8f, 6f, 4f, edge, snow);
            BuildWallZ(gift.transform, "Divider_East_Upper", -8f, 16f, 4f, edge, snow);

            var machine = new GameObject("Room_SnowMachine");
            machine.transform.SetParent(parent);
        }

        private static void BuildWalkRoom(Transform parent, Material cyan, Material snow, Material edge)
        {
            Transform room = parent.Find("Room_Walk");
            CreateCylinder(room, "WalkStartOuter", new Vector3(PlayerSpawn.x, 0.045f, PlayerSpawn.z),
                new Vector3(1.15f, 0.035f, 1.15f), cyan);
            CreateCylinder(room, "WalkStartInner", new Vector3(PlayerSpawn.x, 0.075f, PlayerSpawn.z),
                new Vector3(0.82f, 0.025f, 0.82f), snow);
            CreateCylinder(room, "WalkFinishOuter", new Vector3(WalkTarget.x, 0.045f, WalkTarget.z),
                new Vector3(1.25f, 0.035f, 1.25f), cyan);
            CreateCylinder(room, "WalkFinishInner", new Vector3(WalkTarget.x, 0.075f, WalkTarget.z),
                new Vector3(0.88f, 0.025f, 0.88f), snow);
            BuildGateZ(room, "Gate_WalkStart", new Vector3(-20f, 0f, -17.5f), 4.5f, cyan, snow, edge);
        }

        private static void BuildRunRoom(Transform parent, Material green)
        {
            // The room color and gate communicate the stage. Floor lanes were removed because
            // low camera angles turned them into noisy, unreadable markings.
        }

        private static void BuildSlideRoom(Transform parent, Material orange)
        {
            // Deliberately open: the slide is taught by movement feedback, not floor arrows.
        }

        private static void BuildSnowballRoom(Transform parent, Material snow, Material cyan)
        {
            Transform room = parent.Find("Room_Snowball");
            CreateCube(room, "SnowGatherPatchOuter", new Vector3(15f, 0.045f, 9f),
                new Vector3(4.0f, 0.04f, 4.0f), cyan, false);
            CreateCube(room, "SnowGatherPatchInner", new Vector3(15f, 0.075f, 9f),
                new Vector3(3.2f, 0.045f, 3.2f), snow, false);
            CreateSnowMound(room, "SnowPile_SouthEast", new Vector3(21f, 0.10f, 6.5f), snow);
            CreateSnowMound(room, "SnowPile_NorthEast", new Vector3(21f, 0.10f, 15.0f), snow);
            CreateSnowMound(room, "SnowPile_NorthWest", new Vector3(11f, 0.10f, 15.2f), snow);
        }

        private static void BuildGiftRoom(Transform parent, Material green, Material orange,
            Material edge, Font font)
        {
            Transform room = parent.Find("Room_Gift");
            CreateCube(room, "GiftPad_Shadow", new Vector3(-16f, 0.035f, 11f),
                new Vector3(15.0f, 0.06f, 12.0f), edge, false);
            CreateCube(room, "GiftPad_Send", GiftRoomSenderPosition + Vector3.down * 0.05f,
                new Vector3(3.8f, 0.07f, 4.2f), orange, false);
            CreateCube(room, "GiftPad_Arrival", GiftRoomWarehousePosition + Vector3.down * 0.02f,
                new Vector3(6.0f, 0.07f, 5.8f), green, false);

            Transform deliveryZoneRoot = Child(room, "NeighborDeliveryZone");
            deliveryZoneRoot.position = NeighborDeliveryPosition;
            CreateCube(deliveryZoneRoot, "NeighborDeliveryPad_Shadow", NeighborDeliveryPosition + Vector3.down * 0.01f,
                new Vector3(3.8f, 0.07f, 3.4f), edge, false);
            CreateCube(deliveryZoneRoot, "NeighborDeliveryPad", NeighborDeliveryPosition + Vector3.up * 0.035f,
                new Vector3(3.25f, 0.08f, 2.85f), green, false);
            GiftDropZone deliveryZone = deliveryZoneRoot.gameObject.AddComponent<GiftDropZone>();
            deliveryZone.Configure(new Vector3(3.4f, 2.2f, 3.0f), 1);

            Transform neighborBay = Child(room, "NeighborParcelBay");
            CreateCube(neighborBay, "BayWall", new Vector3(-12.3f, 1.55f, 17.05f),
                new Vector3(5.6f, 3.1f, 0.25f), edge, false);
            CreateCube(neighborBay, "BayOpening", new Vector3(-12.3f, 1.45f, 16.88f),
                new Vector3(3.8f, 2.15f, 0.12f), orange, false);
            TextMesh bayLabel = CreateWorldText(neighborBay, "BayLabel", font,
                "NEIGHBOR DELIVERY", Color.white, 0.034f);
            bayLabel.transform.position = new Vector3(-12.3f, 3.15f, 16.84f);
        }

        private static void BuildRoomIdentitySigns(Transform parent, Font font, Material edge,
            Material keyPlate, Material titleTextMaterial, Material keyTextMaterial,
            Material cyan, Material green, Material orange, Material[] iconMaterials)
        {
            if (iconMaterials == null || iconMaterials.Length != 6 ||
                Array.Exists(iconMaterials, material => material == null))
                throw new InvalidOperationException("튜토리얼 구간 표지판 아이콘 머티리얼 6종이 필요하다.");

            Transform signs = Child(parent, "RoomIdentitySigns");
            BuildRoomSignX(signs, "Sign_01_Walk", new Vector3(-16f, 2.25f, -4.32f),
                "01  WALK", "WASD", font, edge, keyPlate, cyan, iconMaterials[0],
                titleTextMaterial, keyTextMaterial);
            BuildRoomSignX(signs, "Sign_02_Run", new Vector3(0f, 2.25f, -4.32f),
                "02  RUN", "SHIFT + WASD", font, edge, keyPlate, green, iconMaterials[1],
                titleTextMaterial, keyTextMaterial);
            BuildRoomSignZ(signs, "Sign_03_Slide", new Vector3(23.32f, 2.25f, -4f),
                "03  SLIDE", "SHIFT + SPACE", font, edge, keyPlate, orange, iconMaterials[2],
                titleTextMaterial, keyTextMaterial);
            // The north structural columns sit at x=0 and x=16. Pull these signs forward from
            // the wall and offset the machine sign so neither the columns nor the machine hide them.
            BuildRoomSignX(signs, "Sign_04_Snowball", new Vector3(19.25f, 2.25f, 16.45f),
                "04  SNOWBALL", "E  >  W / A D", font, edge, keyPlate, cyan, iconMaterials[3],
                titleTextMaterial, keyTextMaterial);
            BuildRoomSignX(signs, "Sign_05_Machine", new Vector3(4.25f, 2.25f, 16.45f),
                "05  MAKE GIFT", "E  >  W", font, edge, keyPlate, orange, iconMaterials[4],
                titleTextMaterial, keyTextMaterial, true);
            BuildRoomSignX(signs, "Sign_06_Delivery", new Vector3(-17f, 2.25f, 17.32f),
                "06  SEND GIFT", "W / A D", font, edge, keyPlate, green, iconMaterials[5],
                titleTextMaterial, keyTextMaterial);
        }

        private static void BuildRoomSignX(Transform parent, string name, Vector3 position,
            string label, string keyLabel, Font font, Material edge, Material keyPlate,
            Material accent, Material iconMaterial, Material titleTextMaterial, Material keyTextMaterial,
            bool mirrorLayout = false)
        {
            Transform sign = Child(parent, name);
            CreateCube(sign, "Backing", position, new Vector3(7.2f, 1.65f, 0.18f), edge, false);
            CreateCube(sign, "AccentTop", position + Vector3.up * 0.77f + Vector3.back * 0.10f,
                new Vector3(6.8f, 0.11f, 0.09f), accent, false);
            float layoutDirection = mirrorLayout ? -1f : 1f;
            CreateRoomSignIcon(sign, position + new Vector3(-2.75f * layoutDirection, 0f, -0.105f),
                Quaternion.identity, iconMaterial);
            TextMesh text = CreateWorldText(sign, "TitleLabel", font, label, Color.white, 0.047f);
            text.transform.position = position + new Vector3(-0.55f * layoutDirection, 0.27f, -0.105f);
            ConfigureRoomSignText(text, titleTextMaterial);
            CreateCube(sign, "KeyBadge", position + new Vector3(1.55f * layoutDirection, -0.30f, -0.12f),
                new Vector3(3.05f, 0.50f, 0.08f), keyPlate, false);
            TextMesh key = CreateWorldText(sign, "KeyLabel", font, keyLabel,
                new Color(0.035f, 0.09f, 0.16f), 0.032f);
            key.transform.position = position + new Vector3(1.55f * layoutDirection, -0.30f, -0.17f);
            ConfigureRoomSignText(key, keyTextMaterial);
        }

        private static void BuildRoomSignZ(Transform parent, string name, Vector3 position,
            string label, string keyLabel, Font font, Material edge, Material keyPlate,
            Material accent, Material iconMaterial, Material titleTextMaterial, Material keyTextMaterial)
        {
            Transform sign = Child(parent, name);
            CreateCube(sign, "Backing", position, new Vector3(0.18f, 1.65f, 7.2f), edge, false);
            CreateCube(sign, "AccentTop", position + Vector3.up * 0.77f + Vector3.left * 0.10f,
                new Vector3(0.09f, 0.11f, 6.8f), accent, false);
            CreateRoomSignIcon(sign, position + new Vector3(-0.105f, 0f, 2.75f),
                Quaternion.Euler(0f, 90f, 0f), iconMaterial);
            TextMesh text = CreateWorldText(sign, "TitleLabel", font, label, Color.white, 0.047f);
            text.transform.position = position + new Vector3(-0.105f, 0.27f, 0.55f);
            text.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            ConfigureRoomSignText(text, titleTextMaterial);
            CreateCube(sign, "KeyBadge", position + new Vector3(-0.12f, -0.30f, -1.55f),
                new Vector3(0.08f, 0.50f, 3.05f), keyPlate, false);
            TextMesh key = CreateWorldText(sign, "KeyLabel", font, keyLabel,
                new Color(0.035f, 0.09f, 0.16f), 0.032f);
            key.transform.position = position + new Vector3(-0.17f, -0.30f, -1.55f);
            key.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            ConfigureRoomSignText(key, keyTextMaterial);
        }

        private static void ConfigureRoomSignText(TextMesh text, Material depthTestedMaterial)
        {
            MeshRenderer renderer = text.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = depthTestedMaterial;
            renderer.sortingOrder = 0;
        }

        private static void CreateRoomSignIcon(Transform parent, Vector3 position, Quaternion rotation,
            Material material)
        {
            GameObject icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
            icon.name = "PenguinActionIcon";
            icon.transform.SetParent(parent);
            icon.transform.SetPositionAndRotation(position, rotation);
            icon.transform.localScale = Vector3.one * 1.30f;
            MeshRenderer renderer = icon.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 40;
            UnityEngine.Object.DestroyImmediate(icon.GetComponent<Collider>());
        }

        private static void CreateRouteStrip(Transform parent, string name, Vector3 start, Vector3 end,
            float width, Material material)
        {
            Vector3 direction = end - start;
            direction.y = 0f;
            float length = direction.magnitude;
            if (length < 0.01f) return;
            Vector3 center = (start + end) * 0.5f;
            center.y = 0.055f;
            GameObject strip = CreateCube(parent, name, center,
                new Vector3(width, 0.06f, length), material, false);
            strip.transform.rotation = Quaternion.LookRotation(direction.normalized);
        }

        private static void CreateFloorChevron(Transform parent, string name, Vector3 position,
            Vector3 direction, Material material)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;
            direction.Normalize();
            Vector3 right = new Vector3(direction.z, 0f, -direction.x);
            Vector3 tip = position + direction * 0.48f;
            Vector3 tail = position - direction * 0.38f;
            CreateRouteStrip(parent, name + "_L", tail + right * 0.42f, tip, 0.16f, material);
            CreateRouteStrip(parent, name + "_R", tail - right * 0.42f, tip, 0.16f, material);
        }

        private static void CreateSnowMound(Transform parent, string name, Vector3 position,
            Material snow)
        {
            Transform mound = Child(parent, name);
            CreateSphere(mound, "Large", position, new Vector3(1.35f, 0.42f, 1.05f), snow);
            CreateSphere(mound, "Small_Left", position + new Vector3(-0.72f, 0.02f, 0.18f),
                new Vector3(0.72f, 0.30f, 0.70f), snow);
            CreateSphere(mound, "Small_Right", position + new Vector3(0.66f, 0.01f, -0.10f),
                new Vector3(0.78f, 0.32f, 0.66f), snow);
        }

        private static void BuildFloorLabel(Transform parent, string name, Vector3 position,
            string label, Font font, Color color)
        {
            TextMesh text = CreateWorldText(parent, name, font, label, color, 0.058f);
            text.transform.position = position;
            text.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static void BuildSnowMachineRoom(Transform parent, Material cyan, Material orange)
        {
            Transform room = parent.Find("Room_SnowMachine");
            CreateCube(room, "MachinePad_Shadow", new Vector3(2f, 0.035f, 10.5f),
                new Vector3(10.8f, 0.06f, 9.6f), orange, false);
            CreateCube(room, "MachinePad", new Vector3(2f, 0.08f, 10.5f),
                new Vector3(10.0f, 0.07f, 8.8f), cyan, false);
        }

        private static void BuildGateZ(Transform parent, string name, Vector3 center, float width,
            Material material, Material snow, Material edge, Material particleMaterial = null,
            Material barrierMaterial = null, int unlockAfterStep = -1)
        {
            var gate = new GameObject(name);
            gate.transform.SetParent(parent);
            CreateCheckpointPost(gate.transform, "Post_L", center + Vector3.left * width * 0.5f,
                Vector3.right, material, snow, edge);
            CreateCheckpointPost(gate.transform, "Post_R", center + Vector3.right * width * 0.5f,
                Vector3.left, material, snow, edge);
            BuildGateTrail(gate.transform, center, Vector3.right, width, material);
            if (unlockAfterStep >= 0)
                BuildStageBarrier(gate.transform, center, Vector3.right, width, particleMaterial,
                    barrierMaterial, unlockAfterStep);
        }

        private static void BuildGateX(Transform parent, string name, Vector3 center, float width,
            Material material, Material snow, Material edge, Material particleMaterial = null,
            Material barrierMaterial = null, int unlockAfterStep = -1)
        {
            var gate = new GameObject(name);
            gate.transform.SetParent(parent);
            CreateCheckpointPost(gate.transform, "Post_L", center + Vector3.back * width * 0.5f,
                Vector3.forward, material, snow, edge);
            CreateCheckpointPost(gate.transform, "Post_R", center + Vector3.forward * width * 0.5f,
                Vector3.back, material, snow, edge);
            BuildGateTrail(gate.transform, center, Vector3.forward, width, material);
            if (unlockAfterStep >= 0)
                BuildStageBarrier(gate.transform, center, Vector3.forward, width, particleMaterial,
                    barrierMaterial, unlockAfterStep);
        }

        private static void CreateCheckpointPost(Transform parent, string name, Vector3 position,
            Vector3 inward, Material material, Material snow, Material edge)
        {
            var post = new GameObject(name);
            post.transform.SetParent(parent);

            CreateCylinder(post.transform, "DarkBase", position + Vector3.up * 0.12f,
                new Vector3(0.62f, 0.12f, 0.62f), edge);
            CreateCube(post.transform, "ColorPost", position + Vector3.up * 0.82f,
                new Vector3(0.44f, 1.35f, 0.44f), material, false);
            CreateCylinder(post.transform, "SnowCap", position + Vector3.up * 1.55f,
                new Vector3(0.47f, 0.10f, 0.47f), snow);

            // 방향은 좌측 상단 퀘스트 카드가 안내한다. 체크포인트는 색상 기둥만 남겨
            // 공간의 경계로 읽히게 하고, 큰 화살표 표지판은 만들지 않는다.
        }

        private static void BuildGateTrail(Transform parent, Vector3 center, Vector3 axis,
            float width, Material material)
        {
            for (int index = -2; index <= 2; index++)
            {
                Vector3 position = center + axis * index * width / 6f + Vector3.up * 0.055f;
                Vector3 scale = Mathf.Abs(axis.x) > 0.5f
                    ? new Vector3(0.55f, 0.06f, 0.20f)
                    : new Vector3(0.20f, 0.06f, 0.55f);
                CreateCube(parent, $"Trail_{index + 2:00}", position, scale, material, false);
            }
        }

        private static void BuildStageBarrier(Transform parent, Vector3 center, Vector3 spanAxis,
            float width, Material particleMaterial, Material barrierMaterial, int unlockAfterStep)
        {
            var barrierObject = new GameObject("QuestBarrier");
            barrierObject.transform.SetParent(parent);
            barrierObject.transform.position = center + Vector3.up * 1.15f;

            bool spansAlongX = Mathf.Abs(spanAxis.x) > 0.5f;
            var barrier = barrierObject.AddComponent<BoxCollider>();
            barrier.size = spansAlongX
                ? new Vector3(width - 0.2f, 2.3f, 0.55f)
                : new Vector3(0.55f, 2.3f, width - 0.2f);

            Transform fieldRoot = Child(barrierObject.transform, "EnergyFieldVisuals");
            fieldRoot.SetPositionAndRotation(barrierObject.transform.position, Quaternion.identity);
            Vector3 fieldScale = spansAlongX
                ? new Vector3(width - 0.28f, 2.16f, 0.075f)
                : new Vector3(0.075f, 2.16f, width - 0.28f);
            GameObject field = CreateCube(fieldRoot, "EnergyField", barrierObject.transform.position,
                fieldScale, barrierMaterial, false);

            const float borderThickness = 0.105f;
            Vector3 topScale = spansAlongX
                ? new Vector3(width - 0.12f, borderThickness, 0.14f)
                : new Vector3(0.14f, borderThickness, width - 0.12f);
            Vector3 sideScale = spansAlongX
                ? new Vector3(borderThickness, 2.22f, 0.14f)
                : new Vector3(0.14f, 2.22f, borderThickness);
            GameObject topBeam = CreateCube(fieldRoot, "EnergyEdge_Top",
                barrierObject.transform.position + Vector3.up * 1.07f, topScale, barrierMaterial, false);
            GameObject bottomBeam = CreateCube(fieldRoot, "EnergyEdge_Bottom",
                barrierObject.transform.position + Vector3.down * 1.07f, topScale, barrierMaterial, false);
            GameObject leftBeam = CreateCube(fieldRoot, "EnergyEdge_Left",
                barrierObject.transform.position - spanAxis * (width * 0.5f - 0.08f),
                sideScale, barrierMaterial, false);
            GameObject rightBeam = CreateCube(fieldRoot, "EnergyEdge_Right",
                barrierObject.transform.position + spanAxis * (width * 0.5f - 0.08f),
                sideScale, barrierMaterial, false);
            Renderer[] fieldRenderers =
            {
                field.GetComponent<Renderer>(), topBeam.GetComponent<Renderer>(),
                bottomBeam.GetComponent<Renderer>(), leftBeam.GetComponent<Renderer>(),
                rightBeam.GetComponent<Renderer>()
            };

            // 공장 레이저 장벽 대신 산타 마을의 임시 통제선처럼 읽히도록
            // 빨강/크림색 지팡이사탕 봉을 남긴다. 얼음막이 녹아도 봉은 다음 구역의 문틀이 된다.
            Material candyRed = GetOrCreateMaterial(CandyRedMaterialPath, "M_TutorialCandyRed",
                new Color(0.72f, 0.075f, 0.12f), 0.30f, true);
            Material candyCream = GetOrCreateMaterial(CandyCreamMaterialPath, "M_TutorialCandyCream",
                new Color(0.94f, 0.97f, 0.91f), 0.22f);
            Transform sealPosts = Child(barrierObject.transform, "CandyCaneSealPosts");
            const int stripeCount = 8;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 edgePosition = barrierObject.transform.position
                    + spanAxis * side * (width * 0.5f - 0.04f);
                for (int stripe = 0; stripe < stripeCount; stripe++)
                {
                    float y = -0.96f + stripe * 0.275f;
                    CreateCube(sealPosts, $"Seal_{side}_{stripe:00}", edgePosition + Vector3.up * y,
                        new Vector3(0.27f, 0.29f, 0.27f),
                        stripe % 2 == 0 ? candyRed : candyCream, false);
                }
                CreateSphere(sealPosts, $"SnowCap_{side}", edgePosition + Vector3.up * 1.25f,
                    new Vector3(0.40f, 0.22f, 0.40f), candyCream);
            }

            ParticleSystem particles = CreateParticleSystem(barrierObject.transform, "LockedSnowVFX", particleMaterial);
            particles.transform.localPosition = Vector3.zero;
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 2.8f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.015f, 0.075f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.17f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.62f, 0.88f, 1f, 0.18f), new Color(0.96f, 0.99f, 1f, 0.42f));
            main.maxParticles = 32;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 7f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = spansAlongX
                ? new Vector3(width - 0.45f, 1.85f, 0.12f)
                : new Vector3(0.12f, 1.85f, width - 0.45f);
            AddParticleFade(particles);
            AddParticleRotation(particles, 0.55f);

            ParticleSystem unlockParticles = CreateParticleSystem(
                barrierObject.transform, "BarrierUnlockBurstVFX", particleMaterial);
            ParticleSystem.MainModule unlockMain = unlockParticles.main;
            unlockMain.loop = false;
            unlockMain.playOnAwake = false;
            unlockMain.duration = 0.45f;
            unlockMain.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            unlockMain.startSpeed = new ParticleSystem.MinMaxCurve(0.65f, 1.55f);
            unlockMain.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.20f);
            unlockMain.maxParticles = 24;
            ParticleSystem.EmissionModule unlockEmission = unlockParticles.emission;
            unlockEmission.rateOverTime = 0f;
            unlockEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
            ParticleSystem.ShapeModule unlockShape = unlockParticles.shape;
            unlockShape.shapeType = ParticleSystemShapeType.Box;
            unlockShape.scale = spansAlongX
                ? new Vector3(width - 0.40f, 1.82f, 0.16f)
                : new Vector3(0.16f, 1.82f, width - 0.40f);
            AddParticleFade(unlockParticles);
            AddParticleRotation(unlockParticles, 1.35f);
            unlockParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            TutorialStageGate stageGate = barrierObject.AddComponent<TutorialStageGate>();
            SetSerialized(stageGate, "_unlockAfterStep", unlockAfterStep);
            SetSerialized(stageGate, "_barrier", barrier);
            SetSerialized(stageGate, "_fieldPulseRoot", fieldRoot);
            SetObjectArray(stageGate, "_fieldRenderers", fieldRenderers);
            SetSerialized(stageGate, "_lockParticles", particles);
            SetSerialized(stageGate, "_unlockParticles", unlockParticles);
        }

        private static void BuildWallX(Transform parent, string name, float x, float z, float length,
            Material material, Material snowCap = null)
        {
            CreateCube(parent, name, new Vector3(x, 0.55f, z), new Vector3(length, 1.1f, 0.42f), material, true);
            if (snowCap != null)
                CreateCube(parent, name + "_SnowCap", new Vector3(x, 1.15f, z),
                    new Vector3(length + 0.18f, 0.16f, 0.66f), snowCap, false);
        }

        private static void BuildWallZ(Transform parent, string name, float x, float z, float length,
            Material material, Material snowCap = null)
        {
            CreateCube(parent, name, new Vector3(x, 0.55f, z), new Vector3(0.42f, 1.1f, length), material, true);
            if (snowCap != null)
                CreateCube(parent, name + "_SnowCap", new Vector3(x, 1.15f, z),
                    new Vector3(0.66f, 0.16f, length + 0.18f), snowCap, false);
        }

        private static void BuildWindowX(Transform parent, string name, Vector3 position,
            Material window, Material frame)
        {
            Transform root = Child(parent, name);
            CreateCube(root, "Glass", position, new Vector3(8.0f, 1.55f, 0.08f), window, false);
            CreateCube(root, "Frame_Top", position + Vector3.up * 0.86f,
                new Vector3(8.5f, 0.18f, 0.18f), frame, false);
            CreateCube(root, "Frame_Bottom", position + Vector3.down * 0.86f,
                new Vector3(8.5f, 0.18f, 0.18f), frame, false);
            for (int index = -2; index <= 2; index++)
                CreateCube(root, $"Mullion_{index + 2:00}", position + Vector3.right * index * 1.9f,
                    new Vector3(0.14f, 1.8f, 0.18f), frame, false);
        }

        private static void BuildWindowZ(Transform parent, string name, Vector3 position,
            Material window, Material frame)
        {
            Transform root = Child(parent, name);
            CreateCube(root, "Glass", position, new Vector3(0.08f, 1.55f, 7.0f), window, false);
            CreateCube(root, "Frame_Top", position + Vector3.up * 0.86f,
                new Vector3(0.18f, 0.18f, 7.5f), frame, false);
            CreateCube(root, "Frame_Bottom", position + Vector3.down * 0.86f,
                new Vector3(0.18f, 0.18f, 7.5f), frame, false);
            for (int index = -2; index <= 2; index++)
                CreateCube(root, $"Mullion_{index + 2:00}", position + Vector3.forward * index * 1.65f,
                    new Vector3(0.18f, 1.8f, 0.14f), frame, false);
        }

        private static void BuildWallPipe(Transform parent, string name, Vector3 position,
            float length, Material material)
        {
            Transform root = Child(parent, name);
            GameObject pipe = CreateCylinder(root, "MainPipe", position,
                new Vector3(0.22f, length * 0.5f, 0.22f), material);
            pipe.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "EndCap_South", position + Vector3.back * length * 0.5f,
                new Vector3(0.34f, 0.12f, 0.34f), material).transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "EndCap_North", position + Vector3.forward * length * 0.5f,
                new Vector3(0.34f, 0.12f, 0.34f), material).transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCube(root, "PipeDrop", position + Vector3.forward * length * 0.5f + Vector3.down * 1.0f,
                new Vector3(0.42f, 2.0f, 0.42f), material, false);
        }

        private static void BuildCrateStack(Transform parent, string name, Vector3 position,
            Material primary, Material secondary, Material edge)
        {
            Transform root = Child(parent, name);
            CreateCube(root, "Crate_A", position + new Vector3(0f, 0.55f, 0f),
                new Vector3(2.4f, 1.1f, 2.0f), primary, false);
            CreateCube(root, "Crate_B", position + new Vector3(1.9f, 0.45f, 0.15f),
                new Vector3(1.3f, 0.9f, 1.5f), secondary, false);
            CreateCube(root, "Band_A_X", position + new Vector3(0f, 0.56f, 0f),
                new Vector3(2.5f, 0.12f, 0.24f), edge, false);
            CreateCube(root, "Band_A_Z", position + new Vector3(0f, 0.56f, 0f),
                new Vector3(0.24f, 0.12f, 2.1f), edge, false);
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent);
            return child.transform;
        }

        private static SnowCpuStage BuildSnowStage(Transform parent)
        {
            var stageObject = new GameObject("SnowCpuStage");
            stageObject.transform.SetParent(parent);
            SnowCpuStage stage = stageObject.AddComponent<SnowCpuStage>();
            stageObject.AddComponent<SnowCpuStageView>();
            stageObject.AddComponent<SnowSystem>();
            SnowDisplaceView displace = stageObject.AddComponent<SnowDisplaceView>();
            displace.enabled = false;

            SetSerialized(stage, "_originXZ", new Vector2(-24f, -18f));
            SetSerialized(stage, "_sizeMeters", new Vector2(48f, 36f));
            SetSerialized(stage, "_initialDepthMm", 300);
            SetSerialized(stage, "_gatherRadiusM", 0.45f);
            // 기본 눈덩이의 0.5 성장 가중치까지 적용해도 첫 시도에서 생성 임계치를
            // 넉넉히 넘기도록 한다. 전역 눈 물리는 바꾸지 않고 이 튜토리얼 Stage만 조정한다.
            SetSerialized(stage, "_gatherResidueMm", 180);
            return stage;
        }

        private static PenguinTutorialHud BuildHud(Transform parent, VisualTreeAsset uxml, PanelSettings panelSettings)
        {
            var hudObject = new GameObject("TutorialHUD");
            hudObject.SetActive(false);
            hudObject.transform.SetParent(parent);
            UIDocument document = hudObject.AddComponent<UIDocument>();
            PenguinTutorialHud hud = hudObject.AddComponent<PenguinTutorialHud>();
            // RequireComponent가 동반 컴포넌트를 초기화한 뒤에 UI 참조를 기록해야
            // Unity 6 beta에서 참조가 다시 null로 덮이지 않는다.
            ConfigureUiDocument(document, panelSettings, uxml, 0);
            MMF_Player stepCompleteFeedbacks = BuildHudStepCompleteFeedback(
                hudObject.transform, document);
            MMF_Player nextStepFeedbacks = BuildHudNextStepFeedback(hudObject.transform, document);
            SetSerialized(hud, "_stepCompleteFeedbacks", stepCompleteFeedbacks);
            SetSerialized(hud, "_nextStepFeedbacks", nextStepFeedbacks);
            hudObject.SetActive(true);
            return hud;
        }

        private static MMF_Player BuildHudStepCompleteFeedback(Transform parent, UIDocument document)
        {
            var feedbackObject = new GameObject("Feel_QuestClear");
            feedbackObject.transform.SetParent(parent, false);
            MMF_Player player = feedbackObject.AddComponent<MMF_Player>();
            player.RestoreInitialValuesOnDisable = false;
            player.CanPlayWhileAlreadyPlaying = false;

            player.AddFeedback(new MMF_UIToolkitScale
            {
                TargetDocument = document,
                Query = "tutorial-card",
                QueryMode = MMF_UIToolkit.QueryModes.Name,
                Mode = MMF_UIToolkitVector2Base.Modes.Interpolate,
                Duration = 0.30f,
                CurveRemapZeroX = 1f,
                CurveRemapZeroY = 1f,
                CurveRemapOneX = 1.045f,
                CurveRemapOneY = 1.045f,
                CurveX = HudPunchTween(),
                CurveY = HudPunchTween(),
                AllowAdditivePlays = false
            });
            player.AddFeedback(new MMF_UIToolkitOpacity
            {
                TargetDocument = document,
                Query = "clear-badge",
                QueryMode = MMF_UIToolkit.QueryModes.Name,
                Mode = MMF_UIToolkitFloatBase.Modes.Interpolate,
                Duration = 0.16f,
                CurveRemapZero = 0f,
                CurveRemapOne = 1f,
                Curve = new MMTweenType(MMTween.MMTweenCurve.EaseOutCubic)
            });
            player.AddFeedback(new MMF_UIToolkitScale
            {
                TargetDocument = document,
                Query = "clear-badge",
                QueryMode = MMF_UIToolkit.QueryModes.Name,
                Mode = MMF_UIToolkitVector2Base.Modes.Interpolate,
                Duration = 0.34f,
                CurveRemapZeroX = 0.70f,
                CurveRemapZeroY = 0.70f,
                CurveRemapOneX = 1f,
                CurveRemapOneY = 1f,
                CurveX = HudPunchTween(),
                CurveY = HudPunchTween(),
                AllowAdditivePlays = false
            });
            player.AddFeedback(new MMF_UIToolkitRotate
            {
                TargetDocument = document,
                Query = "clear-badge",
                QueryMode = MMF_UIToolkit.QueryModes.Name,
                Mode = MMF_UIToolkitFloatBase.Modes.Interpolate,
                Duration = 0.30f,
                CurveRemapZero = -10f,
                CurveRemapOne = 3f,
                Curve = new MMTweenType(MMTween.MMTweenCurve.EaseOutOverhead)
            });
            return player;
        }

        private static MMF_Player BuildHudNextStepFeedback(Transform parent, UIDocument document)
        {
            var feedbackObject = new GameObject("Feel_NextQuestArrive");
            feedbackObject.transform.SetParent(parent, false);
            MMF_Player player = feedbackObject.AddComponent<MMF_Player>();
            player.RestoreInitialValuesOnDisable = false;
            player.CanPlayWhileAlreadyPlaying = false;

            player.AddFeedback(new MMF_UIToolkitTranslate
            {
                TargetDocument = document,
                Query = "tutorial-card",
                QueryMode = MMF_UIToolkit.QueryModes.Name,
                Mode = MMF_UIToolkitVector2Base.Modes.Interpolate,
                Duration = 0.40f,
                CurveRemapZeroX = -34f,
                CurveRemapZeroY = 0f,
                CurveRemapOneX = 0f,
                CurveRemapOneY = 0f,
                CurveX = HudArrivalTween(),
                CurveY = HudArrivalTween(),
                AllowAdditivePlays = false
            });
            player.AddFeedback(new MMF_UIToolkitScale
            {
                TargetDocument = document,
                Query = "tutorial-card",
                QueryMode = MMF_UIToolkit.QueryModes.Name,
                Mode = MMF_UIToolkitVector2Base.Modes.Interpolate,
                Duration = 0.40f,
                CurveRemapZeroX = 0.94f,
                CurveRemapZeroY = 0.94f,
                CurveRemapOneX = 1f,
                CurveRemapOneY = 1f,
                CurveX = HudArrivalTween(),
                CurveY = HudArrivalTween(),
                AllowAdditivePlays = false
            });
            player.AddFeedback(new MMF_UIToolkitOpacity
            {
                TargetDocument = document,
                Query = "tutorial-card",
                QueryMode = MMF_UIToolkit.QueryModes.Name,
                Mode = MMF_UIToolkitFloatBase.Modes.Interpolate,
                Duration = 0.26f,
                CurveRemapZero = 0.70f,
                CurveRemapOne = 1f,
                Curve = new MMTweenType(MMTween.MMTweenCurve.EaseOutCubic)
            });
            return player;
        }

        private static MMTweenType HudPunchTween()
        {
            return new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 3.1f),
                new Keyframe(0.68f, 1.09f, 0.08f, 0.08f),
                new Keyframe(1f, 1f, 0f, 0f)));
        }

        private static MMTweenType HudArrivalTween()
        {
            return new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 2.8f),
                new Keyframe(0.76f, 1.025f, 0.05f, 0.05f),
                new Keyframe(1f, 1f, 0f, 0f)));
        }

        private static TutorialComicCutscene BuildComicCutscene(Transform parent, string objectName,
            int sortingOrder, VisualTreeAsset uxml, PanelSettings panelSettings, Texture2D[] cards,
            TutorialComicCutscene.ComicDialogueLine[] dialogueLines, AudioClip revealSfx,
            AudioClip bubbleSfx, AudioClip magicSfx, AudioClip whooshSfx)
        {
            var cutsceneObject = new GameObject(objectName);
            cutsceneObject.SetActive(false);
            cutsceneObject.transform.SetParent(parent);
            UIDocument document = cutsceneObject.AddComponent<UIDocument>();

            AudioSource sfxSource = cutsceneObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.dopplerLevel = 0f;
            sfxSource.ignoreListenerPause = true;

            TutorialComicCutscene cutscene = cutsceneObject.AddComponent<TutorialComicCutscene>();
            SetObjectArray(cutscene, "_cardTextures", cards);
            cutscene.ConfigureDialogue(dialogueLines);
            SetSerialized(cutscene, "_showStartPrompt", false);
            SetSerialized(cutscene, "_cardRevealSfx", revealSfx);
            SetSerialized(cutscene, "_bubbleRevealSfx", bubbleSfx);
            SetSerialized(cutscene, "_magicRevealSfx", magicSfx);
            SetSerialized(cutscene, "_whooshRevealSfx", whooshSfx);
            SetSerialized(cutscene, "_sfxSource", sfxSource);
            SetSerialized(cutscene, "_cardRevealSfxVolume", 0.5f);
            SetSerialized(cutscene, "_bubbleRevealSfxVolume", 0.34f);
            MMF_Player leftReveal = BuildComicRevealFeedback(cutsceneObject.transform, false);
            MMF_Player rightReveal = BuildComicRevealFeedback(cutsceneObject.transform, true);
            MMF_Player leftBubbleReveal = BuildComicBubbleRevealFeedback(cutsceneObject.transform, false);
            MMF_Player rightBubbleReveal = BuildComicBubbleRevealFeedback(cutsceneObject.transform, true);
            SetSerialized(cutscene, "_leftRevealFeedbacks", leftReveal);
            SetSerialized(cutscene, "_rightRevealFeedbacks", rightReveal);
            SetSerialized(cutscene, "_leftBubbleRevealFeedbacks", leftBubbleReveal);
            SetSerialized(cutscene, "_rightBubbleRevealFeedbacks", rightBubbleReveal);
            EditorUtility.SetDirty(cutscene);
            ConfigureUiDocument(document, panelSettings, uxml, sortingOrder);
            cutsceneObject.SetActive(true);
            return cutscene;
        }

        private static ParticleSystem BuildSnowballFailureVfx(Transform parent, Material particleMaterial)
        {
            ParticleSystem particles = CreateParticleSystem(parent, "SnowballCreateFailureVFX", particleMaterial);
            particles.transform.position = SnowballSpawn + Vector3.up * 0.18f;

            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.38f, 0.72f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.30f, 0.24f, 0.95f), new Color(1f, 0.78f, 0.24f, 1f));
            main.gravityModifier = 0.10f;
            main.maxParticles = 28;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.15f;
            shape.radiusThickness = 0.55f;
            AddParticleFade(particles);
            AddParticleRotation(particles, 2.8f);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static void ConfigureUiDocument(UIDocument document, PanelSettings panelSettings,
            VisualTreeAsset visualTree, int sortingOrder)
        {
            document.panelSettings = panelSettings;
            document.visualTreeAsset = visualTree;
            document.sortingOrder = sortingOrder;

            // Unity 6 beta에서 새 UIDocument를 만든 직후 프로퍼티 setter만 사용하면
            // m_PanelSettings가 씬에 0으로 저장되는 경우가 있어 직렬화 필드도 명시한다.
            var serialized = new SerializedObject(document);
            SerializedProperty panelProperty = serialized.FindProperty("m_PanelSettings");
            SerializedProperty treeProperty = serialized.FindProperty("sourceAsset");
            SerializedProperty sortingProperty = serialized.FindProperty("m_SortingOrder");
            if (panelProperty != null) panelProperty.objectReferenceValue = panelSettings;
            if (treeProperty != null) treeProperty.objectReferenceValue = visualTree;
            if (sortingProperty != null) sortingProperty.floatValue = sortingOrder;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(document);
        }

        private static PanelSettings GetOrCreateTutorialPanelSettings(PanelSettings sharedSettings)
        {
            PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(TutorialPanelSettingsPath);
            bool created = settings == null;
            if (created)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, TutorialPanelSettingsPath);
            }

            // Keep the project's theme/text configuration, but isolate tutorial scaling from the
            // shared Stage HUD asset so other scenes cannot be affected by onboarding tuning.
            EditorUtility.CopySerialized(sharedSettings, settings);
            settings.name = "PenguinTutorialPanelSettings";
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            EditorUtility.SetDirty(settings);
            if (created) AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }

        private static MMF_Player BuildComicRevealFeedback(Transform parent, bool isRight)
        {
            var feedbackObject = new GameObject(isRight ? "Feel_RightCardReveal" : "Feel_LeftCardReveal");
            feedbackObject.transform.SetParent(parent, false);
            feedbackObject.transform.localPosition = Vector3.zero;
            feedbackObject.transform.localScale = Vector3.one;
            feedbackObject.transform.localRotation = Quaternion.identity;

            float duration = isRight ? 0.54f : 0.58f;
            float positionDuration = duration * 0.86f;
            float rotationDuration = duration * 0.92f;
            Vector3 initialPosition = isRight
                ? new Vector3(44f, -3f, 0f)
                : new Vector3(-38f, 4f, 0f);
            float initialScale = isRight ? 0.978f : 0.982f;

            MMF_Player player = feedbackObject.AddComponent<MMF_Player>();
            player.RestoreInitialValuesOnDisable = false;
            player.CanPlayWhileAlreadyPlaying = false;
            player.AddFeedback(new MMF_Position
            {
                AnimatePositionTarget = feedbackObject,
                Mode = MMF_Position.Modes.AtoB,
                Space = MMF_Position.Spaces.Local,
                MovementMode = MMF_Position.MovementModes.Duration,
                AnimatePositionDuration = positionDuration,
                InitialPosition = initialPosition,
                DestinationPosition = Vector3.zero,
                AnimatePositionTween = ComicPositionTween(),
                RelativePosition = false,
                DeterminePositionsOnPlay = false,
                AllowAdditivePlays = false
            });

            MMTweenType scaleTween = ComicScaleTween();
            player.AddFeedback(new MMF_Scale
            {
                AnimateScaleTarget = feedbackObject.transform,
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = duration,
                RemapCurveZero = initialScale,
                RemapCurveOne = 1f,
                UniformScaling = true,
                AnimateScaleTweenX = scaleTween,
                AnimateScaleTweenY = scaleTween,
                AnimateScaleTweenZ = scaleTween,
                DetermineScaleOnPlay = false,
                AllowAdditivePlays = false
            });

            player.AddFeedback(new MMF_Rotation
            {
                AnimateRotationTarget = feedbackObject.transform,
                Mode = MMF_Rotation.Modes.ToDestination,
                AnimateRotationDuration = rotationDuration,
                ToDestinationSpace = Space.Self,
                DestinationAngles = Vector3.zero,
                ToDestinationTween = ComicRotationTween(),
                DetermineRotationOnPlay = true,
                AllowAdditivePlays = false
            });
            return player;
        }

        private static MMF_Player BuildComicBubbleRevealFeedback(Transform parent, bool isRight)
        {
            var feedbackObject = new GameObject(isRight ? "Feel_RightBubbleReveal" : "Feel_LeftBubbleReveal");
            feedbackObject.transform.SetParent(parent, false);
            feedbackObject.transform.localPosition = Vector3.zero;
            feedbackObject.transform.localScale = Vector3.one;
            feedbackObject.transform.localRotation = Quaternion.identity;

            const float duration = 0.30f;
            Vector3 initialPosition = isRight ? new Vector3(16f, 8f, 0f) : new Vector3(-16f, 8f, 0f);
            MMF_Player player = feedbackObject.AddComponent<MMF_Player>();
            player.RestoreInitialValuesOnDisable = false;
            player.CanPlayWhileAlreadyPlaying = false;
            player.AddFeedback(new MMF_Position
            {
                AnimatePositionTarget = feedbackObject,
                Mode = MMF_Position.Modes.AtoB,
                Space = MMF_Position.Spaces.Local,
                MovementMode = MMF_Position.MovementModes.Duration,
                AnimatePositionDuration = duration * 0.86f,
                InitialPosition = initialPosition,
                DestinationPosition = Vector3.zero,
                AnimatePositionTween = ComicPositionTween(),
                RelativePosition = false,
                DeterminePositionsOnPlay = false,
                AllowAdditivePlays = false
            });

            MMTweenType scaleTween = ComicScaleTween();
            player.AddFeedback(new MMF_Scale
            {
                AnimateScaleTarget = feedbackObject.transform,
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = duration,
                RemapCurveZero = 0.82f,
                RemapCurveOne = 1f,
                UniformScaling = true,
                AnimateScaleTweenX = scaleTween,
                AnimateScaleTweenY = scaleTween,
                AnimateScaleTweenZ = scaleTween,
                DetermineScaleOnPlay = false,
                AllowAdditivePlays = false
            });

            player.AddFeedback(new MMF_Rotation
            {
                AnimateRotationTarget = feedbackObject.transform,
                Mode = MMF_Rotation.Modes.ToDestination,
                AnimateRotationDuration = duration * 0.92f,
                ToDestinationSpace = Space.Self,
                DestinationAngles = Vector3.zero,
                ToDestinationTween = ComicRotationTween(),
                DetermineRotationOnPlay = true,
                AllowAdditivePlays = false
            });
            return player;
        }

        private static MMTweenType ComicPositionTween()
        {
            return new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 2.8f),
                new Keyframe(0.74f, 1.012f, 0.08f, 0.08f),
                new Keyframe(1f, 1f, 0f, 0f)));
        }

        private static MMTweenType ComicScaleTween()
        {
            return new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 2.4f),
                new Keyframe(0.68f, 1.08f, 0.10f, 0.10f),
                new Keyframe(0.88f, 0.992f, -0.02f, -0.02f),
                new Keyframe(1f, 1f, 0f, 0f)));
        }

        private static MMTweenType ComicRotationTween()
        {
            return new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 2.6f),
                new Keyframe(0.80f, 1.01f, 0.04f, 0.04f),
                new Keyframe(1f, 1f, 0f, 0f)));
        }

        private static TutorialMusicLooper BuildTutorialMusic(Transform parent, AudioClip music)
        {
            var musicObject = new GameObject("TutorialMusic");
            musicObject.transform.SetParent(parent);
            AudioSource sourceA = musicObject.AddComponent<AudioSource>();
            AudioSource sourceB = musicObject.AddComponent<AudioSource>();
            TutorialMusicLooper looper = musicObject.AddComponent<TutorialMusicLooper>();
            SetSerialized(looper, "_music", music);
            SetSerialized(looper, "_sourceA", sourceA);
            SetSerialized(looper, "_sourceB", sourceB);
            SetSerialized(looper, "_volume", 0.22f);
            SetSerialized(looper, "_fadeInDuration", 3f);
            SetSerialized(looper, "_crossfadeDuration", 4.5f);
            SetSerialized(looper, "_scheduleLeadTime", 1f);
            return looper;
        }

        private static Transform BuildMarker(Transform parent, Font koreanFont, Material particleMaterial,
            Material guideMaterial, Texture2D guideTexture)
        {
            var root = new GameObject("WorldTargetVFX");
            root.transform.SetParent(parent);
            root.transform.position = WalkTarget + Vector3.up * 0.04f;

            ParticleSystem ambient = CreateParticleSystem(root.transform, "GoalOrbitVFX", particleMaterial);
            ambient.transform.localPosition = Vector3.up * 0.10f;
            ambient.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem.MainModule ambientMain = ambient.main;
            ambientMain.loop = true;
            ambientMain.playOnAwake = true;
            ambientMain.duration = 1.2f;
            ambientMain.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.45f);
            ambientMain.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
            ambientMain.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.32f);
            ambientMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ambientMain.maxParticles = 64;
            ParticleSystem.EmissionModule ambientEmission = ambient.emission;
            ambientEmission.rateOverTime = 18f;
            ParticleSystem.ShapeModule ambientShape = ambient.shape;
            ambientShape.shapeType = ParticleSystemShapeType.Circle;
            ambientShape.radius = 1.3f;
            ambientShape.radiusThickness = 0.24f;
            AddParticleFade(ambient);
            AddParticleRotation(ambient, 2.4f);

            ParticleSystem guideRise = CreateParticleSystem(root.transform, "GoalGuideRiseVFX", guideMaterial);
            ParticleSystem.MainModule guideMain = guideRise.main;
            guideMain.loop = true;
            guideMain.playOnAwake = true;
            guideMain.duration = 1.4f;
            guideMain.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.35f);
            guideMain.startSpeed = 0f;
            guideMain.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.30f);
            guideMain.maxParticles = 48;
            ParticleSystem.EmissionModule guideEmission = guideRise.emission;
            guideEmission.rateOverTime = 7f;
            ParticleSystem.ShapeModule guideShape = guideRise.shape;
            guideShape.shapeType = ParticleSystemShapeType.Box;
            guideShape.position = new Vector3(0f, 1.10f, 0f);
            guideShape.scale = new Vector3(0.70f, 2.0f, 0.70f);
            ParticleSystem.VelocityOverLifetimeModule guideVelocity = guideRise.velocityOverLifetime;
            guideVelocity.enabled = true;
            guideVelocity.space = ParticleSystemSimulationSpace.Local;
            // Unity requires all velocity axes to use the same MinMaxCurve mode.
            // Keep the horizontal axes still while preserving the varied upward drift.
            guideVelocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            guideVelocity.y = new ParticleSystem.MinMaxCurve(0.28f, 0.65f);
            guideVelocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            AddParticleFade(guideRise);

            var guideBillboard = new GameObject("GoalGuideBillboard");
            guideBillboard.transform.SetParent(root.transform, false);
            guideBillboard.transform.localPosition = Vector3.up * 2.55f;
            GameObject guideQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            guideQuad.name = "GuideArrowTexture";
            guideQuad.transform.SetParent(guideBillboard.transform, false);
            guideQuad.transform.localScale = Vector3.one * 0.90f;
            MeshRenderer guideRenderer = guideQuad.GetComponent<MeshRenderer>();
            guideRenderer.sharedMaterial = guideMaterial;
            guideRenderer.shadowCastingMode = ShadowCastingMode.Off;
            guideRenderer.receiveShadows = false;
            UnityEngine.Object.DestroyImmediate(guideQuad.GetComponent<Collider>());
            TextMesh guideLabelShadow = CreateWorldText(guideBillboard.transform, "GuideLabelShadow", koreanFont,
                "목표", new Color(0.015f, 0.05f, 0.08f, 0.96f), 0.030f);
            guideLabelShadow.transform.localPosition = new Vector3(0.035f, -0.78f, 0.025f);
            TextMesh guideLabel = CreateWorldText(guideBillboard.transform, "GuideLabel", koreanFont,
                "목표", Color.white, 0.030f);
            guideLabel.transform.localPosition = new Vector3(0f, -0.75f, 0f);

            ParticleSystem successBurst = CreateParticleSystem(root.transform, "GoalSuccessBurstVFX", particleMaterial);
            successBurst.transform.localPosition = Vector3.up * 0.35f;
            ParticleSystem.MainModule burstMain = successBurst.main;
            burstMain.loop = false;
            burstMain.playOnAwake = false;
            burstMain.duration = 0.8f;
            burstMain.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.05f);
            burstMain.startSpeed = new ParticleSystem.MinMaxCurve(1.3f, 2.8f);
            burstMain.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.40f);
            burstMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            burstMain.gravityModifier = 0.18f;
            burstMain.maxParticles = 40;
            ParticleSystem.EmissionModule burstEmission = successBurst.emission;
            burstEmission.rateOverTime = 0f;
            burstEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });
            ParticleSystem.ShapeModule burstShape = successBurst.shape;
            burstShape.shapeType = ParticleSystemShapeType.Hemisphere;
            burstShape.radius = 0.34f;
            burstShape.radiusThickness = 1f;
            AddParticleFade(successBurst);
            AddParticleRotation(successBurst, 3.4f);
            successBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var statusRoot = new GameObject("GoalStatusBillboard");
            statusRoot.transform.SetParent(root.transform, false);
            statusRoot.transform.localPosition = Vector3.up * 1.95f;
            TextMesh shadow = CreateWorldText(statusRoot.transform, "StatusShadow", koreanFont,
                TutorialGoalEffect.PendingMessage, new Color(0.03f, 0.10f, 0.15f, 0.92f), 0.050f);
            shadow.transform.localPosition = new Vector3(0.045f, -0.045f, 0.025f);
            TextMesh status = CreateWorldText(statusRoot.transform, "StatusText", koreanFont,
                TutorialGoalEffect.PendingMessage, new Color(0.22f, 0.82f, 0.46f, 1f), 0.050f);

            var grabPromptRoot = new GameObject("SnowballGrabPrompt");
            grabPromptRoot.transform.SetParent(root.transform, false);
            grabPromptRoot.transform.localPosition = Vector3.up * 1.45f;
            TextMesh grabPromptShadow = CreateWorldText(grabPromptRoot.transform, "GrabPromptShadow", koreanFont,
                TutorialGoalEffect.GrabPromptMessage, new Color(0.03f, 0.10f, 0.15f, 0.96f), 0.034f);
            grabPromptShadow.transform.localPosition = new Vector3(0.045f, -0.045f, 0.025f);
            TextMesh grabPrompt = CreateWorldText(grabPromptRoot.transform, "GrabPromptText", koreanFont,
                TutorialGoalEffect.GrabPromptMessage, new Color(1f, 0.74f, 0.16f, 1f), 0.034f);
            TextMesh arrowShadow = CreateWorldText(grabPromptRoot.transform, "GrabArrowShadow", koreanFont,
                "▼", new Color(0.03f, 0.10f, 0.15f, 0.96f), 0.052f);
            arrowShadow.transform.localPosition = new Vector3(0.04f, -0.56f, 0.025f);
            TextMesh arrow = CreateWorldText(grabPromptRoot.transform, "GrabArrow", koreanFont,
                "▼", new Color(1f, 0.74f, 0.16f, 1f), 0.052f);
            arrow.transform.localPosition = new Vector3(0f, -0.52f, 0f);
            grabPromptRoot.SetActive(false);

            TutorialGoalEffect effect = root.AddComponent<TutorialGoalEffect>();
            SetSerialized(effect, "_ambientParticles", ambient);
            SetSerialized(effect, "_successBurst", successBurst);
            SetSerialized(effect, "_statusRoot", statusRoot.transform);
            SetSerialized(effect, "_statusText", status);
            SetSerialized(effect, "_statusShadow", shadow);
            SetSerialized(effect, "_grabPromptRoot", grabPromptRoot.transform);
            SetSerialized(effect, "_grabPromptText", grabPrompt);
            SetSerialized(effect, "_grabPromptShadow", grabPromptShadow);
            SetSerialized(effect, "_guideRiseParticles", guideRise);
            SetSerialized(effect, "_guideBillboardRoot", guideBillboard.transform);
            SetSerialized(effect, "_guideBillboardRenderer", guideRenderer);
            SetSerialized(effect, "_guideLabelText", guideLabel);
            SetSerialized(effect, "_guideLabelShadow", guideLabelShadow);
            SetSerialized(effect, "_screenGuideTexture", guideTexture);
            return root.transform;
        }

        private static ParticleSystem CreateParticleSystem(Transform parent, string name, Material material)
        {
            var particleObject = new GameObject(name, typeof(ParticleSystem));
            particleObject.transform.SetParent(parent, false);
            ParticleSystem particles = particleObject.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 30;
            return particles;
        }

        private static void AddParticleFade(ParticleSystem particles)
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
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0.86f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                }
            });
        }

        private static void AddParticleRotation(ParticleSystem particles, float speed)
        {
            ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-speed, speed);
        }

        private static TextMesh CreateWorldText(Transform parent, string name, Font font,
            string text, Color color, float characterSize = 0.070f)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.font = font;
            textMesh.text = text;
            textMesh.color = color;
            textMesh.fontSize = 64;
            textMesh.characterSize = characterSize;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.richText = false;

            MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 50;
            return textMesh;
        }

        private static Light[] BuildLighting(Transform parent, Material edge, Material bulb)
        {
            var sunObject = new GameObject("TutorialSun");
            sunObject.transform.SetParent(parent);
            sunObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.78f, 0.88f, 1f);
            sun.intensity = 0.58f;
            sun.shadows = LightShadows.Soft;

            return new[]
            {
                CreatePendantLight(parent, "WarmFill_Walk", new Vector3(-16f, 7.35f, -11f), edge, bulb),
                CreatePendantLight(parent, "WarmFill_Run", new Vector3(0f, 7.35f, -11f), edge, bulb),
                CreatePendantLight(parent, "WarmFill_Slide", new Vector3(16f, 7.35f, -4f), edge, bulb),
                CreatePendantLight(parent, "WarmFill_Snowball", new Vector3(16f, 7.35f, 11f), edge, bulb),
                CreatePendantLight(parent, "WarmFill_SnowMachine", new Vector3(0f, 7.35f, 11f), edge, bulb),
                CreatePendantLight(parent, "WarmFill_GiftSend", new Vector3(-10.5f, 7.35f, 6.5f), edge, bulb),
                CreatePendantLight(parent, "WarmFill_Warehouse", new Vector3(-19f, 7.35f, 13.5f), edge, bulb),
                CreatePendantLight(parent, "WarmFill_Neighbor", new Vector3(-12.3f, 7.35f, 14f), edge, bulb)
            };
        }

        private static Light CreatePendantLight(Transform parent, string name, Vector3 position,
            Material edge, Material bulb)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent);
            lightObject.transform.position = position;
            CreateCylinder(lightObject.transform, "CeilingStem", position + Vector3.up * 0.75f,
                new Vector3(0.10f, 0.75f, 0.10f), edge);
            CreateCylinder(lightObject.transform, "Shade", position,
                new Vector3(0.62f, 0.16f, 0.62f), edge);
            CreateCylinder(lightObject.transform, "WarmBulb", position + Vector3.down * 0.22f,
                new Vector3(0.25f, 0.16f, 0.25f), bulb);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.67f, 0.34f);
            light.intensity = 32f;
            light.range = 11.5f;
            light.shadows = LightShadows.None;
            return light;
        }

        private static GameObject CreateCube(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material, bool keepCollider)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static GameObject CreateCylinder(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent);
            cylinder.transform.position = position;
            cylinder.transform.localScale = scale;
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            return cylinder;
        }

        private static GameObject CreateSphere(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(parent);
            sphere.transform.position = position;
            sphere.transform.localScale = scale;
            sphere.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
            return sphere;
        }

        private static Material GetOrCreateMaterial(string path, string name, Color color,
            float smoothness, bool emissive = false)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit 셰이더를 찾을 수 없다.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.35f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateRoomSignIconMaterial(Texture2D texture, string path, string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit 셰이더를 찾을 수 없다.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Back);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateRoomSignKeyPlateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit 셰이더를 찾을 수 없다.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(RoomSignKeyPlateMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_RoomSign_KeyPlate" };
                AssetDatabase.CreateAsset(material, RoomSignKeyPlateMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", new Color(0.98f, 0.96f, 0.86f, 1f));
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateRoomSignTextMaterial(Font font, string path, string name, Color color)
        {
            Shader shader = Shader.Find("PPack/Tutorial/RoomSignTextDepth");
            if (shader == null) throw new InvalidOperationException("구간 표지판 깊이 검사 글자 셰이더를 찾을 수 없다.");
            if (font == null || font.material == null || font.material.mainTexture == null)
                throw new InvalidOperationException("구간 표지판 영문 폰트 아틀라스를 찾을 수 없다.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            Texture atlas = font.material.mainTexture;
            material.SetTexture("_MainTex", atlas);
            // The TextMesh vertex color owns the per-label tint. Keep the material neutral so
            // white titles remain white and navy key labels are not multiplied twice.
            material.SetColor("_Color", Color.white);
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateParticleMaterial(Texture2D texture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(GoalParticleMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) throw new InvalidOperationException("URP 파티클 셰이더를 찾을 수 없다.");
                material = new Material(shader) { name = "M_TutorialGoalParticle" };
                AssetDatabase.CreateAsset(material, GoalParticleMaterialPath);
            }

            material.SetColor("_BaseColor", new Color(0.22f, 0.82f, 0.46f, 1f));
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateBarrierMaterial(Texture2D texture)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                throw new InvalidOperationException("URP Unlit 셰이더를 찾을 수 없다.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(BarrierMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_TutorialQuestBarrier" };
                AssetDatabase.CreateAsset(material, BarrierMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            Color frost = new Color(0.66f, 0.90f, 1f, 0.18f);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", new Vector2(1.35f, 1.0f));
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", new Vector2(1.35f, 1.0f));
            }
            material.SetColor("_BaseColor", frost);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (material.HasProperty("_DstBlendAlpha"))
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            material.DisableKeyword("ADDITIVECONFIG_ON");
            material.DisableKeyword("GLOW_ON");
            material.DisableKeyword("SHAPE1CONTRAST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent + 4;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateBarrierParticleMaterial(Texture2D texture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BarrierParticleMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) throw new InvalidOperationException("URP 파티클 셰이더를 찾을 수 없다.");
                material = new Material(shader) { name = "M_TutorialQuestBarrierSnowflake" };
                AssetDatabase.CreateAsset(material, BarrierParticleMaterialPath);
            }

            Color ice = new Color(0.72f, 0.92f, 1f, 0.46f);
            material.SetColor("_BaseColor", ice);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent + 10;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D GetOrCreateBarrierTexture()
        {
            const int size = 256;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BarrierTexturePath);
            bool isNew = texture == null;
            if (isNew)
            {
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "T_TutorialQuestBarrierFrostVeil",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat
                };
            }
            else if (texture.width != size || texture.height != size)
            {
                texture.Reinitialize(size, size, TextureFormat.RGBA32, false);
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;
                float broad = Mathf.PerlinNoise(u * 2.15f + 11.7f, v * 2.15f + 4.2f);
                float detail = Mathf.PerlinNoise(u * 6.4f + 2.1f, v * 6.4f + 15.3f);
                float wisps = Mathf.SmoothStep(0.58f, 0.82f,
                    Mathf.PerlinNoise(u * 3.8f + v * 0.7f, v * 3.8f + 8.6f));
                float alpha = Mathf.Clamp01(0.22f + broad * 0.30f + detail * 0.10f + wisps * 0.12f);
                byte tint = (byte)Mathf.RoundToInt(Mathf.Lerp(226f, 255f, broad));
                pixels[y * size + x] = new Color32(tint, 248, 255, (byte)(alpha * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            if (isNew) AssetDatabase.CreateAsset(texture, BarrierTexturePath);
            else EditorUtility.SetDirty(texture);
            return texture;
        }

        private static Texture2D GetOrCreateBarrierSnowflakeTexture()
        {
            const int size = 128;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BarrierParticleTexturePath);
            bool isNew = texture == null;
            if (isNew)
            {
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "T_TutorialQuestBarrierSnowflake",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var pixels = new Color32[size * size];
            Vector2 center = Vector2.one * (size * 0.5f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = (new Vector2(x + 0.5f, y + 0.5f) - center) / (size * 0.5f);
                float radius = p.magnitude;
                float alpha = 0f;
                for (int arm = 0; arm < 6; arm++)
                {
                    float angle = arm * Mathf.PI / 3f;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    alpha = Mathf.Max(alpha, Stroke(DistanceToSegment(p, Vector2.zero,
                        direction * 0.72f), 0.024f));
                    Vector2 branchRoot = direction * 0.42f;
                    alpha = Mathf.Max(alpha, Stroke(DistanceToSegment(p, branchRoot,
                        branchRoot + Rotate(direction, 0.72f) * 0.17f), 0.021f));
                    alpha = Mathf.Max(alpha, Stroke(DistanceToSegment(p, branchRoot,
                        branchRoot + Rotate(direction, -0.72f) * 0.17f), 0.021f));
                }
                alpha *= 1f - Mathf.SmoothStep(0.76f, 0.92f, radius);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            if (isNew) AssetDatabase.CreateAsset(texture, BarrierParticleTexturePath);
            else EditorUtility.SetDirty(texture);
            return texture;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000001f) return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        private static Vector2 Rotate(Vector2 value, float radians)
        {
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine);
        }

        private static float Stroke(float distance, float width)
        {
            return 1f - Mathf.SmoothStep(width * 0.42f, width, distance);
        }

        private static Texture2D GetOrCreateGoalParticleTexture()
        {
            const int size = 32;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(GoalParticleTexturePath);
            bool isNew = texture == null;
            if (isNew)
            {
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "T_TutorialGoalStar",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                float py = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
                float diamond = Mathf.Clamp01(1f - (px + py) * 1.10f);
                float vertical = Mathf.Clamp01(1f - px * 7.5f) * Mathf.Clamp01(1f - py * 0.92f);
                float horizontal = Mathf.Clamp01(1f - py * 7.5f) * Mathf.Clamp01(1f - px * 0.92f);
                float alpha = Mathf.Pow(Mathf.Max(diamond, Mathf.Max(vertical, horizontal)), 1.55f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            if (isNew) AssetDatabase.CreateAsset(texture, GoalParticleTexturePath);
            else EditorUtility.SetDirty(texture);
            return texture;
        }

        private static Material GetOrCreateGuideMaterial(Texture2D texture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(GuideArrowMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("URP Unlit 셰이더를 찾을 수 없다.");
                material = new Material(shader) { name = "M_TutorialGuideArrow" };
                AssetDatabase.CreateAsset(material, GuideArrowMaterialPath);
            }

            material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent + 20;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D GetOrCreateGuideArrowTexture()
        {
            const int size = 128;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(GuideArrowTexturePath);
            bool isNew = texture == null;
            if (isNew)
            {
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "T_TutorialGuideArrow",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f) / size * 2f - 1f;
                float py = (y + 0.5f) / size * 2f - 1f;

                float stemEdge = 0.24f - Mathf.Abs(px);
                float stemAlpha = py >= -0.05f && py <= 0.84f
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.035f, 0.035f, stemEdge))
                    : 0f;

                float headWidth = Mathf.InverseLerp(-0.88f, 0.10f, py) * 0.84f;
                float headEdge = headWidth - Mathf.Abs(px);
                float headAlpha = py >= -0.88f && py <= 0.10f
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.045f, 0.045f, headEdge))
                    : 0f;

                float alpha = Mathf.Max(stemAlpha, headAlpha);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            if (isNew) AssetDatabase.CreateAsset(texture, GuideArrowTexturePath);
            else EditorUtility.SetDirty(texture);
            return texture;
        }

        private static void SetSerialized(UnityEngine.Object target, string propertyName, object value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 필드를 찾을 수 없다.");

            switch (value)
            {
                case UnityEngine.Object objectReference:
                    property.objectReferenceValue = objectReference;
                    break;
                case Vector2 vector2:
                    property.vector2Value = vector2;
                    break;
                case Vector3 vector3:
                    property.vector3Value = vector3;
                    break;
                case int integer:
                    property.intValue = integer;
                    break;
                case float number:
                    property.floatValue = number;
                    break;
                case bool flag:
                    property.boolValue = flag;
                    break;
                default:
                    throw new ArgumentException($"지원하지 않는 SerializedProperty 값: {value?.GetType().Name}");
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(UnityEngine.Object target, string propertyName,
            UnityEngine.Object[] values)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 필드를 찾을 수 없다.");

            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Game/InGame/Tutorial", "Scenes");
            EnsureFolder("Assets/Game/InGame/Tutorial", "Materials");
            EnsureFolder("Assets/Game/InGame/Tutorial", "VFX");
            EnsureFolder("Assets/Game/InGame/Tutorial", "Audio");
            EnsureFolder("Assets/Game/InGame/Tutorial", "Cutscene");
            EnsureFolder("Assets/Game/InGame/Tutorial/Cutscene", "Images");
            EnsureFolder("Assets/Game/InGame/Tutorial", "UI");
            EnsureFolder("Assets/Game/InGame/Tutorial/UI", "RoomSigns");
            EnsureFolder("Assets/Game/InGame/Tutorial/UI/RoomSigns", "Icons");
            EnsureFolder("Assets/Game/InGame/Tutorial/UI/RoomSigns", "Materials");
            EnsureFolder("Assets/Game/InGame/Tutorial/UI/RoomSigns", "Shaders");
        }

        private static void EnsureBuildSettingsScene()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int index = 0; index < scenes.Length; index++)
            {
                if (scenes[index].path != ScenePath) continue;
                scenes[index] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = scenes;
                return;
            }

            Array.Resize(ref scenes, scenes.Length + 1);
            scenes[^1] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = scenes;
        }

        [MenuItem("PPack/Tutorial/Register Penguin Tutorial In Build")]
        private static void RegisterBuildSettingsScene()
        {
            EnsureBuildSettingsScene();
            AssetDatabase.SaveAssets();
            Debug.Log($"Penguin tutorial registered in Build Settings: {ScenePath}");
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = $"{parent}/{name}";
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}

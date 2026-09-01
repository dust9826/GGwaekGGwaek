using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>실제 씬을 Play Mode로 열어 첫 단계와 핵심 런타임 배선을 확인한 뒤 원래 씬으로 돌아간다.</summary>
    [InitializeOnLoad]
    public static class PenguinTutorialSmokeTest
    {
        private const string PendingKey = "PPack.PenguinTutorialSmoke.Pending";
        private const string QueuedKey = "PPack.PenguinTutorialSmoke.Queued";
        private const string ResumeAfterPlayKey = "PPack.PenguinTutorialSmoke.ResumeAfterPlay";
        private const string OriginalSceneKey = "PPack.PenguinTutorialSmoke.OriginalScene";
        private const string ScreenshotPath = "Assets/Game/InGame/Tutorial/Docs/PenguinTutorial_Play.png";

        private static double _enteredPlayAt;
        private static double _stopAfterCaptureAt;
        private static bool _sampling;
        private static bool _captureRequested;

        static PenguinTutorialSmokeTest()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("PPack/Tutorial/Validate Penguin Tutorial Scene")]
        public static void ValidateScene()
        {
            Scene original = SceneManager.GetActiveScene();
            Scene tutorial = EditorSceneManager.OpenScene(PenguinTutorialSceneBuilder.ScenePath, OpenSceneMode.Additive);
            try
            {
                ValidateLoadedScene(tutorial);
                Debug.Log("Penguin tutorial validation passed: eight-step snow-to-neighbor cycle, immediate comics, camera, lighting, and HUD are wired.");
            }
            finally
            {
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(tutorial, true);
            }
        }

        [MenuItem("PPack/Tutorial/Run Penguin Tutorial Smoke Test")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(QueuedKey, true);
                SessionState.SetBool(ResumeAfterPlayKey, true);
                Debug.Log("Penguin tutorial smoke: leaving the current Play Mode before opening the tutorial scene.");
                EditorApplication.isPlaying = false;
                return;
            }

            BeginRun();
        }

        private static void BeginRun()
        {
            // EditorSceneManager는 Play Mode에서 절대 호출하면 안 된다. 특히 다른 씬이
            // LoadSceneAsync로 활성화되는 프레임에는 isPlayingOrWillChangePlaymode 값이 잠시
            // 전이 중일 수 있으므로 Application.isPlaying도 함께 확인한다.
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(QueuedKey, true);
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                SessionState.SetBool(QueuedKey, true);
                EditorApplication.delayCall += BeginQueuedRunWhenReady;
                return;
            }

            Scene original = SceneManager.GetActiveScene();
            if (!original.IsValid() || string.IsNullOrEmpty(original.path))
                throw new InvalidOperationException("저장된 원래 씬이 필요하다.");
            if (original.isDirty)
                throw new InvalidOperationException("현재 씬에 저장하지 않은 변경이 있어 자동 Play 검증을 시작하지 않았다.");

            SessionState.SetString(OriginalSceneKey, original.path);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(QueuedKey, false);
            EditorSceneManager.OpenScene(PenguinTutorialSceneBuilder.ScenePath, OpenSceneMode.Single);
            EditorApplication.delayCall -= EnterPlayModeForPendingRun;
            EditorApplication.delayCall += EnterPlayModeForPendingRun;
        }

        private static void EnterPlayModeForPendingRun()
        {
            EditorApplication.delayCall -= EnterPlayModeForPendingRun;
            if (!SessionState.GetBool(PendingKey, false) ||
                Application.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EditorApplication.isPlaying = true;
        }

        private static void BeginQueuedRunWhenReady()
        {
            EditorApplication.delayCall -= BeginQueuedRunWhenReady;
            if (!SessionState.GetBool(QueuedKey, false)) return;

            // 일반 게임 재생 중에는 Smoke Test 예약을 계속 delayCall에 매달아 두지 않는다.
            // 씬 전환 프레임에 이 콜백이 끼어들어 EditorSceneManager.OpenScene을 호출하면
            // LoadingScreen → SinglePlay 전환과 충돌한다. Play 종료 후 재개가 필요한 명시적
            // Smoke Test 요청은 EnteredEditMode에서 다시 예약한다.
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += BeginQueuedRunWhenReady;
                return;
            }

            SessionState.SetBool(QueuedKey, false);
            SessionState.SetBool(ResumeAfterPlayKey, false);
            BeginRun();
        }

        [MenuItem("PPack/Tutorial/Restore Scene Before Smoke Test")]
        public static void RestorePreviousScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string originalPath = SessionState.GetString(OriginalSceneKey, string.Empty);
            if (string.IsNullOrEmpty(originalPath)) return;
            EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
            SessionState.SetBool(PendingKey, false);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            bool pending = SessionState.GetBool(PendingKey, false);

            if (state == PlayModeStateChange.EnteredPlayMode && !pending)
            {
                // 이전 컴파일 또는 중단된 Smoke Test가 남긴 예약은 일반 게임 재생에
                // 승계하지 않는다. DEPLOY를 포함한 프로덕션 씬 전환이 최우선이다.
                SessionState.SetBool(QueuedKey, false);
                SessionState.SetBool(ResumeAfterPlayKey, false);
                EditorApplication.delayCall -= BeginQueuedRunWhenReady;
                EditorApplication.delayCall -= EnterPlayModeForPendingRun;
                return;
            }

            if (!pending)
            {
                if (state == PlayModeStateChange.EnteredEditMode &&
                    SessionState.GetBool(QueuedKey, false) &&
                    SessionState.GetBool(ResumeAfterPlayKey, false))
                    EditorApplication.delayCall += BeginQueuedRunWhenReady;
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _enteredPlayAt = EditorApplication.timeSinceStartup;
                _sampling = true;
                _captureRequested = false;
                EditorApplication.update -= SamplePlay;
                EditorApplication.update += SamplePlay;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= SamplePlay;
                _sampling = false;
                string originalPath = SessionState.GetString(OriginalSceneKey, string.Empty);
                SessionState.SetBool(PendingKey, false);
                if (!string.IsNullOrEmpty(originalPath)) EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);

                if (SessionState.GetBool(QueuedKey, false))
                    EditorApplication.delayCall += BeginQueuedRunWhenReady;
            }
        }

        private static void SamplePlay()
        {
            if (!_sampling) return;
            double now = EditorApplication.timeSinceStartup;
            if (_captureRequested)
            {
                if (now < _stopAfterCaptureAt) return;
                _sampling = false;
                EditorApplication.update -= SamplePlay;
                EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
                return;
            }
            double playElapsed = now - _enteredPlayAt;
            if (playElapsed > 0.45d)
            {
                foreach (TutorialComicCutscene cutscene in UnityEngine.Object.FindObjectsByType<TutorialComicCutscene>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (cutscene.IsPlaying) cutscene.Skip();
            }
            if (playElapsed < 4d) return;

            PenguinTutorialDirector director = UnityEngine.Object.FindAnyObjectByType<PenguinTutorialDirector>();
            PenguinTutorialHud hud = UnityEngine.Object.FindAnyObjectByType<PenguinTutorialHud>();
            TutorialGoalEffect goalEffect = UnityEngine.Object.FindObjectsByType<TutorialGoalEffect>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            TutorialStageGate[] stageGates = UnityEngine.Object.FindObjectsByType<TutorialStageGate>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            SnowCpuStage snow = UnityEngine.Object.FindAnyObjectByType<SnowCpuStage>();
            PenguinLocomotion player = UnityEngine.Object.FindAnyObjectByType<PenguinLocomotion>();
            if (director == null || hud == null || goalEffect == null || stageGates.Length != 5 ||
                snow == null || player == null ||
                !director.isActiveAndEnabled || !hud.IsReady)
            {
                Debug.LogError("Penguin tutorial smoke failed: runtime component missing.");
                RequestPlayModeStop(now);
                return;
            }
            else if (director.CurrentStep != EPenguinTutorialStep.Walk)
            {
                Debug.LogError($"Penguin tutorial smoke failed: expected Walk, got {director.CurrentStep}.");
                RequestPlayModeStop(now);
                return;
            }
            else if (goalEffect.gameObject.activeSelf || !AllGatesLocked(stageGates))
            {
                Debug.LogError("Penguin tutorial smoke failed: world marker must stay hidden and quest barriers must start locked.");
                RequestPlayModeStop(now);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScreenshotPath) ?? "Assets/Game/InGame/Tutorial/Docs");
            ScreenCapture.CaptureScreenshot(ScreenshotPath, 1);
            Debug.Log("Penguin tutorial smoke passed: the click-driven comic opens the real Walk step with the top-left quest and hidden world marker.");
            RequestPlayModeStop(now);
        }

        private static bool AllGatesLocked(TutorialStageGate[] gates)
        {
            foreach (TutorialStageGate gate in gates)
                if (!gate.IsLocked) return false;
            return true;
        }

        private static void RequestPlayModeStop(double now)
        {
            _captureRequested = true;
            _stopAfterCaptureAt = now + 1d;
        }

        private static void ValidateLoadedScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("튜토리얼 씬을 열 수 없다.");

            int directors = CountInScene<PenguinTutorialDirector>(scene);
            int players = CountInScene<PenguinLocomotion>(scene);
            int stages = CountInScene<SnowCpuStage>(scene);
            int machines = CountInScene<SnowGiftMachinePresentation>(scene);
            int deliveryTerminals = CountInScene<GiftDeliveryTerminal>(scene);
            int warehouses = CountInScene<SnowballWarehouseStorage>(scene);
            int deliveryZones = CountInScene<GiftDropZone>(scene);
            int documents = CountInScene<UIDocument>(scene);
            int goalEffects = CountInScene<TutorialGoalEffect>(scene);
            int stageGates = CountInScene<TutorialStageGate>(scene);
            int comicCutscenes = CountInScene<TutorialComicCutscene>(scene);
            if (directors != 1 || players != 1 || stages != 1 || machines != 1 || deliveryTerminals != 2 || warehouses != 1 || deliveryZones != 1 || documents != 3 ||
                comicCutscenes != 2 ||
                goalEffects != 1 || stageGates != 5)
                throw new InvalidOperationException(
                    $"튜토리얼 씬 배선 수가 잘못됐다: director={directors}, player={players}, snow={stages}, " +
                    $"machine={machines}, delivery={deliveryTerminals}, warehouse={warehouses}, deliveryZones={deliveryZones}, ui={documents}, comics={comicCutscenes}, " +
                    $"goalVfx={goalEffects}, gates={stageGates}");

            PenguinTutorialDirector director = FindInScene<PenguinTutorialDirector>(scene);
            SnowGiftMachinePresentation machine = FindInScene<SnowGiftMachinePresentation>(scene);
            GiftDeliveryTerminal machineDeliveryTerminal = UnityEngine.Object.FindObjectsByType<GiftDeliveryTerminal>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(terminal => terminal.gameObject.scene == scene &&
                                            terminal.name == "GiftDeliveryTerminal_SnowMachine");
            GiftDeliveryTerminal warehouseEndpoint = UnityEngine.Object.FindObjectsByType<GiftDeliveryTerminal>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(terminal => terminal.gameObject.scene == scene &&
                                            terminal.name == "GiftDeliveryTerminal_Tutorial");
            SnowballWarehouseStorage warehouse = FindInScene<SnowballWarehouseStorage>(scene);
            GiftDropZone houseDeliveryZone = FindInScene<GiftDropZone>(scene);
            SnowCpuStage snowStage = FindInScene<SnowCpuStage>(scene);
            PenguinCameraOrbit cameraOrbit = FindInScene<PenguinCameraOrbit>(scene);
            TutorialGoalEffect goalEffect = FindInScene<TutorialGoalEffect>(scene);
            SerializedObject directorData = new SerializedObject(director);
            SerializedObject machineData = new SerializedObject(machine);
            if (directorData.FindProperty("_snowGiftMachine")?.objectReferenceValue != machine ||
                directorData.FindProperty("_giftDeliveryTerminal")?.objectReferenceValue != machineDeliveryTerminal ||
                directorData.FindProperty("_warehouseStorage")?.objectReferenceValue != warehouse ||
                directorData.FindProperty("_warehouseDeliveryTerminal")?.objectReferenceValue != warehouseEndpoint ||
                directorData.FindProperty("_houseDeliveryZone")?.objectReferenceValue != houseDeliveryZone ||
                directorData.FindProperty("_useQuestCycle")?.boolValue != true ||
                directorData.FindProperty("_useWorldMarker")?.boolValue != false ||
                directorData.FindProperty("_returnToMainMenuOnComplete")?.boolValue != true ||
                directorData.FindProperty("_snowballFailureVfx")?.objectReferenceValue == null ||
                directorData.FindProperty("_stageLights")?.arraySize != PenguinTutorialHud.StepCount ||
                directorData.FindProperty("_openingCutscene")?.objectReferenceValue == null ||
                directorData.FindProperty("_deliveryStoryCutscene")?.objectReferenceValue == null ||
                cameraOrbit == null ||
                machineData.FindProperty("_conversionStage")?.objectReferenceValue != snowStage)
                throw new InvalidOperationException("튜토리얼 Director, 눈 선물 기계, 배송 단말기 배선이 올바르지 않다.");

            SerializedObject snowData = new SerializedObject(snowStage);
            if (snowData.FindProperty("_gatherResidueMm")?.intValue > 180)
                throw new InvalidOperationException("튜토리얼 눈 패치가 첫 눈덩이 생성에 충분하지 않다.");
            SerializedObject cameraData = new SerializedObject(cameraOrbit);
            if (!Mathf.Approximately(cameraData.FindProperty("_distanceLow")?.floatValue ?? 0f, 4f) ||
                !Mathf.Approximately(cameraData.FindProperty("_heightLow")?.floatValue ?? 0f, 0.4f) ||
                !Mathf.Approximately(cameraData.FindProperty("_initialPitch")?.floatValue ?? 0f, 22f))
                throw new InvalidOperationException("튜토리얼 전용 카메라 프레이밍 값이 올바르지 않다.");

            foreach (TutorialComicCutscene comic in UnityEngine.Object.FindObjectsByType<TutorialComicCutscene>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (comic.gameObject.scene != scene) continue;
                SerializedObject comicData = new SerializedObject(comic);
                if (comicData.FindProperty("_showStartPrompt")?.boolValue == true)
                    throw new InvalidOperationException($"컷신이 START STORY 입력을 요구한다: {comic.name}");
                if (comicData.FindProperty("_dialogueLines")?.arraySize != comic.CardCount ||
                    comicData.FindProperty("_leftBubbleRevealFeedbacks")?.objectReferenceValue == null ||
                    comicData.FindProperty("_rightBubbleRevealFeedbacks")?.objectReferenceValue == null ||
                    comicData.FindProperty("_bubbleRevealSfx")?.objectReferenceValue == null ||
                    comicData.FindProperty("_magicRevealSfx")?.objectReferenceValue == null ||
                    comicData.FindProperty("_whooshRevealSfx")?.objectReferenceValue == null)
                    throw new InvalidOperationException($"컷신 말풍선 대사, FEEL 또는 SFX 연결이 올바르지 않다: {comic.name}");
            }

            foreach (UIDocument document in UnityEngine.Object.FindObjectsByType<UIDocument>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (document.gameObject.scene == scene && document.panelSettings == null)
                    throw new InvalidOperationException($"UIDocument PanelSettings가 비어 있다: {document.name}");
            UIDocument tutorialHud = UnityEngine.Object.FindObjectsByType<UIDocument>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(document => document.gameObject.scene == scene && document.name == "TutorialHUD");
            if (tutorialHud == null || tutorialHud.panelSettings == null ||
                tutorialHud.panelSettings.scaleMode != PanelScaleMode.ScaleWithScreenSize ||
                tutorialHud.panelSettings.referenceResolution != new Vector2Int(1920, 1080) ||
                tutorialHud.panelSettings.screenMatchMode != PanelScreenMatchMode.MatchWidthOrHeight ||
                !Mathf.Approximately(tutorialHud.panelSettings.match, 1f))
                throw new InvalidOperationException("튜토리얼 HUD 전용 1920×1080 세로 맞춤 PanelSettings가 올바르지 않다.");
            PenguinTutorialHud hud = tutorialHud.GetComponent<PenguinTutorialHud>();
            if (hud == null) throw new InvalidOperationException("튜토리얼 HUD 컴포넌트가 없다.");
            SerializedObject hudData = new SerializedObject(hud);
            if (hudData.FindProperty("_stepCompleteFeedbacks")?.objectReferenceValue == null ||
                hudData.FindProperty("_nextStepFeedbacks")?.objectReferenceValue == null)
                throw new InvalidOperationException("튜토리얼 HUD의 Feel 퀘스트 전환 피드백이 연결되지 않았다.");
            if (machineDeliveryTerminal == null || machineDeliveryTerminal.transform.parent == null ||
                machineDeliveryTerminal.transform.parent.name != "Room_Gift" ||
                machineDeliveryTerminal.transform.position.x >= -10f)
                throw new InvalidOperationException("택배 발송기가 6번 배송 구역 안에 배치되지 않았다.");
            if (Vector3.Dot(machineDeliveryTerminal.transform.forward, Vector3.back) < 0.99f ||
                machineDeliveryTerminal.EntryAnchor == null ||
                machineDeliveryTerminal.EntryAnchor.position.z <= machineDeliveryTerminal.transform.position.z)
                throw new InvalidOperationException("택배 발송기 입구가 벽이 아닌 열린 북쪽 바닥을 향하지 않는다.");
            Vector3 terminalScale = machineDeliveryTerminal.transform.localScale;
            if (!Mathf.Approximately(terminalScale.x, 0.65f) ||
                !Mathf.Approximately(terminalScale.y, 0.84f) ||
                !Mathf.Approximately(terminalScale.z, 0.70f))
                throw new InvalidOperationException("택배 발송기 축소 비율이 선물 통과 규격과 다르다.");
            if (warehouse == null || warehouseEndpoint == null ||
                !warehouseEndpoint.transform.IsChildOf(warehouse.transform) ||
                warehouseEndpoint.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled) ||
                warehouseEndpoint.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled))
                throw new InvalidOperationException("창고 또는 창고 내부의 숨은 배송 수신 엔드포인트가 올바르지 않다.");
            int visibleDeliveryTerminals = UnityEngine.Object.FindObjectsByType<GiftDeliveryTerminal>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(terminal => terminal.gameObject.scene == scene &&
                                   terminal.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled));
            if (visibleDeliveryTerminals != 1)
                throw new InvalidOperationException($"화면에 보이는 배송기는 한 대여야 한다: {visibleDeliveryTerminals}");
            if (goalEffect.gameObject.activeSelf)
                throw new InvalidOperationException("메인 메뉴 튜토리얼의 월드 화살표는 비활성 상태여야 한다.");

            SerializedObject goalData = new SerializedObject(goalEffect);
            if (goalData.FindProperty("_guideRiseParticles")?.objectReferenceValue == null ||
                goalData.FindProperty("_guideBillboardRoot")?.objectReferenceValue == null ||
                goalData.FindProperty("_guideBillboardRenderer")?.objectReferenceValue == null ||
                goalData.FindProperty("_guideLabelText")?.objectReferenceValue == null ||
                goalData.FindProperty("_screenGuideTexture")?.objectReferenceValue == null)
                throw new InvalidOperationException("플레이 화면용 목표 길라잡이 VFX 배선이 올바르지 않다.");
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/Game/InGame/Tutorial/VFX/T_TutorialGuideArrow.asset") == null ||
                AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Game/InGame/Tutorial/VFX/M_TutorialGuideArrow.mat") == null)
                throw new InvalidOperationException("목표 길라잡이 Material 또는 Texture 에셋이 없다.");

            int[] unlockSteps = UnityEngine.Object.FindObjectsByType<TutorialStageGate>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(gate => gate.gameObject.scene == scene)
                .Select(gate => gate.UnlockAfterStep)
                .OrderBy(step => step)
                .ToArray();
            if (!unlockSteps.SequenceEqual(new[] { 0, 1, 2, 3, 4 }))
                throw new InvalidOperationException(
                    $"튜토리얼 잠금문 단계가 잘못됐다: {string.Join(", ", unlockSteps)}");
            foreach (TutorialStageGate gate in UnityEngine.Object.FindObjectsByType<TutorialStageGate>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (gate.gameObject.scene != scene) continue;
                SerializedObject gateData = new SerializedObject(gate);
                if (gateData.FindProperty("_fieldPulseRoot")?.objectReferenceValue == null ||
                    gateData.FindProperty("_fieldRenderers")?.arraySize < 5 ||
                    gateData.FindProperty("_lockParticles")?.objectReferenceValue == null ||
                    gateData.FindProperty("_unlockParticles")?.objectReferenceValue == null)
                    throw new InvalidOperationException($"잠금벽 VFX 배선이 완전하지 않다: {gate.name}");
            }
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/Game/InGame/Tutorial/VFX/T_TutorialQuestBarrierGrid.asset") == null ||
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/Game/InGame/Tutorial/VFX/T_TutorialQuestBarrierSnowflake.asset") == null ||
                AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Game/InGame/Tutorial/VFX/M_TutorialQuestBarrier.mat") == null)
                throw new InvalidOperationException("반투명 서리막 Material 또는 Texture 에셋이 없다.");
            if (AssetDatabase.LoadAssetAtPath<Font>(
                    "Assets/Game/InGame/UI/MissionHUD/Fonts/Jua-Regular.ttf") == null)
                throw new InvalidOperationException("좌측 미션 카드용 한글 표시 폰트가 없다.");

            const string mainMenuPath = "Assets/Game/OutGame/UI/MainMenu/Scenes/MainMenu.unity";
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == mainMenuPath))
                throw new InvalidOperationException("튜토리얼 완료 후 돌아갈 MainMenu가 Build Settings에 없다.");

            string[] requiredRooms =
            {
                "Room_Walk", "Room_Run", "Room_Slide", "Room_Snowball", "Room_SnowMachine", "Room_Gift"
            };
            foreach (string requiredRoom in requiredRooms)
                if (!ContainsNamedObject(scene, requiredRoom))
                    throw new InvalidOperationException($"튜토리얼 훈련 공간이 없다: {requiredRoom}");

            Transform roomSigns = FindNamedTransform(scene, "RoomIdentitySigns");
            if (roomSigns == null || roomSigns.childCount != 6)
                throw new InvalidOperationException("튜토리얼 구간 표지판은 정확히 6개여야 한다.");
            foreach (Transform roomSign in roomSigns)
                if (roomSign.Find("PenguinActionIcon")?.GetComponent<MeshRenderer>()?.sharedMaterial?.mainTexture == null ||
                    roomSign.Find("TitleLabel")?.GetComponent<TextMesh>() == null ||
                    roomSign.Find("KeyBadge") == null ||
                    roomSign.Find("KeyLabel")?.GetComponent<TextMesh>() == null)
                    throw new InvalidOperationException($"펭귄 아이콘·구간명·조작 키가 빠진 표지판이 있다: {roomSign.name}");
                else
                {
                    MeshRenderer titleRenderer = roomSign.Find("TitleLabel").GetComponent<MeshRenderer>();
                    MeshRenderer keyRenderer = roomSign.Find("KeyLabel").GetComponent<MeshRenderer>();
                    if (titleRenderer.sharedMaterial?.shader?.name != "PPack/Tutorial/RoomSignTextDepth" ||
                        keyRenderer.sharedMaterial?.shader?.name != "PPack/Tutorial/RoomSignTextDepth" ||
                        titleRenderer.sortingOrder != 0 || keyRenderer.sortingOrder != 0)
                        throw new InvalidOperationException($"뒤쪽 글자가 벽을 뚫고 보일 수 있는 표지판이 있다: {roomSign.name}");
                }

            Transform snowballSign = roomSigns.Find("Sign_04_Snowball");
            Transform machineSign = roomSigns.Find("Sign_05_Machine");
            Vector3 snowballSignPosition = snowballSign?.Find("Backing")?.position ?? Vector3.positiveInfinity;
            Vector3 machineSignPosition = machineSign?.Find("Backing")?.position ?? Vector3.positiveInfinity;
            if (snowballSign == null || snowballSignPosition.z > 16.5f || snowballSignPosition.x < 19f ||
                machineSign == null || machineSignPosition.z > 16.5f || machineSignPosition.x < 4f ||
                machineSign.Find("PenguinActionIcon")?.position.x <= machineSign.Find("TitleLabel")?.position.x)
                throw new InvalidOperationException("눈덩이·기계 구간 표지판이 기둥 또는 기계에 가려지는 위치다.");

            string[] roomSignIconPaths =
            {
                "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Walk.png",
                "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Run.png",
                "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Slide.png",
                "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Snowball.png",
                "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Machine.png",
                "Assets/Game/InGame/Tutorial/UI/RoomSigns/Icons/T_RoomSign_Delivery.png"
            };
            foreach (string iconPath in roomSignIconPaths)
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath) == null)
                    throw new InvalidOperationException($"튜토리얼 구간 표지판 PNG가 없다: {iconPath}");

            string[] requiredIndoorParts =
            {
                "IndoorHall", "WorkshopWalls", "WorkshopColumns", "WorkshopWindows",
                "WorkshopCeiling", "WorkshopProps"
            };
            foreach (string requiredPart in requiredIndoorParts)
                if (!ContainsNamedObject(scene, requiredPart))
                    throw new InvalidOperationException($"실내 튜토리얼 구조물이 없다: {requiredPart}");

            Transform snowGround = FindNamedTransform(scene, "SnowGround");
            if (snowGround == null || snowGround.localScale.x > 48.1f || snowGround.localScale.z > 36.1f)
                throw new InvalidOperationException("메인 메뉴 튜토리얼 맵이 축소 기준 48×36m를 벗어났다.");

            ValidateQuestBoundaryContinuity(scene);

            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Game/InGame/Tutorial/UI/PenguinTutorial.uxml");
            if (visualTree == null) throw new InvalidOperationException("튜토리얼 UXML을 로드할 수 없다.");

            TemplateContainer tree = visualTree.CloneTree();
            string[] requiredNames =
            {
                "tutorial-card", "complete-card", "step-counter", "step-title", "key-shadow", "key-badge",
                "instruction", "progress-fill", "progress-text", "clear-badge", "step-dot-0", "step-dot-7"
            };
            foreach (string requiredName in requiredNames)
                if (tree.Q<VisualElement>(requiredName) == null)
                    throw new InvalidOperationException($"튜토리얼 UXML 필수 요소가 없다: {requiredName}");
        }

        private static int CountInScene<T>(Scene scene) where T : Component
        {
            int count = 0;
            foreach (T component in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include))
                if (component.gameObject.scene == scene) count++;
            return count;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (T component in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include))
                if (component.gameObject.scene == scene) return component;
            return null;
        }

        private static void ValidateQuestBoundaryContinuity(Scene scene)
        {
            AssertWallEdge(scene, "Room_Walk", "Divider_East_Lower", false, -14f, true);
            AssertWallEdge(scene, "Room_Walk", "Divider_East_Upper", false, -8f, false);
            AssertWallEdge(scene, "Room_Slide", "Divider_West_Lower", false, -14f, true);
            AssertWallEdge(scene, "Room_Slide", "Divider_West_Upper", false, -8f, false);
            AssertWallEdge(scene, "Room_Slide", "Divider_North_Left", true, 13f, true);
            AssertWallEdge(scene, "Room_Slide", "Divider_North_Right", true, 19f, false);
            AssertWallEdge(scene, "Room_Snowball", "Divider_West_Lower", false, 8f, true);
            AssertWallEdge(scene, "Room_Snowball", "Divider_West_Upper", false, 14f, false);
            AssertWallEdge(scene, "Room_Gift", "Divider_East_Lower", false, 8f, true);
            AssertWallEdge(scene, "Room_Gift", "Divider_East_Upper", false, 14f, false);
        }

        private static void AssertWallEdge(Scene scene, string roomName, string wallName,
            bool useX, float requiredEdge, bool requireMaximum)
        {
            Transform room = FindNamedTransform(scene, roomName);
            Renderer wall = room?.Find(wallName)?.GetComponent<Renderer>();
            if (wall == null) throw new InvalidOperationException($"퀘스트 경계 벽이 없다: {roomName}/{wallName}");

            float edge = useX
                ? (requireMaximum ? wall.bounds.max.x : wall.bounds.min.x)
                : (requireMaximum ? wall.bounds.max.z : wall.bounds.min.z);
            bool covered = requireMaximum ? edge >= requiredEdge - 0.02f : edge <= requiredEdge + 0.02f;
            if (!covered)
                throw new InvalidOperationException(
                    $"차단문 옆으로 우회 가능한 틈이 남았다: {roomName}/{wallName}, edge={edge:0.00}, required={requiredEdge:0.00}");
        }

        private static bool ContainsNamedObject(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (ContainsNamedObject(root.transform, objectName)) return true;
            return false;
        }

        private static Transform FindNamedTransform(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindNamedTransform(root.transform, objectName);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindNamedTransform(Transform current, string objectName)
        {
            if (current.name == objectName) return current;
            foreach (Transform child in current)
            {
                Transform found = FindNamedTransform(child, objectName);
                if (found != null) return found;
            }
            return null;
        }

        private static bool ContainsNamedObject(Transform current, string objectName)
        {
            if (current.name == objectName) return true;
            for (int index = 0; index < current.childCount; index++)
                if (ContainsNamedObject(current.GetChild(index), objectName)) return true;
            return false;
        }
    }
}

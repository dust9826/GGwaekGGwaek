using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    public enum EPenguinTutorialStep
    {
        Walk,
        Run,
        Slide,
        Snowball,
        SnowMachine,
        GiftDelivery,
        WarehousePickup,
        HouseDelivery,
        Complete
    }

    /// <summary>
    /// 실제 펭귄 컴포넌트가 만든 결과를 관찰해 튜토리얼을 순서대로 진행한다.
    /// 입력을 흉내 내거나 이동 규칙을 복제하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PenguinTutorialDirector : MonoBehaviour
    {
        private const float WalkDistanceM = 5f;
        private const float RunDistanceM = 7f;
        private const float SlideDistanceM = 3f;
        private const float SnowballDistanceM = 2.5f;
        private const float ReturnToMenuDelaySeconds = 3f;
        private const string MainMenuScenePath = "Assets/Game/OutGame/UI/MainMenu/Scenes/MainMenu.unity";

        [Header("플레이어")]
        [SerializeField] private Transform _player;
        [SerializeField] private PenguinInputReader _input;
        [SerializeField] private PenguinLocomotion _locomotion;
        [SerializeField] private PenguinSnowball _snowballControl;

        [Header("튜토리얼 오브젝트")]
        [SerializeField] private SnowCpuStage _snowStage;
        [SerializeField] private SnowGiftMachinePresentation _snowGiftMachine;
        [SerializeField] private GiftDeliveryTerminal _giftDeliveryTerminal;
        [SerializeField] private Transform _worldMarker;
        [SerializeField] private PenguinTutorialHud _hud;
        [SerializeField] private TutorialQuestJournal _questJournal;
        [SerializeField] private TutorialSantaGuide _santaGuide;
        [SerializeField] private TutorialComicCutscene _openingCutscene;
        [SerializeField] private TutorialComicCutscene _deliveryStoryCutscene;
        [SerializeField] private PenguinCameraOrbit _cameraOrbit;
        [SerializeField] private SnowballWarehouseStorage _warehouseStorage;
        [SerializeField] private GiftDeliveryTerminal _warehouseDeliveryTerminal;
        [SerializeField] private GiftDropZone _houseDeliveryZone;
        [SerializeField] private Light[] _stageLights;
        [SerializeField] private ParticleSystem _snowballFailureVfx;

        [Header("코스 좌표")]
        [SerializeField] private Vector3 _walkTarget = new Vector3(-25f, 0f, -18f);
        [SerializeField] private Vector3 _runTarget = new Vector3(5f, 0f, -23f);
        [SerializeField] private Vector3 _slideTarget = new Vector3(30f, 0f, 6f);
        [SerializeField] private Vector3 _snowballSpawn = new Vector3(29f, 0.25f, 22f);

        [Header("단계 공간")]
        [SerializeField] private Vector2 _walkRoomCenter = new Vector2(-31f, -22f);
        [SerializeField] private Vector2 _walkRoomSize = new Vector2(26f, 20f);
        [SerializeField] private Vector2 _runRoomCenter = new Vector2(-2f, -23f);
        [SerializeField] private Vector2 _runRoomSize = new Vector2(28f, 18f);
        [SerializeField] private Vector2 _slideRoomCenter = new Vector2(30f, -10f);
        [SerializeField] private Vector2 _slideRoomSize = new Vector2(28f, 44f);

        [Header("온보딩 표현")]
        [SerializeField] private bool _useVillageInstructions;
        [SerializeField] private bool _useQuestCycle;
        [SerializeField] private bool _useWorldMarker = true;
        [SerializeField] private bool _returnToMainMenuOnComplete;

        private EPenguinTutorialStep _step;
        private float _progress;
        private Vector3 _previousPlayerPosition;
        private SnowBallCarrier _tutorialBall;
        private Vector3 _ballStartPosition;
        private bool _ballTravelStarted;
        private bool _transitioning;
        private bool _returningToMenu;
        private Gift _tutorialGift;
        private TutorialGoalEffect _goalEffect;
        private TutorialStageGate[] _stageGates;
        private bool _tutorialStarted;
        private bool _giftDeliveryCompleted;
        private bool _giftIntakeCompleted;
        private bool _warehouseOutputCompleted;
        private float _warehouseOutputCompletedAt;
        private Gift _warehouseGift;
        private bool _warehouseGiftWasStored;
        private float _tutorialStartedAt;
        private float _stepStartedAt;
        private float _lastProgressAt;
        private float _lastObservedProgress;
        private int _hintStage;
        private int _stepRetryCount;
        private bool _snowballAttemptPending;
        private float _snowballAttemptAt;
        private bool _tutorialFinished;

        public EPenguinTutorialStep CurrentStep => _step;
        public float CurrentProgress01 => _step == EPenguinTutorialStep.Snowball
            ? (_tutorialBall == null ? 0f : 0.2f + Mathf.Clamp01(_progress / SnowballDistanceM) * 0.8f)
            : Mathf.Clamp01(_progress / RequiredProgress(_step));

        private void Awake()
        {
            if (_player == null && _locomotion != null) _player = _locomotion.transform;
            if (_giftDeliveryTerminal == null && _snowGiftMachine != null && _snowGiftMachine.GiftOutputAnchor != null)
                _giftDeliveryTerminal = FindClosestDeliveryTerminal(_snowGiftMachine.GiftOutputAnchor.position);
            if (_worldMarker != null) _goalEffect = _worldMarker.GetComponent<TutorialGoalEffect>();
            _stageGates = FindStageGates();
            bool hasProgressUi = _questJournal != null || _hud != null;
            bool hasQuestCycleReferences = !_useQuestCycle ||
                (_warehouseStorage != null && _warehouseDeliveryTerminal != null &&
                 _houseDeliveryZone != null);
            if (_player == null || _input == null || _locomotion == null || _snowballControl == null ||
                _snowStage == null || _snowGiftMachine == null || _giftDeliveryTerminal == null ||
                _snowGiftMachine.IntakeAnchor == null || _snowGiftMachine.GiftOutputAnchor == null ||
                _giftDeliveryTerminal.EntryAnchor == null ||
                _worldMarker == null || !hasProgressUi || !hasQuestCycleReferences ||
                _openingCutscene == null || _deliveryStoryCutscene == null ||
                _goalEffect == null || (!_useQuestCycle && _stageGates.Length < 5))
            {
                Debug.LogError("PenguinTutorialDirector: 튜토리얼 참조가 완전하지 않다.", this);
                enabled = false;
                return;
            }

            _previousPlayerPosition = _player.position;
            SetCompletedStageGates(-1);
            _input.enabled = false;
            if (_cameraOrbit != null) _cameraOrbit.enabled = false;
            if (_hud != null) _hud.gameObject.SetActive(false);
            if (_questJournal != null) _questJournal.gameObject.SetActive(false);
            if (_santaGuide != null) _santaGuide.gameObject.SetActive(false);
            SetWorldMarkerVisible(false);
            _giftDeliveryTerminal.GiftIntakeCompleted += HandleGiftIntakeCompleted;
            if (_warehouseDeliveryTerminal != null)
                _warehouseDeliveryTerminal.GiftOutputCompleted += HandleWarehouseGiftOutputCompleted;
        }

        private void OnDestroy()
        {
            if (_giftDeliveryTerminal != null)
                _giftDeliveryTerminal.GiftIntakeCompleted -= HandleGiftIntakeCompleted;
            if (_warehouseDeliveryTerminal != null)
                _warehouseDeliveryTerminal.GiftOutputCompleted -= HandleWarehouseGiftOutputCompleted;
            if (_tutorialStarted && !_tutorialFinished) RecordMetric("tutorial_abandoned");
        }

        private IEnumerator Start()
        {
            yield return _openingCutscene.PlayAndWait();
            _input.enabled = true;
            if (_cameraOrbit != null) _cameraOrbit.enabled = true;
            if (_hud != null) _hud.gameObject.SetActive(true);
            if (_questJournal != null) _questJournal.gameObject.SetActive(true);
            if (_santaGuide != null) _santaGuide.gameObject.SetActive(true);
            SetWorldMarkerVisible(true);

            // SnowCpuStage와 UIDocument가 Awake를 마친 뒤 첫 목표를 연다.
            yield return null;
            _tutorialStarted = true;
            _tutorialStartedAt = Time.unscaledTime;
            _stepStartedAt = _tutorialStartedAt;
            RecordMetric("tutorial_started");
            BeginStep(EPenguinTutorialStep.Walk);
        }

        private void Update()
        {
            if (!_tutorialStarted || _transitioning || _step == EPenguinTutorialStep.Complete) return;

            float travelled = HorizontalDistance(_previousPlayerPosition, _player.position);
            _previousPlayerPosition = _player.position;
            // 리스폰이나 에디터 이동을 진행도로 오인하지 않는다.
            travelled = Mathf.Min(travelled, 0.75f);

            bool insideStepRoom = IsInsideStepRoom(_step, _player.position);
            switch (_step)
            {
                case EPenguinTutorialStep.Walk:
                    if (insideStepRoom && !_input.SprintHeld && !_locomotion.UsesSlidingLocomotion && _locomotion.Speed > 0.35f)
                        _progress += travelled;
                    break;

                case EPenguinTutorialStep.Run:
                    if (insideStepRoom && _input.SprintHeld && !_locomotion.UsesSlidingLocomotion &&
                        _locomotion.Speed > _locomotion.WalkSpeedMps + 0.35f)
                        _progress += travelled;
                    break;

                case EPenguinTutorialStep.Slide:
                    if (insideStepRoom && _locomotion.IsSliding && _locomotion.Speed > 1f) _progress += travelled;
                    break;

                case EPenguinTutorialStep.Snowball:
                    TickSnowballProgress();
                    break;

                case EPenguinTutorialStep.SnowMachine:
                    TickSnowMachineProgress();
                    break;

                case EPenguinTutorialStep.GiftDelivery:
                    TickGiftDeliveryProgress();
                    break;

                case EPenguinTutorialStep.WarehousePickup:
                    TickWarehousePickupProgress();
                    break;

                case EPenguinTutorialStep.HouseDelivery:
                    TickHouseDeliveryProgress();
                    break;
            }

            TickInactivityGuidance();

            SetProgressUi(CurrentProgress01, ProgressText());
            if (_progress >= RequiredProgress(_step)) StartCoroutine(AdvanceAfterSuccess());
        }

        private void TickSnowballProgress()
        {
            SnowBallCarrier held = _snowballControl.Held;
            if (held != null && held != _tutorialBall)
            {
                _tutorialBall = held;
                _ballStartPosition = held.transform.position;
                _ballTravelStarted = true;
                _snowballAttemptPending = false;
                if (_hud != null) _hud.SetKeyAttention(false);
                if (_snowballFailureVfx != null)
                    _snowballFailureVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_input.CreateSnowballPressedThisFrame && held == null)
            {
                _snowballAttemptPending = true;
                _snowballAttemptAt = Time.unscaledTime;
            }
            if (_snowballAttemptPending && Time.unscaledTime - _snowballAttemptAt > 0.4f)
            {
                _snowballAttemptPending = false;
                if (_snowballControl.Held == null)
                {
                    _stepRetryCount++;
                    if (_hud != null)
                    {
                        _hud.SetInstruction("파란 눈 패치 중앙에 서서 E를 다시 누르세요. 눈이 충분한 곳에서만 뭉쳐집니다.");
                        _hud.SetKeyAttention(true);
                    }
                    if (_snowballFailureVfx != null)
                    {
                        _snowballFailureVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        _snowballFailureVfx.Play(true);
                    }
                    RecordMetric("snowball_create_failed");
                }
            }

            if (_tutorialBall == null) return;
            if (UsesWorldMarker) _goalEffect.MoveTo(Grounded(_tutorialBall.transform.position));

            if (UsesWorldMarker && held == _tutorialBall && _goalEffect.IsGrabPromptVisible)
                _goalEffect.HideGrabPrompt();

            if (!_ballTravelStarted && held == _tutorialBall)
            {
                _ballStartPosition = _tutorialBall.transform.position;
                _ballTravelStarted = true;
            }

            if (_ballTravelStarted)
                _progress = HorizontalDistance(_ballStartPosition, _tutorialBall.transform.position);
        }

        private IEnumerator AdvanceAfterSuccess()
        {
            _transitioning = true;
            RecordMetric("step_completed", _tutorialGift);
            ShowCurrentStepSuccess();
            yield return new WaitForSeconds(1f);

            if (_step == EPenguinTutorialStep.SnowMachine)
                yield return PlayDeliveryStoryCutscene();

            EPenguinTutorialStep next = NextStep(_step);
            BeginStep(next);
            _transitioning = false;
        }

        private IEnumerator PlayDeliveryStoryCutscene()
        {
            _input.enabled = false;
            if (_cameraOrbit != null) _cameraOrbit.enabled = false;
            if (_hud != null) _hud.gameObject.SetActive(false);
            if (_questJournal != null) _questJournal.gameObject.SetActive(false);
            if (_santaGuide != null) _santaGuide.gameObject.SetActive(false);
            SetWorldMarkerVisible(false);

            yield return _deliveryStoryCutscene.PlayAndWait();

            _input.enabled = true;
            if (_cameraOrbit != null) _cameraOrbit.enabled = true;
            if (_hud != null) _hud.gameObject.SetActive(true);
            if (_questJournal != null) _questJournal.gameObject.SetActive(true);
            if (_santaGuide != null) _santaGuide.gameObject.SetActive(true);
            SetWorldMarkerVisible(true);
        }

        private EPenguinTutorialStep NextStep(EPenguinTutorialStep current)
        {
            if (current == EPenguinTutorialStep.GiftDelivery && !_useQuestCycle)
                return EPenguinTutorialStep.Complete;
            return (EPenguinTutorialStep)((int)current + 1);
        }

        private void BeginStep(EPenguinTutorialStep step)
        {
            _step = step;
            _progress = 0f;
            _previousPlayerPosition = _player.position;
            _stepStartedAt = Time.unscaledTime;
            _lastProgressAt = _stepStartedAt;
            _lastObservedProgress = 0f;
            _hintStage = 0;
            _stepRetryCount = 0;
            _snowballAttemptPending = false;
            if (_hud != null) _hud.SetKeyAttention(false);
            SetStageLighting(step);

            switch (step)
            {
                case EPenguinTutorialStep.Walk:
                    SetMarker(_walkTarget, "여기까지 걸어가세요");
                    ShowStepUi(0, "걸어 보기", "WASD", _useVillageInstructions
                        ? "마을 입구의 눈길을 따라 5m 걸어 보세요."
                        : "파란 WALK 방에서 5m 걸어 보세요.");
                    break;

                case EPenguinTutorialStep.Run:
                    SetMarker(_runTarget, "여기까지 달려가세요");
                    ShowStepUi(1, "달려 보기", "SHIFT + WASD", _useVillageInstructions
                        ? "가로등을 지나 Shift를 누른 채 7m 달리세요."
                        : "초록 RUN 방에서 Shift를 누른 채 7m 달리세요.");
                    break;

                case EPenguinTutorialStep.Slide:
                    SetMarker(_slideTarget, "이 방향으로 슬라이딩");
                    ShowStepUi(2, "미끄러지기", "SHIFT + SPACE", _useVillageInstructions
                        ? "넓은 삼거리에서 Shift + Space로 3m 활강하세요."
                        : "주황 SLIDE 방에서 Shift + Space로 3m 활강하세요.");
                    break;

                case EPenguinTutorialStep.Snowball:
                    PrepareSnowball();
                    ShowStepUi(3, "눈덩이 만들고 굴리기", "E  →  W / A D",
                        "파란 눈 패치에서 E로 눈덩이를 만든 뒤, W와 A/D로 2.5m 굴리세요.");
                    break;

                case EPenguinTutorialStep.SnowMachine:
                    PrepareSnowMachine();
                    ShowStepUi(4, "기계에 눈 넣기", "E  →  W", _useVillageInstructions
                        ? "눈덩이를 제작소의 파란 투입구 안까지 밀어 넣으세요."
                        : "눈덩이를 중앙 기계의 파란 투입구 안까지 밀어 넣으세요.");
                    break;

                case EPenguinTutorialStep.GiftDelivery:
                    PrepareGiftDelivery();
                    ShowStepUi(5, "택배 보내기", "W / A D", "완성된 선물을 발로 밀어 우편 단말기 경사로 안까지 넣으세요.");
                    break;

                case EPenguinTutorialStep.WarehousePickup:
                    PrepareWarehousePickup();
                    ShowStepUi(6, "창고에서 택배 꺼내기", "W / A D", "도착한 색상 칸의 상자를 진열대 밖으로 밀어 꺼내세요.");
                    break;

                case EPenguinTutorialStep.HouseDelivery:
                    PrepareHouseDelivery();
                    ShowStepUi(7, "옆집에 배달하기", "W / A D", "꺼낸 상자를 옆 배송처의 초록 납품 패드까지 밀어 주세요.");
                    break;

                case EPenguinTutorialStep.Complete:
                    SetCompletedStageGates((int)EPenguinTutorialStep.HouseDelivery);
                    SetWorldMarkerVisible(false);
                    ShowCompleteUi();
                    if (_santaGuide != null)
                        _santaGuide.ShowComplete("호호호! 눈을 선물로 만들고 이웃에게 전하는 일까지, 이제 혼자서도 해낼 수 있겠구나.");
                    _tutorialFinished = true;
                    RecordMetric("tutorial_completed");
                    if ((_returnToMainMenuOnComplete || !_useQuestCycle) && !_returningToMenu)
                        StartCoroutine(ReturnToMainMenu());
                    break;
            }

            if (step != EPenguinTutorialStep.Complete) RecordMetric("step_started");

            if (_useQuestCycle && step != EPenguinTutorialStep.Complete)
                ShowSantaGuide(step);

            SetProgressUi(CurrentProgress01, ProgressText());
        }

        private void ShowSantaGuide(EPenguinTutorialStep step)
        {
            if (_santaGuide == null) return;
            switch (step)
            {
                case EPenguinTutorialStep.Walk:
                    _santaGuide.ShowStep(0, "마을길을 걸어 보자",
                        "호호, 우선 천천히 몸을 풀어 보렴. WASD로 앞길을 따라 걸어 보자꾸나.",
                        "반짝이는 표식을 향해 WASD로 조금만 더 걸어 보렴.");
                    break;
                case EPenguinTutorialStep.Run:
                    _santaGuide.ShowStep(1, "이번에는 힘껏 달려 보자",
                        "시간은 금이지! Shift를 누른 채 달리면 마을을 더 빠르게 오갈 수 있단다.",
                        "Shift를 계속 누르고 WASD로 달리면 된단다.");
                    break;
                case EPenguinTutorialStep.Slide:
                    _santaGuide.ShowStep(2, "눈길에서는 미끄러져 보렴",
                        "달리다가 Space를 누르면 매끄러운 눈 위를 시원하게 활강할 수 있지.",
                        "Shift로 달리면서 Space! 넓은 삼거리에서 해 보렴.");
                    break;
                case EPenguinTutorialStep.Snowball:
                    _santaGuide.ShowStep(3, "깨끗한 눈을 모으자",
                        "눈 위에서 E를 눌러 직접 눈덩이를 만든 뒤 앞으로 밀어 보렴. 굴릴수록 단단해진단다.",
                        "눈이 쌓인 곳에서 E를 누른 뒤 W로 새 눈덩이를 밀어 보렴.");
                    break;
                case EPenguinTutorialStep.SnowMachine:
                    _santaGuide.ShowStep(4, "제작기에 눈을 넣어 주게",
                        "좋아, 그 눈덩이를 중앙 제작기의 파란 투입구까지 밀어 넣어 보렴.",
                        "반짝이는 표식이 가리키는 파란 입구 안쪽까지 밀어야 한단다.");
                    break;
                case EPenguinTutorialStep.GiftDelivery:
                    _santaGuide.ShowStep(5, "완성된 선물을 보내자",
                        "선물이 나왔구나! 들 수는 없으니 우편 단말기까지 발로 조심히 밀어 보내렴.",
                        "선물 상자를 독립된 우편 단말기의 경사로 위로 밀어 올려 보렴.");
                    break;
                case EPenguinTutorialStep.WarehousePickup:
                    _santaGuide.ShowStep(6, "외곽 창고에서 찾아보자",
                        "택배는 색깔에 맞는 창고 칸으로 도착했을 게야. 진열칸에서 밖으로 꺼내 보렴.",
                        "맵 가장자리의 창고로 가서 같은 색 상자를 진열칸 밖으로 밀어 보렴.");
                    break;
                case EPenguinTutorialStep.HouseDelivery:
                    _santaGuide.ShowStep(7, "마지막 배송이란다",
                        "이제 창고 옆집의 표시된 앞마당까지 상자를 밀어 주면 한 사이클이 끝난단다.",
                        "창고 옆집 앞의 작은 납품 표식 안에 상자를 놓아 보렴.");
                    break;
            }
        }

        private void PrepareSnowball()
        {
            // 이 단계의 핵심은 준비된 공을 잡는 것이 아니라 실제 플레이 규칙대로
            // 발밑의 눈을 E로 뭉쳐 새 눈덩이를 만드는 것이다.
            _tutorialBall = null;
            _ballTravelStarted = false;
            SetMarker(_snowballSpawn, "눈 위에서 E로 눈덩이를 만드세요");
        }

        private void PrepareGiftDelivery()
        {
            _giftDeliveryCompleted = false;
            _giftIntakeCompleted = false;
            _warehouseOutputCompleted = false;
            _warehouseOutputCompletedAt = 0f;
            _warehouseGift = null;
            _warehouseGiftWasStored = false;
            _tutorialGift = _snowGiftMachine.LastSpawnedGift;
            if (_tutorialGift == null)
                _tutorialGift = FindClosestGift(_snowGiftMachine.GiftOutputAnchor.position);
            SetMarker(_giftDeliveryTerminal.EntryAnchor.position, "선물을 이곳으로 보내세요");
        }

        private void PrepareWarehousePickup()
        {
            _warehouseGiftWasStored = _warehouseGift != null && _warehouseStorage.ContainsGift(_warehouseGift);
            if (_warehouseGift != null)
                SetMarker(_warehouseGift.transform.position, "도착한 선물을 꺼내세요");
            else
                SetMarker(_warehouseStorage.transform.position, "택배 창고로 이동하세요");
        }

        private void PrepareHouseDelivery()
        {
            SetMarker(_houseDeliveryZone.transform.position, "이웃집에 배달하세요");
        }

        private void PrepareSnowMachine()
        {
            _tutorialGift = null;
            SetMarker(_snowGiftMachine.IntakeAnchor.position, "눈덩이를 투입구에 넣으세요");
            if (UsesWorldMarker) _goalEffect.HideGrabPrompt();
        }

        private void TickSnowMachineProgress()
        {
            Gift produced = _snowGiftMachine.LastSpawnedGift;
            if (produced != null && produced.isActiveAndEnabled)
            {
                _tutorialGift = produced;
                _progress = 1f;
                if (UsesWorldMarker) _goalEffect.MoveTo(Grounded(produced.transform.position));
                return;
            }

            Transform target = _snowGiftMachine.IsProcessing
                ? _snowGiftMachine.GiftOutputAnchor
                : _snowGiftMachine.IntakeAnchor;
            if (UsesWorldMarker) _goalEffect.MoveTo(Grounded(target.position));
            _progress = _snowGiftMachine.IsProcessing ? 0.65f : 0f;
        }

        private void TickGiftDeliveryProgress()
        {
            if (UsesWorldMarker) _goalEffect.MoveTo(Grounded(_giftDeliveryTerminal.EntryAnchor.position));
            if (_giftDeliveryCompleted)
            {
                _progress = 1f;
                return;
            }

            if (_warehouseOutputCompleted)
            {
                _progress = 0.98f;
                if (Time.unscaledTime - _warehouseOutputCompletedAt >= 0.5f)
                {
                    _giftDeliveryCompleted = true;
                    _progress = 1f;
                }
                return;
            }
            if (_giftIntakeCompleted)
            {
                _progress = 0.92f;
                return;
            }

            if (_tutorialGift == null || !_tutorialGift.isActiveAndEnabled)
                _tutorialGift = FindClosestGift(_snowGiftMachine.GiftOutputAnchor.position);
            if (_tutorialGift == null)
            {
                _progress = 0f;
                return;
            }

            float distance = HorizontalDistance(
                _tutorialGift.transform.position, _giftDeliveryTerminal.TunnelAnchor.position);
            _progress = Mathf.InverseLerp(5.5f, 0.45f, distance) * 0.9f;
        }

        private void TickWarehousePickupProgress()
        {
            if (_warehouseGift == null || !_warehouseGift.isActiveAndEnabled)
                _warehouseGift = FindClosestGift(_warehouseStorage.transform.position, 16f);

            if (_warehouseGift == null)
            {
                if (UsesWorldMarker) _goalEffect.MoveTo(Grounded(_warehouseStorage.transform.position));
                _progress = 0.08f;
                return;
            }

            bool stored = _warehouseStorage.ContainsGift(_warehouseGift);
            if (UsesWorldMarker) _goalEffect.MoveTo(Grounded(_warehouseGift.transform.position));
            if (stored)
            {
                _warehouseGiftWasStored = true;
                _progress = 0.55f;
                return;
            }

            if (_warehouseGiftWasStored)
            {
                _progress = 1f;
                return;
            }

            float distance = HorizontalDistance(_warehouseGift.transform.position, _warehouseStorage.transform.position);
            _progress = Mathf.InverseLerp(16f, 3f, distance) * 0.45f;
        }

        private void TickHouseDeliveryProgress()
        {
            if (UsesWorldMarker) _goalEffect.MoveTo(Grounded(_houseDeliveryZone.transform.position));
            if (_warehouseGift == null || !_warehouseGift.isActiveAndEnabled)
            {
                _progress = 0f;
                return;
            }

            if (!_warehouseGift.IsCarried && _houseDeliveryZone.Contains(_warehouseGift.transform.position))
            {
                _progress = 1f;
                return;
            }

            float distance = HorizontalDistance(
                _warehouseGift.transform.position, _houseDeliveryZone.transform.position);
            _progress = Mathf.InverseLerp(18f, 1.5f, distance) * 0.92f;
        }

        private void HandleGiftIntakeCompleted(GiftDeliveryTerminal terminal)
        {
            if (_step != EPenguinTutorialStep.GiftDelivery || terminal != _giftDeliveryTerminal) return;
            _giftIntakeCompleted = true;
            RecordMetric("gift_sent", _tutorialGift);
        }

        private void HandleWarehouseGiftOutputCompleted(GiftDeliveryTerminal terminal, Gift gift)
        {
            if (terminal != _warehouseDeliveryTerminal || gift == null) return;
            _warehouseGift = gift;
            _warehouseGiftWasStored = false;
            RecordMetric("warehouse_output", gift);
            if (_step == EPenguinTutorialStep.GiftDelivery)
            {
                _warehouseOutputCompleted = true;
                _warehouseOutputCompletedAt = Time.unscaledTime;
            }
        }

        private Gift FindClosestGift(Vector3 around, float searchDistance = 12f)
        {
            Gift best = null;
            float bestDistance = searchDistance;
            foreach (Gift gift in Gift.All)
            {
                if (gift == null || !gift.isActiveAndEnabled || gift.gameObject.scene != gameObject.scene) continue;
                float distance = HorizontalDistance(around, gift.transform.position);
                if (distance >= bestDistance) continue;
                best = gift;
                bestDistance = distance;
            }
            return best;
        }

        private GiftDeliveryTerminal FindClosestDeliveryTerminal(Vector3 around)
        {
            GiftDeliveryTerminal best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (GiftDeliveryTerminal terminal in FindObjectsByType<GiftDeliveryTerminal>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (terminal.gameObject.scene != gameObject.scene || terminal.EntryAnchor == null) continue;
                float distance = HorizontalDistance(around, terminal.EntryAnchor.position);
                if (distance >= bestDistance) continue;
                best = terminal;
                bestDistance = distance;
            }
            return best;
        }

        private IEnumerator ReturnToMainMenu()
        {
            _returningToMenu = true;
            yield return new WaitForSeconds(ReturnToMenuDelaySeconds);
            SceneManager.LoadScene(MainMenuScenePath, LoadSceneMode.Single);
        }

        private SnowBallCarrier FindClosestTutorialBall(Vector3 around)
        {
            SnowBallCarrier best = null;
            float bestDistance = 4f;
            foreach (SnowBallCarrier ball in FindObjectsByType<SnowBallCarrier>())
            {
                if (ball.gameObject.scene != gameObject.scene) continue;
                float distance = HorizontalDistance(around, ball.transform.position);
                if (distance >= bestDistance) continue;
                best = ball;
                bestDistance = distance;
            }
            return best;
        }

        private void SetMarker(Vector3 worldPosition, string guideLabel = null)
        {
            if (!UsesWorldMarker) return;
            if (!string.IsNullOrWhiteSpace(guideLabel)) _goalEffect.SetGuideLabel(guideLabel);
            _goalEffect.ShowPending(Grounded(worldPosition));
        }

        private void ShowCurrentStepSuccess()
        {
            if (_questJournal != null) _questJournal.ShowSuccess();
            else if (_hud != null) _hud.ShowSuccess();
            if (UsesWorldMarker) _goalEffect.ShowSuccess();
            SetCompletedStageGates((int)_step);
        }

        private bool UsesWorldMarker => _useWorldMarker && _worldMarker != null && _goalEffect != null;

        private void SetWorldMarkerVisible(bool visible)
        {
            if (_worldMarker != null) _worldMarker.gameObject.SetActive(visible && _useWorldMarker);
        }

        private void ShowStepUi(int index, string title, string key, string instruction)
        {
            if (_questJournal != null) _questJournal.ShowStep(index, title, key, instruction);
            else if (_hud != null) _hud.ShowStep(index, title, key, instruction);
        }

        private void SetProgressUi(float progress01, string text)
        {
            if (_questJournal != null) _questJournal.SetProgress(progress01, text);
            else if (_hud != null) _hud.SetProgress(progress01, text);
        }

        private void TickInactivityGuidance()
        {
            float progress = CurrentProgress01;
            if (progress > _lastObservedProgress + 0.002f)
            {
                _lastObservedProgress = progress;
                _lastProgressAt = Time.unscaledTime;
                if (_hud != null) _hud.SetKeyAttention(false);
                return;
            }

            if (_step != EPenguinTutorialStep.Snowball) return;
            float idleSeconds = Time.unscaledTime - _lastProgressAt;
            if (_hintStage < 1 && idleSeconds >= 8f)
            {
                _hintStage = 1;
                if (_hud != null)
                    _hud.SetInstruction("눈이 쌓인 바닥 위에서 E를 누르면 새 눈덩이가 생깁니다.");
                RecordMetric("step_hint_shown");
            }
            if (_hintStage < 2 && idleSeconds >= 18f)
            {
                _hintStage = 2;
                if (_hud != null)
                {
                    _hud.SetInstruction("눈 위에 서서 E → 눈덩이가 생기면 W로 앞으로 밀어 보세요.");
                    _hud.SetKeyAttention(true);
                }
                RecordMetric("step_hint_shown");
            }
        }

        private void SetStageLighting(EPenguinTutorialStep step)
        {
            if (_stageLights == null) return;
            int activeIndex = Mathf.Clamp((int)step, 0, Mathf.Max(0, _stageLights.Length - 1));
            for (int index = 0; index < _stageLights.Length; index++)
            {
                Light stageLight = _stageLights[index];
                if (stageLight == null) continue;
                stageLight.intensity = step != EPenguinTutorialStep.Complete && index == activeIndex
                    ? 32f
                    : 17f;
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void RecordMetric(string eventName, Gift gift = null)
        {
            float now = Time.unscaledTime;
            int tutorialMs = Mathf.Max(0, Mathf.RoundToInt((now - _tutorialStartedAt) * 1000f));
            int stepMs = Mathf.Max(0, Mathf.RoundToInt((now - _stepStartedAt) * 1000f));
            string giftKind = gift != null ? gift.Kind.ToString() : string.Empty;
            Debug.Log($"[TutorialMetric] event={eventName} step={_step} tutorial_ms={tutorialMs} " +
                      $"step_ms={stepMs} retries={_stepRetryCount} gift_kind={giftKind}", this);
        }

        private void ShowCompleteUi()
        {
            if (_questJournal != null) _questJournal.ShowComplete();
            else if (_hud != null) _hud.ShowComplete();
        }

        private void SetCompletedStageGates(int completedStep)
        {
            foreach (TutorialStageGate gate in _stageGates)
                if (gate != null) gate.SetCompletedStep(completedStep);
        }

        private TutorialStageGate[] FindStageGates()
        {
            TutorialStageGate[] allGates = FindObjectsByType<TutorialStageGate>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var sceneGates = new List<TutorialStageGate>(allGates.Length);
            foreach (TutorialStageGate gate in allGates)
                if (gate.gameObject.scene == gameObject.scene) sceneGates.Add(gate);
            return sceneGates.ToArray();
        }

        private string ProgressText()
        {
            return _step switch
            {
                EPenguinTutorialStep.Walk => $"{Mathf.Min(_progress, WalkDistanceM):0.0} / {WalkDistanceM:0} m",
                EPenguinTutorialStep.Run => $"{Mathf.Min(_progress, RunDistanceM):0.0} / {RunDistanceM:0} m",
                EPenguinTutorialStep.Slide => $"{Mathf.Min(_progress, SlideDistanceM):0.0} / {SlideDistanceM:0} m",
                EPenguinTutorialStep.Snowball => _tutorialBall == null
                    ? "눈 위에서 E로 눈덩이 만들기"
                    : $"만들기 완료 · {Mathf.Min(_progress, SnowballDistanceM):0.0} / {SnowballDistanceM:0.0} m",
                EPenguinTutorialStep.SnowMachine => _progress >= 1f
                    ? "선물 완성!"
                    : _snowGiftMachine.IsProcessing ? "선물 만드는 중..." : "눈덩이를 투입구로 밀기",
                EPenguinTutorialStep.GiftDelivery => _progress >= 1f
                    ? "창고 도착 확인!"
                    : _giftIntakeCompleted ? "배송 중..." : "선물을 우편 단말기로 밀기",
                EPenguinTutorialStep.WarehousePickup => _progress >= 1f
                    ? "창고에서 꺼냈어요!"
                    : _warehouseGiftWasStored ? "상자를 진열칸 밖으로 밀기" : "택배가 창고로 이동 중...",
                EPenguinTutorialStep.HouseDelivery => _progress >= 1f
                    ? "옆집 배송 완료!"
                    : "상자를 표시된 앞마당까지 밀기",
                _ => string.Empty
            };
        }

        private static float RequiredProgress(EPenguinTutorialStep step)
        {
            return step switch
            {
                EPenguinTutorialStep.Walk => WalkDistanceM,
                EPenguinTutorialStep.Run => RunDistanceM,
                EPenguinTutorialStep.Slide => SlideDistanceM,
                EPenguinTutorialStep.Snowball => SnowballDistanceM,
                EPenguinTutorialStep.SnowMachine => 1f,
                EPenguinTutorialStep.GiftDelivery => 1f,
                EPenguinTutorialStep.WarehousePickup => 1f,
                EPenguinTutorialStep.HouseDelivery => 1f,
                _ => 1f
            };
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            Vector2 delta = new Vector2(a.x - b.x, a.z - b.z);
            return delta.magnitude;
        }

        private bool IsInsideStepRoom(EPenguinTutorialStep step, Vector3 worldPosition)
        {
            Vector2 center;
            Vector2 size;
            switch (step)
            {
                case EPenguinTutorialStep.Walk:
                    center = _walkRoomCenter;
                    size = _walkRoomSize;
                    break;
                case EPenguinTutorialStep.Run:
                    center = _runRoomCenter;
                    size = _runRoomSize;
                    break;
                case EPenguinTutorialStep.Slide:
                    center = _slideRoomCenter;
                    size = _slideRoomSize;
                    break;
                default:
                    return true;
            }

            Vector2 offset = new Vector2(worldPosition.x, worldPosition.z) - center;
            return Mathf.Abs(offset.x) <= size.x * 0.5f && Mathf.Abs(offset.y) <= size.y * 0.5f;
        }

        private static Vector3 Grounded(Vector3 position) => new Vector3(position.x, 0.04f, position.z);
    }
}

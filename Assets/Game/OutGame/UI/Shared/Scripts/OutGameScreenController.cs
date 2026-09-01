using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    [RequireComponent(typeof(UIDocument), typeof(AudioSource))]
    public sealed class OutGameScreenController : MonoBehaviour
    {
        private const string UiSoundPreference = "PPack.UiSoundEnabled";
        private const string ViewButtonPrefix = "view-";
        private const string ViewElementPrefix = "view-";
        private const string DefaultNickname = "PENGUIN";
        private const string SelectedStagePreference = "PPack.SelectedStage";
        private const string WinterVillageStageId = "winter-village";
        private const string TutorialScenePath = "Assets/Game/InGame/Tutorial/Scenes/PenguinTutorial.unity";
        private static readonly char[] RoomCodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

        [SerializeField] private AudioClip _hoverClip;
        [SerializeField, Range(0f, 1f)] private float _hoverVolume = 0.28f;
        [SerializeField] private AudioClip _clickClip;
        [SerializeField, Range(0f, 1f)] private float _clickVolume = 0.42f;
        [SerializeField] private AudioClip _navigationClip;
        [SerializeField, Range(0f, 1f)] private float _navigationVolume = 0.24f;
        [SerializeField] private AudioClip _confirmClip;
        [SerializeField, Range(0f, 1f)] private float _confirmVolume = 0.34f;

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<VisualElement> _floatShapes = new List<VisualElement>();
        private readonly List<VisualElement> _twinkles = new List<VisualElement>();
        private readonly List<Tween> _ambientTweens = new List<Tween>();
        private readonly Dictionary<string, VisualElement> _views = new Dictionary<string, VisualElement>();

        private static string _roomCode;
        private static string _nickname;
        private static bool _isHost;

        /// <summary>
        /// 로비 화면이 떠 있는 동안만 인원수를 폴링한다. <see cref="SessionLobby"/> 의 <c>[Networked]</c>
        /// 상태는 변경 알림이 없어서 매 프레임 읽는 쪽이 단순하고, 로비를 벗어나면 읽을 것이 없다.
        /// </summary>
        private bool _lobbyPollUntilPhaseChanges;

        /// <summary>로비 슬롯 폴링용 재사용 버퍼. 매 프레임 새로 만들지 않는다.</summary>
        private SessionLobby.LobbySlot[] _lobbySlots;

        private VisualElement _root;
        private VisualElement _activeView;
        private VisualElement _effectLayer;
        private AudioSource _audioSource;
        private Sequence _intro;
        private float _nextHoverSoundTime;
        private float _nextHoverEffectTime;
        private bool _soundEnabled;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _audioSource = GetComponent<AudioSource>();
            _soundEnabled = PlayerPrefs.GetInt(UiSoundPreference, 1) == 1;

            // 세션 단계는 UI 가 읽는 유일한 네트워크 상태다. 로비의 상태 라벨을 이것만 보고 갱신한다.
            SessionLauncher.PhaseChanged += OnSessionPhaseChanged;

            ConfigureAudioSource();
            CacheElements();
            BindButtons();
            ShowViewImmediate("home");
            _root.schedule.Execute(PlayIntro).StartingIn(80);
        }

        private void OnDisable()
        {
            SessionLauncher.PhaseChanged -= OnSessionPhaseChanged;

            _intro?.Kill();
            StopAmbientMotion();
            OutGameTransitionWipe.Cancel();
            DOTween.Kill(_effectLayer);
            _effectLayer?.Clear();

            if (_audioSource != null)
            {
                DOTween.Kill(_audioSource);
                _audioSource.Stop();
            }

            foreach (Button button in _buttons)
            {
                DOTween.Kill(button);
            }
        }

        private void ConfigureAudioSource()
        {
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f;
            _audioSource.ignoreListenerPause = true;
        }

        private void CacheElements()
        {
            _buttons.Clear();
            _root.Query<Button>(className: "flow-button").ToList(_buttons);

            _floatShapes.Clear();
            _root.Query<VisualElement>(className: "float-shape").ToList(_floatShapes);

            _twinkles.Clear();
            _root.Query<VisualElement>(className: "winter-twinkle").ToList(_twinkles);

            _effectLayer = _root.Q<VisualElement>("ui-effect-layer");

            _views.Clear();
            List<VisualElement> viewElements = _root.Query<VisualElement>(className: "menu-view").ToList();
            foreach (VisualElement view in viewElements)
            {
                if (string.IsNullOrEmpty(view.name) || !view.name.StartsWith(ViewElementPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                _views[view.name.Substring(ViewElementPrefix.Length)] = view;
            }
        }

        private void BindButtons()
        {
            foreach (Button button in _buttons)
            {
                button.RegisterCallback<PointerEnterEvent>(_ => OnPointerEnter(button));
                button.RegisterCallback<PointerLeaveEvent>(_ => AnimateHover(button, false));
                button.clicked += () => OnButtonClicked(button);
            }
        }

        private void OnPointerEnter(Button button)
        {
            AnimateHover(button, true);
            PlayHoverGlints(button);
            PlayHoverSound(button);
        }

        private void PlayHoverSound(Button button)
        {
            if (!_soundEnabled || _hoverClip == null || Time.unscaledTime < _nextHoverSoundTime)
            {
                return;
            }

            float targetPitch = button.ClassListContains("danger") ? 0.94f : 1.02f;
            DOTween.Kill(_audioSource);
            _audioSource.pitch = targetPitch - 0.04f;
            _audioSource.volume = _hoverVolume;
            _audioSource.PlayOneShot(_hoverClip);
            _nextHoverSoundTime = Time.unscaledTime + 0.05f;

            DOTween.To(() => _audioSource.pitch, value => _audioSource.pitch = value, targetPitch, 0.11f)
                .SetTarget(_audioSource)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        private void OnButtonClicked(Button button)
        {
            PlayClickSound(button);

            if (button.name.StartsWith(ViewButtonPrefix, StringComparison.Ordinal))
            {
                PulseButton(button, null);
                ActivateButton(button);
                return;
            }

            PulseButton(button, () => ActivateButton(button));
        }

        private void PlayClickSound(Button button)
        {
            if (!_soundEnabled)
            {
                return;
            }

            AudioClip clip = _clickClip;
            float volume = _clickVolume;
            if (button.name.StartsWith(ViewButtonPrefix, StringComparison.Ordinal) && _navigationClip != null)
            {
                clip = _navigationClip;
                volume = _navigationVolume;
            }
            else if (IsConfirmAction(button.name) && _confirmClip != null)
            {
                clip = _confirmClip;
                volume = _confirmVolume;
            }

            if (clip == null) return;

            DOTween.Kill(_audioSource);
            _audioSource.pitch = button.ClassListContains("danger") ? 0.94f : 1f;
            _audioSource.volume = volume;
            _audioSource.PlayOneShot(clip);
        }

        private static bool IsConfirmAction(string buttonName)
        {
            return buttonName is "action-host-room"
                or "action-join-room"
                or "action-start-game"
                or "action-single-start"
                or "action-stage-start"
                or "action-tutorial-start";
        }

        private void ActivateButton(Button button)
        {
            string buttonName = button.name;
            if (buttonName.StartsWith(ViewButtonPrefix, StringComparison.Ordinal))
            {
                SwitchView(buttonName.Substring(ViewButtonPrefix.Length));
                return;
            }

            switch (buttonName)
            {
                case "action-host-room":
                    OpenHostedLobby();
                    break;
                case "action-join-room":
                    JoinLobby();
                    break;
                case "action-connecting-back":
                    SwitchView("home");
                    break;
                case "action-copy-code":
                    GUIUtility.systemCopyBuffer = _roomCode ?? string.Empty;
                    SetStatus("ROOM CODE COPIED");
                    break;
                case "action-start-game":
                    // 방장 판정은 서버가 한다(SessionLobby). 이 화면이 호스트인지로 잠그면 안 된다 —
                    // Server Mode 에서 방장은 서버를 띄운 사람이 아니라 **방을 만든 클라이언트**이고,
                    // 그 사람은 여기서 _isHost = false 다. 로컬 플래그로 막으면 방장이 시작할 수 없다.
                    StartMatchAsync();
                    break;
                case "action-single-start":
                    StartSinglePlayer();
                    break;
                case "stage-winter-village":
                    SelectWinterVillage();
                    break;
                case "action-stage-start":
                    SelectWinterVillage(false);
                    StartSinglePlayer();
                    break;
                case "action-tutorial-start":
                    StartTutorial();
                    break;
                case "action-sound":
                    ToggleUiSound();
                    break;
                case "action-quality":
                    CycleQuality();
                    break;
                case "action-fullscreen":
                    Screen.fullScreen = !Screen.fullScreen;
                    RefreshSettingsLabels();
                    SetStatus(Screen.fullScreen ? "FULLSCREEN ON" : "FULLSCREEN OFF");
                    break;
                case "action-quit":
                    Application.Quit();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                    break;
                default:
                    SetStatus(string.IsNullOrEmpty(button.tooltip) ? "COMING SOON" : button.tooltip.ToUpperInvariant());
                    break;
            }
        }

        private void StartSinglePlayer()
        {
            _nickname = ReadTextField("single-nickname-input", DefaultNickname);
            SessionLauncher.LocalNickname = _nickname;
            if (!PlayerPrefs.HasKey(SelectedStagePreference))
            {
                PlayerPrefs.SetString(SelectedStagePreference, WinterVillageStageId);
                PlayerPrefs.Save();
            }

            LoadingScreenController.Open();
        }

        private static void StartTutorial()
        {
            LoadingScreenController.Open(TutorialScenePath);
        }

        private void SwitchView(string viewId)
        {
            if (!_views.TryGetValue(viewId, out VisualElement nextView)
                || nextView == _activeView
                || OutGameTransitionWipe.IsTransitioning)
            {
                return;
            }

            bool currentIsStageWorld = _activeView?.name == ViewElementPrefix + "stage-select";
            bool nextIsStageWorld = viewId == "stage-select";
            bool useCurtain = currentIsStageWorld != nextIsStageWorld;

            PrepareViewData(viewId);
            if (!OutGameTransitionWipe.SwitchView(
                    _root,
                    _activeView,
                    nextView,
                    useCurtain,
                    () => ApplyVisualMode(viewId),
                    () => OnViewShown(viewId)))
            {
                return;
            }

            _activeView = nextView;
        }

        private void ShowViewImmediate(string viewId)
        {
            if (!_views.TryGetValue(viewId, out VisualElement target))
            {
                return;
            }

            foreach (VisualElement view in _views.Values)
            {
                bool active = view == target;
                view.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                view.style.opacity = 1f;
                view.style.translate = new Translate(0f, 0f, 0f);
                view.EnableInClassList("menu-view-active", active);
                view.EnableInClassList("menu-view-hidden", !active);
            }

            _activeView = target;
            PrepareViewData(viewId);
            ApplyVisualMode(viewId);
            OnViewShown(viewId);
        }

        private void ApplyVisualMode(string viewId)
        {
            VisualElement mainMenu = _root.Q<VisualElement>("main-menu");
            mainMenu?.EnableInClassList("stage-world-active", viewId == "stage-select");
        }

        private void PrepareViewData(string viewId)
        {
            if (viewId == "host")
            {
                _roomCode = GenerateRoomCode();
                SetLabel("room-preview-label", _roomCode);
            }
            else if (viewId == "lobby")
            {
                RefreshLobby();
            }
            else if (viewId == "settings")
            {
                RefreshSettingsLabels();
            }
            else if (viewId == "stage-select")
            {
                SelectWinterVillage(false);
            }
        }

        private void OnViewShown(string viewId)
        {
            if (viewId == "stage-select")
            {
                _root.schedule.Execute(() => _root.Q<Button>("stage-winter-village")?.Focus()).StartingIn(30);
            }
        }

        private void SelectWinterVillage(bool announce = true)
        {
            PlayerPrefs.SetString(SelectedStagePreference, WinterVillageStageId);
            PlayerPrefs.Save();

            Button stageButton = _root.Q<Button>("stage-winter-village");
            stageButton?.AddToClassList("stage-node-selected");
            if (announce)
            {
                SetStatus("WINTER VILLAGE SELECTED");
            }
        }

        private void OpenHostedLobby()
        {
            _nickname = ReadTextField("host-nickname-input", DefaultNickname);
            SessionLauncher.LocalNickname = _nickname;
            if (string.IsNullOrEmpty(_roomCode))
            {
                _roomCode = GenerateRoomCode();
            }

            _isHost = true;
            RefreshLobby();

            // <b>로비로 바로 가지 않는다.</b> 결과를 알기 전에 로비를 띄우면, 실패해도 사람은 방 코드와
            // 슬롯이 보이는 화면에 앉아 있게 된다 - 실패인지 기다림인지 화면으로 구분할 수 없다.
            // 스펙: docs/specs/2026-08-30-lobby-connecting-view.md
            EnterConnecting("CREATING ROOM...", "HOLD ON WHILE THE ICE CAMP OPENS");
            HostRoomAsync();
        }

        private void JoinLobby()
        {
            TextField codeInput = _root.Q<TextField>("room-code-input");
            string code = codeInput?.value?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("ENTER A ROOM CODE FIRST");
                codeInput?.Focus();
                return;
            }

            _nickname = ReadTextField("join-nickname-input", DefaultNickname);
            SessionLauncher.LocalNickname = _nickname;
            _roomCode = code;
            _isHost = false;
            RefreshLobby();

            EnterConnecting("JOINING ROOM...", "LOOKING FOR THE ICE CAMP");
            JoinRoomAsync(code);
        }

        /// <summary>도는 고리. <b>남은 양을 말하지 않는다</b> — 진행 바와 달리 "아직 살아 있다" 만
        /// 알리므로 모르는 것을 아는 척하지 않는다. UI Toolkit 에는 <c>@keyframes</c> 가 없어서
        /// 회전은 코드가 돌린다(USS 는 모양만 준다).</summary>
        private IVisualElementScheduledItem _spinner;

        /// <summary>접속 중 화면으로 넘어간다. 문구는 <see cref="ShowConnectingFailure"/> 가 덮어쓴다.</summary>
        private void EnterConnecting(string title, string kicker)
        {
            SwitchView("connecting");
            SetConnectingText(title, kicker);
            SetBackVisible(false);
            SetStatus(string.Empty);
            SetStatusError(false);
            StartSpinner();
        }

        private void StartSpinner()
        {
            VisualElement ring = FindConnecting("connecting-spinner");
            if (ring == null) return;

            ring.style.display = DisplayStyle.Flex;
            _spinner?.Pause();

            float angle = 0f;
            _spinner = ring.schedule.Execute(() =>
            {
                angle = (angle + 9f) % 360f;
                ring.style.rotate = new StyleRotate(new Rotate(new Angle(angle, AngleUnit.Degree)));
            }).Every(16);
        }

        /// <summary>실패하면 고리를 멈추고 감춘다. <b>도는 채로 두면 아직 시도 중이라는 뜻이 된다.</b></summary>
        private void StopSpinner()
        {
            _spinner?.Pause();
            _spinner = null;

            VisualElement ring = FindConnecting("connecting-spinner");
            if (ring != null) ring.style.display = DisplayStyle.None;
        }

        private VisualElement FindConnecting(string elementName)
        {
            return _views.TryGetValue("connecting", out VisualElement view)
                ? view.Q<VisualElement>(elementName)
                : null;
        }

        private void SetConnectingText(string title, string kicker)
        {
            if (!_views.TryGetValue("connecting", out VisualElement view)) return;
            Label t = view.Q<Label>("connecting-title");
            Label k = view.Q<Label>("connecting-kicker");
            if (t != null) t.text = title;
            if (k != null) k.text = kicker;
        }

        /// <summary>BACK 은 <b>실패한 뒤에만</b> 보인다. 시도 중에 누르면 반쯤 시작된 러너를 내리는
        /// 경로가 필요해지는데, 최악이 6초 남짓이라 그 복잡도를 살 이유가 없다(스펙 §6).</summary>
        private void SetBackVisible(bool visible)
        {
            if (!_views.TryGetValue("connecting", out VisualElement view)) return;
            Button back = view.Q<Button>("action-connecting-back");
            if (back != null) back.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>실패를 접속 중 화면에 남긴다. <b>원인마다 다음에 할 일이 다르므로 뭉뚱그리지
        /// 않는다</b> - "방이 아직 없다" 와 "에디터가 설정을 못 읽었다" 는 전혀 다른 문제다.</summary>
        private void ShowConnectingFailure(string headline)
        {
            StopSpinner();

            string reason = HumanReadableFailure(SessionLauncher.LastStartFailure);
            SetConnectingText(headline, "TAP BACK TO MENU AND TRY AGAIN");

            // <b>이유는 상태 라벨에도 적고 붉게 만든다.</b> 배너 문구만으로는 사람이 그냥 지나친다 —
            // 실제로 "BACK 버튼만 보인다" 는 보고를 받았다(2026-08-30). 평소 안내와 같은 모양이면
            // 실패가 안내처럼 읽힌다.
            SetStatus(reason);
            SetStatusError(true);
            SetBackVisible(true);
        }

        private void SetStatusError(bool isError)
        {
            Label status = _activeView?.Q<Label>(className: "status-label");
            if (status == null) return;
            if (isError) status.AddToClassList("error");
            else status.RemoveFromClassList("error");
        }

        private static string HumanReadableFailure(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "SEE CONSOLE";
            if (reason == "GameNotFound") return "ROOM NOT FOUND - CHECK THE CODE OR WAIT FOR THE HOST";
            if (reason.StartsWith("Fusion 설정", StringComparison.Ordinal)) return "EDITOR ISSUE - SEE CONSOLE";
            return reason.ToUpperInvariant();
        }

        private void RefreshLobby()
        {
            if (string.IsNullOrEmpty(_roomCode))
            {
                _roomCode = "PREVIEW";
            }

            if (string.IsNullOrEmpty(_nickname))
            {
                _nickname = DefaultNickname;
            }

            SetLabel("room-code-label", _roomCode);
            SetLabel("role-label", _isHost ? "HOST" : "GUEST");
            // 세션 상태가 오기 전의 초기 표시. 로비에 들어가면 FillPlayerSlots 가 이어받는다.
            SetLabel("player-slot-0", _nickname + (_isHost ? "  /  HOST" : "  /  READY"));

            Button startButton = _root.Q<Button>("action-start-game");
            if (startButton != null)
            {
                startButton.text = _isHost ? "START GAME" : "READY";
            }
        }

        private string ReadTextField(string name, string fallback)
        {
            string value = _root.Q<TextField>(name)?.value?.Trim();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private void ToggleUiSound()
        {
            _soundEnabled = !_soundEnabled;
            PlayerPrefs.SetInt(UiSoundPreference, _soundEnabled ? 1 : 0);
            PlayerPrefs.Save();

            if (!_soundEnabled)
            {
                DOTween.Kill(_audioSource);
                _audioSource.Stop();
            }

            RefreshSettingsLabels();
            SetStatus(_soundEnabled ? "UI SOUND ON" : "UI SOUND OFF");
        }

        private void CycleQuality()
        {
            string[] names = QualitySettings.names;
            if (names.Length == 0)
            {
                SetStatus("NO QUALITY LEVELS FOUND");
                return;
            }

            int next = (QualitySettings.GetQualityLevel() + 1) % names.Length;
            QualitySettings.SetQualityLevel(next, true);
            RefreshSettingsLabels();
            SetStatus("QUALITY: " + names[next].ToUpperInvariant());
        }

        private void RefreshSettingsLabels()
        {
            SetLabel("sound-value", _soundEnabled ? "ON" : "OFF");

            string[] qualityNames = QualitySettings.names;
            int qualityIndex = QualitySettings.GetQualityLevel();
            string quality = qualityNames.Length > qualityIndex && qualityIndex >= 0
                ? qualityNames[qualityIndex].ToUpperInvariant()
                : "DEFAULT";
            SetLabel("quality-value", quality);
            SetLabel("fullscreen-value", Screen.fullScreen ? "ON" : "OFF");
        }

        private void SetLabel(string name, string value)
        {
            Label label = _root.Q<Label>(name);
            if (label != null)
            {
                label.text = value;
            }
        }

        private void SetStatus(string message)
        {
            Label status = _activeView?.Q<Label>(className: "status-label");
            if (status == null)
            {
                return;
            }

            status.text = message;
            DOTween.Kill(status);
            status.style.opacity = 1f;
            status.style.translate = new Translate(0f, -7f, 0f);
            MoveTo(status, Vector2.zero, 0.3f)
                .SetEase(Ease.OutElastic, 1.05f, 0.35f)
                .SetUpdate(true);
        }

        private static string GenerateRoomCode()
        {
            char[] code = new char[6];
            for (int i = 0; i < code.Length; i++)
            {
                code[i] = RoomCodeCharacters[UnityEngine.Random.Range(0, RoomCodeCharacters.Length)];
            }

            return new string(code);
        }

        private void PlayIntro()
        {
            VisualElement card = _activeView?.Q<VisualElement>(className: "flow-card-shadow");
            VisualElement titleBanner = _activeView?.Q<VisualElement>(className: "title-banner");
            if (card == null)
            {
                StartAmbientMotion();
                return;
            }

            List<Button> visibleButtons = _activeView.Query<Button>(className: "flow-button").ToList();
            PrepareElement(card, new Vector2(0f, 70f), 0.92f);
            if (titleBanner != null)
            {
                PrepareElement(titleBanner, new Vector2(0f, -70f), 0.88f);
            }

            for (int i = 0; i < visibleButtons.Count; i++)
            {
                PrepareElement(visibleButtons[i], new Vector2(i % 2 == 0 ? -55f : 55f, 0f), 0.96f);
            }

            _intro = DOTween.Sequence().SetUpdate(true);
            _intro.Append(FadeTo(card, 1f, 0.22f));
            _intro.Join(MoveTo(card, Vector2.zero, 0.46f).SetEase(Ease.OutBack, 1.05f));
            _intro.Join(ScaleTo(card, 1f, 0.4f).SetEase(Ease.OutBack));

            if (titleBanner != null)
            {
                _intro.Insert(0.1f, FadeTo(titleBanner, 1f, 0.18f));
                _intro.Insert(0.1f, MoveTo(titleBanner, Vector2.zero, 0.42f).SetEase(Ease.OutBack));
                _intro.Insert(0.1f, ScaleTo(titleBanner, 1f, 0.36f).SetEase(Ease.OutBack));
            }

            for (int i = 0; i < visibleButtons.Count; i++)
            {
                float at = 0.24f + i * 0.055f;
                Button button = visibleButtons[i];
                _intro.Insert(at, FadeTo(button, 1f, 0.16f));
                _intro.Insert(at, MoveTo(button, Vector2.zero, 0.28f).SetEase(Ease.OutQuad));
                _intro.Insert(at, ScaleTo(button, 1f, 0.24f).SetEase(Ease.OutBack));
            }

            _intro.OnComplete(StartAmbientMotion);
        }

        private void StartAmbientMotion()
        {
            StopAmbientMotion();
            for (int i = 0; i < _floatShapes.Count; i++)
            {
                VisualElement shape = _floatShapes[i];
                Vector2 target = i % 2 == 0 ? new Vector2(10f, -8f) : new Vector2(-8f, 10f);
                Tween tween = MoveTo(shape, target, 3.2f + i * 0.35f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
                _ambientTweens.Add(tween);
            }

            for (int i = 0; i < _twinkles.Count; i++)
            {
                VisualElement twinkle = _twinkles[i];
                float restingOpacity = 0.48f + i * 0.07f;
                float opacity = restingOpacity;
                Vector2 scale = Vector2.one * 0.82f;
                twinkle.style.opacity = opacity;
                twinkle.style.scale = new Scale(scale);

                Sequence pulse = DOTween.Sequence()
                    .SetTarget(twinkle)
                    .SetUpdate(true)
                    .AppendInterval(0.2f + i * 0.18f)
                    .Append(DOTween.To(() => opacity, value =>
                    {
                        opacity = value;
                        twinkle.style.opacity = value;
                    }, 1f, 0.55f).SetEase(Ease.OutQuad))
                    .Join(DOTween.To(() => scale, value =>
                    {
                        scale = value;
                        twinkle.style.scale = new Scale(value);
                    }, Vector2.one * 1.18f, 0.55f).SetEase(Ease.OutBack))
                    .Append(DOTween.To(() => opacity, value =>
                    {
                        opacity = value;
                        twinkle.style.opacity = value;
                    }, restingOpacity, 0.8f).SetEase(Ease.InOutSine))
                    .Join(DOTween.To(() => scale, value =>
                    {
                        scale = value;
                        twinkle.style.scale = new Scale(value);
                    }, Vector2.one * 0.82f, 0.8f).SetEase(Ease.InOutSine))
                    .AppendInterval(0.35f + i * 0.08f)
                    .SetLoops(-1);
                _ambientTweens.Add(pulse);
            }
        }

        private void StopAmbientMotion()
        {
            foreach (Tween tween in _ambientTweens)
            {
                tween?.Kill();
            }

            _ambientTweens.Clear();
            foreach (VisualElement twinkle in _twinkles)
            {
                twinkle.style.opacity = 1f;
                twinkle.style.scale = new Scale(Vector2.one);
            }
        }

        private void PlayHoverGlints(Button button)
        {
            if (_effectLayer == null || Time.unscaledTime < _nextHoverEffectTime)
            {
                return;
            }

            _nextHoverEffectTime = Time.unscaledTime + 0.08f;
            Rect bounds = button.worldBound;
            Vector2 left = _effectLayer.WorldToLocal(new Vector2(bounds.xMin + 16f, bounds.yMin + 9f));
            Vector2 right = _effectLayer.WorldToLocal(new Vector2(bounds.xMax - 18f, bounds.yMin + 12f));
            VisualElement leftGlint = CreateEffectElement("snow-effect-glint", "snow-effect-white", left, 9f);
            VisualElement rightGlint = CreateEffectElement("snow-effect-glint", "snow-effect-ice", right, 7f);
            AnimateParticle(leftGlint, new Vector2(-12f, -13f), 1.25f, 0.24f, 0f);
            AnimateParticle(rightGlint, new Vector2(10f, -15f), 1.15f, 0.22f, 0.035f);
        }

        private VisualElement CreateEffectElement(
            string shapeClass,
            string colorClass,
            Vector2 center,
            float size)
        {
            VisualElement effect = new VisualElement { pickingMode = PickingMode.Ignore };
            effect.AddToClassList("snow-effect");
            effect.AddToClassList(shapeClass);
            if (!string.IsNullOrEmpty(colorClass))
            {
                effect.AddToClassList(colorClass);
            }

            effect.style.left = center.x - size * 0.5f;
            effect.style.top = center.y - size * 0.5f;
            effect.style.width = size;
            effect.style.height = size;
            _effectLayer.Add(effect);
            return effect;
        }

        private void AnimateParticle(
            VisualElement particle,
            Vector2 offset,
            float endScale,
            float duration,
            float delay)
        {
            Vector2 translate = Vector2.zero;
            Vector2 scale = Vector2.one * 0.52f;
            float opacity = 1f;
            particle.style.scale = new Scale(scale);
            particle.style.opacity = opacity;

            DOTween.Sequence()
                .SetTarget(_effectLayer)
                .SetUpdate(true)
                .AppendInterval(delay)
                .Append(DOTween.To(() => translate, value =>
                {
                    translate = value;
                    particle.style.translate = new Translate(value.x, value.y, 0f);
                }, offset, duration).SetEase(Ease.OutCubic))
                .Join(DOTween.To(() => scale, value =>
                {
                    scale = value;
                    particle.style.scale = new Scale(value);
                }, Vector2.one * endScale, duration).SetEase(Ease.OutBack))
                .Join(DOTween.To(() => opacity, value =>
                {
                    opacity = value;
                    particle.style.opacity = value;
                }, 0f, duration).SetEase(Ease.InQuad))
                .OnComplete(particle.RemoveFromHierarchy);
        }

        private static void AnimateHover(Button button, bool entered)
        {
            DOTween.Kill(button);
            ScaleTo(button, entered ? 1.05f : 1f, 0.16f)
                .SetEase(entered ? Ease.OutBack : Ease.OutQuad)
                .SetUpdate(true);
        }

        private static void PulseButton(Button button, Action onComplete)
        {
            DOTween.Kill(button);
            DOTween.Sequence()
                .SetTarget(button)
                .SetUpdate(true)
                .Append(ScaleTo(button, 0.97f, 0.06f).SetEase(Ease.InQuad))
                .Append(ScaleTo(button, 1f, 0.1f).SetEase(Ease.OutQuad))
                .OnComplete(() => onComplete?.Invoke());
        }

        private static void PrepareElement(VisualElement element, Vector2 offset, float scale)
        {
            element.style.opacity = 0f;
            element.style.translate = new Translate(offset.x, offset.y, 0f);
            element.style.scale = new Scale(new Vector2(scale, scale));
        }

        private static Tweener FadeTo(VisualElement element, float endValue, float duration)
        {
            float value = element.resolvedStyle.opacity;
            return DOTween.To(() => value, x =>
            {
                value = x;
                element.style.opacity = x;
            }, endValue, duration).SetTarget(element);
        }

        private static Tweener MoveTo(VisualElement element, Vector2 endValue, float duration)
        {
            Vector2 value = element.resolvedStyle.translate;
            return DOTween.To(() => value, x =>
            {
                value = x;
                element.style.translate = new Translate(x.x, x.y, 0f);
            }, endValue, duration).SetTarget(element);
        }

        private static Tweener ScaleTo(VisualElement element, float endValue, float duration)
        {
            Vector2 value = element.resolvedStyle.scale.value;
            Vector2 target = new Vector2(endValue, endValue);
            return DOTween.To(() => value, x =>
            {
                value = x;
                element.style.scale = new Scale(x);
            }, target, duration).SetTarget(element);
        }

        /// <summary>
        /// 방을 만든다. <see cref="SessionLauncher.HostRoom"/> 는 서버 피어와 이 사람의 클라이언트 피어를
        /// 차례로 띄우므로 시간이 걸린다. UI 콜백에서 호출하니 <c>async void</c> 지만, 결과를 버리지 않고
        /// 실패를 상태 라벨에 적는다. <c>await</c> 뒤는 Unity 메인 스레드로 돌아오므로 UI 를 만져도 된다.
        /// </summary>
        private async void HostRoomAsync()
        {
            try
            {
                if (await SessionLauncher.HostRoom(_roomCode))
                {
                    StopSpinner();
                    SwitchView("lobby");
                }
                else ShowConnectingFailure("COULD NOT CREATE ROOM");
            }
            catch (Exception ex)
            {
                ReportSessionStartFailure("HOST", ex);
            }
        }

        /// <summary>이미 있는 방에 붙는다. <b>방이 생길 때까지 잠깐 다시 해 본다.</b>
        ///
        /// <para><b>왜 한 번으로는 안 되는가.</b> 사람은 방이 열렸는지 볼 방법이 없다 — 방 목록이
        /// 없고 코드를 받아서 누를 뿐이다. 호스트가 아직 방을 다 열지 못한 순간에 누르면 Photon 이
        /// <c>GameNotFound</c> 를 돌려주는데, 전에는 그 한 번으로 끝났다. 화면은 이미 로비로 넘어가
        /// 있으므로 <b>"가끔 접속이 안 된다" 로만 보인다</b>(2026-08-30 사용자 보고).</para>
        ///
        /// <para><c>MultiplayerRoleBootstrap.JoinWhenRoomExists</c> 가 자동 접속에서 이미 같은 이유로
        /// 폴링한다. 그 주석은 "사람이 누르는 경로는 방이 이미 있을 때만 누르므로 필요 없다" 고
        /// 적었는데, <b>그 전제가 틀렸다.</b> 같은 대비를 이쪽에도 둔다.</para>
        ///
        /// <para>사람이 보고 있으므로 자동 접속(20초)보다 짧게 잡고, 시도 중임을 라벨에 적는다.
        /// 중간에 화면을 벗어나면(뒤로 가기, 직접 방 만들기) <see cref="_roomCode"/> 가 바뀌므로
        /// 거기서 멈춘다 — 안 그러면 떠난 화면을 위해 계속 붙으려 든다.</para></summary>
        private async void JoinRoomAsync(string roomCode)
        {
            const int Attempts = 8;
            const int RetryMs = 700;

            try
            {
                for (int attempt = 1; attempt <= Attempts; attempt++)
                {
                    if (await SessionLauncher.JoinRoom(roomCode))
                    {
                        StopSpinner();
                        SwitchView("lobby");
                        return;
                    }

                    if (attempt == Attempts || _roomCode != roomCode) break;

                    SetConnectingText($"JOINING ROOM - {attempt}/{Attempts}",
                                      "LOOKING FOR THE ICE CAMP");
                    await System.Threading.Tasks.Task.Delay(RetryMs);
                }

                ShowConnectingFailure("COULD NOT JOIN ROOM");
            }
            catch (Exception ex)
            {
                ReportSessionStartFailure("JOIN", ex);
            }
        }

        /// <summary>
        /// <b>이 두 메서드는 <c>async void</c> 다.</b> 그래서 <c>await</c> 한 작업이 <c>false</c> 를
        /// 돌려주는 대신 <b>예외를 던지면</b> 그 예외를 받을 곳이 없다 — 상태 라벨은 그대로 남고
        /// 화면에는 아무 단서 없이 "안 된다" 만 보인다. 2026-08-29 에 실제로 그렇게 잃었다:
        /// Fusion 설정 자산을 못 읽어 <c>StartPeer</c> 가 예외로 빠져나갔고, 사용자에게는
        /// <b>접속이 조용히 안 되는 것</b>으로만 보였다.
        ///
        /// <para><c>async void</c> 자체는 유지한다 — UI 콜백이라 <c>Task</c> 를 돌려줄 곳이 없다.
        /// 대신 몸통을 통째로 감싸서 <b>어떤 실패든 반드시 화면과 콘솔 양쪽에 남게</b> 한다.</para>
        /// </summary>
        private void ReportSessionStartFailure(string what, Exception ex)
        {
            Debug.LogError($"{nameof(OutGameScreenController)}: {what} 중 예외 - {ex}");
            SetStatus($"FAILED TO {what} ROOM - SEE CONSOLE");
        }

        /// <summary>매치를 시작한다. 씬 권위(서버 피어)가 게임플레이 씬을 올린다.</summary>
        private void StartMatchAsync()
        {
            // 클라이언트는 씬을 올릴 수 없다 - 서버에 **요청**하고 서버가 방장 여부를 판단한다.
            // 이 화면이 서버 인스턴스일 때(로비가 아직 없을 때)는 직접 시작한다.
            // 이 프로세스가 서버면 직접 시작한다. 요청은 클라이언트 입력에 실려 오는데 서버는 입력을
            // 만들지 않으므로(ProvideInput = false), 서버에서 요청 경로를 타면 아무 일도 일어나지 않는다.
            if (SessionLauncher.LocalServer != null)
            {
                SetStatus("STARTING MATCH...");
                _ = SessionLauncher.StartMatch();
                return;
            }

            SessionLobby lobby = SessionLobby.Instance;
            if (lobby != null)
            {
                if (!lobby.LocalCanStart)
                {
                    SetStatus("ONLY THE ROOM OWNER CAN START");
                    return;
                }

                SetStatus("STARTING MATCH...");
                SessionLauncher.RequestStartMatch();   // 입력 채널로 요청 - RPC 는 쓸 수 없다(SessionLobby 주석)
                return;
            }

            SetStatus("STARTING MATCH...");
            _ = SessionLauncher.StartMatch();
        }

        /// <summary>
        /// 세션 단계가 바뀔 때 상태 라벨만 갱신한다. <b>화면을 바꾸지 않는다</b> —
        /// 게임플레이 씬은 Fusion 이 올리므로 이 화면은 그때 씬과 함께 사라진다.
        /// </summary>
        private void OnSessionPhaseChanged(ESessionPhase phase)
        {
            // <b>접속 중 화면은 자기 문구를 직접 관리한다.</b> 여기서 상태 줄을 덮으면 제목과 어긋난다 —
            // 실측(2026-08-30): 제목이 "JOINING ROOM - 2/8" 인데 아래에 "SESSION CLOSED" 가 떴다.
            // 재시도가 매번 러너를 세웠다 내리므로 Offline 이 정상적으로 여러 번 지나가는데, 그 문구는
            // 사람에게 "끝났다" 로 읽힌다. 화면을 감추고 보이는 일은 그대로 한다.
            bool connectingOwnsStatus = _activeView != null
                                        && _activeView.name == ViewElementPrefix + "connecting";

            switch (phase)
            {
                case ESessionPhase.Matchmaking:
                    if (!connectingOwnsStatus) SetStatus("MATCHMAKING - ROOM " + (_roomCode ?? string.Empty));
                    break;
                case ESessionPhase.Lobby:
                    if (!connectingOwnsStatus) SetStatus(_isHost ? "ROOM OPEN - WAITING FOR PLAYERS" : "CONNECTED - WAITING FOR HOST");
                    SetUiVisible(true);
                    _lobbyPollUntilPhaseChanges = true;
                    break;
                case ESessionPhase.Loading:
                    if (!connectingOwnsStatus) SetStatus("LOADING GAMEPLAY...");
                    break;
                case ESessionPhase.Playing:
                    if (!connectingOwnsStatus) SetStatus("IN GAME");
                    // PeerMode = Multiple 로 로컬 검증할 때는 Fusion 이 피어마다 씬을 따로 올려서
                    // MainMenu 씬이 그대로 남는다. 그러면 게임플레이 위에 이 UI 가 계속 그려지므로 감춘다.
                    // 배포 빌드(PeerMode = Single)에서는 MainMenu 가 언로드되니 이 줄은 무해하다.
                    SetUiVisible(false);
                    break;
                case ESessionPhase.Offline:
                    if (!connectingOwnsStatus) SetStatus("SESSION CLOSED");
                    SetUiVisible(true);
                    break;
            }
        }

        /// <summary>이 화면 전체를 보이거나 감춘다. 게임플레이 중에 로비가 위에 남지 않게 하려고 쓴다.</summary>
        private void SetUiVisible(bool visible)
        {
            if (_root == null) return;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// 로비 표시를 실제 세션 상태로 갱신한다. <b>확인용</b>이다 — 방 코드·인원·방장이 화면에 없으면
        /// 두 인스턴스가 정말 같은 방에 있는지 사람이 알 수 없다.
        /// </summary>
        private void UpdateLobbyView()
        {
            SessionLobby lobby = SessionLobby.Instance;
            if (lobby == null) return;

            SetLabel("room-code-label", SessionLauncher.RoomCode);
            SetLabel("role-label", lobby.LocalIsOwner ? "OWNER" : "CLEANER");

            FillPlayerSlots(lobby);
            int count = lobby.PlayerCount;

            Button startButton = _root?.Q<Button>("action-start-game");
            if (startButton != null)
            {
                startButton.SetEnabled(lobby.LocalCanStart && count > 0);
                startButton.text = lobby.LocalCanStart ? "START GAME" : "WAITING FOR OWNER";
            }
        }

        /// <summary>
        /// 로비의 사람 목록을 한 줄에 하나씩 채운다. <b>인원수만큼 보여야 두 인스턴스가 정말 같은
        /// 방에 있는지 사람이 알 수 있다</b>.
        ///
        /// <para><b>2026-08-24 에 고쳤다.</b> 전에는 이름 있는 줄이 <c>player-one-label</c> 하나뿐이고
        /// 나머지 셋은 이름 없는 <c>WAITING FOR DRIVER...</c> 자리표시자였다. 이름이 없으면
        /// <c>Q&lt;Label&gt;</c> 로 찾을 수 없으므로 <b>인원이 몇이든 아래 셋은 영원히 안 바뀌었다</b>.
        /// 지금은 네 줄 모두 <c>player-slot-N</c> 이름을 갖는다.</para>
        ///
        /// <para><b>2026-09-01 에 뒤집었다 — 이제 닉네임이 복제된다.</b> 그 전에는 <c>_nickname</c> 이
        /// 이 프로세스의 정적 값이라 남의 이름을 알 방법이 없었고, 그래서 "없는 정보를 만들어 내지
        /// 않는다" 로 번호와 역할만 보여 줬다. 지금은 <see cref="SessionLobby"/> 가 이름을 나르므로
        /// 그 전제가 사라졌다.</para>
        ///
        /// <para>표시는 <b>이름 + <c>#id</c></b> 다(<see cref="SessionLobby.Format"/>). 접미사가 둘을
        /// 동시에 푼다 — 이름이 겹쳐도 구분되고, 이름이 아직 안 왔으면 <c>#2</c> 만으로 누군지
        /// 특정된다.</para>
        /// </summary>
        private void FillPlayerSlots(SessionLobby lobby)
        {
            _lobbySlots ??= new SessionLobby.LobbySlot[SessionLauncher.MaxPlayers];
            int filled = lobby.FillSlots(_lobbySlots);


            for (int slot = 0; slot < SessionLauncher.MaxPlayers; slot++)
            {
                Label row = _root?.Q<Label>("player-slot-" + slot);
                if (row == null) continue;

                if (slot < filled)
                {
                    SessionLobby.LobbySlot s = _lobbySlots[slot];
                    string tag = s.IsLocal ? "YOU" : "CLEANER";
                    if (s.IsOwner) tag += "  /  OWNER";
                    // 이름 + #id. 이름이 겹쳐도 구분되고, 아직 안 왔으면 #id 만 나온다.
                    row.text = s.Display + "  /  " + tag;
                    row.EnableInClassList("player-ready", true);
                }
                else
                {
                    row.text = "WAITING FOR CLEANER...";
                    row.EnableInClassList("player-ready", false);
                }
            }
        }

        private void Update()
        {
            // 로비 단계에서만 돈다. 화면이 로비가 아니면 읽을 것도 없다.
            if (_lobbyPollUntilPhaseChanges && SessionLauncher.Phase == ESessionPhase.Lobby) UpdateLobbyView();
        }

    }
}

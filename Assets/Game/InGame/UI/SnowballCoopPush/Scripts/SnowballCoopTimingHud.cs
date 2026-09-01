using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>
    /// 협동 밀치기 상태를 읽어 그리기만 하는 로컬 HUD. 타이밍 판정과 Impulse는 Penguin/Snow에 있다.
    /// </summary>
    public sealed class SnowballCoopTimingHud : MonoBehaviour
    {
        private const string ResourceRoot = "SnowballCoopPush/";

        private PenguinSnowball _singleOwner;
        private PenguinNetAvatar _networkOwner;
        private VisualElement _hud;
        private VisualElement _interactionPrompt;
        private VisualElement _growthHud;
        private VisualElement _marker;
        private readonly VisualElement[] _growthSegmentFills = new VisualElement[4];
        private Label _crewLabel;
        private Label _statusLabel;
        private Label _interactionKey;
        private Label _interactionLabel;
        private Label _growthStageLabel;
        private Label _growthDiameterLabel;
        private Label _growthRemainingLabel;
        private PenguinCarry _carry;

        public static void Create(PenguinSnowball owner)
        {
            SnowballCoopTimingHud hud = CreateHost(owner);
            if (hud != null) hud._singleOwner = owner;
        }

        /// <summary>
        /// 멀티 판. <b>로컬 플레이어의 아바타에만 붙인다</b> — 진입점이 그렇게 부른다.
        ///
        /// <para>싱글과 나뉘는 이유는 공을 찾는 길이 다르기 때문이다. 클라이언트에서는 붙기가 서버
        /// 일이라 <c>PenguinSnowball.Held</c> 가 항상 null 이고, 복제된
        /// <see cref="PenguinNetAvatar.HeldForPresentation"/> 만이 지금 붙어 있는 공을 안다.</para>
        /// </summary>
        public static void Create(PenguinNetAvatar owner)
        {
            SnowballCoopTimingHud hud = CreateHost(owner);
            if (hud != null) hud._networkOwner = owner;
        }

        private static SnowballCoopTimingHud CreateHost(Component owner)
        {
            if (owner == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;

            SnowballCoopTimingHud existing = owner.GetComponentInChildren<SnowballCoopTimingHud>(true);
            if (existing != null) return existing;

            PanelSettings panel = Resources.Load<PanelSettings>(ResourceRoot + "SnowballCoopPushPanelSettings");
            VisualTreeAsset tree = Resources.Load<VisualTreeAsset>(ResourceRoot + "SnowballCoopTiming");
            if (panel == null || tree == null)
            {
                Debug.LogError("협동 밀치기 HUD의 UXML 또는 PanelSettings를 찾을 수 없다.");
                return null;
            }

            var host = new GameObject("SnowballCoopTimingHUD");
            host.transform.SetParent(owner.transform, false);
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.sortingOrder = 80;
            document.visualTreeAsset = tree;
            return host.AddComponent<SnowballCoopTimingHud>();
        }

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _hud = root.Q<VisualElement>("coop-shove-hud");
            _interactionPrompt = root.Q<VisualElement>("snowball-select-prompt");
            _growthHud = root.Q<VisualElement>("snowball-growth-hud");
            _marker = root.Q<VisualElement>("timing-marker");
            _crewLabel = root.Q<Label>("crew-label");
            _statusLabel = root.Q<Label>("status-label");
            _interactionKey = root.Q<Label>("interaction-key");
            _interactionLabel = root.Q<Label>("interaction-label");
            _growthStageLabel = root.Q<Label>("growth-stage-label");
            _growthDiameterLabel = root.Q<Label>("growth-diameter-label");
            _growthRemainingLabel = root.Q<Label>("growth-remaining-label");
            for (int i = 0; i < _growthSegmentFills.Length; i++)
                _growthSegmentFills[i] = root.Q<VisualElement>($"growth-segment-{i + 1}-fill");
            Component owner = _singleOwner != null ? (Component)_singleOwner : _networkOwner;
            if (owner != null) _carry = owner.GetComponent<PenguinCarry>();
            SetCoopVisible(false);
            SetInteractionVisible(false);
            SetGrowthVisible(false);
        }

        private void LateUpdate()
        {
            UpdateInteractionPrompt();

            SnowBallCarrier ball = _singleOwner != null
                ? _singleOwner.Held
                : _networkOwner != null ? _networkOwner.HeldForPresentation : null;
            Component owner = _singleOwner != null ? (Component)_singleOwner : _networkOwner;

            UpdateGrowthHud(ball);

            if (ball == null || owner == null ||
                !ball.TryGetCoopTiming(owner, out float phase01, out bool submitted,
                    out int participantCount))
            {
                SetCoopVisible(false);
                return;
            }

            SetCoopVisible(true);
            _marker.style.left = new Length(Mathf.Clamp01(phase01) * 100f, LengthUnit.Percent);
            _crewLabel.text = $"{participantCount} PUSHERS";
            _statusLabel.text = submitted ? "READY!" : "RIGHT CLICK";
            _hud.EnableInClassList("coop-submitted", submitted);
            _hud.EnableInClassList("coop-in-window", SnowBallCarrier.IsCoopTimingSuccess(phase01));
        }

        private void UpdateGrowthHud(SnowBallCarrier ball)
        {
            if (ball == null)
            {
                SetGrowthVisible(false);
                return;
            }

            SetGrowthVisible(true);
            int stageIndex = Mathf.Clamp((int)ball.GrowthStage, 0, 4);
            float currentProgress = ball.GrowthStageProgress01;

            if (_growthStageLabel != null)
                _growthStageLabel.text = stageIndex == 0 ? "SEED" : $"STAGE {stageIndex}";
            if (_growthDiameterLabel != null) _growthDiameterLabel.text = $"Ø {ball.DiameterM:0.00} m";
            if (_growthRemainingLabel != null)
            {
                _growthRemainingLabel.text = ball.IsVisibleGrowthComplete
                    ? "MAX SIZE"
                    : stageIndex == 0
                        ? $"{ball.RemainingDiameterToNextGrowthTargetM:0.00} m TO STAGE 1"
                        : stageIndex < 4
                        ? $"{ball.RemainingDiameterToNextGrowthTargetM:0.00} m TO NEXT"
                        : $"{ball.RemainingDiameterToNextGrowthTargetM:0.00} m TO MAX";
            }

            for (int i = 0; i < _growthSegmentFills.Length; i++)
            {
                VisualElement fill = _growthSegmentFills[i];
                if (fill == null) continue;
                float amount = i < stageIndex
                    ? 1f
                    : i == stageIndex && stageIndex < 4 ? currentProgress : 0f;
                fill.style.width = new Length(Mathf.Clamp01(amount) * 100f, LengthUnit.Percent);
            }

            for (int i = 1; i <= 4; i++)
                _growthHud.EnableInClassList($"growth-stage-{i}", i == stageIndex);
            _growthHud.EnableInClassList("growth-seed", stageIndex == 0);
            _growthHud.EnableInClassList("growth-complete", ball.IsVisibleGrowthComplete);
        }

        private void SetCoopVisible(bool visible)
        {
            if (_hud == null) return;
            _hud.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetInteractionVisible(bool visible)
        {
            if (_interactionPrompt == null) return;
            _interactionPrompt.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetGrowthVisible(bool visible)
        {
            if (_growthHud == null) return;
            _growthHud.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateInteractionPrompt()
        {
            PenguinSnowball snowball = _singleOwner != null
                ? _singleOwner
                : _networkOwner != null ? _networkOwner.GetComponent<PenguinSnowball>() : null;
            if (snowball == null)
            {
                SetInteractionVisible(false);
                return;
            }

            if (_carry == null) _carry = snowball.GetComponent<PenguinCarry>();

            bool push = snowball.CanSelectNearbyBall;
            bool approach = _networkOwner != null
                ? _networkOwner.CarryApproachingForPresentation
                : _carry != null && _carry.IsApproaching;
            bool carrying = _networkOwner != null
                ? _networkOwner.CarryingForPresentation
                : _carry != null && _carry.IsCarrying;
            bool carry = _carry != null && _carry.CanApproachCargo;
            SetInteractionVisible(push || approach || carrying || carry);
            if (_interactionKey == null || _interactionLabel == null) return;

            if (approach)
            {
                _interactionKey.text = "F";
                _interactionLabel.text = "CANCEL";
            }
            else if (carrying)
            {
                _interactionKey.text = "F";
                _interactionLabel.text = "DROP";
            }
            else if (push && carry)
            {
                _interactionKey.text = "E / F";
                _interactionLabel.text = "PUSH / CARRY";
            }
            else if (push)
            {
                _interactionKey.text = "E";
                _interactionLabel.text = "PUSH";
            }
            else
            {
                _interactionKey.text = "F";
                _interactionLabel.text = "CARRY";
            }
        }
    }
}

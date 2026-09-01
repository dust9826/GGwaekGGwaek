using UnityEngine;

namespace PPack
{
    /// <summary>튜토리얼 목표를 파티클과 월드 텍스트로 보여준다.</summary>
    [DisallowMultipleComponent]
    public sealed class TutorialGoalEffect : MonoBehaviour
    {
        public const string PendingMessage = "◆";
        public const string SuccessMessage = "✓";
        public const string GrabPromptMessage = "E로 눈을 잡으세요";

        private static readonly Color PendingColor = new Color(0.22f, 0.82f, 0.46f, 1f);
        private static readonly Color SuccessColor = new Color(0.18f, 0.94f, 0.72f, 1f);

        [SerializeField] private ParticleSystem _ambientParticles;
        [SerializeField] private ParticleSystem _successBurst;
        [SerializeField] private Transform _statusRoot;
        [SerializeField] private TextMesh _statusText;
        [SerializeField] private TextMesh _statusShadow;
        [SerializeField] private Transform _grabPromptRoot;
        [SerializeField] private TextMesh _grabPromptText;
        [SerializeField] private TextMesh _grabPromptShadow;
        [SerializeField] private ParticleSystem _guideRiseParticles;
        [SerializeField] private Transform _guideBillboardRoot;
        [SerializeField] private Renderer _guideBillboardRenderer;
        [SerializeField] private TextMesh _guideLabelText;
        [SerializeField] private TextMesh _guideLabelShadow;
        [SerializeField] private Texture2D _screenGuideTexture;

        private Vector3 _statusBasePosition;
        private Vector3 _grabPromptBasePosition;
        private Vector3 _guideBillboardBasePosition;
        private float _successStartedAt;
        private string _guideLabel = "목표";

        public string StatusText => _statusText != null ? _statusText.text : string.Empty;
        public bool IsShowingSuccess { get; private set; }
        public string GrabPromptText => _grabPromptText != null ? _grabPromptText.text : string.Empty;
        public bool IsGrabPromptVisible => _grabPromptRoot != null && _grabPromptRoot.gameObject.activeSelf;

        private void Awake()
        {
            if (_statusRoot != null) _statusBasePosition = _statusRoot.localPosition;
            if (_grabPromptRoot != null) _grabPromptBasePosition = _grabPromptRoot.localPosition;
            if (_guideBillboardRoot != null)
                _guideBillboardBasePosition = _guideBillboardRoot.localPosition;
        }

        public void ShowPending(Vector3 worldPosition)
        {
            gameObject.SetActive(true);
            transform.position = worldPosition;
            IsShowingSuccess = false;
            ApplyMessage(PendingMessage, PendingColor);
            HideGrabPrompt();

            if (_successBurst != null)
                _successBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_ambientParticles != null)
            {
                SetParticleColor(_ambientParticles, PendingColor);
                _ambientParticles.Clear(true);
                _ambientParticles.Play(true);
            }
            if (_guideRiseParticles != null)
            {
                SetParticleColor(_guideRiseParticles, PendingColor);
                _guideRiseParticles.Clear(true);
                _guideRiseParticles.Play(true);
            }
            ApplyGuideColor(PendingColor);
        }

        public void MoveTo(Vector3 worldPosition)
        {
            transform.position = worldPosition;
        }

        public void ShowSuccess()
        {
            IsShowingSuccess = true;
            _successStartedAt = Time.unscaledTime;
            ApplyMessage(SuccessMessage, SuccessColor);
            HideGrabPrompt();

            if (_ambientParticles != null) SetParticleColor(_ambientParticles, SuccessColor);
            if (_guideRiseParticles != null) SetParticleColor(_guideRiseParticles, SuccessColor);
            ApplyGuideColor(SuccessColor);
            if (_successBurst != null)
            {
                SetParticleColor(_successBurst, SuccessColor);
                _successBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _successBurst.Play(true);
            }
        }

        public void ShowGrabPrompt()
        {
            if (_grabPromptText != null) _grabPromptText.text = GrabPromptMessage;
            if (_grabPromptShadow != null) _grabPromptShadow.text = GrabPromptMessage;
            if (_grabPromptRoot != null) _grabPromptRoot.gameObject.SetActive(true);
        }

        public void HideGrabPrompt()
        {
            if (_grabPromptRoot != null) _grabPromptRoot.gameObject.SetActive(false);
        }

        public void SetGuideLabel(string label)
        {
            _guideLabel = string.IsNullOrWhiteSpace(label) ? "목표" : label;
            if (_guideLabelText != null) _guideLabelText.text = _guideLabel;
            if (_guideLabelShadow != null) _guideLabelShadow.text = _guideLabel;
        }

        private void LateUpdate()
        {
            if (_statusRoot == null && _grabPromptRoot == null && _guideBillboardRoot == null) return;

            Camera camera = Camera.main;
            Quaternion billboardRotation = Quaternion.identity;
            bool hasCamera = camera != null;
            if (hasCamera)
            {
                Transform facingRoot = _statusRoot != null ? _statusRoot : _grabPromptRoot;
                billboardRotation = Quaternion.LookRotation(facingRoot.position - camera.transform.position, Vector3.up);
            }

            if (_statusRoot != null)
            {
                if (hasCamera) _statusRoot.rotation = billboardRotation;
                float time = Time.unscaledTime;
                _statusRoot.localPosition = _statusBasePosition + Vector3.up * (Mathf.Sin(time * 3.2f) * 0.10f);
                float pop = IsShowingSuccess
                    ? Mathf.Exp(-(time - _successStartedAt) * 3.2f) * Mathf.Abs(Mathf.Sin((time - _successStartedAt) * 11f)) * 0.28f
                    : 0f;
                _statusRoot.localScale = Vector3.one * (1f + pop);
            }

            if (_grabPromptRoot != null && _grabPromptRoot.gameObject.activeSelf)
            {
                if (hasCamera)
                    _grabPromptRoot.rotation = Quaternion.LookRotation(
                        _grabPromptRoot.position - camera.transform.position, Vector3.up);
                float promptWave = (Mathf.Sin(Time.unscaledTime * 5.2f) + 1f) * 0.5f;
                _grabPromptRoot.localPosition = _grabPromptBasePosition + Vector3.up * (promptWave * 0.14f);
                _grabPromptRoot.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.05f, promptWave);
            }

            if (_guideBillboardRoot != null)
            {
                bool billboardVisible = hasCamera && IsInsideGuideViewport(
                    camera.WorldToViewportPoint(_guideBillboardRoot.position));
                if (_guideBillboardRoot.gameObject.activeSelf != billboardVisible)
                    _guideBillboardRoot.gameObject.SetActive(billboardVisible);
                if (billboardVisible)
                {
                    _guideBillboardRoot.rotation = Quaternion.LookRotation(
                        _guideBillboardRoot.position - camera.transform.position, Vector3.up);
                    float wave = (Mathf.Sin(Time.unscaledTime * 3.6f) + 1f) * 0.5f;
                    _guideBillboardRoot.localPosition =
                        _guideBillboardBasePosition + Vector3.up * (wave * 0.20f);
                    _guideBillboardRoot.localScale = Vector3.one * Mathf.Lerp(0.92f, 1.08f, wave);
                }
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _screenGuideTexture == null) return;
            Camera camera = Camera.main;
            if (camera == null) return;

            Vector3 viewport = camera.WorldToViewportPoint(transform.position + Vector3.up * 2.55f);
            bool behindCamera = viewport.z <= 0f;
            if (behindCamera)
            {
                viewport.x = 1f - viewport.x;
                viewport.y = 1f - viewport.y;
            }

            Vector2 unclamped = new Vector2(viewport.x * Screen.width, (1f - viewport.y) * Screen.height);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 direction = unclamped - screenCenter;
            if (behindCamera) direction = -direction;
            if (direction.sqrMagnitude < 0.001f) direction = Vector2.down;

            const float edgePadding = 72f;
            bool outside = behindCamera || viewport.x < 0.06f || viewport.x > 0.94f ||
                           viewport.y < 0.08f || viewport.y > 0.92f;
            if (!outside) return;
            Vector2 drawCenter = new Vector2(
                Mathf.Clamp(unclamped.x, edgePadding, Screen.width - edgePadding),
                Mathf.Clamp(unclamped.y, edgePadding, Screen.height - edgePadding));

            float wave = (Mathf.Sin(Time.unscaledTime * 4.6f) + 1f) * 0.5f;
            float size = Mathf.Lerp(outside ? 68f : 50f, outside ? 78f : 58f, wave);
            Rect rect = new Rect(drawCenter.x - size * 0.5f, drawCenter.y - size * 0.5f, size, size);
            float rotation = outside
                ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f
                : 0f;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUIUtility.RotateAroundPivot(rotation, drawCenter);
            GUI.color = IsShowingSuccess
                ? new Color(SuccessColor.r, SuccessColor.g, SuccessColor.b, 0.96f)
                : new Color(PendingColor.r, PendingColor.g, PendingColor.b, 0.92f);
            GUI.DrawTexture(rect, _screenGuideTexture, ScaleMode.ScaleToFit, true);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void ApplyGuideColor(Color color)
        {
            if (_guideBillboardRenderer == null) return;
            var properties = new MaterialPropertyBlock();
            _guideBillboardRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            _guideBillboardRenderer.SetPropertyBlock(properties);
        }

        private static bool IsInsideGuideViewport(Vector3 viewport)
        {
            return viewport.z > 0f && viewport.x >= 0.06f && viewport.x <= 0.94f &&
                   viewport.y >= 0.08f && viewport.y <= 0.92f;
        }

        private void ApplyMessage(string message, Color color)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
                _statusText.color = color;
            }
            if (_statusShadow != null) _statusShadow.text = message;
        }

        private static void SetParticleColor(ParticleSystem particles, Color color)
        {
            ParticleSystem.MainModule main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.45f));
        }
    }
}

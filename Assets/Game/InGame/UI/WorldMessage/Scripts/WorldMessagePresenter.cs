using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>
    /// 게임 중 짧게 뜨는 알림을 그린다. <b>무엇을 알릴지는 여기서 정하지 않는다</b> — 아무나
    /// <see cref="Post"/> 로 한 줄 넣으면 순서대로 뜬다.
    ///
    /// <para><b>정적 <see cref="Post"/> 를 두는 이유.</b> 알릴 것이 생기는 자리는 씬 곳곳이고
    /// 대부분 이 오브젝트를 인스펙터로 물릴 수 없다(런타임에 생기거나 남의 피처다). 대신
    /// <b>씬을 뒤져 찾지도 않는다</b> — 살아 있는 프레젠터가 자기를 등록한다.</para>
    ///
    /// <para><b>⚠ 정적 상태는 씬을 넘어 살아남는다.</b> PlayMode 배치는 <c>DisableSceneReload</c> 라
    /// 지난 판의 프레젠터가 남아 있으면 다음 판이 죽은 오브젝트에 메시지를 넣는다. 그래서
    /// <c>OnDisable</c> 에서 자기가 등록한 것만 지운다.</para>
    ///
    /// <para>눈보라 경고(<see cref="BlizzardAlertPresenter"/>)는 아직 옮기지 않았다. 잘 돌고 있고,
    /// 옮기면 큐 때문에 경고가 다른 메시지 뒤에서 기다릴 수 있다 — 경고는 늦으면 경고가 아니다.
    /// 세 번째 메시지가 생기거나 모양이 갈라질 때 우선순위와 함께 다시 본다.</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class WorldMessagePresenter : MonoBehaviour
    {
        [Tooltip("한 줄이 떠 있는 시간(초). 페이드 포함.")]
        [SerializeField, Min(0.1f)] private float _visibleSeconds = 3f;

        [Tooltip("들어오고 나가는 페이드 시간(초).")]
        [SerializeField, Min(0.01f)] private float _fadeSeconds = 0.25f;

        private static WorldMessagePresenter _active;

        private WorldMessageQueue _queue;
        private VisualElement _root;
        private Label _label;

        /// <summary>한 줄 알린다. 프레젠터가 없으면 조용히 버린다 — 알림이 없다고 게임이 멈추면 안 된다.</summary>
        public static void Post(string text) => _active?.Enqueue(text);

        private void OnEnable()
        {
            _queue ??= new WorldMessageQueue(_visibleSeconds, _fadeSeconds);
            _active = this;
            ApplyToUi(0f);
        }

        private void OnDisable()
        {
            // 남이 이미 자리를 가져갔으면 건드리지 않는다.
            if (_active == this) _active = null;
            _queue?.Clear();
            ApplyToUi(0f);
        }

        private void Update()
        {
            if (_queue == null) return;
            _queue.Tick(Time.unscaledTime);
            ApplyToUi(_queue.Opacity(Time.unscaledTime));
        }

        private void Enqueue(string text)
        {
            _queue ??= new WorldMessageQueue(_visibleSeconds, _fadeSeconds);
            _queue.Enqueue(text);
        }

        /// <summary><c>UIDocument</c> 가 트리를 만들기 전에 <c>OnEnable</c> 이 돌면 Q 가 null 을 준다.
        /// 비어 있을 때마다 다시 찾는다(<see cref="StageHUDController"/> 와 같은 이유).</summary>
        private bool ResolveElements()
        {
            if (_root != null) return true;

            var document = GetComponent<UIDocument>();
            VisualElement documentRoot = document != null ? document.rootVisualElement : null;
            if (documentRoot == null) return false;

            _root = documentRoot.Q<VisualElement>("world-message");
            _label = documentRoot.Q<Label>("world-message-label");
            return _root != null;
        }

        private void ApplyToUi(float opacity)
        {
            if (!ResolveElements()) return;

            string text = _queue?.Current;
            if (text == null || opacity <= 0f)
            {
                _root.style.display = DisplayStyle.None;
                return;
            }

            if (_label != null) _label.text = text;
            _root.style.display = DisplayStyle.Flex;
            _root.style.opacity = opacity;
        }
    }
}

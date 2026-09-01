using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 월드 메시지의 <b>순서와 시간만</b> 정한다. 유니티도 UI 도 모른다 — 그래서 EditMode 로 전부
    /// 덮을 수 있다. 그리는 것은 <see cref="WorldMessagePresenter"/> 가 한다.
    ///
    /// <para><b>한 번에 하나만 보여 준다.</b> 여럿이 겹치면 줄을 세운다 — 두 줄이 동시에 뜨면 어느
    /// 것이 방금 일어난 일인지 알 수 없고, 자리도 겹친다.</para>
    ///
    /// <para>시간은 <c>Time.unscaledTime</c> 을 받는다. 일시정지 중에도 메시지는 흘러야 한다 —
    /// 멈춘 화면에 토스트가 박제되면 그것대로 이상하다.</para>
    /// </summary>
    internal sealed class WorldMessageQueue
    {
        private readonly Queue<string> _pending = new Queue<string>();
        private readonly float _visibleSeconds;
        private readonly float _fadeSeconds;

        private string _current;
        private float _shownAt;

        internal WorldMessageQueue(float visibleSeconds, float fadeSeconds)
        {
            _visibleSeconds = Mathf.Max(0.1f, visibleSeconds);
            _fadeSeconds = Mathf.Clamp(fadeSeconds, 0.01f, _visibleSeconds * 0.5f);
        }

        /// <summary>지금 보여야 할 문구. 아무것도 없으면 <c>null</c>.</summary>
        internal string Current => _current;

        internal void Enqueue(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _pending.Enqueue(text);
        }

        /// <summary>시간을 흘린다. 지금 것이 만료되면 다음 것으로 넘어간다.</summary>
        internal void Tick(float now)
        {
            if (_current != null && now - _shownAt >= _visibleSeconds) _current = null;
            if (_current != null || _pending.Count == 0) return;

            _current = _pending.Dequeue();
            _shownAt = now;
        }

        /// <summary>지금 문구의 불투명도. 들어올 때와 나갈 때 페이드한다.</summary>
        internal float Opacity(float now)
        {
            if (_current == null) return 0f;

            float elapsed = now - _shownAt;
            if (elapsed < _fadeSeconds) return Mathf.Clamp01(elapsed / _fadeSeconds);

            float fadeOutStart = _visibleSeconds - _fadeSeconds;
            if (elapsed < fadeOutStart) return 1f;
            return Mathf.Clamp01(1f - (elapsed - fadeOutStart) / _fadeSeconds);
        }

        /// <summary>전부 비운다. 씬이 바뀌거나 프레젠터가 꺼질 때 부른다.</summary>
        internal void Clear()
        {
            _pending.Clear();
            _current = null;
        }
    }
}

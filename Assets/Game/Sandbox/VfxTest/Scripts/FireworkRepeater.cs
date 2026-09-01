using UnityEngine;
using UnityEngine.VFX;

namespace PPack
{
    /// <summary>
    /// 불꽃놀이 테스트 자산을 일정 간격으로 다시 쏜다.
    ///
    /// 그래프의 Spawn 이 <b>단발</b>이라 재생하면 한 번 쏘고 조용해진다. 룩을 눈으로 판정해야 하는
    /// 테스트 씬에서 그건 못 쓴다 — Play 를 누르고 2 초 안에 못 보면 놓치고, 다시 보려면 플레이
    /// 모드를 껐다 켜야 한다.
    ///
    /// 그래프를 주기 발사로 바꾸는 쪽을 먼저 시도했고 <b>파티클이 하나도 안 나왔다</b>
    /// (`VFXSpawnerPeriodicBurst`, 컴파일 에러 없이 alive=0). 원인을 파는 대신 검증된 단발을 두고
    /// 여기서 <see cref="VisualEffect.Reinit"/> 로 되감는다. 샌드박스 자산이므로 이 정도가 맞다.
    /// </summary>
    public sealed class FireworkRepeater : MonoBehaviour
    {
        [SerializeField] private VisualEffect _effect;

        [Tooltip("다시 쏘는 간격. 불꽃 한 발이 끝나는 데 2 초쯤 걸린다.")]
        [SerializeField, Min(0.1f)] private float _interval = 2.5f;

        private float _nextTime;

        private void Reset() => _effect = GetComponent<VisualEffect>();

        private void OnEnable() => _nextTime = Time.time + _interval;

        private void Update()
        {
            if (_effect == null) return;
            if (Time.time < _nextTime) return;

            _nextTime = Time.time + _interval;
            _effect.Reinit();
        }
    }
}

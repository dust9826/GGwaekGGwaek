using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 벽 충돌을 차체 반응으로 넘긴다. <b>차량 루트에 붙는다</b> — <c>OnCollisionEnter</c> 는
    /// 콜라이더를 가진 오브젝트에서 불리고, <c>Body</c> 에는 콜라이더가 없다.
    ///
    /// 위치 킥과 스쿼시 <b>둘 다 충돌 세기에 비례한다.</b> <c>MMF_Player</c> 는
    /// <c>.asmref</c> 로 <c>MoreMountains.Tools</c> 어셈블리에 편입되어 있고
    /// <c>PPack.InGame</c> 이 이미 그것을 참조하므로 타입으로 직접 부를 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleImpactRelay : MonoBehaviour
    {
        [SerializeField] private VehicleBodyMotion _bodyMotion;
        [SerializeField] private MMF_Player _impactFeedback;

        [Header("문턱 (법선 방향 상대속도, m/s)")]
        [SerializeField, Min(0f)] private float _minImpactSpeed = 3f;
        [SerializeField, Min(0.01f)] private float _maxImpactSpeed = 12f;

        [Header("스쿼시")]
        [Tooltip("정면으로 세게 박았을 때의 배율 상한. 1 이면 안 눌리고 낮을수록 세게 눌린다.")]
        [SerializeField, Range(0.5f, 1f)] private float _squashRemapAtFullImpact = 0.85f;

        [Header("발광")]
        [Tooltip("정면으로 세게 박았을 때의 _EmissionColor. 씬의 블룸이 이걸 번지게 한다.")]
        [SerializeField, ColorUsage(false, true)]
        private Color _impactFlashColor = new Color(3f, 1.6f, 0.5f);

        [Header("충돌음")]
        [SerializeField, Range(0f, 1f)] private float _minimumImpactVolume = 0.24f;
        [SerializeField, Range(0f, 1f)] private float _maximumImpactVolume = 0.82f;

        private MMF_SquashAndStretch _squash;
        private MMF_Flicker _flicker;
        private MMF_Sound _impactSound;

        private void Reset()
        {
            _bodyMotion = GetComponentInChildren<VehicleBodyMotion>();
            _impactFeedback = GetComponentInChildren<MMF_Player>();
        }

        private void Awake()
        {
            if (_impactFeedback == null) return;
            _squash = _impactFeedback.GetFeedbackOfType<MMF_SquashAndStretch>();
            _flicker = _impactFeedback.GetFeedbackOfType<MMF_Flicker>();
            _impactSound = _impactFeedback.GetFeedbackOfType<MMF_Sound>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;

            ContactPoint contact = collision.GetContact(0);

            // 바닥 접촉과 착지는 이번 범위가 아니다. 법선이 거의 수직이면 바닥이다.
            if (Mathf.Abs(contact.normal.y) > 0.7f) return;

            float closingSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (closingSpeed < _minImpactSpeed) return;

            float strength01 = Mathf.InverseLerp(_minImpactSpeed, _maxImpactSpeed, closingSpeed);

            // 법선은 벽에서 우리 쪽을 가리킨다. 차체는 관성으로 벽 쪽에 계속 밀렸다가 돌아오므로
            // 반대 방향이다.
            if (_bodyMotion != null) _bodyMotion.AddImpulse(strength01, -contact.normal);

            // 세기를 PlayFeedbacks 의 intensity 인자로 넘기면 안 된다. 그 값은 RemapCurveOne 에
            // 곱해지는데(MMF_SquashAndStretch.cs:175) 우리 배율은 1 미만이라 곱할수록 0 에
            // 가까워진다 — 약한 충돌이 오히려 더 세게 눌린다. 배율 자체를 1(안 눌림)과 상한
            // 사이에서 보간해야 비례한다.
            if (_squash != null) _squash.RemapCurveOne = Mathf.Lerp(1f, _squashRemapAtFullImpact, strength01);

            // 발광은 색을 어둡게 해서 약하게 만든다. MMF_Flicker 는 코루틴 끝에서 초기 색을
            // 무조건 되돌려주므로(MMF_Flicker.cs:317) 밝기를 매번 덮어써도 복귀가 안 깨진다.
            if (_flicker != null)
            {
                _flicker.FlickerColor = new Color(
                    _impactFlashColor.r * strength01,
                    _impactFlashColor.g * strength01,
                    _impactFlashColor.b * strength01,
                    1f);
            }

            if (_impactSound != null)
            {
                float volume = Mathf.Lerp(_minimumImpactVolume, _maximumImpactVolume, strength01);
                float centerPitch = Mathf.Lerp(1.04f, 0.96f, strength01);
                _impactSound.MinVolume = volume * 0.9f;
                _impactSound.MaxVolume = volume;
                _impactSound.MinPitch = centerPitch - 0.025f;
                _impactSound.MaxPitch = centerPitch + 0.025f;
            }

            if (_impactFeedback != null) _impactFeedback.PlayFeedbacks();
        }
    }
}

using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <see cref="PenguinLocomotion"/>/<see cref="PenguinInputReader"/> 의 상태를 Animator
    /// 파라미터로 옮긴다. <c>AC_Penguin.controller</c> 가 이 이름·타입을 그대로 선언해야 한다:
    ///
    /// <list type="bullet">
    /// <item><c>Speed</c> (float) — Idle ↔ Walk</item>
    /// <item><c>IsGrounded</c> (bool)</item>
    /// <item><c>IsSliding</c> (bool) — Shift+Space 공중 전환과 지상 슬라이드 동안 Swim 클립 재사용</item>
    /// <item><c>Jump</c> (trigger)</item>
    /// </list>
    ///
    /// <para>
    /// 눈덩이 밀기는 여기로 들어오지 않는다. 밀기 자세는 <see cref="PenguinSnowballPush"/> 가
    /// <c>LateUpdate</c> 에서 본 세 개(<c>DEF-spine.004</c>, <c>DEF-Wing.L/R</c>)를 직접
    /// 돌려서 만들고, 그 아래 하반신은 이 컨트롤러의 <c>Idle ↔ Walk</c> 가 그대로 담당한다 —
    /// 그래서 밀면서 걸으면 다리가 평소처럼 뒤뚱거린다.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class PenguinAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private PenguinLocomotion _locomotion;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int IsSlidingHash = Animator.StringToHash("IsSliding");
        private static readonly int JumpHash = Animator.StringToHash("Jump");

        private Animator _animator;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            if (_locomotion != null) _locomotion.Jumped += OnJumped;
        }

        private void OnDisable()
        {
            if (_locomotion != null) _locomotion.Jumped -= OnJumped;
        }

        private void Update()
        {
            if (_locomotion == null) return;

            _animator.SetFloat(SpeedHash, _locomotion.Speed);
            _animator.SetBool(IsGroundedHash, _locomotion.IsGrounded);
            _animator.SetBool(IsSlidingHash, _locomotion.IsSlidePose);
        }

        private void OnJumped()
        {
            _animator.SetTrigger(JumpHash);
        }
    }
}

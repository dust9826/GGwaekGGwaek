using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// Adelie 애니메이션 팩의 모든 클립을 테스트 씬에서 한 개씩 확인한다.
    /// 게임 플레이용 상태 머신이 아니라 원본 클립 검수만 소유한다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class PenguinAnimationPreview : MonoBehaviour
    {
        private static readonly string[] StateNames =
        {
            "Idle_Adelie",
            "Idle2_Adelie",
            "Walk_Adelie",
            "Run_Adelie",
            "Jump_Adelie",
            "JumpWater_Adelie",
            "Swim_Adelie",
            "Sleep_Adelie",
            "Eat_Adelie",
            "Attack_Adelie",
            "Attack_Adelie_2",
            "Damage_Adelie",
            "Scream_Adelie",
            "Dead_Adelie"
        };

        private Animator _animator;
        private int _index;
        private GUIStyle _boxStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.applyRootMotion = false;
            PlayCurrent();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                SelectRelative(-1);
            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                SelectRelative(1);
            if (keyboard.spaceKey.wasPressedThisFrame) PlayCurrent();
        }

        private void SelectRelative(int offset)
        {
            _index = (_index + offset + StateNames.Length) % StateNames.Length;
            PlayCurrent();
        }

        private void PlayCurrent()
        {
            if (_animator == null) return;
            _animator.Play(StateNames[_index], 0, 0f);
        }

        private void OnGUI()
        {
            EnsureStyles();

            const float width = 520f;
            Rect panel = new Rect(24f, 24f, width, 126f);
            GUI.Box(panel, GUIContent.none, _boxStyle);
            GUI.Label(new Rect(44f, 40f, width - 40f, 34f),
                $"{_index + 1:00}/{StateNames.Length:00}  {StateNames[_index]}", _titleStyle);
            GUI.Label(new Rect(44f, 82f, width - 40f, 54f),
                "←/→ 또는 A/D : 이전·다음 애니메이션\nSpace : 현재 애니메이션 처음부터 다시 재생",
                _bodyStyle);
        }

        private void EnsureStyles()
        {
            if (_boxStyle != null) return;

            _boxStyle = new GUIStyle(GUI.skin.box);
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.88f, 0.92f, 1f) }
            };
        }
    }
}

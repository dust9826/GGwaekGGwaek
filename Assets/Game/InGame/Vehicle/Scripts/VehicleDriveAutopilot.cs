using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PPack
{
    /// <summary>
    /// 정해진 입력 시퀀스를 키보드에 밀어 넣어 <see cref="VehicleDriveProbe"/> 의 측정 조건을
    /// 재현한다. 손으로 몰면 매번 조건이 달라 튜닝 전후를 비교할 수 없다.
    ///
    /// 실제 키를 흉내내므로 입력 자산·액션 맵·바인딩까지 같은 경로를 탄다 — 모델에 테스트용
    /// 구멍을 뚫지 않아도 되는 이유다. <see cref="VehicleController"/> 는 이 클래스를 모른다.
    ///
    /// <b>Unity 앱이 OS 최상위가 아니면 아무 일도 일어나지 않는다.</b>
    /// <c>InputSettings.backgroundBehavior</c> 가 <c>ResetAndDisableNonBackgroundDevices</c> 라
    /// 포커스를 잃으면 키보드 장치가 리셋·비활성화되고 큐에 넣은 입력이 버려진다. 단계 로그는
    /// 다 찍히는데 차만 제자리인 상태가 되어 모델이 고장난 것처럼 보인다 — 실제로 그렇게
    /// 오진한 적이 있다. 재기 전에 유니티 창을 앞으로 올린다.
    ///
    /// 평소에는 오브젝트가 꺼져 있고, 잴 때만 켠다.
    /// </summary>
    public sealed class VehicleDriveAutopilot : MonoBehaviour
    {
        private readonly struct Step
        {
            public readonly string Label;
            public readonly float Duration;
            public readonly bool Forward;
            public readonly bool Back;
            public readonly bool Left;
            public readonly bool Drift;

            public Step(string label, float duration, bool forward = false, bool back = false,
                        bool left = false, bool drift = false)
            {
                Label = label;
                Duration = duration;
                Forward = forward;
                Back = back;
                Left = left;
                Drift = drift;
            }
        }

        // 튜닝 값이 아니라 시험 절차이므로 인스펙터로 빼지 않는다 — 매번 같아야 비교가 성립한다.
        //
        // 직진 구간을 짧게 끊어 선회를 코스 한가운데에서 시작시킨다. 길게 두면 선회가 시작될 때
        // 이미 벽 앞이라 드리프트가 바깥으로 밀리며 박히는데, 그때 나온 슬립각 89° 와 정지 0.02s 를
        // 모델이 낸 값으로 읽어 없어도 될 코드를 넣은 적이 있다. 선회 둘은 원을 그리므로 안전하다.
        private static readonly Step[] Sequence =
        {
            new Step("정지 대기", 0.5f),
            new Step("기준1 — 전속 가속", 1.5f, forward: true),
            new Step("기준2 — 손 떼고 코스팅", 2.0f),
            new Step("기준7 — 고속 전타(드리프트 안 누름)", 2.5f, forward: true, left: true),
            new Step("기준3 — 고속 전타 + 드리프트 홀드", 3.0f, forward: true, left: true, drift: true),
            new Step("정지 대기", 2.0f),
            new Step("기준4 — 제자리 회전", 2.0f, left: true),
            new Step("기준6 — 저속 선회", 2.0f, back: true, left: true),
            new Step("종료 대기", 1.0f),
        };

        private int _index;
        private float _stepEndTime;
        private bool _finished;

        private void OnEnable()
        {
            _index = 0;
            _finished = false;
            _stepEndTime = Time.time + Sequence[0].Duration;
            Debug.Log($"[VehicleDriveAutopilot] 시작 — {Sequence.Length}단계, 총 {TotalDuration():F1}s");
            Debug.Log($"[VehicleDriveAutopilot] ▶ {Sequence[0].Label}");
        }

        private void Update()
        {
            if (_finished) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogError("[VehicleDriveAutopilot] 키보드 장치가 없다. 헤드리스에서는 못 쓴다.");
                enabled = false;
                return;
            }

            if (Time.time >= _stepEndTime)
            {
                _index++;
                if (_index >= Sequence.Length)
                {
                    _finished = true;
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    Debug.Log("[VehicleDriveAutopilot] 끝. 위 로그의 [VehicleDriveProbe] 수치를 읽는다.");
                    return;
                }

                _stepEndTime = Time.time + Sequence[_index].Duration;
                Debug.Log($"[VehicleDriveAutopilot] ▶ {Sequence[_index].Label}");
            }

            Press(keyboard, Sequence[_index]);
        }

        private void OnDisable()
        {
            // 켠 채로 플레이를 끄면 눌린 키가 남는다.
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        }

        private static void Press(Keyboard keyboard, in Step step)
        {
            // 누른 상태를 유지하려면 매 프레임 같은 상태를 다시 큐에 넣어야 한다.
            // 조합을 if 사슬로 나열하면 하나를 빠뜨렸을 때 조용히 안 눌린 채로 측정이 끝난다.
            var keys = new List<Key>(4);
            if (step.Forward) keys.Add(Key.W);
            if (step.Back) keys.Add(Key.S);
            if (step.Left) keys.Add(Key.A);
            if (step.Drift) keys.Add(Key.Space);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
        }

        private static float TotalDuration()
        {
            float total = 0f;
            foreach (Step step in Sequence) total += step.Duration;
            return total;
        }
    }
}

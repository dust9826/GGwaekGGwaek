using UnityEngine;

namespace PPack
{
    public enum EPenguinControlState
    {
        Normal,
        Sliding,
        CarryApproach,
        Carrying,
        SnowballSide,
        SnowballTop
    }

    /// <summary>
    /// 펭귄 입력을 어느 게임플레이 컴포넌트가 해석할지 정하는 단일 상태 소스.
    /// 이동 물리와 눈덩이 게임플레이는 소유하지 않고 허용된 상태 전환만 관리한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PenguinControlState : MonoBehaviour
    {
        public EPenguinControlState Current { get; private set; }

        public bool IsSnowballState => Current is EPenguinControlState.SnowballSide
            or EPenguinControlState.SnowballTop;

        public bool TryTransitionTo(EPenguinControlState next)
        {
            if (next == Current) return true;
            if (!CanTransition(Current, next)) return false;

            Current = next;
            return true;
        }

        private static bool CanTransition(EPenguinControlState current, EPenguinControlState next)
        {
            if (next == EPenguinControlState.Normal) return true;

            return current switch
            {
                EPenguinControlState.Normal => next is EPenguinControlState.Sliding
                    or EPenguinControlState.CarryApproach or EPenguinControlState.Carrying
                    or EPenguinControlState.SnowballSide,
                EPenguinControlState.CarryApproach => next == EPenguinControlState.Carrying,
                EPenguinControlState.Carrying => false,
                EPenguinControlState.Sliding => next == EPenguinControlState.SnowballSide,
                EPenguinControlState.SnowballSide => next == EPenguinControlState.SnowballTop,
                _ => false
            };
        }
    }
}

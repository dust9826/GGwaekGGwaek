using System;
using UnityEngine;

namespace PPack
{
    public class PlayerState : MonoBehaviour
    {
        [SerializeField] private EPlayerState _current = EPlayerState.Vacuum;

        public EPlayerState Current => _current;
        public event Action<EPlayerState> StateChanged;

        public void SetState(EPlayerState state)
        {
            if (state == _current)
            {
                return;
            }

            _current = state;
            StateChanged?.Invoke(_current);
        }
    }
}

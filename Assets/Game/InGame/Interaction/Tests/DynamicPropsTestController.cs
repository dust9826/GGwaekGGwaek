using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PPack
{
    public sealed class DynamicPropsTestController : MonoBehaviour
    {
        [SerializeField] private DumpsterLidController _dumpster;
        [SerializeField] private RollingBarrel _barrel;
        [SerializeField] private BreakableHydrant _hydrant;
        [SerializeField] private Rigidbody _impactBall;

        private Vector3 _barrelStartPosition;
        private Vector3 _impactBallStartPosition;

        public void Configure(
            DumpsterLidController dumpster,
            RollingBarrel barrel,
            BreakableHydrant hydrant,
            Rigidbody impactBall)
        {
            _dumpster = dumpster;
            _barrel = barrel;
            _hydrant = hydrant;
            _impactBall = impactBall;
        }

        private void Awake()
        {
            if (_barrel != null) _barrelStartPosition = _barrel.transform.position;
            if (_impactBall != null) _impactBallStartPosition = _impactBall.position;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame && _dumpster != null) _dumpster.Toggle();
            if (keyboard.digit2Key.wasPressedThisFrame && _barrel != null)
                _barrel.LayDownAndRoll(_barrelStartPosition + Vector3.up * 0.5f, Vector3.right, 5.5f);
            if (keyboard.digit3Key.wasPressedThisFrame) LaunchImpactBall();
            if (keyboard.rKey.wasPressedThisFrame)
                SceneManager.LoadScene(SceneManager.GetActiveScene().path, LoadSceneMode.Single);
        }

        private void LaunchImpactBall()
        {
            if (_impactBall == null || _hydrant == null || _hydrant.IsBroken) return;

            _impactBall.position = _impactBallStartPosition;
            _impactBall.linearVelocity = Vector3.zero;
            _impactBall.angularVelocity = Vector3.zero;
            Vector3 direction = (_hydrant.transform.position - _impactBall.position).normalized;
            _impactBall.AddForce(direction * 9f, ForceMode.VelocityChange);
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(18f, 18f, 420f, 172f), string.Empty);
            GUI.Label(new Rect(34f, 28f, 350f, 24f), "DYNAMIC PROP TEST");
            GUI.Label(new Rect(34f, 54f, 350f, 22f), "1  Dumpster lids open / close");
            GUI.Label(new Rect(34f, 76f, 350f, 22f), "2  Lay down and roll the barrel");
            GUI.Label(new Rect(34f, 98f, 350f, 22f), "3  Launch impact ball at hydrant");
            GUI.Label(new Rect(34f, 120f, 350f, 22f), "R  Reset test scene");
            GUI.Label(new Rect(34f, 142f, 380f, 22f), "WASD  Move  |  Shift  Run  |  Space  Jump");
            GUI.Label(new Rect(34f, 164f, 380f, 22f), "Mouse  Orbit the current PF_Penguin camera");
        }
    }
}

using System.Collections;
using System.Linq;
using UnityEngine;

namespace PPack
{
    /// <summary>테스트 씬이 열린 뒤 눈덩이를 늦게 투입해 Feel 전 구간을 눈으로 확인하게 한다.</summary>
    public sealed class SnowGiftMachineFeelTestDriver : MonoBehaviour
    {
        [SerializeField] private Transform _machine;
        [SerializeField] private SnowBallCarrier _snowball;
        [SerializeField, Min(0f)] private float _delay = 1.2f;

        public void Configure(Transform machine, SnowBallCarrier snowball, float delay)
        {
            _machine = machine;
            _snowball = snowball;
            _delay = delay;
        }

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(_delay);
            if (_machine == null || _snowball == null) yield break;

            Rigidbody body = _snowball.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = true;
            _snowball.transform.SetPositionAndRotation(
                _machine.TransformPoint(new Vector3(0f, 1.57f, -5.25f)),
                _machine.rotation);
            Physics.SyncTransforms();
            if (body != null) body.isKinematic = false;

            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(0.18f);
            SnowGiftMachinePresentation presentation = _machine.GetComponent<SnowGiftMachinePresentation>();
            MeshRenderer intakeRenderer = presentation.IntakeVisual.GetComponent<MeshRenderer>();
            Debug.Log($"[SnowGiftMachineFeelTest] intake visible={intakeRenderer.enabled}, " +
                      $"scale={presentation.IntakeVisual.localScale:F3}");

            yield return new WaitForSeconds(0.42f);
            Debug.Log($"[SnowGiftMachineFeelTest] digest scale={presentation.MachineMotionRoot.localScale:F3}, " +
                      $"position={presentation.MachineMotionRoot.localPosition:F3}");

            yield return new WaitForSeconds(0.62f);
            Debug.Log($"[SnowGiftMachineFeelTest] gift preview scale={presentation.GiftPopDriver.localScale:F3}, " +
                      $"position={presentation.GiftPopDriver.localPosition:F3}");

            yield return new WaitForSeconds(0.62f);
            int spawnedGiftCount = FindObjectsByType<Gift>(FindObjectsSortMode.None).Count(gift => gift.enabled);
            Debug.Log($"[SnowGiftMachineFeelTest] spawned gifts={spawnedGiftCount}, " +
                      $"processing={presentation.IsProcessing}");
        }
    }
}

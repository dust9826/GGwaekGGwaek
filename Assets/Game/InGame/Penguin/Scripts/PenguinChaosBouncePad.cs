using UnityEngine;

namespace PPack
{
    /// <summary>펭귄과 소품을 함께 날려 보내는 물리 놀이터용 발사 패드.</summary>
    public sealed class PenguinChaosBouncePad : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _upVelocity = 11f;
        [SerializeField, Min(0f)] private float _forwardVelocity = 2.5f;
        [SerializeField, Min(0f)] private float _spinVelocity = 4f;

        private void OnCollisionEnter(Collision collision)
        {
            Rigidbody body = collision.rigidbody;
            if (body == null || body.isKinematic) return;

            body.AddForce(Vector3.up * _upVelocity + transform.forward * _forwardVelocity,
                ForceMode.VelocityChange);
            body.AddTorque(Random.onUnitSphere * _spinVelocity, ForceMode.VelocityChange);
        }
    }
}

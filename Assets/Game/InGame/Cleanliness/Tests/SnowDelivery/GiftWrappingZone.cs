using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 중앙 광장에 완전히 올라온 눈덩이를 성장 단계에 대응하는 선물로 바꾼다.
    /// SnowDelivery 테스트 씬 전용이며 네트워크 선물이 생기기 전까지 로컬 판에서만 동작한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class GiftWrappingZone : MonoBehaviour
    {
        [SerializeField] private SnowCpuStage _snowStage;
        [SerializeField] private Gift _giftPrefab;
        [SerializeField, Min(0.1f)] private float _usableRadiusM = 4.3f;
        [SerializeField] private float _surfaceY = 0.33f;

        public static EGiftBoxKind KindForStage(ESnowBallGrowthStage stage)
        {
            return stage switch
            {
                ESnowBallGrowthStage.Seed => EGiftBoxKind.Blue,
                ESnowBallGrowthStage.Stage1 => EGiftBoxKind.Blue,
                ESnowBallGrowthStage.Stage2 => EGiftBoxKind.Green,
                ESnowBallGrowthStage.Stage3 => EGiftBoxKind.Yellow,
                ESnowBallGrowthStage.Stage4 => EGiftBoxKind.Red,
                _ => EGiftBoxKind.Blue,
            };
        }

        public void Configure(SnowCpuStage snowStage, Gift giftPrefab, float usableRadiusM, float surfaceY)
        {
            _snowStage = snowStage;
            _giftPrefab = giftPrefab;
            _usableRadiusM = Mathf.Max(0.1f, usableRadiusM);
            _surfaceY = surfaceY;

            BoxCollider trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, SnowBallCpu.MaxRadiusM, 0f);
            trigger.size = new Vector3(
                _usableRadiusM * 2f,
                SnowBallCpu.MaxRadiusM * 2f + 0.4f,
                _usableRadiusM * 2f);
        }

        public bool TryWrap(SnowBallCarrier ball)
        {
            if (_snowStage == null || _giftPrefab == null || ball == null || !ball.HasSupport) return false;

            Vector3 offset = ball.transform.position - transform.position;
            float allowedCenterRadiusM = _usableRadiusM - ball.RadiusM;
            if (allowedCenterRadiusM <= 0f) return false;
            if (offset.x * offset.x + offset.z * offset.z > allowedCenterRadiusM * allowedCenterRadiusM)
                return false;

            EGiftBoxKind kind = KindForStage(ball.GrowthStage);
            Vector3 giftPosition = new Vector3(ball.transform.position.x, _surfaceY + 0.02f,
                ball.transform.position.z);
            if (!_snowStage.TryConsumeBallForLocalConversion(ball, out _)) return false;

            Gift gift = Instantiate(_giftPrefab, giftPosition, Quaternion.identity);
            gift.gameObject.SetActive(true);
            EnsureMovable(gift);
            gift.SetKind(kind);
            return true;
        }

        private void OnTriggerStay(Collider other)
        {
            SnowBallCarrier ball = other.GetComponentInParent<SnowBallCarrier>();
            if (ball != null) TryWrap(ball);
        }

        private static void EnsureMovable(Gift gift)
        {
            if (gift.TryGetComponent(out Rigidbody _)) return;

            Rigidbody body = gift.gameObject.AddComponent<Rigidbody>();
            body.mass = 2f;
            body.linearDamping = 0.8f;
            body.angularDamping = 0.8f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
}

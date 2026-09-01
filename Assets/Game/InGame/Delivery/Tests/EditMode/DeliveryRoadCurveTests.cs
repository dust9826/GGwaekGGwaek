using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace PPack
{
    public sealed class DeliveryRoadCurveTests
    {
        [Test]
        public void 직선은_거리로_정확히_평가한다()
        {
            var curve = new DeliveryRoadCurve(
                new[] { Vector3.zero, new Vector3(0f, 0f, 10f) }, 2f, 0.25f);

            Assert.That(curve.Length, Is.EqualTo(10f).Within(0.001f));
            DeliveryRoadPose pose = curve.Evaluate(5f);
            Assert.That(pose.Position, Is.EqualTo(new Vector3(0f, 0f, 5f)).Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(pose.Tangent, Is.EqualTo(Vector3.forward).Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(pose.Right, Is.EqualTo(Vector3.right).Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [Test]
        public void ㄱ자_도로는_코너를_곡선으로_연결한다()
        {
            var curve = new DeliveryRoadCurve(
                new[] { Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(10f, 0f, 10f) },
                2f, 0.1f);

            Assert.That(curve.Length, Is.GreaterThan(17f));
            bool foundDiagonalTangent = false;
            for (float distance = 0f; distance <= curve.Length; distance += 0.1f)
            {
                DeliveryRoadPose pose = curve.Evaluate(distance);
                if (pose.Tangent.x > 0.1f && pose.Tangent.z > 0.1f) foundDiagonalTangent = true;
            }
            Assert.IsTrue(foundDiagonalTangent, "90도 순간 회전이 아니라 대각 접선을 가진 코너여야 한다");
        }

        [Test]
        public void 코너에서_접선이_계단으로_튀지_않는다()
        {
            // 구간별 상수 접선을 쓰면 샘플 경계에서만 방향이 바뀌어 계단이 된다. 실측으로
            // 0.05m 마다 재면 대부분 그대로 있다가 한 번에 3.8도씩 튀었다(2026-08-16).
            // 트럭의 요 각속도 제한은 점프 하나가 그보다 작아서 이걸 못 걸러낸다.
            var curve = new DeliveryRoadCurve(
                new[] { Vector3.zero, new Vector3(0f, 0f, 12f), new Vector3(12f, 0f, 20f) }, 2f, 0.25f);

            const float step = 0.05f;
            float previousAngle = float.NaN;
            float maxStepDeg = 0f;
            for (float distance = 9f; distance <= 15f; distance += step)
            {
                Vector3 tangent = curve.Evaluate(distance).Tangent;
                float angle = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
                if (!float.IsNaN(previousAngle))
                    maxStepDeg = Mathf.Max(maxStepDeg, Mathf.Abs(Mathf.DeltaAngle(previousAngle, angle)));
                previousAngle = angle;
            }

            // 0.05m 당 1도면 5m/s 에서 100도/s — 실제 곡률이 만드는 각속도이지 계단이 아니다.
            Assert.That(maxStepDeg, Is.LessThan(1f),
                        $"접선이 한 번에 {maxStepDeg:F2}도 튄다 — 샘플 구간별 상수 접선으로 돌아갔다");
        }

        [Test]
        public void 도로는_높이값을_무시하고_XZ_평면에서_평가한다()
        {
            var curve = new DeliveryRoadCurve(
                new[] { new Vector3(0f, 5f, 0f), new Vector3(0f, -3f, 4f) }, 0f, 0.25f);

            Assert.That(curve.Evaluate(2f).Position.y, Is.Zero);
            Assert.That(curve.Length, Is.EqualTo(4f).Within(0.001f));
        }
    }
}

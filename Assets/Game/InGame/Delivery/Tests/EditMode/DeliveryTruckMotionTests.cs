using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class DeliveryTruckMotionTests
    {
        [Test]
        public void 직선은_속도_상한이_없다()
        {
            var curve = new DeliveryRoadCurve(
                new[] { Vector3.zero, new Vector3(0f, 0f, 40f) }, 2f, 0.25f);

            Assert.That(curve.CurvatureAt(20f, 2f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(DeliveryTruckMotion.CornerSpeedLimit(curve.CurvatureAt(20f, 2f), 2.5f),
                        Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void 코너는_곡률만큼_속도_상한을_내린다()
        {
            var curve = new DeliveryRoadCurve(
                new[] { Vector3.zero, new Vector3(0f, 0f, 20f), new Vector3(20f, 0f, 20f) },
                2f, 0.1f);

            float cornerCurvature = 0f;
            for (float distance = 0f; distance <= curve.Length; distance += 0.1f)
                cornerCurvature = Mathf.Max(cornerCurvature, curve.CurvatureAt(distance, 2f));

            Assert.That(cornerCurvature, Is.GreaterThan(0.1f), "90도 코너는 곡률이 잡혀야 한다");
            float limit = DeliveryTruckMotion.CornerSpeedLimit(cornerCurvature, 2.5f);
            Assert.That(limit, Is.LessThan(5f), "순항 5m/s 보다 낮은 상한이 나와야 코너에서 느려진다");
        }

        [Test]
        public void 상한이_있는_지점에_닿기_전에_미리_감속한다()
        {
            const float brake = 4f;
            const float cornerLimit = 3f;
            const float cruise = 5f;

            // 멀리 있으면 순항을 안 깎고, 가까워질수록 코너 상한으로 수렴한다.
            float far = DeliveryTruckMotion.ApproachSpeedLimit(cornerLimit, 10f, brake);
            float near = DeliveryTruckMotion.ApproachSpeedLimit(cornerLimit, 1f, brake);
            Assert.That(far, Is.GreaterThan(cruise));
            Assert.That(near, Is.LessThan(cruise));
            Assert.That(DeliveryTruckMotion.ApproachSpeedLimit(cornerLimit, 0f, brake),
                        Is.EqualTo(cornerLimit).Within(0.001f));
        }

        [Test]
        public void 정지에서_가속하면_순항_속도에서_정확히_멈춘다()
        {
            const float cruise = 5f;
            float speed = 0f;
            for (int step = 0; step < 500; step++)
                speed = DeliveryTruckMotion.StepSpeed(speed, cruise, 2f, 4f, 0.02f);

            Assert.That(speed, Is.EqualTo(cruise).Within(0.0001f),
                        "가속 모델이 순항 속도를 넘거나 못 미치면 안 된다");
        }

        [Test]
        public void 가속과_제동은_비대칭이다()
        {
            float accelerated = DeliveryTruckMotion.StepSpeed(0f, 10f, 2f, 4f, 1f);
            float braked = DeliveryTruckMotion.StepSpeed(10f, 0f, 2f, 4f, 1f);
            Assert.That(accelerated, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(braked, Is.EqualTo(6f).Within(0.0001f));
        }

        [Test]
        public void 제동거리_안에서_정지선에_선다()
        {
            const float brake = 4f;
            float stopDistance = DeliveryTruckMotion.BrakingDistance(5f, brake);
            Assert.That(stopDistance, Is.EqualTo(3.125f).Within(0.001f));

            float speed = 5f;
            float travelled = 0f;
            for (int step = 0; step < 500 && speed > 0.001f; step++)
            {
                // DeliveryTruck.StepSpeed 와 같은 보정 — 이번 스텝에 갈 거리를 미리 뺀다.
                float remaining = stopDistance - travelled - speed * 0.02f;
                float target = Mathf.Min(5f, DeliveryTruckMotion.ApproachSpeedLimit(0f, remaining, brake));
                speed = DeliveryTruckMotion.StepSpeed(speed, target, 2f, brake, 0.02f);
                travelled += speed * 0.02f;
            }

            Assert.That(travelled, Is.LessThanOrEqualTo(stopDistance + 0.01f), "정지선을 넘으면 안 된다");
            Assert.That(speed, Is.LessThan(0.05f));
        }

        [Test]
        public void 최대_감속도는_순항에서_1미터_안에_세운다()
        {
            // 12.5m/s² 는 "순항 5m/s 에서 1m" 를 역산한 값이다 — DeliveryTruck._emergencyBrakeMps2.
            Assert.That(DeliveryTruckMotion.BrakingDistance(5f, 12.5f), Is.EqualTo(1f).Within(0.001f));
            Assert.That(DeliveryTruckMotion.BrakingDistance(5f, 4f), Is.GreaterThan(3f));
        }

        [Test]
        public void 정지선은_남은_거리만으로_여유_감속과_급정지를_가른다()
        {
            // 계획은 언제나 평상시 감속도(4)로 하고, 낼 수 있는 감속도는 12.5 다.
            // 눈이 원래 있었는지 방금 쌓였는지 코드는 모른다 — 남은 거리가 가른다.
            const float planned = 4f;
            const float maximum = 12.5f;
            const float cruise = 5f;

            float farTarget = DeliveryTruckMotion.ApproachSpeedLimit(0f, 4f, planned);
            Assert.That(farTarget, Is.GreaterThan(cruise),
                        "검사 창 끝(4m)에서 보이는 눈은 아직 순항을 안 깎는다");

            // 4m 에서 발견해 세우는 경우: 필요한 감속도가 최대치에 한참 못 미친다.
            Assert.That(RequiredDecel(cruise, 4f), Is.LessThan(planned + 0.01f));
            // 1m 앞에 갑자기 쌓인 경우: 최대 감속도가 다 나와야 겨우 선다.
            Assert.That(RequiredDecel(cruise, 1f), Is.EqualTo(maximum).Within(0.01f));
        }

        private static float RequiredDecel(float speed, float distance) => speed * speed / (2f * distance);

        [Test]
        public void 사소한_꺾임도_방향_오차만큼_속도를_누른다()
        {
            // 149.6도 급회전에서는 반 차체 길이(2m) 허용치로도 충분히 눌렸지만, 30도 안팎의
            // 흔한 꺾임에서는 2×120/30=8 이 순항 5m/s 보다 커서 전혀 안 눌렸다(2026-08-16,
            // 사용자가 "사소한 각도도 부드럽게" 요청하며 드러남). 허용치를 0.5m 로 줄인 뒤에는
            // 30도에서 0.5×120/30=2 로 순항보다 낮아져 실제로 개입한다.
            const float maxYawRate = 120f;
            const float tolerance = 0.5f;

            float limitAt30 = DeliveryTruckMotion.HeadingCatchUpSpeedLimit(30f, tolerance, maxYawRate);
            Assert.That(limitAt30, Is.EqualTo(2f).Within(0.001f));
            Assert.That(limitAt30, Is.LessThan(5f), "순항 5m/s 보다 낮아야 실제로 느려진다");

            // 예전 값(반 차체 길이 2m)이었다면 30도에서 개입하지 않았다는 것도 같이 못 박는다.
            float oldLimitAt30 = DeliveryTruckMotion.HeadingCatchUpSpeedLimit(30f, 2f, maxYawRate);
            Assert.That(oldLimitAt30, Is.GreaterThan(5f), "예전 허용치는 30도에서 순항을 못 눌렀다");
        }

        [Test]
        public void 방향_오차가_클수록_속도_상한이_낮다()
        {
            const float maxYawRate = 120f;
            const float tolerance = 0.5f;
            float small = DeliveryTruckMotion.HeadingCatchUpSpeedLimit(10f, tolerance, maxYawRate);
            float large = DeliveryTruckMotion.HeadingCatchUpSpeedLimit(149.6f, tolerance, maxYawRate);
            Assert.That(large, Is.LessThan(small));
            Assert.That(DeliveryTruckMotion.HeadingCatchUpSpeedLimit(0f, tolerance, maxYawRate),
                        Is.EqualTo(float.PositiveInfinity), "오차가 없으면 누를 이유가 없다");
        }

        [Test]
        public void 노드의_꺾임은_각도가_클수록_상한이_낮다()
        {
            float gentle = DeliveryTruckMotion.TurnSpeedLimit(20f, 4f, 2.5f);
            float sharp = DeliveryTruckMotion.TurnSpeedLimit(90f, 4f, 2.5f);
            Assert.That(sharp, Is.LessThan(gentle));
            Assert.That(DeliveryTruckMotion.TurnSpeedLimit(0f, 4f, 2.5f), Is.EqualTo(float.PositiveInfinity),
                        "직진으로 이어지는 노드는 감속시키지 않는다");
        }
    }
}

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class ThiefRaidPlayModeTests
    {
        private GameObject _root;
        private NavMeshSurface _surface;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__ThiefRaid");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "__TEST__NavMeshFloor";
            floor.transform.SetParent(_root.transform);
            floor.transform.position = new Vector3(0f, -0.1f, 0f);
            floor.transform.localScale = new Vector3(24f, 0.2f, 24f);

            _surface = _root.AddComponent<NavMeshSurface>();
            _surface.collectObjects = CollectObjects.Children;
            _surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            _surface.BuildNavMesh();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_surface != null) _surface.RemoveData();
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 먼_플레이어는_웅크리고_가까운_플레이어는_달려_피한다()
        {
            GameObject thief = CreateMover(new Vector3(0f, 0f, -6f), out ThiefMovement movement);
            GameObject player = new GameObject("__TEST__VisiblePenguin");
            player.transform.SetParent(_root.transform);
            player.transform.position = new Vector3(0f, 0f, 3f);
            player.AddComponent<PenguinLocomotion>();
            player.GetComponent<Rigidbody>().isKinematic = true;
            yield return null;

            movement.MoveTo(Vector3.zero, false);
            Assert.That(movement.CurrentGait, Is.EqualTo(EThiefGait.Crouch));
            NavMeshAgent agent = thief.GetComponent<NavMeshAgent>();
            Assert.That(Vector2.Distance(new Vector2(agent.destination.x, agent.destination.z), Vector2.zero),
                Is.LessThan(0.01f),
                "멀리 보이는 플레이어 때문에 보관소 접근 경로를 이탈하면 안 된다");

            player.transform.position = new Vector3(0f, 0f, -3f);
            yield return new WaitForSeconds(0.25f);
            movement.MoveTo(Vector3.zero, false);
            Assert.That(movement.CurrentGait, Is.EqualTo(EThiefGait.Run));
            Assert.That(Vector3.Distance(agent.destination, Vector3.zero), Is.GreaterThan(0.1f),
                "가까운 플레이어에게서는 직접 경로 대신 회피 지점을 사용해야 한다");
            Vector3 avoidanceDestination = agent.destination;

            agent.isStopped = true;
            player.transform.position = thief.transform.position + thief.transform.forward * 8f;
            yield return new WaitForSeconds(0.25f);
            movement.MoveTo(Vector3.zero, false);
            Assert.That(movement.CurrentGait, Is.EqualTo(EThiefGait.Crouch));
            Assert.That(Vector3.Distance(agent.destination, avoidanceDestination), Is.LessThan(0.01f),
                "가까운 위협을 벗어나도 선택한 우회 지점까지 이동해야 왕복하지 않는다");

            Object.Destroy(player);
            yield return new WaitForSeconds(0.85f);
            movement.MoveTo(Vector3.zero, false);
            Assert.That(movement.CurrentGait, Is.EqualTo(EThiefGait.Run));
            Assert.That(movement.CurrentGait, Is.Not.EqualTo(EThiefGait.Walk));
            Object.Destroy(thief);
        }

        [UnityTest]
        public IEnumerator 시야를_완전히_잃어도_기억_창_동안_회피_지점을_다시_잡는다()
        {
            GameObject thief = CreateMover(new Vector3(0f, 0f, -6f), out ThiefMovement movement);
            SetPrivateFloat(movement, "_threatMemorySeconds", 1.5f);
            NavMeshAgent agent = thief.GetComponent<NavMeshAgent>();

            GameObject player = new GameObject("__TEST__VisiblePenguin");
            player.transform.SetParent(_root.transform);
            player.transform.position = new Vector3(0f, 0f, -3f);
            player.AddComponent<PenguinLocomotion>();
            player.GetComponent<Rigidbody>().isKinematic = true;
            yield return null;

            movement.MoveTo(Vector3.zero, false);
            Assert.That(movement.CurrentGait, Is.EqualTo(EThiefGait.Run));
            Vector3 firstAvoidance = agent.destination;
            Assert.That(Vector3.Distance(firstAvoidance, Vector3.zero), Is.GreaterThan(0.1f));

            Object.Destroy(player);
            yield return null;

            agent.Warp(firstAvoidance);
            movement.MoveTo(Vector3.zero, false);
            Vector3 secondAvoidance = agent.destination;
            Assert.That(Vector3.Distance(secondAvoidance, Vector3.zero), Is.GreaterThan(0.1f),
                "플레이어가 완전히 사라졌어도 기억 창 안에서는 직선 대신 마지막 목격 위치를 우회해야 한다");

            yield return new WaitForSeconds(1.6f);
            movement.MoveTo(Vector3.zero, false);
            Assert.That(Vector2.Distance(new Vector2(agent.destination.x, agent.destination.z), Vector2.zero),
                Is.LessThan(0.01f), "기억 창이 지나면 직선 경로로 돌아가야 한다");

            Object.Destroy(thief);
        }

        [UnityTest]
        public IEnumerator 영역의_선물을_선점해_들고_생성_위치로_돌아가면_둘_다_사라진다()
        {
            ThiefRaidSite site = CreateRaidSite();
            Gift gift = CreateGift(Vector3.zero);
            Vector3 home = new Vector3(0f, 0f, -6f);
            GameObject thiefObject = CreateMover(home, out _);
            ThiefActor actor = thiefObject.AddComponent<ThiefActor>();
            actor.Initialize(site, EGiftBoxKind.Red, home);
            yield return null;

            float timeout = Time.time + 18f;
            while (actor != null && Time.time < timeout)
            {
                if (actor.IsEscaping) actor.TickEscape(Time.deltaTime);
                else if (actor.HasClaimedGift) actor.TickSteal(Time.deltaTime);
                else actor.TickAcquireOrApproach(Time.deltaTime);
                yield return null;
            }

            Assert.That(actor == null, Is.True, "도둑은 귀가 지점에서 이탈해야 한다");
            Assert.That(gift == null, Is.True, "훔친 선물도 도둑과 함께 소비되어야 한다");
        }

        [UnityTest]
        public IEnumerator 같은_단계가_없으면_가장_가까운_상위_단계_선물을_고른다()
        {
            ThiefRaidSite site = CreateRaidSite();
            Gift red = CreateGift(new Vector3(-1f, 0f, 0f), true, EGiftBoxKind.Red);
            Gift yellow = CreateGift(new Vector3(1f, 0f, 0f), true, EGiftBoxKind.Yellow);
            yield return null;

            Assert.That(site.TryFindGift(Vector3.zero, EGiftBoxKind.Blue, out Gift selected), Is.True);
            Assert.That(selected, Is.SameAs(yellow), "Blue 다음 상위 단계인 Green이 없으면 Yellow를 골라야 한다");
            Assert.That(selected, Is.Not.SameAs(red));
        }

        [UnityTest]
        public IEnumerator 요청보다_낮은_단계의_선물은_훔치지_않는다()
        {
            ThiefRaidSite site = CreateRaidSite();
            CreateGift(Vector3.zero, true, EGiftBoxKind.Blue);
            yield return null;

            Assert.That(site.TryFindGift(Vector3.zero, EGiftBoxKind.Red, out _), Is.False);
        }

        [UnityTest]
        public IEnumerator 퇴장_지점에서는_멈춰서_5초를_센_뒤_사라진다()
        {
            ThiefRaidSite site = CreateRaidSite();
            GameObject thiefObject = CreateMover(Vector3.zero, out ThiefMovement movement);
            ThiefActor actor = thiefObject.AddComponent<ThiefActor>();
            SetPrivateFloat(actor, "_exitCountdownSeconds", 5f);
            actor.Initialize(site, EGiftBoxKind.Red, thiefObject.transform.position);

            actor.TickAcquireOrApproach(1.5f);
            Assert.That(actor.IsEscaping, Is.True);
            Assert.That(actor.TickEscape(0f), Is.EqualTo(EThiefTaskResult.Running));
            Assert.That(actor.CurrentAction, Is.EqualTo(EThiefAction.ExitCountdown));
            Assert.That(actor.ExitCountdownRemaining, Is.EqualTo(5f).Within(0.001f));
            Assert.That(movement.CurrentGait, Is.EqualTo(EThiefGait.Idle));
            Assert.That(actor.TryBeginImpact(new ImpactHit(EImpactCause.DirectAttack,
                Vector3.forward * 105f, actor.transform.position)), Is.False,
                "퇴장 카운트다운은 공격으로 취소하거나 초기화하지 않는다");

            Assert.That(actor.TickEscape(4.9f), Is.EqualTo(EThiefTaskResult.Running));
            Assert.That(actor, Is.Not.Null);
            Assert.That(actor.ExitCountdownRemaining, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(actor.TickEscape(0.11f), Is.EqualTo(EThiefTaskResult.Success));
            yield return null;
            Assert.That(actor == null, Is.True);
        }

        [UnityTest]
        public IEnumerator 빈손으로_퇴장할_때도_플레이어가_보이면_계속_달린다()
        {
            ThiefRaidSite site = CreateRaidSite();
            GameObject thiefObject = CreateMover(Vector3.zero, out ThiefMovement movement);
            ThiefActor actor = thiefObject.AddComponent<ThiefActor>();
            actor.Initialize(site, EGiftBoxKind.Red, new Vector3(0f, 0f, -6f));

            GameObject player = new GameObject("__TEST__VisiblePenguin");
            player.transform.SetParent(_root.transform);
            player.transform.position = new Vector3(0f, 0f, 8f);
            player.AddComponent<PenguinLocomotion>();
            player.GetComponent<Rigidbody>().isKinematic = true;
            yield return null;

            actor.TickAcquireOrApproach(1.5f);
            actor.TickEscape(0f);
            Assert.That(actor.HasCargo, Is.False);
            Assert.That(movement.CurrentGait, Is.EqualTo(EThiefGait.Run));
        }

        [UnityTest]
        public IEnumerator 리지드바디_없는_선물도_운반_기준점을_계속_따른다()
        {
            ThiefRaidSite site = CreateRaidSite();
            Gift gift = CreateGift(Vector3.zero, false);
            GameObject thiefObject = CreateMover(new Vector3(0f, 0f, -1.5f), out _);
            Transform carryAnchor = new GameObject("__TEST__CarryAnchor").transform;
            carryAnchor.SetParent(thiefObject.transform, false);
            carryAnchor.localPosition = new Vector3(0f, 2f, 0f);

            ThiefActor actor = thiefObject.AddComponent<ThiefActor>();
            FieldInfo carryAnchorField = typeof(ThiefActor).GetField("_carryAnchor",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(carryAnchorField, Is.Not.Null);
            carryAnchorField.SetValue(actor, carryAnchor);
            actor.Initialize(site, EGiftBoxKind.Red, thiefObject.transform.position);

            float timeout = Time.time + 5f;
            while (!actor.HasCargo && Time.time < timeout)
            {
                if (actor.HasClaimedGift) actor.TickSteal(Time.deltaTime);
                else actor.TickAcquireOrApproach(Time.deltaTime);
                yield return null;
            }

            Assert.That(actor.HasCargo, Is.True, "테스트 제한 시간 안에 선물을 들어야 한다");
            thiefObject.transform.position += Vector3.right * 2f;
            yield return null;

            Assert.That(Vector3.Distance(gift.transform.position, carryAnchor.position),
                Is.LessThan(0.01f), "리지드바디가 없어도 선물은 운반 기준점을 따라야 한다");
        }

        [UnityTest]
        public IEnumerator 손이_닿기_전에는_선물이_바닥에_있고_잡은_뒤_머리_위로_올라간다()
        {
            ThiefRaidSite site = CreateRaidSite();
            Gift gift = CreateGift(Vector3.zero);
            Rigidbody giftBody = gift.GetComponent<Rigidbody>();
            Collider giftCollider = gift.GetComponent<Collider>();
            GameObject thiefObject = CreateMover(new Vector3(0f, 0f, -0.9f), out _);
            Transform carryAnchor = new GameObject("__TEST__CarryAnchor").transform;
            carryAnchor.SetParent(thiefObject.transform, false);
            carryAnchor.localPosition = new Vector3(0f, 2.15f, 0.05f);
            ThiefActor actor = thiefObject.AddComponent<ThiefActor>();
            SetPrivateField(actor, "_carryAnchor", carryAnchor);
            actor.Initialize(site, EGiftBoxKind.Red, thiefObject.transform.position);
            Assert.That(actor.TickAcquireOrApproach(0f), Is.EqualTo(EThiefTaskResult.Success));

            Vector3 floorPosition = gift.transform.position;
            actor.TickSteal(0f);
            Assert.That(actor.LiftPhase, Is.EqualTo(EThiefLiftPhase.PrepareCrouch));
            Assert.That(ThiefGiftGeometry.TryCreate(gift, out ThiefGiftGeometry geometry), Is.True);
            Vector3 away = thiefObject.transform.position - geometry.WorldCenter;
            away.y = 0f;
            float surfaceGap = away.magnitude - geometry.SupportRadius(away, gift.transform.rotation) - 0.3f;
            Assert.That(surfaceGap, Is.GreaterThanOrEqualTo(0.24f),
                "도둑 몸은 선물 표면에서 집기 여유만큼 떨어져 있어야 한다");

            actor.TickSteal(0.4f);
            actor.TickSteal(0.55f);
            Assert.That(actor.LiftPhase, Is.EqualTo(EThiefLiftPhase.ReachFloor));
            Assert.That(Vector3.Distance(gift.transform.position, floorPosition), Is.LessThan(0.001f));
            Assert.That(giftCollider.enabled, Is.True);
            Assert.That(giftBody.isKinematic, Is.False);

            actor.TickSteal(0.1f);
            Assert.That(actor.LiftPhase, Is.EqualTo(EThiefLiftPhase.Grip));
            Assert.That(Vector3.Distance(gift.transform.position, floorPosition), Is.LessThan(0.001f));
            Assert.That(giftCollider.enabled, Is.False);
            Assert.That(giftBody.isKinematic, Is.True);

            float timeout = Time.time + 4f;
            while (!actor.HasCargo && Time.time < timeout)
            {
                actor.TickSteal(0.1f);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(actor.HasCargo, Is.True);
            Assert.That(actor.LiftPhase, Is.EqualTo(EThiefLiftPhase.Carrying));
            yield return new WaitForFixedUpdate();
            Assert.That(Vector3.Distance(gift.transform.position, carryAnchor.position),
                Is.LessThan(0.03f), "들어 올리기가 끝나면 선물이 머리 위 운반 기준점에 있어야 한다");
        }

        [UnityTest]
        public IEnumerator 약한_물리_충돌은_무시하고_직접_공격은_한번만_넘어진_뒤_빈손으로_도망친다()
        {
            ThiefRaidSite site = CreateRaidSite();
            GameObject thiefObject = CreateMover(Vector3.zero, out _);
            ThiefActor actor = thiefObject.AddComponent<ThiefActor>();
            ThiefImpactReceiver receiver = thiefObject.AddComponent<ThiefImpactReceiver>();
            SetPrivateFloat(actor, "_fallingSeconds", 0.1f);
            SetPrivateFloat(actor, "_downSeconds", 0.1f);
            SetPrivateFloat(actor, "_gettingUpSeconds", 0.1f);
            actor.Initialize(site, EGiftBoxKind.Red, new Vector3(0f, 0f, -6f));
            yield return null;

            receiver.ReceiveImpact(new ImpactHit(EImpactCause.PhysicalCollision,
                Vector3.forward * 139f, thiefObject.transform.position));
            Assert.That(actor.IsImpactReacting, Is.False);

            receiver.ReceiveImpact(new ImpactHit(EImpactCause.DirectAttack,
                Vector3.forward * 1f, thiefObject.transform.position));
            Assert.That(actor.ImpactPhase, Is.EqualTo(EThiefImpactPhase.Falling));
            Assert.That(actor.CurrentAction, Is.EqualTo(EThiefAction.ImpactReaction));
            Assert.That(actor.TryBeginImpact(new ImpactHit(EImpactCause.DirectAttack,
                Vector3.right, thiefObject.transform.position)), Is.False,
                "넘어진 동안 추가 공격이 반응을 다시 시작하면 안 된다");

            Assert.That(actor.TickImpactReaction(0.31f), Is.EqualTo(EThiefTaskResult.Success));
            Assert.That(actor.IsImpactReacting, Is.False);
            Assert.That(actor.IsEscaping, Is.True);
            Assert.That(actor.HasCargo, Is.False);
            Assert.That(actor.CurrentAction, Is.EqualTo(EThiefAction.Escaping));
        }

        [UnityTest]
        public IEnumerator 선물을_운반하다_공격받으면_물리_선물로_떨어뜨리고_선점을_해제한다()
        {
            ThiefRaidSite site = CreateRaidSite();
            Gift gift = CreateGift(Vector3.zero);
            Rigidbody giftBody = gift.GetComponent<Rigidbody>();
            Collider giftCollider = gift.GetComponent<Collider>();
            GameObject thiefObject = CreateMover(new Vector3(0f, 0f, -0.9f), out _);
            Transform carryAnchor = new GameObject("__TEST__CarryAnchor").transform;
            carryAnchor.SetParent(thiefObject.transform, false);
            carryAnchor.localPosition = new Vector3(0f, 2.15f, 0.05f);
            ThiefActor actor = thiefObject.AddComponent<ThiefActor>();
            SetPrivateField(actor, "_carryAnchor", carryAnchor);
            actor.Initialize(site, EGiftBoxKind.Red, new Vector3(0f, 0f, -6f));

            float timeout = Time.time + 4f;
            while (!actor.HasCargo && Time.time < timeout)
            {
                if (actor.HasClaimedGift) actor.TickSteal(0.1f);
                else actor.TickAcquireOrApproach(0.1f);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(actor.HasCargo, Is.True);

            Assert.That(actor.TryBeginImpact(new ImpactHit(EImpactCause.DirectAttack,
                Vector3.forward * 105f, thiefObject.transform.position)), Is.True);
            yield return new WaitForFixedUpdate();

            Assert.That(actor.HasCargo, Is.False);
            Assert.That(actor.HasClaimedGift, Is.False);
            Assert.That(gift.ClaimOwner, Is.Null);
            Assert.That(giftCollider.enabled, Is.True);
            Assert.That(giftBody.isKinematic, Is.False);
            Assert.That(giftBody.useGravity, Is.True);
            Assert.That(giftBody.linearVelocity.magnitude, Is.GreaterThan(0.1f));
        }

        private GameObject CreateMover(Vector3 position, out ThiefMovement movement)
        {
            GameObject thief = new GameObject("__TEST__Thief");
            thief.transform.SetParent(_root.transform);
            thief.transform.SetPositionAndRotation(position, Quaternion.identity);
            NavMeshAgent agent = thief.AddComponent<NavMeshAgent>();
            agent.speed = 2f;
            agent.acceleration = 20f;
            agent.angularSpeed = 720f;
            CapsuleCollider body = thief.AddComponent<CapsuleCollider>();
            body.radius = 0.3f;
            body.height = 1.8f;
            body.center = new Vector3(0f, 0.9f, 0f);
            thief.AddComponent<ThiefPlayerSensor>();
            movement = thief.AddComponent<ThiefMovement>();
            Assert.That(agent.Warp(position), Is.True, "테스트 도둑 시작점은 NavMesh 위여야 한다");
            return thief;
        }

        private ThiefRaidSite CreateRaidSite()
        {
            GameObject siteObject = new GameObject("__TEST__RaidSite");
            siteObject.transform.SetParent(_root.transform);
            BoxCollider volume = siteObject.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = new Vector3(5f, 3f, 5f);
            ThiefRaidSite site = siteObject.AddComponent<ThiefRaidSite>();
            GameObject approachObject = new GameObject("__TEST__Approach");
            approachObject.transform.SetParent(siteObject.transform);
            approachObject.transform.localPosition = new Vector3(0f, 0f, -1.5f);
            site.Configure(volume, new[] { approachObject.transform });
            return site;
        }

        private Gift CreateGift(Vector3 position, bool addRigidbody = true,
            EGiftBoxKind kind = EGiftBoxKind.Red)
        {
            GameObject giftObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            giftObject.name = "__TEST__Gift";
            giftObject.transform.SetParent(_root.transform);
            giftObject.transform.position = position + Vector3.up * 0.35f;
            giftObject.transform.localScale = Vector3.one * 0.7f;
            if (addRigidbody) giftObject.AddComponent<Rigidbody>();
            Gift gift = giftObject.AddComponent<Gift>();
            gift.SetKind(kind);
            return gift;
        }

        private static void SetPrivateField(Object target, string fieldName, Object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static void SetPrivateFloat(Object target, string fieldName, float value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }
    }
}

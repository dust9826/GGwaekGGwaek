using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 양 날개의 절차적 포즈를 한 곳에서 소유한다. 눈덩이를 미는 동안 날개 끝을 눈덩이
    /// 가장자리에 댄다.
    /// <b>움직이는 본은 <c>DEF-Wing.L</c> / <c>DEF-Wing.R</c> 둘뿐이다.</b>
    ///
    /// <para>
    /// 2본 IK(<c>Wing.001 → Wing.002</c>)를 쓰지 않는다. 날개는 팔꿈치가 없는 납작한 판이라
    /// 2본 IK 로 풀면 체인의 롤이 힌트(폴 벡터)로 결정되고, 폴이 조금만 흔들려도 판 전체가
    /// 제 축을 중심으로 돌아버린다 — 그게 "날개가 뒤틀린다"의 정체였다.
    /// </para>
    ///
    /// <para>
    /// 대신 날개를 리지드하게 두고 뿌리만 돌린다. 그러면 날개끝이 갈 수 있는 자리는 뿌리를
    /// 중심으로 한 반지름 <c>L</c> 의 구면이고, 접점은 그 구면과 눈덩이 구면의 <b>교집합
    /// (원)</b> 위에 있다. 교집합이 존재할 조건이 <c>|L − r| ≤ d ≤ L + r</c> 하나로 떨어져서,
    /// <b>닿을 수 없는 목표를 애초에 요구하지 않는다</b> — 해가 없는 타깃을 솔버에 밀어 넣던
    /// 이전 구현의 실패를 구조적으로 막는다.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PenguinSnowballPush : MonoBehaviour
    {
        [Header("접점")]
        [SerializeField] private Transform _characterRoot;
        [Tooltip("어떤 눈덩이를 붙잡고 있는지는 이 컴포넌트가 정한다(E 로 붙기/뭉치기). " +
            "여기서는 그 결과(PenguinSnowball.Held/IsPushing)를 읽기만 한다.")]
        [SerializeField] private PenguinSnowball _snowball;
        [SerializeField] private PenguinCarry _carry;

        [Header("날개 본")]
        [Tooltip("DEF-Wing.L — 이 본만 회전한다.")]
        [SerializeField] private Transform _leftWing;
        [Tooltip("DEF-Wing.R — 이 본만 회전한다.")]
        [SerializeField] private Transform _rightWing;
        [Tooltip("DEF-Wing.002.L — 날개끝을 이 본의 로컬 +Y 로 뻗어서 잡는다.")]
        [SerializeField] private Transform _leftWingEnd;
        [Tooltip("DEF-Wing.002.R")]
        [SerializeField] private Transform _rightWingEnd;
        [Tooltip("DEF-Wing.002 원점에서 날개 끝까지의 거리. 본 축(로컬 +Y) 방향으로 잰다.")]
        [SerializeField, Min(0f)] private float _tipOffsetM = 0.2971f;

        [Header("운반 접점")]
        [Tooltip("등에 멘 눈덩이를 잡는 좌우 접점을 각 날개 바깥쪽으로 벌리는 정도. " +
            "0이면 날개 뿌리에서 눈덩이 중심을 잇는 방사 접점이고, 1이면 완전히 옆면을 잡는다.")]
        [SerializeField, Range(0f, 1f)] private float _carrySnowballGripSpread01 = 0.35f;

        [Header("허리 숙임")]
        [Tooltip("DEF-spine.004 — 이 본 하나만 굽힌다. 발(DEF-toe.*)은 spine.003 의 자식이라 " +
            "이 본의 형제다. 따라서 아무리 굽혀도 발은 움직이지 않고 상체만 기운다. " +
            "spine.003 을 굽히면 발까지 딸려 간다.")]
        [SerializeField] private Transform _leanBone;
        [Tooltip("눈덩이 반지름(m) → 숙임 각도(도). 로컬 +X 가 앞숙임이다. " +
            "작은 눈덩이일수록 날개 뿌리를 앞아래로 내려야 닿는다 — 가까이 붙어도 " +
            "수직 격차는 그대로이기 때문이다. " +
            "각도는 '최소값'이 아니라 '구간'이다. 너무 적게 숙이면 멀어서 못 닿고, " +
            "너무 많이 숙이면 눈덩이가 날개끝 구면 안으로 들어가 역시 못 닿는다. " +
            "Idle/Walk 기준으로는 구간이 넓어서(전 크기에서 0° 도 닿는다) 이 값은 사실상 " +
            "보기 좋으라고 고르는 값이다 — 실측 구간은 폴더 문서 참조.")]
        [SerializeField] private AnimationCurve _leanByRadius = new AnimationCurve(
            new Keyframe(0.20f, 36f), new Keyframe(0.30f, 33f),
            new Keyframe(0.50f, 29f), new Keyframe(1.00f, 24f));

        [Header("움직임")]
        [SerializeField, Min(0f)] private float _blendSeconds = 0.15f;

        [Header("진단 — 읽기 전용. 왜 안 잡히는지 여기 나온다")]
        [SerializeField] private string _diagState = "-";
        [SerializeField] private string _diagLeft = "-";
        [SerializeField] private string _diagRight = "-";

        /// <summary>좌우 날개가 각각 눈덩이에 닿을 수 있는가. 진단용이다.</summary>
        public bool LeftInRange { get; private set; }
        public bool RightInRange { get; private set; }

        /// <summary>
        /// <b>양 날개가 모두 닿았는가 — 눈덩이를 밀 수 있는 조건이다.</b>
        /// 눈덩이를 소유한 시스템이 이 값이 <c>false</c> 인 동안 눈덩이를 움직이지 않아야 한다.
        ///
        /// <para>
        /// 옆으로 돌아선 채 밀면 한쪽 날개만 닿는데, 그 상태로 밀리면 한 팔로 미는 그림이
        /// 된다. 날개 조준도 같은 조건으로 묶여 있어서 — 한쪽만 닿으면 <b>양쪽 다</b> 조준하지
        /// 않고 애니메이션 자세로 남는다. 판정과 표현이 따로 놀면 "밀리지도 않는데 한 팔만
        /// 공에 붙어 있는" 상태가 생긴다.
        /// </para>
        /// </summary>
        public bool CanPush { get; private set; }

        /// <summary>조준 가중치. 0 이면 애니메이션 그대로다.</summary>
        public float Weight { get; private set; }

        private void LateUpdate()
        {
            if (_carry == null) _carry = GetComponentInParent<PenguinCarry>();
            if (_carry != null && _carry.IsCarrying && _carry.Cargo != null)
            {
                ApplyCarryPose();
                return;
            }

            // <b><c>HeldForPose</c> 를 읽는다.</b> 붙기는 권위 피어만 하므로 클라이언트의
            // <c>Held</c> 는 항상 null 이다 — 그대로 두면 멀티에서 아무도 밀기 자세를 안 잡는다
            // (루트 AGENTS.md, "연출은 복제된 상태로 그린다").
            SnowBallCarrier carrier = _snowball != null ? _snowball.HeldForPose : null;
            bool holdingAtSide = _snowball != null && carrier != null &&
                                 !_snowball.IsMountedOnTop;

            float step = _blendSeconds <= 0f ? 1f : Time.deltaTime / _blendSeconds;
            Weight = Mathf.MoveTowards(Weight, holdingAtSide ? 1f : 0f, step);

            LeftInRange = false;
            RightInRange = false;
            CanPush = false;

            if (carrier == null)
            {
                _diagState = _snowball == null ? "_snowball 이 비어 있다"
                    : "붙잡은 눈덩이가 없다 (E 로 붙기)";
                _diagLeft = _diagRight = "-";
                return;
            }
            if (_characterRoot == null) { _diagState = "_characterRoot 가 비어 있다"; return; }
            if (Weight <= 0f)
            {
                _diagState = "눈덩이 옆 밀기 상태가 아니다";
                _diagLeft = _diagRight = "-";
                return;
            }

            // SnowBallCarrier 는 지름 1 짜리 구 메시를 중심 (0,0,0), 반지름 0.5 인
            // SphereCollider 로 감싸고 스케일만 바꾼다 — 그래서 트랜스폼 위치·RadiusM 이
            // 곧 구의 중심·반지름이다. 콜라이더를 따로 읽을 필요가 없다.
            Vector3 center = carrier.transform.position;
            float radius = carrier.RadiusM;

            Vector3 right = _characterRoot.right;

            // 숙임을 먼저 적용한다 — 날개 뿌리가 여기 딸려 움직이므로, 숙이기 전 위치로
            // 접점을 풀면 실제로 닿는 자리와 어긋난다.
            Quaternion leanBefore = ApplyLean(radius);

            // 양쪽을 다 풀어보고, 둘 다 닿을 때만 적용한다.
            LeftInRange = TrySolve(_leftWing, _leftWingEnd, center, radius, -right,
                out Vector3 toTipL, out Vector3 contactL, out _diagLeft);
            RightInRange = TrySolve(_rightWing, _rightWingEnd, center, radius, right,
                out Vector3 toTipR, out Vector3 contactR, out _diagRight);

            CanPush = LeftInRange && RightInRange;
            _diagState = CanPush ? $"밀 수 있다 (r={radius:F2}, lean={_leanByRadius.Evaluate(radius):F0}°)"
                : (LeftInRange || RightInRange ? "한쪽 날개만 닿는다 — 눈덩이를 더 정면으로"
                                               : "양쪽 다 안 닿는다");
            if (!CanPush)
            {
                // 못 미는데 숙임만 남으면 허공에 대고 숙인 자세가 된다.
                if (_leanBone != null) _leanBone.localRotation = leanBefore;
                return;
            }

            ApplyAim(_leftWing, toTipL, contactL);
            ApplyAim(_rightWing, toTipR, contactR);
        }

        private void ApplyCarryPose()
        {
            Weight = _carry.CarryPoseWeight;
            LeftInRange = _leftWing != null && _leftWingEnd != null;
            RightInRange = _rightWing != null && _rightWingEnd != null;
            CanPush = false;
            if (_characterRoot == null || !LeftInRange || !RightInRange || Weight <= 0f) return;

            Vector3 center = _carry.CargoCenter;
            if (_carry.CargoIsSnowball)
            {
                Vector3 right = _characterRoot.right;
                AimWingAtSurface(_leftWing, _leftWingEnd, center, _carry.CargoRadiusM, -right);
                AimWingAtSurface(_rightWing, _rightWingEnd, center, _carry.CargoRadiusM, right);
            }
            else
            {
                Vector3 side = _characterRoot.right * 0.18f;
                AimWingAtPoint(_leftWing, _leftWingEnd, center - side);
                AimWingAtPoint(_rightWing, _rightWingEnd, center + side);
            }

            _diagState = "운반물을 등에 고정한다";
            _diagLeft = _diagRight = "운반 접점";
        }

        private void AimWingAtSurface(Transform wing, Transform wingEnd, Vector3 center,
            float radius, Vector3 outward)
        {
            Vector3 fromCenter = wing.position - center;
            if (fromCenter.sqrMagnitude < 0.0001f) return;
            Vector3 gripDirection = Vector3.Slerp(fromCenter.normalized, outward.normalized,
                _carrySnowballGripSpread01);
            AimWingAtPoint(wing, wingEnd, center + gripDirection * radius);
        }

        private void AimWingAtPoint(Transform wing, Transform wingEnd, Vector3 point)
        {
            Vector3 tip = wingEnd.position + wingEnd.up * _tipOffsetM;
            Vector3 toTip = tip - wing.position;
            Vector3 toTarget = point - wing.position;
            if (toTip.sqrMagnitude < 0.0001f || toTarget.sqrMagnitude < 0.0001f) return;
            Quaternion delta = Quaternion.FromToRotation(toTip, toTarget);
            wing.rotation = Quaternion.Slerp(wing.rotation, delta * wing.rotation, Weight);
        }

        /// <summary>
        /// 애니메이션이 준 회전에 숙임 델타를 곱한다. 절대각으로 덮어쓰지 않는 이유는
        /// 두 가지다 — 이 리그의 bind pose 가 0 이 아니고(폴더 문서의 "rest pose 는 0 이
        /// 아니다" 참조), 클립의 호흡·보행 흔들림을 지워버리기 때문이다.
        /// </summary>
        /// <returns>덮어쓰기 전의 로컬 회전. 되돌려야 할 때 쓴다.</returns>
        private Quaternion ApplyLean(float radius)
        {
            if (_leanBone == null) return Quaternion.identity;

            Quaternion before = _leanBone.localRotation;
            float degrees = _leanByRadius.Evaluate(radius) * Weight;
            _leanBone.localRotation = before * Quaternion.AngleAxis(degrees, Vector3.right);
            return before;
        }

        /// <summary>
        /// 날개끝이 닿을 접점을 구한다. 회전은 아직 적용하지 않는다 —
        /// 양쪽이 모두 풀린 뒤에야 적용해야 하기 때문이다.
        /// </summary>
        private bool TrySolve(Transform wing, Transform wingEnd,
            Vector3 center, float radius, Vector3 outward,
            out Vector3 toTip, out Vector3 contact, out string diag)
        {
            toTip = default;
            contact = default;
            if (wing == null || wingEnd == null) { diag = "날개 본이 비어 있다"; return false; }

            Vector3 root = wing.position;
            Vector3 tip = wingEnd.position + wingEnd.up * _tipOffsetM;

            // L 은 매 프레임 다시 잰다. Wing.001/002 는 애니메이션이 계속 움직이므로
            // 고정 상수로 두면 날개가 접힌 프레임에서 접점이 표면에서 뜬다.
            toTip = tip - root;
            float armLength = toTip.magnitude;
            if (armLength < 1e-4f) { diag = "날개 길이가 0"; return false; }

            float d = Vector3.Distance(root, center);
            float lo = Mathf.Abs(armLength - radius), hi = armLength + radius;
            bool ok = TryFindContact(root, armLength, center, radius, outward, out contact);
            diag = $"d={d:F3} 허용[{lo:F3},{hi:F3}] " +
                (ok ? "OK" : (d > hi ? $"✗ {d - hi:F3}m 멀다" : $"✗ {lo - d:F3}m 너무 가깝다"));
            return ok;
        }

        private void ApplyAim(Transform wing, Vector3 toTip, Vector3 contact)
        {
            Quaternion delta = Quaternion.FromToRotation(toTip, contact - wing.position);
            wing.rotation = Quaternion.Slerp(wing.rotation, delta * wing.rotation, Weight);
        }

        /// <summary>
        /// 뿌리 중심 반지름 <paramref name="armLength"/> 구면과 눈덩이 구면의 교집합 원에서,
        /// <paramref name="outward"/> 쪽으로 가장 바깥인 점을 고른다 — 그게 "가장자리"다.
        /// </summary>
        private static bool TryFindContact(Vector3 root, float armLength,
            Vector3 center, float radius, Vector3 outward, out Vector3 contact)
        {
            contact = default;

            Vector3 toBall = center - root;
            float d = toBall.magnitude;
            if (d < 1e-4f) return false;

            // 두 구가 만나지 않으면 닿을 수 없다. 멀어서 못 닿거나(>),
            // 너무 가까워 눈덩이가 날개끝 구면 안으로 들어가버리거나(<) 둘 다 여기서 걸린다.
            if (d > armLength + radius || d < Mathf.Abs(armLength - radius)) return false;

            Vector3 u = toBall / d;
            float a = (d * d + armLength * armLength - radius * radius) / (2f * d);
            Vector3 circleCenter = root + a * u;

            float rhoSq = armLength * armLength - a * a;
            if (rhoSq <= 0f)
            {
                contact = circleCenter; // 두 구가 한 점에서 접한다
                return true;
            }

            Vector3 e = outward - Vector3.Dot(outward, u) * u;
            if (e.sqrMagnitude < 1e-6f) e = Vector3.Cross(u, Vector3.up);
            if (e.sqrMagnitude < 1e-6f) return false;

            contact = circleCenter + Mathf.Sqrt(rhoSq) * e.normalized;
            return true;
        }
    }
}

using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 마법으로 물체를 끌어당기는 텔레키네시스 당기기. 방향키를 누르고 있는 동안만 그 방향의
    /// 부채꼴을 표시하고 사거리·가시선 안 대상에 보라색 발광을 켜며(조준), 키를 떼는 순간 그
    /// 방향 부채꼴 안의 모든 대상을 가까운 순서대로 짧은 시간차를 두고 한꺼번에 당겨온다(발동).
    /// 조준 중에 아래쪽 화살표를 누르면 발동 없이 취소된다. 판정과 대상 이동을 소유하며 시각
    /// 상태는 <see cref="SuctionTarget"/>에만 위임한다.
    /// </summary>
    public sealed class VehiclePullAbility : MonoBehaviour
    {
        private enum EPullDirection
        {
            Front,
            Left,
            Right
        }

        private sealed class CapturedTarget
        {
            public SuctionTarget Target;
            public Rigidbody Body;
            public Vector3 StartPosition;
            public Vector3 CurveOffset;
            public float Delay;
            public float Duration;
            public float Elapsed;
        }

        [SerializeField] private VehicleInput _input;
        [SerializeField] private Transform _vehicle;
        [SerializeField] private LayerMask _suctionLayerMask;
        [Tooltip("이 마스크에 걸리는 콜라이더가 차량과 대상 사이를 막으면(벽 등) 하이라이트도 " +
                 "발동도 안 된다. 대상 자신의 콜라이더가 이 마스크에 걸려도 무방하다 — 광선이 " +
                 "맞은 게 그 대상 자신이면 막힌 것으로 치지 않는다.")]
        [SerializeField] private LayerMask _obstacleLayerMask = ~0;
        [Tooltip("물체를 먹을 때마다 재생하는 차량 스케일 팝. BodyPivot에 붙인다 — Body의 스케일은 " +
                 "이미 충돌 스쿼시가 소유하고 있어(VehicleImpactRelay), 같은 채널을 다시 쓰면 " +
                 "충돌 연출과 서로 덮어쓴다. BodyPivot은 위치·회전만 쓰고 스케일은 비어 있어 " +
                 "안전하다 — Body가 자식이라 두 스케일이 곱해져 자연히 합성된다.")]
        [SerializeField] private MMF_Player _eatPopFeedback;

        [Header("부채꼴")]
        [Tooltip("전방·좌·우 각 부채꼴의 전체 각도. 나중에 튜닝할 파라미터.")]
        [SerializeField, Range(10f, 180f)] private float _sectorAngleDeg = 90f;
        [SerializeField, Min(0.1f)] private float _sectorRadius = 10f;

        [Header("당기기")]
        [Tooltip("도착 목표 지점의 차량 기준 높이.")]
        [SerializeField] private float _pullOriginHeight = 1f;
        [Tooltip("가까운 순서로 살짝씩 두는 시간차.")]
        [SerializeField] private float _startDelayPerTarget = 0.06f;
        [SerializeField] private Vector2 _pullDurationRange = new(0.25f, 0.4f);
        [SerializeField] private float _curveSideOffset = 0.6f;
        [SerializeField] private float _curveLift = 0.5f;
        [SerializeField] private float _captureRadius = 0.2f;
        [SerializeField, Range(1, 32)] private int _maxTargetsPerPull = 16;

        [Header("쿨타임")]
        [Tooltip("공용 쿨타임 — 세 방향이 하나의 타이머를 공유한다. 한 방향을 발동하면 다른 " +
                 "두 방향도 쿨타임이 끝날 때까지 발동할 수 없다.")]
        [SerializeField, Min(0f)] private float _cooldown = 3f;

        [Header("부채꼴 범위선")]
        [Tooltip("쿨타임이 끝나 즉시 발동 가능한 상태의 선 색.")]
        [SerializeField] private Color _sectorColorReady = Color.green;
        [Tooltip("공용 쿨타임 대기 중이라 발동할 수 없는 상태의 선 색.")]
        [SerializeField] private Color _sectorColorCooldown = Color.red;
        [SerializeField] private float _sectorLineHeight = 0.05f;
        [SerializeField] private float _sectorLineWidth = 0.05f;
        [SerializeField, Range(2, 32)] private int _sectorArcSegments = 16;

        private readonly List<CapturedTarget> _capturedTargets = new();
        private readonly List<SuctionTarget> _highlighted = new();
        private readonly List<SuctionTarget> _newHighlighted = new();
        private readonly List<SuctionTarget> _sectorCandidates = new();
        private readonly Collider[] _overlapBuffer = new Collider[64];
        private float _cooldownTimer;
        private LineRenderer[] _sectorLines;
        private static Material s_sectorMaterial;

        /// <summary>지금 조준 중인 방향. 방향키를 처음 누른 프레임부터 떼거나 취소될 때까지
        /// 유지된다 — 조준 도중 다른 방향키를 눌러도 무시한다.</summary>
        private EPullDirection? _heldDirection;
        private bool _holdCanceled;

        public float CooldownRemaining => _cooldownTimer;

        private void Awake()
        {
            CreateSectorLines();
        }

        private void OnDisable()
        {
            ReleaseAllTargets();
            SetAllHighlighted(false);
            SetSectorLinesVisible(false);
            _heldDirection = null;
            _holdCanceled = false;
        }

        private void Update()
        {
            if (_input == null || _vehicle == null) return;

            _cooldownTimer = Mathf.Max(0f, _cooldownTimer - Time.deltaTime);
            UpdateCapturedTargets();

            if (_heldDirection == null)
            {
                EPullDirection? pressed = CurrentHeldDirection();
                if (pressed.HasValue)
                {
                    _heldDirection = pressed;
                    _holdCanceled = false;
                }
            }

            if (_heldDirection.HasValue)
            {
                UpdateHeldDirection(_heldDirection.Value);
            }
        }

        private EPullDirection? CurrentHeldDirection()
        {
            if (_input.PullFrontHeld) return EPullDirection.Front;
            if (_input.PullLeftHeld) return EPullDirection.Left;
            if (_input.PullRightHeld) return EPullDirection.Right;
            return null;
        }

        private bool IsDirectionReleased(EPullDirection direction)
        {
            return direction switch
            {
                EPullDirection.Front => _input.PullFrontReleased,
                EPullDirection.Left => _input.PullLeftReleased,
                _ => _input.PullRightReleased
            };
        }

        /// <summary>조준 중(방향키를 누르고 있는) 프레임마다 그 방향 부채꼴만 그리고, 취소·발동
        /// 여부를 판정한다. 쿨타임이 남아 있는 동안은 부채꼴을 빨간색으로만 보여주고 하이라이트는
        /// 켜지 않는다 — 쿨타임이 끝나는 순간 자동으로 초록색·하이라이트로 전환된다.</summary>
        private void UpdateHeldDirection(EPullDirection direction)
        {
            bool ready = _cooldownTimer <= 0f;
            ShowOnlySector(direction, ready ? _sectorColorReady : _sectorColorCooldown);

            if (ready && !_holdCanceled)
            {
                UpdateHighlightsForDirection(direction);
            }
            else
            {
                SetAllHighlighted(false);
            }

            if (!_holdCanceled && _input.PullCancelPressed)
            {
                _holdCanceled = true;
                SetAllHighlighted(false);
            }

            if (IsDirectionReleased(direction))
            {
                HideSector(direction);
                SetAllHighlighted(false);

                if (ready && !_holdCanceled)
                {
                    TryPull(direction);
                }

                _heldDirection = null;
                _holdCanceled = false;
            }
        }

        private void CreateSectorLines()
        {
            _sectorLines = new LineRenderer[3];
            for (int d = 0; d < _sectorLines.Length; d++)
            {
                var lineObject = new GameObject("PullSector_" + (EPullDirection)d);
                lineObject.transform.SetParent(transform, false);

                var line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.widthMultiplier = _sectorLineWidth;
                line.material = SectorMaterial();

                _sectorLines[d] = line;
            }
        }

        private static Material SectorMaterial()
        {
            if (s_sectorMaterial == null)
            {
                s_sectorMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            }

            return s_sectorMaterial;
        }

        private void SetSectorLinesVisible(bool visible)
        {
            if (_sectorLines == null) return;
            foreach (LineRenderer line in _sectorLines)
            {
                if (line != null) line.enabled = visible;
            }
        }

        /// <summary>조준 중인 방향의 부채꼴만 켜고 나머지 둘은 끈다.</summary>
        private void ShowOnlySector(EPullDirection direction, Color color)
        {
            for (int d = 0; d < _sectorLines.Length; d++)
            {
                bool isHeldDirection = d == (int)direction;
                _sectorLines[d].enabled = isHeldDirection;
                if (isHeldDirection) DrawSector(_sectorLines[d], DirectionVector(direction), color);
            }
        }

        private void HideSector(EPullDirection direction)
        {
            _sectorLines[(int)direction].enabled = false;
        }

        private void DrawSector(LineRenderer line, Vector3 directionVector, Color color)
        {
            Vector3 center = _vehicle.position + Vector3.up * _sectorLineHeight;
            float halfAngle = _sectorAngleDeg * 0.5f;

            line.positionCount = _sectorArcSegments + 1;
            line.SetPosition(0, center);
            for (int i = 0; i < _sectorArcSegments; i++)
            {
                float t = _sectorArcSegments > 1 ? i / (float)(_sectorArcSegments - 1) : 0f;
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 arcDirection = Quaternion.AngleAxis(angle, Vector3.up) * directionVector;
                line.SetPosition(i + 1, center + arcDirection * _sectorRadius);
            }

            line.startColor = color;
            line.endColor = color;
        }

        private Vector3 DirectionVector(EPullDirection direction)
        {
            return direction switch
            {
                EPullDirection.Left => -_vehicle.right,
                EPullDirection.Right => _vehicle.right,
                _ => _vehicle.forward
            };
        }

        /// <summary>벽 등 장애물이 차량과 대상 사이를 가로막는지 본다. 사거리·각도만으로는
        /// 벽 너머 대상도 통과해 잡혔다 — 부채꼴 판정에 가시선 확인을 더한다. 원점은
        /// <see cref="_pullOriginHeight"/>(발동 목표 지점과 같은 높이)로 잡아 바닥 콜라이더를
        /// 스치지 않게 한다. 원점이 차량 자신의 콜라이더 안에 있을 수 있어(박스가 그 높이까지
        /// 올라오면) 차량 자신에게 맞은 히트도 막힌 것으로 치지 않는다 — 안에서 시작한 광선이
        /// 콜라이더를 빠져나가는 면에서 히트로 잡히는 유니티 동작 때문에 실제로 발생한다.</summary>
        private bool HasLineOfSight(Vector3 targetPosition, Collider targetCollider)
        {
            Vector3 origin = _vehicle.position + Vector3.up * _pullOriginHeight;
            Vector3 toTarget = targetPosition - origin;
            float distance = toTarget.magnitude;
            if (distance < 0.001f) return true;

            if (Physics.Raycast(origin, toTarget / distance, out RaycastHit hit, distance, _obstacleLayerMask, QueryTriggerInteraction.Ignore))
            {
                return hit.collider == targetCollider || hit.collider.transform.IsChildOf(_vehicle);
            }

            return true;
        }

        /// <summary>조준 중인 한 방향의 부채꼴 안에 들어온 대상에만 보라색 발광을 켠다. 매 프레임
        /// 전부 껐다 켜지 않고 이전·이번 프레임의 대상 목록을 비교해 **드나든 대상만** 토글한다 —
        /// 계속 켜져 있는 대상을 매 프레임 껐다 켜면 <see cref="SuctionTarget"/>의 맥동 이미션이
        /// 매 프레임 검정으로 리셋돼 버린다(예전 링은 단순 on/off라 문제없었지만 발광은 그렇지
        /// 않다).</summary>
        private void UpdateHighlightsForDirection(EPullDirection direction)
        {
            _newHighlighted.Clear();

            Vector3 directionVector = DirectionVector(direction);
            float halfAngle = _sectorAngleDeg * 0.5f;

            int count = Physics.OverlapSphereNonAlloc(_vehicle.position, _sectorRadius, _overlapBuffer, _suctionLayerMask);
            for (int i = 0; i < count; i++)
            {
                if (!_overlapBuffer[i].TryGetComponent(out SuctionTarget target)) continue;
                if (IsAlreadyCaptured(target)) continue;

                Vector3 toTarget = target.transform.position - _vehicle.position;
                if (toTarget.sqrMagnitude < 0.001f) continue;
                if (Vector3.Angle(directionVector, toTarget) > halfAngle) continue;
                if (!HasLineOfSight(target.transform.position, _overlapBuffer[i])) continue;

                _newHighlighted.Add(target);
            }

            foreach (SuctionTarget target in _highlighted)
            {
                if (target != null && !_newHighlighted.Contains(target)) target.SetHighlighted(false);
            }

            foreach (SuctionTarget target in _newHighlighted)
            {
                if (!_highlighted.Contains(target)) target.SetHighlighted(true);
            }

            _highlighted.Clear();
            _highlighted.AddRange(_newHighlighted);
        }

        private void SetAllHighlighted(bool highlighted)
        {
            foreach (SuctionTarget target in _highlighted)
            {
                if (target != null) target.SetHighlighted(highlighted);
            }
        }

        private void TryPull(EPullDirection direction)
        {
            Vector3 directionVector = DirectionVector(direction);
            float halfAngle = _sectorAngleDeg * 0.5f;

            _sectorCandidates.Clear();
            int count = Physics.OverlapSphereNonAlloc(_vehicle.position, _sectorRadius, _overlapBuffer, _suctionLayerMask);
            for (int i = 0; i < count; i++)
            {
                if (!_overlapBuffer[i].TryGetComponent(out SuctionTarget target)) continue;
                if (IsAlreadyCaptured(target)) continue;

                Vector3 toTarget = target.transform.position - _vehicle.position;
                if (toTarget.sqrMagnitude < 0.001f) continue;
                if (Vector3.Angle(directionVector, toTarget) > halfAngle) continue;
                if (!HasLineOfSight(target.transform.position, _overlapBuffer[i])) continue;

                _sectorCandidates.Add(target);
            }

            if (_sectorCandidates.Count > 0)
            {
                _sectorCandidates.Sort((a, b) =>
                    (a.transform.position - _vehicle.position).sqrMagnitude
                    .CompareTo((b.transform.position - _vehicle.position).sqrMagnitude));

                int captureCount = Mathf.Min(_maxTargetsPerPull, _sectorCandidates.Count);
                for (int i = 0; i < captureCount; i++)
                {
                    CaptureTarget(_sectorCandidates[i], i, captureCount);
                }
            }

            // 대상이 없어도 쿨타임은 소모한다 — "빗나가면 무반응, 다시 눌러보기"가 아니라
            // 방향키를 누르는 순간 항상 발동으로 취급한다.
            _cooldownTimer = _cooldown;
        }

        private void CaptureTarget(SuctionTarget target, int order, int captureCount)
        {
            Rigidbody body = target.GetComponent<Rigidbody>();
            float order01 = captureCount > 1 ? order / (float)(captureCount - 1) : 0f;
            float sideSign = order % 2 == 0 ? 1f : -1f;
            float side = sideSign * _curveSideOffset * Random.Range(0.55f, 1f);
            float lift = _curveLift * Random.Range(0.7f, 1.3f);

            target.SetHighlighted(false);
            target.BeginSuction();
            if (body != null) body.isKinematic = true;

            _capturedTargets.Add(new CapturedTarget
            {
                Target = target,
                Body = body,
                StartPosition = target.transform.position,
                CurveOffset = _vehicle.right * side + Vector3.up * lift,
                Delay = order * _startDelayPerTarget,
                Duration = Mathf.Lerp(_pullDurationRange.x, _pullDurationRange.y, order01),
                Elapsed = 0f
            });
        }

        private bool IsAlreadyCaptured(SuctionTarget target)
        {
            foreach (CapturedTarget captured in _capturedTargets)
            {
                if (captured.Target == target) return true;
            }

            return false;
        }

        private void UpdateCapturedTargets()
        {
            Vector3 end = _vehicle.position + Vector3.up * _pullOriginHeight;

            for (int i = _capturedTargets.Count - 1; i >= 0; i--)
            {
                CapturedTarget captured = _capturedTargets[i];
                if (captured.Target == null)
                {
                    _capturedTargets.RemoveAt(i);
                    continue;
                }

                captured.Elapsed += Time.deltaTime;
                if (captured.Elapsed < captured.Delay) continue;

                float t = Mathf.Clamp01((captured.Elapsed - captured.Delay) / Mathf.Max(captured.Duration, 0.01f));
                Vector3 midpoint = Vector3.Lerp(captured.StartPosition, end, 0.5f) + captured.CurveOffset;
                captured.Target.transform.position = QuadraticBezier(captured.StartPosition, midpoint, end, t);
                captured.Target.SetSuctionTension(t);

                if (t >= 1f || Vector3.Distance(captured.Target.transform.position, end) <= _captureRadius)
                {
                    ConsumeTarget(i);
                }
            }
        }

        private static Vector3 QuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * start + 2f * oneMinusT * t * control + t * t * end;
        }

        private void ConsumeTarget(int index)
        {
            CapturedTarget captured = _capturedTargets[index];
            _capturedTargets.RemoveAt(index);
            if (captured.Body != null) captured.Body.isKinematic = false;
            captured.Target.EndSuction();

            if (_eatPopFeedback != null) _eatPopFeedback.PlayFeedbacks();

            // 도착 시 Destroy — 테스트 전용 placeholder. 실제 집계는 Trash/가 생기면 그쪽 몫.
            Destroy(captured.Target.gameObject);
        }

        private void ReleaseAllTargets()
        {
            foreach (CapturedTarget captured in _capturedTargets)
            {
                if (captured.Target == null) continue;
                captured.Target.EndSuction();
                if (captured.Body != null) captured.Body.isKinematic = false;
            }

            _capturedTargets.Clear();
        }
    }
}

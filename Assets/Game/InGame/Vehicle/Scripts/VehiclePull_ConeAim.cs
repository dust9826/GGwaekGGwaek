using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 획득 방식 비교 테스트 1번 — 전방 부채꼴이 기본으로 항상 표시되고, Left/Right를 누르고
    /// 있는 동안 조준각이 한도 내에서 계속 회전한다(2026-08-15). Front를 누르고 있으면(Left/Right와
    /// 동시에 눌러도 됨) 그 순간 조준각의 하이라이트가 켜지고, 떼는 순간 발동한다 — 기존
    /// <see cref="VehiclePullAbility"/>와 같은 "홀드해서 조준 → 떼면 발동" 리듬을 유지하되, 방향을
    /// 셋 중 고르는 대신 연속 각도로 돌린다는 점만 다르다.
    /// </summary>
    [RequireComponent(typeof(VehicleTargetPuller))]
    public sealed class VehiclePull_ConeAim : MonoBehaviour
    {
        [SerializeField] private VehicleInput _input;
        [SerializeField] private Transform _vehicle;
        [SerializeField] private LayerMask _suctionLayerMask;
        [SerializeField] private LayerMask _obstacleLayerMask = ~0;

        [Header("부채꼴")]
        [SerializeField, Range(10f, 180f)] private float _sectorAngleDeg = 70f;
        [SerializeField, Min(0.1f)] private float _sectorRadius = 10f;
        [Tooltip("조준각이 정면 기준으로 좌우로 돌 수 있는 한도.")]
        [SerializeField, Range(0f, 90f)] private float _aimLimitDeg = 60f;
        [Tooltip("Left/Right를 누르고 있을 때 조준각이 도는 속도.")]
        [SerializeField, Min(1f)] private float _aimSpeedDegPerSec = 90f;

        [Header("쿨타임")]
        [SerializeField, Min(0f)] private float _cooldown = 3f;

        [Tooltip("가시선 판정 광선의 시작 높이. 차체 BoxCollider 안에서 광선이 시작하면 자기 " +
                 "자신에 막힌 것으로 오판하므로, 선 그리기용 높이(거의 지면)와 분리해 차체보다 " +
                 "높게 둔다 — VehiclePullAbility의 _pullOriginHeight와 같은 이유.")]
        [SerializeField] private float _losOriginHeight = 1f;

        [Header("선")]
        [SerializeField] private Color _sectorColorReady = Color.green;
        [SerializeField] private Color _sectorColorAiming = new(0.6f, 0.6f, 1f);
        [SerializeField] private Color _sectorColorCooldown = Color.red;
        [SerializeField] private float _sectorLineHeight = 0.05f;
        [SerializeField, Range(2, 32)] private int _sectorArcSegments = 16;

        private VehicleTargetPuller _puller;
        private LineRenderer _line;
        private static Material s_sectorMaterial;

        private readonly List<SuctionTarget> _highlighted = new();
        private readonly List<SuctionTarget> _newHighlighted = new();
        private readonly List<SuctionTarget> _candidates = new();
        private readonly Collider[] _overlapBuffer = new Collider[64];

        private float _aimDeg;
        private float _cooldownTimer;

        private void Awake()
        {
            _puller = GetComponent<VehicleTargetPuller>();
            var lineObject = new GameObject("PullSector_ConeAim");
            lineObject.transform.SetParent(transform, false);
            _line = lineObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.loop = true;
            _line.widthMultiplier = 0.05f;
            _line.material = SectorMaterial();
        }

        private void OnDisable()
        {
            SetAllHighlighted(false);
            if (_line != null) _line.enabled = false;
            _aimDeg = 0f;
        }

        private static Material SectorMaterial()
        {
            if (s_sectorMaterial == null)
            {
                s_sectorMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            }

            return s_sectorMaterial;
        }

        private void Update()
        {
            if (_input == null || _vehicle == null) return;

            _cooldownTimer = Mathf.Max(0f, _cooldownTimer - Time.deltaTime);

            if (_input.PullLeftHeld) _aimDeg -= _aimSpeedDegPerSec * Time.deltaTime;
            if (_input.PullRightHeld) _aimDeg += _aimSpeedDegPerSec * Time.deltaTime;
            _aimDeg = Mathf.Clamp(_aimDeg, -_aimLimitDeg, _aimLimitDeg);

            bool aiming = _input.PullFrontHeld;
            bool ready = _cooldownTimer <= 0f;

            Color color = !ready ? _sectorColorCooldown : aiming ? _sectorColorAiming : _sectorColorReady;
            _line.enabled = true;
            DrawSector(color);

            if (aiming && ready)
            {
                UpdateHighlights();
            }
            else
            {
                SetAllHighlighted(false);
            }

            if (_input.PullFrontReleased)
            {
                if (ready) TryPull();
                SetAllHighlighted(false);
            }
        }

        private Vector3 AimDirection()
        {
            return Quaternion.AngleAxis(_aimDeg, Vector3.up) * _vehicle.forward;
        }

        private void DrawSector(Color color)
        {
            Vector3 center = _vehicle.position + Vector3.up * _sectorLineHeight;
            Vector3 aimDirection = AimDirection();
            float halfAngle = _sectorAngleDeg * 0.5f;

            _line.positionCount = _sectorArcSegments + 1;
            _line.SetPosition(0, center);
            for (int i = 0; i < _sectorArcSegments; i++)
            {
                float t = _sectorArcSegments > 1 ? i / (float)(_sectorArcSegments - 1) : 0f;
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 arcDirection = Quaternion.AngleAxis(angle, Vector3.up) * aimDirection;
                _line.SetPosition(i + 1, center + arcDirection * _sectorRadius);
            }

            _line.startColor = color;
            _line.endColor = color;
        }

        private bool HasLineOfSight(Vector3 targetPosition, Collider targetCollider)
        {
            Vector3 origin = _vehicle.position + Vector3.up * _losOriginHeight;
            Vector3 toTarget = targetPosition - origin;
            float distance = toTarget.magnitude;
            if (distance < 0.001f) return true;

            if (Physics.Raycast(origin, toTarget / distance, out RaycastHit hit, distance, _obstacleLayerMask, QueryTriggerInteraction.Ignore))
            {
                return hit.collider == targetCollider || hit.collider.transform.IsChildOf(_vehicle);
            }

            return true;
        }

        private void CollectCandidates(List<SuctionTarget> result)
        {
            result.Clear();
            Vector3 aimDirection = AimDirection();
            float halfAngle = _sectorAngleDeg * 0.5f;

            int count = Physics.OverlapSphereNonAlloc(_vehicle.position, _sectorRadius, _overlapBuffer, _suctionLayerMask);
            for (int i = 0; i < count; i++)
            {
                if (!_overlapBuffer[i].TryGetComponent(out SuctionTarget target)) continue;
                if (_puller.IsCaptured(target)) continue;

                Vector3 toTarget = target.transform.position - _vehicle.position;
                if (toTarget.sqrMagnitude < 0.001f) continue;
                if (Vector3.Angle(aimDirection, toTarget) > halfAngle) continue;
                if (!HasLineOfSight(target.transform.position, _overlapBuffer[i])) continue;

                result.Add(target);
            }
        }

        private void UpdateHighlights()
        {
            CollectCandidates(_newHighlighted);

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

            _highlighted.Clear();
        }

        private void TryPull()
        {
            CollectCandidates(_candidates);
            _puller.RequestCapture(_candidates);
            _cooldownTimer = _cooldown;
        }
    }
}

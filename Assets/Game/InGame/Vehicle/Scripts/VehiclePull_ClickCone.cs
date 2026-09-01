using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 획득 방식 비교 테스트 3번 — 전방의 넓은 부채꼴(다른 두 모드보다 각도가 큼)에 들어온 대상은
    /// 조준 입력 없이 상시로 하이라이트되고, 마우스 왼쪽 클릭으로 화면상의 특정 대상 하나를
    /// 즉시 포획한다(2026-08-15). 홀드해서 조준하는 다른 두 모드와 리듬이 다르다 — 발동 단위가
    /// 방향 전체가 아니라 대상 하나이므로 쿨타임을 두지 않는다(클릭 자체가 속도 제한).
    /// </summary>
    [RequireComponent(typeof(VehicleTargetPuller))]
    public sealed class VehiclePull_ClickCone : MonoBehaviour
    {
        [SerializeField] private Transform _vehicle;
        [Tooltip("클릭 판정에 쓰는 카메라. Camera.main 대신 명시 참조 — 씬에 카메라가 여럿일 수 있다.")]
        [SerializeField] private Camera _camera;
        [SerializeField] private LayerMask _suctionLayerMask;
        [SerializeField] private LayerMask _obstacleLayerMask = ~0;

        [Header("부채꼴")]
        [Tooltip("다른 두 모드(70도)보다 크게 잡는다 — 조준이 없는 대신 상시 판정 범위가 넓다.")]
        [SerializeField, Range(10f, 180f)] private float _sectorAngleDeg = 130f;
        [SerializeField, Min(0.1f)] private float _sectorRadius = 10f;
        [SerializeField] private float _sectorLineHeight = 0.05f;
        [Tooltip("가시선 판정 광선의 시작 높이. 차체 BoxCollider 안에서 시작하면 자기 자신에 " +
                 "막힌 것으로 오판하므로 선 그리기용 높이와 분리해 차체보다 높게 둔다.")]
        [SerializeField] private float _losOriginHeight = 1f;
        [SerializeField, Range(2, 32)] private int _sectorArcSegments = 24;
        [SerializeField] private Color _sectorColor = new(0.6f, 0.9f, 0.6f);

        private VehicleTargetPuller _puller;
        private LineRenderer _line;
        private static Material s_sectorMaterial;

        private readonly List<SuctionTarget> _highlighted = new();
        private readonly List<SuctionTarget> _newHighlighted = new();
        private readonly Collider[] _overlapBuffer = new Collider[64];

        private void Awake()
        {
            _puller = GetComponent<VehicleTargetPuller>();
            var lineObject = new GameObject("PullSector_ClickCone");
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
            if (_vehicle == null) return;

            _line.enabled = true;
            DrawSector();
            UpdateHighlights();

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && _camera != null)
            {
                TryClickCapture();
            }
        }

        private void DrawSector()
        {
            Vector3 center = _vehicle.position + Vector3.up * _sectorLineHeight;
            Vector3 forward = _vehicle.forward;
            float halfAngle = _sectorAngleDeg * 0.5f;

            _line.positionCount = _sectorArcSegments + 1;
            _line.SetPosition(0, center);
            for (int i = 0; i < _sectorArcSegments; i++)
            {
                float t = _sectorArcSegments > 1 ? i / (float)(_sectorArcSegments - 1) : 0f;
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 arcDirection = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                _line.SetPosition(i + 1, center + arcDirection * _sectorRadius);
            }

            _line.startColor = _sectorColor;
            _line.endColor = _sectorColor;
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

        private void UpdateHighlights()
        {
            _newHighlighted.Clear();
            Vector3 forward = _vehicle.forward;
            float halfAngle = _sectorAngleDeg * 0.5f;

            int count = Physics.OverlapSphereNonAlloc(_vehicle.position, _sectorRadius, _overlapBuffer, _suctionLayerMask);
            for (int i = 0; i < count; i++)
            {
                if (!_overlapBuffer[i].TryGetComponent(out SuctionTarget target)) continue;
                if (_puller.IsCaptured(target)) continue;

                Vector3 toTarget = target.transform.position - _vehicle.position;
                if (toTarget.sqrMagnitude < 0.001f) continue;
                if (Vector3.Angle(forward, toTarget) > halfAngle) continue;
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

            _highlighted.Clear();
        }

        /// <summary>화면상의 클릭 지점과 가장 가까운, 지금 하이라이트된 대상을 찾는다 — 정확한
        /// 콜라이더 피킹이 아니라 화면공간 최근접이다. 낮은 폴리 소품이 겹쳐 있을 때 콜라이더
        /// 경계보다 사용자가 노린 지점에 더 관대하다.</summary>
        private void TryClickCapture()
        {
            if (_highlighted.Count == 0) return;

            Vector2 clickScreen = Mouse.current.position.ReadValue();
            SuctionTarget closest = null;
            float closestSqrDist = float.MaxValue;

            foreach (SuctionTarget target in _highlighted)
            {
                if (target == null) continue;

                Vector3 screenPoint = _camera.WorldToScreenPoint(target.transform.position);
                if (screenPoint.z < 0f) continue;

                float sqrDist = ((Vector2)screenPoint - clickScreen).sqrMagnitude;
                if (sqrDist < closestSqrDist)
                {
                    closestSqrDist = sqrDist;
                    closest = target;
                }
            }

            const float clickRadiusPx = 80f;
            if (closest != null && closestSqrDist <= clickRadiusPx * clickRadiusPx)
            {
                _highlighted.Remove(closest);
                _puller.RequestCaptureSingle(closest);
            }
        }
    }
}

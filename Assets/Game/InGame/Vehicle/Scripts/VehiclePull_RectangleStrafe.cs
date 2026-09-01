using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 획득 방식 비교 테스트 2번 — Left/Right 방향키를 누르고 있는 동안 그 방향 자리의 직사각형
    /// 범위가 표시되고, 그 범위 안의 프롭을 매 프레임 계속 획득한다(쿨타임 없음, 2026-08-15 수정).
    /// 방향키를 떼면 범위와 하이라이트가 사라진다.
    /// </summary>
    [RequireComponent(typeof(VehicleTargetPuller))]
    public sealed class VehiclePull_RectangleStrafe : MonoBehaviour
    {
        [SerializeField] private VehicleInput _input;
        [SerializeField] private Transform _vehicle;
        [SerializeField] private LayerMask _suctionLayerMask;
        [SerializeField] private LayerMask _obstacleLayerMask = ~0;

        [Header("직사각형 (로컬 기준)")]
        [SerializeField] private float _rectWidth = 3f;
        [SerializeField] private float _rectLength = 8f;
        [Tooltip("차량 기준 전방 오프셋 — 상자 중심이 차량 바로 위가 아니라 이만큼 앞에 선다.")]
        [SerializeField] private float _forwardOffset = 4f;
        [SerializeField] private float _boxHeight = 2f;

        [Tooltip("가시선 판정 광선의 시작 높이. 차체 BoxCollider 안에서 시작하면 자기 자신에 " +
                 "막힌 것으로 오판하므로 선 그리기용 높이와 분리해 차체보다 높게 둔다.")]
        [SerializeField] private float _losOriginHeight = 1f;

        [Header("선")]
        [SerializeField] private Color _rectColorActive = new(0.6f, 0.6f, 1f);
        [SerializeField] private float _lineHeight = 0.05f;

        private VehicleTargetPuller _puller;
        private LineRenderer _line;
        private static Material s_rectMaterial;

        private readonly List<SuctionTarget> _highlighted = new();
        private readonly List<SuctionTarget> _newHighlighted = new();
        private readonly Collider[] _overlapBuffer = new Collider[64];

        private float _strafeOffset;

        private void Awake()
        {
            _puller = GetComponent<VehicleTargetPuller>();
            var lineObject = new GameObject("PullRect_Strafe");
            lineObject.transform.SetParent(transform, false);
            _line = lineObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.loop = true;
            _line.widthMultiplier = 0.05f;
            _line.positionCount = 5;
            _line.material = RectMaterial();
        }

        private void OnDisable()
        {
            SetAllHighlighted(false);
            if (_line != null) _line.enabled = false;
            _strafeOffset = 0f;
        }

        private static Material RectMaterial()
        {
            if (s_rectMaterial == null)
            {
                s_rectMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            }

            return s_rectMaterial;
        }

        private void Update()
        {
            if (_input == null || _vehicle == null) return;

            bool left = _input.PullLeftHeld;
            bool right = _input.PullRightHeld;
            bool active = left ^ right;

            if (!active)
            {
                _line.enabled = false;
                SetAllHighlighted(false);
                return;
            }

            // 안쪽 모서리(왼쪽=오른쪽 아래, 오른쪽=왼쪽 아래)가 차체 중심에 오도록 폭의 절반만큼만 옮긴다 —
            // 차체 BoxCollider 반폭(0.9)보다 안쪽이라 그 모서리가 항상 차체 안에 들어간다.
            float strafeStep = _rectWidth * 0.5f;
            _strafeOffset = left ? -strafeStep : strafeStep;

            _line.enabled = true;
            DrawRect(_rectColorActive);

            UpdateHighlights();
            _puller.RequestCapture(_highlighted);
        }

        private (Vector3 center, Quaternion rotation) RectPose()
        {
            Vector3 center = _vehicle.position
                              + _vehicle.forward * _forwardOffset
                              + _vehicle.right * _strafeOffset;
            return (center, _vehicle.rotation);
        }

        private void DrawRect(Color color)
        {
            (Vector3 center, Quaternion rotation) pose = RectPose();
            float halfW = _rectWidth * 0.5f;
            float halfL = _rectLength * 0.5f;
            Vector3 up = Vector3.up * _lineHeight;

            Vector3 p0 = pose.center + pose.rotation * new Vector3(-halfW, 0f, -halfL) + up;
            Vector3 p1 = pose.center + pose.rotation * new Vector3(halfW, 0f, -halfL) + up;
            Vector3 p2 = pose.center + pose.rotation * new Vector3(halfW, 0f, halfL) + up;
            Vector3 p3 = pose.center + pose.rotation * new Vector3(-halfW, 0f, halfL) + up;

            _line.SetPosition(0, p0);
            _line.SetPosition(1, p1);
            _line.SetPosition(2, p2);
            _line.SetPosition(3, p3);
            _line.SetPosition(4, p0);
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
            (Vector3 center, Quaternion rotation) pose = RectPose();
            Vector3 halfExtents = new(_rectWidth * 0.5f, _boxHeight * 0.5f, _rectLength * 0.5f);

            int count = Physics.OverlapBoxNonAlloc(pose.center, halfExtents, _overlapBuffer, pose.rotation, _suctionLayerMask);
            for (int i = 0; i < count; i++)
            {
                if (!_overlapBuffer[i].TryGetComponent(out SuctionTarget target)) continue;
                if (_puller.IsCaptured(target)) continue;
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
    }
}

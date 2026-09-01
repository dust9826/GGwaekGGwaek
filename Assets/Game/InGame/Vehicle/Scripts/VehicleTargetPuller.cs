using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 포획된 대상을 곡선으로 끌어와 소비하는 공용 파이프라인. <see cref="VehiclePullAbility"/>의
    /// 해당 부분을 그대로 추출한 것 — 4가지 획득 방식(부채꼴 조준 회전·직사각형 평행이동·큰
    /// 부채꼴+클릭·기존 방식) 비교 테스트를 위해 만든 세 신규 모드가 이걸 공유한다(2026-08-15).
    /// 조준·발동 시점은 모드마다 다르므로 여기서 다루지 않는다 — 모드 스크립트가 <see cref="RequestCapture"/>만
    /// 호출하면 이후 이동·소비는 이 컴포넌트가 전담한다.
    ///
    /// <see cref="VehiclePullAbility"/>는 이 컴포넌트를 쓰지 않는다 — 기존 방식(4번)은 회귀 위험 없이
    /// 그대로 두기 위해 자기 파이프라인을 유지한다. 넷 중 하나가 최종 채택되면 그때 통합을 검토한다.
    /// </summary>
    public sealed class VehicleTargetPuller : MonoBehaviour
    {
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

        [SerializeField] private Transform _vehicle;
        [SerializeField] private MMF_Player _eatPopFeedback;

        [Header("당기기")]
        [SerializeField] private float _pullOriginHeight = 1f;
        [SerializeField] private float _startDelayPerTarget = 0.06f;
        [SerializeField] private Vector2 _pullDurationRange = new(0.25f, 0.4f);
        [SerializeField] private float _curveSideOffset = 0.6f;
        [SerializeField] private float _curveLift = 0.5f;
        [SerializeField] private float _captureRadius = 0.2f;
        [SerializeField, Range(1, 32)] private int _maxTargetsPerPull = 16;

        private readonly List<CapturedTarget> _capturedTargets = new();

        private void OnDisable()
        {
            ReleaseAllTargets();
        }

        private void Update()
        {
            UpdateCapturedTargets();
        }

        public bool IsCaptured(SuctionTarget target)
        {
            foreach (CapturedTarget captured in _capturedTargets)
            {
                if (captured.Target == target) return true;
            }

            return false;
        }

        /// <summary>가까운 순서로 정렬해 최대 <see cref="_maxTargetsPerPull"/>개까지 포획한다.</summary>
        public void RequestCapture(List<SuctionTarget> candidates)
        {
            if (candidates.Count == 0) return;

            candidates.Sort((a, b) =>
                (a.transform.position - _vehicle.position).sqrMagnitude
                .CompareTo((b.transform.position - _vehicle.position).sqrMagnitude));

            int captureCount = Mathf.Min(_maxTargetsPerPull, candidates.Count);
            for (int i = 0; i < captureCount; i++)
            {
                CaptureTarget(candidates[i], i, captureCount);
            }
        }

        /// <summary>하나만 즉시 포획한다 — 클릭-즉시-포획 모드용.</summary>
        public void RequestCaptureSingle(SuctionTarget target)
        {
            CaptureTarget(target, 0, 1);
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

            // 도착 시 Destroy — 테스트 전용 placeholder. VehiclePullAbility와 동일한 취급.
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

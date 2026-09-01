using System;
using System.Reflection;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// asmdef 밖의 Final IK를 반사로 연결하는 표현 전용 어댑터다. 권위 절도 판정은 IK 성공과 무관하다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThiefFinalIkAdapter : MonoBehaviour
    {
        [SerializeField] private Component _fullBodyBipedIk;
        [SerializeField] private Animator _animator;
        [SerializeField] private Transform _leftHandTarget;
        [SerializeField] private Transform _rightHandTarget;
        [SerializeField] private Transform _bodyTarget;
        [SerializeField] private Transform _leftElbowTarget;
        [SerializeField] private Transform _rightElbowTarget;
        [SerializeField] private Transform _leftFootTarget;
        [SerializeField] private Transform _rightFootTarget;
        [SerializeField] private ThiefNetworkHub _networkHub;
        [SerializeField] private ThiefActor _actor;
        [SerializeField, Min(0f)] private float _blendSpeed = 8f;
        [SerializeField, Range(0f, 1f)] private float _bodyWeight;
        [SerializeField, Range(0f, 1f)] private float _footPinWeight;
        [SerializeField, Min(0f)] private float _palmClearanceM = 0.045f;
        [SerializeField, Min(0f)] private float _forearmClearanceM = 0.12f;
        [SerializeField, Range(0f, 1f)] private float _gripHeight01 = 0.22f;
        [SerializeField, Range(0f, 1f)] private float _gripNearFace01 = 0.45f;
        [SerializeField, Range(0f, 0.5f)] private float _supportInset01 = 0.22f;

        private object _solver;
        private object _leftHandEffector;
        private object _rightHandEffector;
        private object _bodyEffector;
        private object _leftFootEffector;
        private object _rightFootEffector;
        private object _leftArmChain;
        private object _rightArmChain;
        private FieldInfo _targetField;
        private FieldInfo _positionWeightField;
        private FieldInfo _rotationWeightField;
        private FieldInfo _chainPullField;
        private FieldInfo _chainReachField;
        private FieldInfo _bendConstraintField;
        private FieldInfo _bendGoalField;
        private FieldInfo _bendWeightField;
        private FieldInfo _onPreUpdateField;
        private Delegate _preUpdateCallback;
        private EThiefLiftPhase _previousPhase;
        private Gift _cachedGift;
        private ThiefGiftGeometry _giftGeometry;
        private bool _hasGiftGeometry;
        private Vector3 _leftHandApproachStart;
        private Vector3 _rightHandApproachStart;
        private Vector3 _bodyBaselineLocal;
        private float _weight;
        private float _leftArmPullDefault = 1f;
        private float _rightArmPullDefault = 1f;
        private float _leftArmReachDefault = 0.1f;
        private float _rightArmReachDefault = 0.1f;
        private bool _subscriptionFailed;
        private bool _hasLiftBaseline;

        private void Awake()
        {
            if (_networkHub == null) _networkHub = GetComponent<ThiefNetworkHub>();
            if (_actor == null) _actor = GetComponent<ThiefActor>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
            if (_fullBodyBipedIk == null) FindFinalIkComponent();
            CacheReflection();
        }

        private void OnEnable()
        {
            SubscribeSolver();
        }

        private void LateUpdate()
        {
            if (_preUpdateCallback == null) ApplyBeforeSolver();
        }

        private void OnDisable()
        {
            UnsubscribeSolver();
            _weight = 0f;
            ApplyAllWeights(0f, 0f, 0f, 0f);
        }

        private void ApplyBeforeSolver()
        {
            EThiefLiftPhase phase = _networkHub != null
                ? _networkHub.PresentedLiftPhase : _actor != null ? _actor.LiftPhase : EThiefLiftPhase.None;
            float progress = _networkHub != null
                ? _networkHub.PresentedLiftPhaseProgress : _actor != null ? _actor.LiftPhaseProgress01 : 0f;
            EThiefImpactPhase impactPhase = _networkHub != null
                ? _networkHub.PresentedImpactPhase : _actor != null
                    ? _actor.ImpactPhase : EThiefImpactPhase.None;

            if (impactPhase != EThiefImpactPhase.None)
            {
                _weight = 0f;
                _previousPhase = EThiefLiftPhase.None;
                _hasLiftBaseline = false;
                ApplyAllWeights(0f, 0f, 0f, 0f);
                return;
            }

            if (phase != _previousPhase)
            {
                if (!_hasLiftBaseline && phase is > EThiefLiftPhase.None and < EThiefLiftPhase.Carrying)
                    CaptureLiftBaseline();
                if (phase == EThiefLiftPhase.None) _hasLiftBaseline = false;
                _previousPhase = phase;
            }

            float targetWeight = TargetHandWeight(phase, progress);
            _weight = Mathf.MoveTowards(_weight, targetWeight, _blendSpeed * Time.deltaTime);
            UpdateTargetPose(phase, progress);

            float body = phase switch
            {
                >= EThiefLiftPhase.ReachFloor and <= EThiefLiftPhase.LiftToChest => _bodyWeight * _weight,
                EThiefLiftPhase.StandAndOverhead => Mathf.Lerp(_bodyWeight, 0.12f, progress) * _weight,
                _ => 0f,
            };
            float feet = phase switch
            {
                >= EThiefLiftPhase.PrepareCrouch and < EThiefLiftPhase.StandAndOverhead =>
                    _footPinWeight * _weight,
                EThiefLiftPhase.StandAndOverhead =>
                    Mathf.Lerp(_footPinWeight, 0f, Smooth(progress)) * _weight,
                _ => 0f,
            };
            const float armPull = 0f;
            ApplyAllWeights(_weight, body, feet, armPull);
        }

        private void UpdateTargetPose(EThiefLiftPhase phase, float progress)
        {
            Gift gift = _actor != null ? _actor.CurrentLiftGift : null;
            if (gift != _cachedGift)
            {
                _cachedGift = gift;
                CacheGiftGeometry(gift);
            }

            if (gift != null) UpdateHandTargets(phase, progress);
            UpdateElbowTargets();
            UpdateBodyTarget(phase, progress);
        }

        private void UpdateHandTargets(EThiefLiftPhase phase, float progress)
        {
            Transform giftTransform = _giftGeometry.Root;
            if (!_hasGiftGeometry || giftTransform == null) return;
            Vector3 localCenter = _giftGeometry.LocalCenter;
            Vector3 localExtents = _giftGeometry.LocalExtents;
            Vector3 toThief = giftTransform.InverseTransformDirection(
                transform.position - giftTransform.position);
            float nearSign = Mathf.Abs(toThief.z) > 0.001f ? Mathf.Sign(toThief.z) : -1f;
            float localPalmClearance = _palmClearanceM /
                                       Mathf.Max(Mathf.Abs(giftTransform.lossyScale.x), 0.0001f);
            float localBottomClearance = _palmClearanceM /
                                         Mathf.Max(Mathf.Abs(giftTransform.lossyScale.y), 0.0001f);
            Vector3 sideGrip = localCenter;
            sideGrip.y -= localExtents.y * Mathf.Lerp(0.85f, 0.15f, _gripHeight01);
            sideGrip.z += nearSign * localExtents.z * _gripNearFace01;
            Vector3 supportGrip = localCenter;
            supportGrip.y -= localExtents.y + localBottomClearance;
            supportGrip.z += nearSign * localExtents.z * _gripNearFace01;
            float supportBlend = phase switch
            {
                EThiefLiftPhase.LiftToChest => Smooth(progress),
                >= EThiefLiftPhase.StandAndOverhead => 1f,
                _ => 0f,
            };
            float sideOffset = localExtents.x + localPalmClearance;
            float supportOffset = localExtents.x * (1f - _supportInset01);
            Vector3 leftLocal = Vector3.Lerp(sideGrip + Vector3.left * sideOffset,
                supportGrip + Vector3.left * supportOffset, supportBlend);
            Vector3 rightLocal = Vector3.Lerp(sideGrip + Vector3.right * sideOffset,
                supportGrip + Vector3.right * supportOffset, supportBlend);
            Vector3 leftGrip = giftTransform.TransformPoint(leftLocal);
            Vector3 rightGrip = giftTransform.TransformPoint(rightLocal);
            if (phase is EThiefLiftPhase.PrepareCrouch or EThiefLiftPhase.ReachFloor)
            {
                float reach = phase == EThiefLiftPhase.PrepareCrouch ? 0f : Smooth(progress);
                Vector3 near = transform.position - _giftGeometry.WorldCenter;
                near.y = 0f;
                if (near.sqrMagnitude < 0.001f) near = -transform.forward;
                near.Normalize();
                Vector3 leftPreGrip = leftGrip + near * _forearmClearanceM - Vector3.up * 0.06f;
                Vector3 rightPreGrip = rightGrip + near * _forearmClearanceM - Vector3.up * 0.06f;
                leftGrip = QuadraticBezier(_leftHandApproachStart, leftPreGrip, leftGrip, reach);
                rightGrip = QuadraticBezier(_rightHandApproachStart, rightPreGrip, rightGrip, reach);
            }

            Vector3 giftForward = _giftGeometry.WorldCenter - transform.position;
            giftForward.y = 0f;
            if (giftForward.sqrMagnitude < 0.001f) giftForward = transform.forward;
            Quaternion baseRotation = Quaternion.LookRotation(giftForward.normalized, transform.up);
            if (_leftHandTarget != null)
            {
                _leftHandTarget.position = leftGrip;
                _leftHandTarget.rotation = baseRotation * Quaternion.Euler(0f, -90f, 90f);
            }
            if (_rightHandTarget != null)
            {
                _rightHandTarget.position = rightGrip;
                _rightHandTarget.rotation = baseRotation * Quaternion.Euler(0f, 90f, -90f);
            }
        }

        private void UpdateElbowTargets()
        {
            Transform giftTransform = _giftGeometry.Root;
            Vector3 giftCenter = _hasGiftGeometry
                ? _giftGeometry.WorldCenter : transform.position + transform.forward;
            Vector3 near = transform.position + transform.up - giftCenter;
            near.y = 0f;
            if (near.sqrMagnitude < 0.001f) near = -transform.forward;
            near.Normalize();
            Vector3 side = giftTransform != null ? giftTransform.right.normalized : transform.right;
            if (_leftHandTarget != null && _leftElbowTarget != null)
                _leftElbowTarget.position = _leftHandTarget.position - side *
                    (0.38f + _forearmClearanceM) + near * _forearmClearanceM + transform.up * 0.04f;
            if (_rightHandTarget != null && _rightElbowTarget != null)
                _rightElbowTarget.position = _rightHandTarget.position + side *
                    (0.38f + _forearmClearanceM) + near * _forearmClearanceM + transform.up * 0.04f;
        }

        private void UpdateBodyTarget(EThiefLiftPhase phase, float progress)
        {
            if (_bodyTarget == null) return;
            Vector3 baseline = transform.TransformPoint(_bodyBaselineLocal);
            float low = phase is >= EThiefLiftPhase.ReachFloor and <= EThiefLiftPhase.LiftToChest ? 1f : 0f;
            if (phase == EThiefLiftPhase.StandAndOverhead) low = 1f - Smooth(progress);
            _bodyTarget.position = baseline - transform.up * (0.22f * low);
            _bodyTarget.rotation = transform.rotation;
        }

        private void CaptureLiftBaseline()
        {
            if (_animator == null || !_animator.isHuman) return;
            Transform leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _leftHandApproachStart = leftHand != null ? leftHand.position : transform.position;
            _rightHandApproachStart = rightHand != null ? rightHand.position : transform.position;
            _bodyBaselineLocal = hips != null
                ? transform.InverseTransformPoint(hips.position) : new Vector3(0f, 0.8f, 0f);
            CopyBonePose(HumanBodyBones.LeftFoot, _leftFootTarget);
            CopyBonePose(HumanBodyBones.RightFoot, _rightFootTarget);
            _hasLiftBaseline = true;
        }

        private void CopyBonePose(HumanBodyBones bone, Transform target)
        {
            if (target == null) return;
            Transform source = _animator.GetBoneTransform(bone);
            if (source != null) target.SetPositionAndRotation(source.position, source.rotation);
        }

        private void CacheGiftGeometry(Gift gift)
        {
            _hasGiftGeometry = ThiefGiftGeometry.TryCreate(gift, out _giftGeometry);
        }

        private float TargetHandWeight(EThiefLiftPhase phase, float progress)
        {
            return phase switch
            {
                EThiefLiftPhase.PrepareCrouch => progress * 0.08f,
                EThiefLiftPhase.ReachFloor => Smooth(progress) * 0.35f,
                EThiefLiftPhase.Grip => Mathf.Lerp(0.35f, 0.5f, Smooth(progress)),
                EThiefLiftPhase.LiftToChest => Mathf.Lerp(0.5f, 1f, Smooth(progress)),
                EThiefLiftPhase.Carrying => 1f,
                EThiefLiftPhase.StandAndOverhead => 1f,
                _ => 0f,
            };
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static Vector3 QuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            float inverse = 1f - ratio;
            return inverse * inverse * start + 2f * inverse * ratio * control + ratio * ratio * end;
        }

        private void FindFinalIkComponent()
        {
            Type type = Type.GetType("RootMotion.FinalIK.FullBodyBipedIK, Assembly-CSharp-firstpass");
            if (type != null) _fullBodyBipedIk = GetComponentInChildren(type, true);
        }

        private void CacheReflection()
        {
            if (_fullBodyBipedIk == null) return;
            FieldInfo solverField = _fullBodyBipedIk.GetType().GetField("solver",
                BindingFlags.Instance | BindingFlags.Public);
            _solver = solverField?.GetValue(_fullBodyBipedIk);
            if (_solver == null) return;

            Type solverType = _solver.GetType();
            _leftHandEffector = GetProperty(solverType, "leftHandEffector");
            _rightHandEffector = GetProperty(solverType, "rightHandEffector");
            _bodyEffector = GetProperty(solverType, "bodyEffector");
            _leftFootEffector = GetProperty(solverType, "leftFootEffector");
            _rightFootEffector = GetProperty(solverType, "rightFootEffector");
            _leftArmChain = GetProperty(solverType, "leftArmChain");
            _rightArmChain = GetProperty(solverType, "rightArmChain");

            object effector = _leftHandEffector ?? _rightHandEffector;
            if (effector != null)
            {
                Type type = effector.GetType();
                _targetField = type.GetField("target");
                _positionWeightField = type.GetField("positionWeight");
                _rotationWeightField = type.GetField("rotationWeight");
            }

            object chain = _leftArmChain ?? _rightArmChain;
            if (chain != null)
            {
                Type type = chain.GetType();
                _chainPullField = type.GetField("pull");
                _chainReachField = type.GetField("reach");
                _bendConstraintField = type.GetField("bendConstraint");
                object bend = _bendConstraintField?.GetValue(chain);
                if (bend != null)
                {
                    Type bendType = bend.GetType();
                    _bendGoalField = bendType.GetField("bendGoal");
                    _bendWeightField = bendType.GetField("weight");
                }
            }
            _leftArmPullDefault = ReadFloat(_leftArmChain, _chainPullField, 1f);
            _rightArmPullDefault = ReadFloat(_rightArmChain, _chainPullField, 1f);
            _leftArmReachDefault = ReadFloat(_leftArmChain, _chainReachField, 0.1f);
            _rightArmReachDefault = ReadFloat(_rightArmChain, _chainReachField, 0.1f);
            _onPreUpdateField = solverType.GetField("OnPreUpdate", BindingFlags.Instance | BindingFlags.Public);
        }

        private object GetProperty(Type solverType, string propertyName)
        {
            return solverType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(_solver);
        }

        private void SubscribeSolver()
        {
            if (_solver == null || _onPreUpdateField == null || _preUpdateCallback != null ||
                _subscriptionFailed) return;
            try
            {
                MethodInfo method = GetType().GetMethod(nameof(ApplyBeforeSolver),
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _preUpdateCallback = Delegate.CreateDelegate(_onPreUpdateField.FieldType, this, method);
                Delegate existing = _onPreUpdateField.GetValue(_solver) as Delegate;
                _onPreUpdateField.SetValue(_solver, Delegate.Combine(existing, _preUpdateCallback));
            }
            catch (Exception exception)
            {
                _preUpdateCallback = null;
                _subscriptionFailed = true;
                Debug.LogWarning($"Final IK OnPreUpdate 연결에 실패해 LateUpdate 보조 경로를 사용합니다: {exception.Message}", this);
            }
        }

        private void UnsubscribeSolver()
        {
            if (_solver == null || _onPreUpdateField == null || _preUpdateCallback == null) return;
            Delegate existing = _onPreUpdateField.GetValue(_solver) as Delegate;
            _onPreUpdateField.SetValue(_solver, Delegate.Remove(existing, _preUpdateCallback));
            _preUpdateCallback = null;
        }

        private static float ReadFloat(object target, FieldInfo field, float fallback)
        {
            return target != null && field?.GetValue(target) is float value ? value : fallback;
        }

        private void ApplyAllWeights(float hands, float body, float feet, float armPull)
        {
            ApplyEffector(_leftHandEffector, _leftHandTarget, hands, hands * 0.8f);
            ApplyEffector(_rightHandEffector, _rightHandTarget, hands, hands * 0.8f);
            ApplyEffector(_bodyEffector, _bodyTarget, body, 0f);
            ApplyEffector(_leftFootEffector, _leftFootTarget, feet, feet);
            ApplyEffector(_rightFootEffector, _rightFootTarget, feet, feet);
            ApplyArmChain(_leftArmChain, _leftElbowTarget, hands, armPull,
                _leftArmPullDefault, _leftArmReachDefault);
            ApplyArmChain(_rightArmChain, _rightElbowTarget, hands, armPull,
                _rightArmPullDefault, _rightArmReachDefault);
        }

        private void ApplyEffector(object effector, Transform target, float positionWeight, float rotationWeight)
        {
            if (effector == null) return;
            _targetField?.SetValue(effector, target);
            _positionWeightField?.SetValue(effector, Mathf.Clamp01(positionWeight));
            _rotationWeightField?.SetValue(effector, Mathf.Clamp01(rotationWeight));
        }

        private void ApplyArmChain(object chain, Transform bendGoal, float weight, float pull,
            float defaultPull, float defaultReach)
        {
            if (chain == null) return;
            _chainPullField?.SetValue(chain, weight > 0f ? Mathf.Clamp01(pull) : defaultPull);
            _chainReachField?.SetValue(chain, weight > 0f ? Mathf.Lerp(0.12f, 0.35f, weight) : defaultReach);
            object bend = _bendConstraintField?.GetValue(chain);
            if (bend == null) return;
            _bendGoalField?.SetValue(bend, bendGoal);
            _bendWeightField?.SetValue(bend, Mathf.Clamp01(weight));
        }
    }
}

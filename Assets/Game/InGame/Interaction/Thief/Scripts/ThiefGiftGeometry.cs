using UnityEngine;

namespace PPack
{
    /// <summary>선물 콜라이더를 도둑의 접근·운반·IK가 함께 쓰는 루트 로컬 상자로 바꾼다.</summary>
    public readonly struct ThiefGiftGeometry
    {
        private readonly Transform _root;
        private readonly Vector3 _localCenter;
        private readonly Vector3 _localExtents;

        private ThiefGiftGeometry(Transform root, Vector3 localCenter, Vector3 localExtents)
        {
            _root = root;
            _localCenter = localCenter;
            _localExtents = localExtents;
        }

        public Transform Root => _root;
        public Vector3 LocalCenter => _localCenter;
        public Vector3 LocalExtents => _localExtents;
        public Vector3 WorldCenter => _root != null ? _root.TransformPoint(_localCenter) : Vector3.zero;

        public static bool TryCreate(Gift gift, out ThiefGiftGeometry geometry)
        {
            geometry = default;
            if (gift == null) return false;

            Transform root = gift.transform;
            BoxCollider box = gift.GetComponentInChildren<BoxCollider>(true);
            if (box != null)
            {
                EncapsulateLocalBox(root, box.transform, box.center, box.size * 0.5f,
                    out Vector3 center, out Vector3 extents);
                geometry = new ThiefGiftGeometry(root, center, extents);
                return true;
            }

            Renderer[] renderers = gift.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                geometry = new ThiefGiftGeometry(root, Vector3.zero, Vector3.one * 0.25f);
                return true;
            }

            Bounds world = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) world.Encapsulate(renderers[index].bounds);
            EncapsulateWorldBounds(root, world, out Vector3 localCenter, out Vector3 localExtents);
            geometry = new ThiefGiftGeometry(root, localCenter, localExtents);
            return true;
        }

        public Vector3 GrabStandPosition(Vector3 actorPosition, float actorRadiusM, float surfaceClearanceM)
        {
            Vector3 center = WorldCenter;
            Vector3 away = actorPosition - center;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = -_root.forward;
            away.Normalize();

            float distance = SupportRadius(away, _root.rotation) +
                             Mathf.Max(0f, actorRadiusM) + Mathf.Max(0f, surfaceClearanceM);
            Vector3 position = center + away * distance;
            position.y = actorPosition.y;
            return position;
        }

        public float SupportRadius(Vector3 worldDirection, Quaternion rotation)
        {
            if (worldDirection.sqrMagnitude < 0.001f) return 0f;
            Vector3 direction = worldDirection.normalized;
            Vector3 scaledExtents = Scaled(_localExtents);
            Vector3 axisX = rotation * new Vector3(scaledExtents.x, 0f, 0f);
            Vector3 axisY = rotation * new Vector3(0f, scaledExtents.y, 0f);
            Vector3 axisZ = rotation * new Vector3(0f, 0f, scaledExtents.z);
            return Mathf.Abs(Vector3.Dot(axisX, direction)) +
                   Mathf.Abs(Vector3.Dot(axisY, direction)) +
                   Mathf.Abs(Vector3.Dot(axisZ, direction));
        }

        public Vector3 RootPositionForCenter(Vector3 worldCenter, Quaternion rotation)
        {
            return worldCenter - rotation * Scaled(_localCenter);
        }

        public Vector3 CenterAt(Vector3 rootPosition, Quaternion rotation)
        {
            return rootPosition + rotation * Scaled(_localCenter);
        }

        private Vector3 Scaled(Vector3 value)
        {
            Vector3 scale = _root != null ? _root.lossyScale : Vector3.one;
            return Vector3.Scale(value, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }

        private static void EncapsulateLocalBox(Transform root, Transform boxTransform,
            Vector3 center, Vector3 extents, out Vector3 localCenter, out Vector3 localExtents)
        {
            Bounds local = new Bounds(root.InverseTransformPoint(boxTransform.TransformPoint(center)), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                local.Encapsulate(root.InverseTransformPoint(boxTransform.TransformPoint(corner)));
            }
            localCenter = local.center;
            localExtents = local.extents;
        }

        private static void EncapsulateWorldBounds(Transform root, Bounds world,
            out Vector3 localCenter, out Vector3 localExtents)
        {
            Bounds local = new Bounds(root.InverseTransformPoint(world.center), Vector3.zero);
            Vector3 extents = world.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                local.Encapsulate(root.InverseTransformPoint(world.center +
                    Vector3.Scale(extents, new Vector3(x, y, z))));
            localCenter = local.center;
            localExtents = local.extents;
        }
    }
}

using UnityEngine;

namespace PPack
{
    internal readonly struct PrefabFootprint
    {
        public PrefabFootprint(Bounds localBounds)
        {
            LocalBounds = localBounds;
        }

        public Bounds LocalBounds { get; }

        public static bool TryCreate(GameObject prefab, out PrefabFootprint footprint)
        {
            footprint = default;
            if (prefab == null) return false;

            bool hasBounds = false;
            Bounds bounds = default;
            Matrix4x4 rootToLocal = prefab.transform.worldToLocalMatrix;

            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                EncapsulateBounds(rootToLocal * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds, ref bounds, ref hasBounds);
            }

            foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                EncapsulateBounds(rootToLocal * renderer.transform.localToWorldMatrix, renderer.sharedMesh.bounds, ref bounds, ref hasBounds);
            }

            if (!hasBounds) return false;

            Vector3 size = bounds.size;
            size.x = Mathf.Max(size.x, 0.5f);
            size.z = Mathf.Max(size.z, 0.5f);
            bounds.size = size;
            footprint = new PrefabFootprint(bounds);
            return true;
        }

        private static void EncapsulateBounds(
            Matrix4x4 matrix,
            Bounds source,
            ref Bounds destination,
            ref bool hasBounds)
        {
            Vector3 min = source.min;
            Vector3 max = source.max;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 corner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        Vector3 point = matrix.MultiplyPoint3x4(corner);
                        if (!hasBounds)
                        {
                            destination = new Bounds(point, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            destination.Encapsulate(point);
                        }
                    }
                }
            }
        }
    }
}

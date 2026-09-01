using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class SceneRaycaster
    {
        private const float MaxRayDistance = 10000f;

        public static bool TryGetSurfaceHit(Vector2 guiPosition, out RaycastHit hit)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);
            return TryGetSurfaceHit(ray, out hit);
        }

        internal static bool TryGetSurfaceHit(Ray ray, out RaycastHit hit)
        {
            return Physics.Raycast(
                ray,
                out hit,
                MaxRayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class PedestrianAppearance : MonoBehaviour
    {
        [SerializeField] private NpcAppearanceCatalog _catalog;

        private readonly Dictionary<ENpcAppearanceSlot, SkinnedMeshRenderer> _renderers = new();

        private void Awake()
        {
            CacheRenderers();
        }

        public void Apply(NpcAppearanceData appearance)
        {
            if (_renderers.Count == 0) CacheRenderers();
            foreach (ENpcAppearanceSlot slot in System.Enum.GetValues(typeof(ENpcAppearanceSlot))) {
                ApplySlot(slot, appearance.GetId(slot));
            }
        }

        private void CacheRenderers()
        {
            _renderers.Clear();
            foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if (TryGetSlot(renderer.name, out ENpcAppearanceSlot slot)) _renderers[slot] = renderer;
            }
        }

        private void ApplySlot(ENpcAppearanceSlot slot, int id)
        {
            if (!_renderers.TryGetValue(slot, out SkinnedMeshRenderer renderer)) return;
            if (id == 0) {
                renderer.gameObject.SetActive(false);
                return;
            }
            if (_catalog == null || !_catalog.TryGet(slot, id, out NpcAppearanceCatalog.Entry entry)) return;
            renderer.sharedMesh = entry.Mesh;
            if (entry.Mesh != null) renderer.localBounds = entry.Mesh.bounds;
            renderer.gameObject.SetActive(entry.Mesh != null);
        }

        private static bool TryGetSlot(string rendererName, out ENpcAppearanceSlot slot)
        {
            switch (rendererName) {
                case "Body": slot = ENpcAppearanceSlot.Body; return true;
                case "Faces": slot = ENpcAppearanceSlot.Face; return true;
                case "Hairstyle": slot = ENpcAppearanceSlot.Hair; return true;
                case "T_Shirt": slot = ENpcAppearanceSlot.Top; return true;
                case "Outerwear": slot = ENpcAppearanceSlot.Coat; return true;
                case "Pants": slot = ENpcAppearanceSlot.Pants; return true;
                case "Shoes": slot = ENpcAppearanceSlot.Shoes; return true;
                case "Hat": slot = ENpcAppearanceSlot.Hat; return true;
                default: slot = default; return false;
            }
        }
    }
}

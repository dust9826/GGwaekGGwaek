using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    public enum TerrainPlacementMode
    {
        KeepSurface,
        FlattenTerrain
    }

    [CreateAssetMenu(fileName = "PrefabPalette", menuName = "PPack/Level Design/Prefab Palette")]
    public sealed class PrefabPalette : ScriptableObject
    {
        [Serializable]
        public sealed class Category
        {
            [SerializeField] private string _name = "Environment";
            [SerializeField] private TerrainPlacementMode _terrainPlacementMode;
            [SerializeField] private List<GameObject> _prefabs = new List<GameObject>();

            public string Name => string.IsNullOrWhiteSpace(_name) ? "Uncategorized" : _name;
            public TerrainPlacementMode TerrainPlacementMode => _terrainPlacementMode;
            public IReadOnlyList<GameObject> Prefabs => _prefabs;

            internal void RemoveInvalidEntries()
            {
                if (_prefabs == null)
                {
                    _prefabs = new List<GameObject>();
                    return;
                }

                for (int i = _prefabs.Count - 1; i >= 0; i--)
                {
                    GameObject prefab = _prefabs[i];
                    if (prefab != null && EditorUtility.IsPersistent(prefab)
                        && PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab)
                    {
                        continue;
                    }

                    _prefabs.RemoveAt(i);
                }
            }
        }

        [SerializeField] private List<Category> _categories = new List<Category> { new Category() };

        public IReadOnlyList<Category> Categories => _categories;

        private void OnValidate()
        {
            if (_categories == null)
            {
                _categories = new List<Category>();
                return;
            }

            foreach (Category category in _categories)
            {
                category?.RemoveInvalidEntries();
            }
        }
    }
}

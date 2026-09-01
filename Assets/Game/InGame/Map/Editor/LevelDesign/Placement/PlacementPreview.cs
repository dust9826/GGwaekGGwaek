using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    internal sealed class PlacementPreview : IDisposable
    {
        private readonly struct PreviewMesh
        {
            public PreviewMesh(Mesh mesh, Matrix4x4 localMatrix)
            {
                Mesh = mesh;
                LocalMatrix = localMatrix;
            }

            public Mesh Mesh { get; }
            public Matrix4x4 LocalMatrix { get; }
        }

        private static readonly Color PreviewColor = new Color(0.1f, 0.82f, 1f, 0.38f);
        private readonly List<PreviewMesh> _meshes = new List<PreviewMesh>();
        private Material _previewMaterial;

        internal int MeshCount => _meshes.Count;

        public void SetPrefab(GameObject prefab)
        {
            _meshes.Clear();
            if (prefab == null) return;

            Matrix4x4 rootToLocal = prefab.transform.worldToLocalMatrix;
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;

                Matrix4x4 relativeMatrix = rootToLocal * filter.transform.localToWorldMatrix;
                _meshes.Add(new PreviewMesh(filter.sharedMesh, relativeMatrix));
            }

            SkinnedMeshRenderer[] skinnedRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer renderer in skinnedRenderers)
            {
                if (renderer.sharedMesh == null) continue;

                Matrix4x4 relativeMatrix = rootToLocal * renderer.transform.localToWorldMatrix;
                _meshes.Add(new PreviewMesh(renderer.sharedMesh, relativeMatrix));
            }
        }

        public void Draw(Matrix4x4 placementMatrix)
        {
            if (Event.current.type != EventType.Repaint || _meshes.Count == 0) return;
            if (!EnsureMaterial() || !_previewMaterial.SetPass(0)) return;

            foreach (PreviewMesh previewMesh in _meshes)
            {
                Matrix4x4 matrix = placementMatrix * previewMesh.LocalMatrix;
                int subMeshCount = Mathf.Max(1, previewMesh.Mesh.subMeshCount);
                for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                {
                    Graphics.DrawMeshNow(previewMesh.Mesh, matrix, subMesh);
                }
            }
        }

        public void Dispose()
        {
            _meshes.Clear();
            if (_previewMaterial == null) return;

            UnityEngine.Object.DestroyImmediate(_previewMaterial);
            _previewMaterial = null;
        }

        private bool EnsureMaterial()
        {
            if (_previewMaterial != null) return true;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return false;

            _previewMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _previewMaterial.SetColor("_Color", PreviewColor);
            _previewMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _previewMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _previewMaterial.SetInt("_Cull", (int)CullMode.Off);
            _previewMaterial.SetInt("_ZWrite", 0);
            _previewMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return true;
        }
    }
}

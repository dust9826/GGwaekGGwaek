using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    internal sealed class RoadPreview : IDisposable
    {
        private Mesh _mesh;
        private Material _material;

        public int VertexCount => _mesh == null ? 0 : _mesh.vertexCount;

        public void Set(
            IReadOnlyList<Vector3> worldPoints,
            float width,
            Material material,
            float startJunctionRadius = 0f,
            float endJunctionRadius = 0f)
        {
            ReleaseMesh();
            _material = material;
            if (worldPoints == null || worldPoints.Count < 2) return;
            _mesh = RoadPath.BuildRibbonMesh(
                worldPoints,
                width,
                "Road Builder Preview",
                startJunctionRadius,
                endJunctionRadius);
            _mesh.hideFlags = HideFlags.HideAndDontSave;
        }

        public void Draw()
        {
            if (_mesh == null || _material == null) return;
            if (_material.SetPass(0)) Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
        }

        public void Dispose()
        {
            ReleaseMesh();
            _material = null;
        }

        private void ReleaseMesh()
        {
            if (_mesh == null) return;
            UnityEngine.Object.DestroyImmediate(_mesh);
            _mesh = null;
        }
    }
}

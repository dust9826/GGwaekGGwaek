using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    internal sealed class TerrainFlattenPreview : IDisposable
    {
        private static readonly Color PreviewColor = new Color(0.2f, 1f, 0.55f, 0.42f);
        private Mesh _mesh;
        private Material _material;
        private TerrainFlattenPlan _plan;

        public void SetPlan(TerrainFlattenPlan plan)
        {
            _plan = plan;
            DestroyMesh();
            if (plan == null) return;

            int width = plan.Width;
            int height = plan.Height;
            Vector3[] vertices = new Vector3[width * height];
            int[] triangles = new int[(width - 1) * (height - 1) * 6];

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    vertices[z * width + x] = plan.GetWorldPoint(x, z) + Vector3.up * 0.035f;
                }
            }

            int triangle = 0;
            for (int z = 0; z < height - 1; z++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    int a = z * width + x;
                    int b = a + 1;
                    int c = a + width;
                    int d = c + 1;
                    triangles[triangle++] = a;
                    triangles[triangle++] = c;
                    triangles[triangle++] = b;
                    triangles[triangle++] = b;
                    triangles[triangle++] = c;
                    triangles[triangle++] = d;
                }
            }

            _mesh = new Mesh
            {
                name = "Terrain Flatten Preview",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = vertices,
                triangles = triangles
            };
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        public void Draw()
        {
            if (_plan == null || _mesh == null || Event.current.type != EventType.Repaint) return;
            if (!EnsureMaterial() || !_material.SetPass(0)) return;

            Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
            UnityEditor.Handles.color = new Color(0.15f, 1f, 0.45f, 0.95f);
            Vector3[] corners = _plan.GetFootprintCorners(0.08f);
            for (int i = 0; i < corners.Length; i++)
            {
                UnityEditor.Handles.DrawAAPolyLine(3f, corners[i], corners[(i + 1) % corners.Length]);
            }
        }

        public void Dispose()
        {
            _plan = null;
            DestroyMesh();
            if (_material == null) return;

            UnityEngine.Object.DestroyImmediate(_material);
            _material = null;
        }

        private bool EnsureMaterial()
        {
            if (_material != null) return true;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return false;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetColor("_Color", PreviewColor);
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return true;
        }

        private void DestroyMesh()
        {
            if (_mesh == null) return;
            UnityEngine.Object.DestroyImmediate(_mesh);
            _mesh = null;
        }
    }
}

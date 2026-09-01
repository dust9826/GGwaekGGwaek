using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace PPack
{
    [CustomEditor(typeof(IceFunnelProfile))]
    internal sealed class IceFunnelProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var profile = (IceFunnelProfile)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(profile.Describe(), MessageType.None);

            if (!GUILayout.Button("Rebuild Mesh", GUILayout.Height(28f))) return;

            string report = IceFunnelBuilder.Rebuild(profile);
            Debug.Log(report, profile);
        }
    }

    internal static class IceFunnelBuilder
    {
        public static string Rebuild(IceFunnelProfile profile)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(profile.MeshPath);
            if (mesh == null) return $"메시가 없다: {profile.MeshPath}";

            int rx = profile.Segments;
            int ry = profile.Rings;
            // 이음매 열을 하나 더 낸다. 그래야 u 가 1 까지 도달한다 — 닫아버리면 마지막 칸의
            // u 가 1 에 못 미쳐 타일링이 어긋난다.
            int cols = rx + 1;
            int rows = ry + 1;

            var verts = new Vector3[cols * rows];
            var uvs = new Vector2[cols * rows];
            var norms = new Vector3[cols * rows];

            float rb = profile.BaseRadius;
            float rt = profile.TopRadius;
            float h = profile.Height;
            float curve = profile.Curve;

            for (int y = 0; y < rows; y++)
            {
                float t = (float)y / ry;
                float r = rb + (rt - rb) * Mathf.Pow(t, curve);
                // 벽의 기울기. 법선을 바깥으로 향하게 하려면 이게 필요하다.
                float dr = (rt - rb) * curve * Mathf.Pow(Mathf.Max(t, 1e-4f), curve - 1f) / h;
                for (int x = 0; x < cols; x++)
                {
                    float u = (float)x / rx;
                    float a = u * Mathf.PI * 2f;
                    float cs = Mathf.Cos(a);
                    float sn = Mathf.Sin(a);
                    int i = y * cols + x;
                    verts[i] = new Vector3(cs * r, t * h, sn * r);
                    uvs[i] = new Vector2(u, t);
                    norms[i] = new Vector3(cs, -dr, sn).normalized;
                }
            }

            var tris = new List<int>(rx * ry * 6);
            for (int y = 0; y < ry; y++)
            {
                for (int x = 0; x < rx; x++)
                {
                    int i0 = y * cols + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + cols;
                    int i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            }

            // 같은 에셋을 그대로 갈아끼운다. 지우고 다시 만들면 GUID 가 바뀌어 씬 참조가 끊긴다.
            mesh.Clear();
            mesh.indexFormat = verts.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = norms;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();

            return $"{mesh.name} 재생성 — 정점 {verts.Length}, 삼각형 {tris.Count / 3}, " +
                   $"바운즈 {mesh.bounds.size}. {profile.Describe()}";
        }
    }
}

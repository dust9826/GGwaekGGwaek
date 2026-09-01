// AnyTest 스파이크 v7 에서 이식. 원본: Assets/SnowGrainFakeV7/Scripts/SnowFakeV7Meshes.cs
// 프록시 박스 메시 — 마처의 커버리지를 담당한다. 슬랩 테스트는 월드 공간이라 메시의 정확한
// 모양이 아니라 **볼륨을 덮는지**만 중요하다.
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// The meshes v7 needs, generated in code so the variant stays asset free.
    ///
    /// v7 needs exactly ONE: the raymarch proxy box.
    ///
    /// v6 needed two, because v6's snow load was a separate SPHERE MESH drawn beside the height field.
    /// v7's load is HEIGHT FIELD - the heap in front of the blade is written into the same texture the
    /// virgin slab lives in - so there is no second surface to build, no icosphere, no welded smooth
    /// normals, and no risk of the drawn silhouette disagreeing with the collidable one. Deleting
    /// <c>CreateBlob</c> is therefore not a simplification, it is the variant's central decision showing
    /// up in the file list.
    /// </summary>
    internal static class SnowProxyBoxMesh
    {
        /// <summary>
        /// Axis-aligned box, centred on the origin in object space, with OUTWARD winding. The shader
        /// draws it with Cull Front, so what actually rasterises is the inside of the box - the only
        /// version of this that survives the camera being inside the volume, and the camera in this
        /// scene sits at ~4 m against a box that is metres tall.
        ///
        /// Built from a tangent pair per face rather than a hand-written corner table. For outward
        /// normal n the tangents are chosen so that t x b = n; the quad is then c-t-b, c+t-b, c+t+b,
        /// c-t+b and the first triangle's geometric normal cross(v1-v0, v2-v0) works out to
        /// 4(t x b) = 4n. That makes "wound outward" a property of the construction instead of
        /// something to be checked corner by corner, and getting one face backwards would cull the
        /// wrong side and leave a hole in the volume's coverage.
        /// </summary>
        public static Mesh CreateProxyBox(Vector3 size)
        {
            Vector3 h = size * 0.5f;

            Vector3[] n =
            {
                Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
            };
            Vector3[] tan =
            {
                Vector3.up, Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.up,
            };
            Vector3[] bit =
            {
                Vector3.forward, Vector3.up, Vector3.right, Vector3.forward, Vector3.up, Vector3.right,
            };

            var verts = new Vector3[24];
            var tris = new int[36];

            for (int f = 0; f < 6; ++f)
            {
                Vector3 c = Vector3.Scale(n[f], h);
                Vector3 t = Vector3.Scale(tan[f], h);
                Vector3 b2 = Vector3.Scale(bit[f], h);

                int b = f * 4;
                verts[b + 0] = c - t - b2;
                verts[b + 1] = c + t - b2;
                verts[b + 2] = c + t + b2;
                verts[b + 3] = c - t + b2;

                int i = f * 6;
                tris[i + 0] = b + 0; tris[i + 1] = b + 1; tris[i + 2] = b + 2;
                tris[i + 3] = b + 0; tris[i + 4] = b + 2; tris[i + 5] = b + 3;
            }

            var mesh = new Mesh { name = "SnowFakeV7ProxyBox" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, size);
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}

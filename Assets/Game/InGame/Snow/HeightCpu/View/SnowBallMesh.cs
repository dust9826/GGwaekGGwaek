using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 눈덩이 메시. 용접된 아이코스피어 하나를 만들어 <b>모든 눈덩이가 공유</b>한다.
    ///
    /// <para><b>왜 유니티 기본 구가 아닌가.</b> 기본 구는 UV 구라서 극에서 삼각형이 한 점으로
    /// 모인다. 이 셰이더는 정점에서 표면을 밀어내므로(<c>SnowBall.shader</c>) 극의 밀도가 다르면
    /// 그 자리에만 변위가 뭉치고, 반지름 2 m 짜리 공에서 그것이 눈에 보인다. 아이코스피어는
    /// 삼각형이 고르다.</para>
    ///
    /// <para><b>왜 에셋이 아니라 코드인가.</b> 메시 <c>.asset</c> 은 GUID 를 지닌 바이너리라
    /// Plastic 이 병합하지 못한다 - 이 프로젝트의 <c>.vfx</c>·<c>.shadergraph</c> 와 같은 제약이다.
    /// 만드는 비용은 642 정점 한 번이므로 저장할 이유가 없다.</para>
    ///
    /// <para><b>지름 1 이다</b>, 반지름 1 이 아니다. 유니티 기본 구와 같은 관례라서
    /// <c>SnowBallCarrier</c> 의 스케일 계산(<c>_meshDiameterM</c>)과 <c>SphereCollider</c> 반지름
    /// 0.5 를 그대로 쓸 수 있다 - 통째로 갈아끼우면서 물리는 한 줄도 안 건드린다.</para>
    /// </summary>
    public static class SnowBallMesh
    {
        /// <summary>
        /// 세분 3 = 삼각형 1,280 · 정점 642. 화면에 공이 몇 개뿐이라 정점 비용은 잡음이지만
        /// <b>실루엣이 전부</b>다 - 세분 1(삼각형 80)은 지름 4 m 에서 다면체로 보인다. 그리고
        /// 변위를 싣는 것이 정점 단계이므로, 거친 구는 로브를 실루엣에서 잃고 음영에만 남긴다.
        /// 그것이 바로 이 룩이 아닌 범프맵 룩이다.
        /// </summary>
        private const int Subdivisions = 3;

        private static Mesh _shared;

        public static Mesh Shared
        {
            get
            {
                if (_shared == null) _shared = Build(Subdivisions);
                return _shared;
            }
        }

        /// <summary>
        /// 정이십면체. 황금비 직사각형 구성으로 만든다 - 손으로 적은 표가 아니라서 12 정점이
        /// 정확히 구 위에 있고, 그래야 정규화한 위치가 <b>정확한</b> 매끄러운 노멀이 된다.
        /// </summary>
        private static void Icosahedron(out Vector3[] verts, out int[] tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            float s = 1f / Mathf.Sqrt(1f + t * t);

            verts = new[]
            {
                new Vector3(-1f,  t, 0f) * s, new Vector3( 1f,  t, 0f) * s,
                new Vector3(-1f, -t, 0f) * s, new Vector3( 1f, -t, 0f) * s,
                new Vector3(0f, -1f,  t) * s, new Vector3(0f,  1f,  t) * s,
                new Vector3(0f, -1f, -t) * s, new Vector3(0f,  1f, -t) * s,
                new Vector3( t, 0f, -1f) * s, new Vector3( t, 0f,  1f) * s,
                new Vector3(-t, 0f, -1f) * s, new Vector3(-t, 0f,  1f) * s,
            };

            // cross(b - a, c - a) 가 바깥을 향하도록 감았다 - 유니티의 앞면 관례다.
            tris = new[]
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,    0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2,  10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,    3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,   8, 6, 7,   9, 8, 1,
            };
        }

        private static Mesh Build(int subdivisions)
        {
            Icosahedron(out Vector3[] baseVerts, out int[] baseTris);

            var verts = new List<Vector3>(baseVerts);
            var tris = new List<int>(baseTris);

            int levels = Mathf.Clamp(subdivisions, 0, 3);
            for (int level = 0; level < levels; level++)
            {
                var midpoints = new Dictionary<long, int>(tris.Count * 2);
                var next = new List<int>(tris.Count * 4);

                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Midpoint(verts, midpoints, a, b);
                    int bc = Midpoint(verts, midpoints, b, c);
                    int ca = Midpoint(verts, midpoints, c, a);

                    // 자식 넷의 감기가 부모와 같아서, "바깥" 이 세분을 넘어 살아남는다.
                    next.Add(a); next.Add(ab); next.Add(ca);
                    next.Add(b); next.Add(bc); next.Add(ab);
                    next.Add(c); next.Add(ca); next.Add(bc);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }

                tris = next;
            }

            // 노멀이 곧 위치다. 모든 정점이 단위구 위에 있으므로 정규화한 위치가 매끄러운 노멀이고,
            // 그것이 삼각형 1,280개를 부드러운 덩어리로 읽히게 하는 전부이며 계산 비용이 0 이다.
            var normals = new Vector3[verts.Count];
            var scaled = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                normals[i] = verts[i];
                scaled[i] = verts[i] * 0.5f;    // 지름 1
            }

            var mesh = new Mesh { name = "SnowBallIco" + levels };
            mesh.SetVertices(scaled);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0, false);

            // 바운즈에 변위 여유를 준다. 셰이더가 반지름의 최대 25%(_BallLumpAmp 상한)까지 밀어내므로
            // 딱 맞는 바운즈는 화면 가장자리에서 공을 잘라 사라지게 만든다.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1.25f);
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// 변 (a,b) 의 정규화된 중점 인덱스. 그 변을 쓰는 두 삼각형이 <b>같은</b> 정점을 공유하게
        /// 한다 - 용접이다. 용접하지 않으면 세분 3 이 642 대신 3,840 정점을 올리고, 더 중요하게는
        /// 셰이더가 정점마다 계산하는 오브젝트 공간 노이즈가 모든 변에서 이어지지 않고 이음선을 낸다.
        /// </summary>
        private static int Midpoint(List<Vector3> verts, Dictionary<long, int> cache, int a, int b)
        {
            long lo = Mathf.Min(a, b);
            long hi = Mathf.Max(a, b);
            long key = (lo << 32) | hi;

            if (cache.TryGetValue(key, out int existing)) return existing;

            int index = verts.Count;
            verts.Add((verts[a] + verts[b]).normalized);
            cache[key] = index;
            return index;
        }
    }
}

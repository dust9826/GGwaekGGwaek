using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// 높이를 담는 쿼드트리. <b>권위 시뮬이 쓰는 자료구조가 아니다</b> — 평평한 배열에서 만들어
    /// 내보내는 <b>표현</b>이다(`docs/specs/2026-08-21-snow-quadtree-commands.md` 4절).
    ///
    /// <para>시뮬은 매 틱 좁은 사각형에 쓴다. 쿼드트리 쓰기는 분할·병합이 붙어 배열 쓰기보다
    /// 비싸므로, 권위는 <see cref="SnowHeightFieldCpu"/> 의 배열을 그대로 쓰고 이 트리는 스냅샷을
    /// 만들 때만 세운다.</para>
    ///
    /// <para><b>무엇을 접는가:</b> 균일한 사각형이다. 처녀설은 루트 하나로 접히고, 완전히 깎인
    /// 넓은 자리도 마찬가지다. 자국 경계만 잎까지 펼쳐진다. 늦게 참가하는 피어에게 필드 전체를
    /// 밀어 보내는 대신 이 트리를 보내는 것이 목표다.</para>
    ///
    /// <para>격자는 2의 거듭제곱 정사각형으로 패딩한다. 필드 밖 셀은 <see cref="Outside"/> 로
    /// 두고, 그 값끼리는 균일한 것으로 본다 — 밖은 어차피 보낼 것이 없다.</para>
    /// </summary>
    public sealed class SnowHeightQuadtree
    {
        /// <summary>필드 밖을 나타내는 센티넬. 실제 높이는 ushort 이므로 이 값과 겹치지 않는다.</summary>
        public const int Outside = -1;

        private readonly SnowFieldGeometry _geo;
        private readonly int _side;

        /// <summary>잎 노드 수(= 보낼 높이의 개수).</summary>
        public int LeafCount { get; private set; }

        /// <summary>내부 노드 수.</summary>
        public int InternalCount { get; private set; }

        /// <summary>필드 밖으로 잘려 값이 없는 잎 수. 바이트에 들어가지 않는다.</summary>
        public int OutsideLeafCount { get; private set; }

        public SnowHeightQuadtree(SnowFieldGeometry geo)
        {
            _geo = geo;

            int max = geo.ResX > geo.ResZ ? geo.ResX : geo.ResZ;
            int side = 1;
            while (side < max) side <<= 1;
            _side = side;
        }

        public int Side => _side;

        /// <summary>
        /// 트리를 세우고 <b>직렬화한 바이트</b>를 돌려준다.
        ///
        /// <para>형식: 선순회. 노드마다 태그 1 비트(0 = 잎, 1 = 내부), 잎이면 높이 2 바이트.
        /// 필드 밖 잎은 태그만 나가고 높이는 안 나간다 — 받는 쪽도 자기 격자 크기를 알기 때문이다.</para>
        /// </summary>
        public byte[] Serialize(SnowHeightFieldCpu field)
        {
            LeafCount = 0;
            InternalCount = 0;
            OutsideLeafCount = 0;

            var tags = new List<bool>(1024);
            var heights = new List<ushort>(1024);

            Emit(field, 0, 0, _side, tags, heights);

            int tagBytes = (tags.Count + 7) / 8;
            var bytes = new byte[tagBytes + heights.Count * 2];

            for (int i = 0; i < tags.Count; i++)
                if (tags[i]) bytes[i >> 3] |= (byte)(1 << (i & 7));

            int at = tagBytes;
            foreach (ushort h in heights)
            {
                bytes[at++] = (byte)(h & 0xFF);
                bytes[at++] = (byte)(h >> 8);
            }
            return bytes;
        }

        /// <summary>이 사각형이 한 값으로 균일한가. 균일하면 그 값, 아니면 <c>int.MinValue</c>.</summary>
        private int Uniform(SnowHeightFieldCpu field, int x0, int z0, int size)
        {
            int first = int.MinValue;

            for (int z = z0; z < z0 + size; z++)
            for (int x = x0; x < x0 + size; x++)
            {
                int v = _geo.InBounds(x, z) ? field.HeightMm[_geo.CellIndex(x, z)] : Outside;
                if (first == int.MinValue) first = v;
                else if (v != first) return int.MinValue;
            }
            return first;
        }

        private void Emit(SnowHeightFieldCpu field, int x0, int z0, int size,
                          List<bool> tags, List<ushort> heights)
        {
            int uniform = Uniform(field, x0, z0, size);

            if (uniform != int.MinValue || size == 1)
            {
                tags.Add(false);
                LeafCount++;

                if (uniform == Outside) { OutsideLeafCount++; return; }

                int v = uniform == int.MinValue
                    ? (_geo.InBounds(x0, z0) ? field.HeightMm[_geo.CellIndex(x0, z0)] : 0)
                    : uniform;
                heights.Add((ushort)v);
                return;
            }

            tags.Add(true);
            InternalCount++;

            int half = size >> 1;
            Emit(field, x0,        z0,        half, tags, heights);
            Emit(field, x0 + half, z0,        half, tags, heights);
            Emit(field, x0,        z0 + half, half, tags, heights);
            Emit(field, x0 + half, z0 + half, half, tags, heights);
        }

        /// <summary>평평한 배열로 보낼 때의 바이트. 비교 기준이다.</summary>
        public int FlatBytes => _geo.CellCount * 2;
    }
}

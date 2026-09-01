using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <b>패널을 타일로 나누는 계산 전부.</b> 상태가 없다 — 그래서 그래픽 장치 없이 EditMode 로
    /// 전수 검증할 수 있고, <see cref="SnowDisplaceView"/> 는 이것을 부르기만 한다.
    ///
    /// <para><b>왜 떼어냈는가.</b> 틀리면 화면에 <b>실금</b>이 가는 부분이 전부 여기다 —
    /// 이웃 타일이 공유하는 모서리 정점이 한 비트라도 다르면 그 선이 보인다. 뷰 안에 두면
    /// <c>GameObject</c>·<c>Texture2D</c> 때문에 테스트가 안 된다.</para>
    /// </summary>
    public static class SnowPanelTiling
    {
        /// <summary>스테이징 텍스처의 최소 한 변(셀). 이보다 작게 만들 이유가 없다.</summary>
        public const int MinStagingCells = 16;

        /// <summary>
        /// 축 하나의 전역 정점 수. <b>지금 <c>BuildGrid</c> 가 쓰는 식과 같아야 한다</b> —
        /// 타일링 전후로 정점이 같은 자리에 있어야 화면이 안 바뀐다.
        /// </summary>
        public static int LatticeCount(float sizeM, float spacingM)
            => Mathf.Max(2, Mathf.RoundToInt(sizeM / spacingM) + 1);

        /// <summary>
        /// 전역 정점 <paramref name="index"/> 의 좌표.
        ///
        /// <para>⚠ <b>타일이 달라도 같은 index 는 반드시 같은 값을 내야 한다.</b> 타일마다 로컬
        /// 원점 기준으로 다시 계산하면 부동소수 결과가 미세하게 갈려 공유 모서리에 실금이 간다.
        /// 그래서 이 함수는 타일을 인자로 받지 않는다.</para>
        /// </summary>
        public static float LatticePos(float minM, float sizeM, int count, int index)
            => minM + index / (float)(count - 1) * sizeM;

        /// <summary>타일 하나가 갖는 quad 수(축 하나). 최소 1.</summary>
        public static int QuadsPerTile(float tileSizeM, float spacingM)
            => Mathf.Max(1, Mathf.RoundToInt(tileSizeM / spacingM));

        /// <summary>축 하나의 타일 수. 필드보다 타일이 커도 1 이다.</summary>
        public static int TileCountOnAxis(int latticeCount, int quadsPerTile)
            => Mathf.Max(1, Mathf.CeilToInt((latticeCount - 1) / (float)quadsPerTile));

        /// <summary>
        /// 타일 <paramref name="tile"/> 이 갖는 정점 인덱스 구간. <b>양 끝을 포함</b>하고,
        /// 끝 인덱스는 다음 타일의 시작 인덱스와 <b>같다</b>(경계 정점을 공유한다).
        /// </summary>
        public static void TileVertexRange(int latticeCount, int quadsPerTile, int tile,
                                           out int lo, out int hi)
        {
            lo = tile * quadsPerTile;
            hi = lo + quadsPerTile;
            if (hi > latticeCount - 1) hi = latticeCount - 1;
        }

        /// <summary>
        /// 더러운 청크들을 덮는 <b>셀 좌표</b> 사각형(양 끝 포함). 비어 있으면 <c>false</c>.
        /// 사각형은 청크 정렬이므로 항상 청크 배수 폭이다.
        /// </summary>
        public static bool TryDirtyCellRect(SnowFieldGeometry geo, IReadOnlyList<int> dirtyChunks,
                                            out int cx0, out int cz0, out int cx1, out int cz1)
        {
            cx0 = cz0 = int.MaxValue;
            cx1 = cz1 = int.MinValue;
            if (dirtyChunks == null || dirtyChunks.Count == 0) return false;

            for (int i = 0; i < dirtyChunks.Count; i++)
            {
                geo.ChunkCellBounds(dirtyChunks[i], out int x0, out int z0, out int x1, out int z1);
                if (x0 < cx0) cx0 = x0;
                if (z0 < cz0) cz0 = z0;
                if (x1 > cx1) cx1 = x1;
                if (z1 > cz1) cz1 = z1;
            }

            return true;
        }

        /// <summary>
        /// 사각형 <paramref name="width"/> × <paramref name="height"/> 를 담을 정사각 스테이징의
        /// 한 변. 2의 거듭제곱으로 올림한다 — 매 프레임 크기가 달라도 <b>몇 종류만</b> 만들어
        /// 재사용하기 위해서다. <paramref name="maxCells"/> 를 넘으면 <b>0</b>(= 전체 업로드).
        /// </summary>
        public static int StagingSizeFor(int width, int height, int maxCells)
        {
            int need = width > height ? width : height;
            if (need <= 0 || need > maxCells) return 0;

            int size = MinStagingCells;
            while (size < need) size <<= 1;
            return size > maxCells ? 0 : size;
        }
    }
}

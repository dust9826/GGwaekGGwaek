using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// 이번 스텝에 시뮬레이션할 청크 목록. <b>블레이드 주변 사각형</b>과 <b>아직 잠들지 않은 청크</b>의
    /// 합집합을, 오름차순 · 중복 없이 · 상한 이하로.
    ///
    /// 상한과 정렬은 성능 노브가 아니라 <b>시뮬레이션 규칙</b>이다. 피어마다 상한이나 순회 순서가
    /// 다르면 필드가 갈라지고, 그것은 매 프레임 블록 해시 불일치로 나타나 델타 동기화의 이점을
    /// 없앤다. 그래서 상한은 빌드 상수여야 하고, 프레임 시간에 따라 움직이는 적응형 상한은 금지다.
    /// </summary>
    public sealed class SnowActiveSet
    {
        private readonly List<int> _scratchRect = new List<int>(256);
        private readonly List<int> _scratchDirty = new List<int>(1024);
        private readonly List<int> _chunks;

        public SnowActiveSet(int capacity)
        {
            _chunks = new List<int>(capacity);
        }

        public IReadOnlyList<int> Chunks => _chunks;

        /// <summary>상한에 걸려 버려진 청크 수. 0 이 아니면 그 스텝은 시뮬을 덜 돈 것이다.</summary>
        public int DroppedByCap { get; private set; }

        public void Build(SnowChunkQuadtree tree, int cx0, int cz0, int cx1, int cz1, int cap)
        {
            tree.QueryRect(cx0, cz0, cx1, cz1, _scratchRect);
            tree.QueryDirty(_scratchDirty);

            _chunks.Clear();
            _chunks.AddRange(_scratchRect);
            _chunks.AddRange(_scratchDirty);
            _chunks.Sort();

            // 제자리 중복 제거.
            int w = 0;
            for (int r = 0; r < _chunks.Count; r++)
            {
                if (w > 0 && _chunks[r] == _chunks[w - 1]) continue;
                _chunks[w++] = _chunks[r];
            }
            if (w < _chunks.Count) _chunks.RemoveRange(w, _chunks.Count - w);

            if (_chunks.Count > cap)
            {
                DroppedByCap = _chunks.Count - cap;
                _chunks.RemoveRange(cap, DroppedByCap);
            }
            else
            {
                DroppedByCap = 0;
            }
        }
    }
}

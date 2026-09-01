using System;
using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// 접지한 액터가 <b>새로 밟은 셀</b>에서 일정 깊이만큼 눈을 걷는다.
    /// 같은 자리에 서 있는 동안은 다시 걷지 않고, 벗어났다가 돌아오면 새 통과로 본다.
    /// </summary>
    public sealed class SnowFootprintCpu
    {
        public const int PlayerCutDepthMm = 50;

        private HashSet<int> _previousContact = new HashSet<int>();
        private HashSet<int> _currentContact = new HashSet<int>();
        private readonly HashSet<int> _sweptCells = new HashSet<int>();

        private SnowHeightFieldCpu _field;
        private float _previousX;
        private float _previousZ;
        private bool _hasPreviousPose;
        private bool _hasSampled;

        /// <summary>현재 발밑에 겹친 셀 수. 검증과 디버그 표시가 읽는다.</summary>
        public int ContactCellCount => _previousContact.Count;

        /// <summary>
        /// 한 접지 스텝. 최초 배치 때는 자국을 만들지 않고 기준만 심는다. 이후 이동·재착지·필드 진입에서
        /// 새로 닿은 셀만 <paramref name="cutDepthMm"/> 만큼 줄인다.
        /// </summary>
        public long Step(SnowHeightFieldCpu field, float centerX, float centerZ, float radiusM,
                         bool grounded, int cutDepthMm = PlayerCutDepthMm)
        {
            if (!grounded || field == null)
            {
                _hasPreviousPose = false;
                _previousContact.Clear();
                return 0L;
            }

            radiusM = Math.Max(radiusM, SnowFieldGeometry.CellSizeM * 0.25f);
            cutDepthMm = Math.Max(0, cutDepthMm);

            bool fieldChanged = !ReferenceEquals(_field, field);
            if (fieldChanged)
            {
                _field = field;
                _hasPreviousPose = false;
                _previousContact.Clear();
            }

            FillCircle(field.Geo, centerX, centerZ, radiusM, _currentContact);

            // 씬 시작부터 가만히 서 있는 펭귄이 눈을 파지 않게 최초 표본은 기준만 심는다.
            if (!_hasSampled)
            {
                _hasSampled = true;
                StoreCurrentPose(centerX, centerZ);
                return 0L;
            }

            _sweptCells.Clear();
            if (_hasPreviousPose && !fieldChanged)
            {
                FillCapsule(field.Geo, _previousX, _previousZ, centerX, centerZ, radiusM, _sweptCells);
            }
            else
            {
                _sweptCells.UnionWith(_currentContact);
            }

            long removedMm = 0L;
            if (cutDepthMm > 0)
            {
                foreach (int cellIndex in _sweptCells)
                {
                    if (_previousContact.Contains(cellIndex)) continue;

                    int heightMm = field.GetAt(cellIndex);
                    if (heightMm <= 0) continue;

                    int removeMm = Math.Min(cutDepthMm, heightMm);
                    removedMm += -field.AddAt(cellIndex, -removeMm);
                    field.WakeChunkOfCell(cellIndex % field.Geo.ResX, cellIndex / field.Geo.ResX);
                }
            }

            StoreCurrentPose(centerX, centerZ);
            return removedMm;
        }

        public void Reset()
        {
            _field = null;
            _hasPreviousPose = false;
            _hasSampled = false;
            _previousContact.Clear();
            _currentContact.Clear();
            _sweptCells.Clear();
        }

        private void StoreCurrentPose(float centerX, float centerZ)
        {
            HashSet<int> swap = _previousContact;
            _previousContact = _currentContact;
            _currentContact = swap;
            _currentContact.Clear();
            _previousX = centerX;
            _previousZ = centerZ;
            _hasPreviousPose = true;
        }

        private static void FillCircle(SnowFieldGeometry geo, float centerX, float centerZ, float radiusM,
                                       HashSet<int> result)
        {
            result.Clear();
            FillCapsule(geo, centerX, centerZ, centerX, centerZ, radiusM, result);
        }

        private static void FillCapsule(SnowFieldGeometry geo, float fromX, float fromZ,
                                        float toX, float toZ, float radiusM, HashSet<int> result)
        {
            float minX = Math.Min(fromX, toX) - radiusM;
            float minZ = Math.Min(fromZ, toZ) - radiusM;
            float maxX = Math.Max(fromX, toX) + radiusM;
            float maxZ = Math.Max(fromZ, toZ) + radiusM;
            if (!geo.TryWorldRectToCellRect(minX, minZ, maxX, maxZ,
                                            out int cx0, out int cz0, out int cx1, out int cz1))
            {
                return;
            }

            float segmentX = toX - fromX;
            float segmentZ = toZ - fromZ;
            float segmentLengthSq = segmentX * segmentX + segmentZ * segmentZ;
            float radiusSq = radiusM * radiusM;

            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                geo.CellCenterWorld(cx, cz, out float cellX, out float cellZ);
                float t = segmentLengthSq > 1e-8f
                    ? ((cellX - fromX) * segmentX + (cellZ - fromZ) * segmentZ) / segmentLengthSq
                    : 0f;
                t = Math.Max(0f, Math.Min(1f, t));

                float closestX = fromX + segmentX * t;
                float closestZ = fromZ + segmentZ * t;
                float dx = cellX - closestX;
                float dz = cellZ - closestZ;
                if (dx * dx + dz * dz <= radiusSq) result.Add(geo.CellIndex(cx, cz));
            }
        }
    }
}

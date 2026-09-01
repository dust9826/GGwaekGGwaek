namespace PPack
{
    /// <summary>평면상의 방향이 있는 사각형 하나. 블레이드는 이것 1~3개의 합집합이다.</summary>
    public struct SnowObb
    {
        public float CenterX, CenterZ;
        public float RightX, RightZ;
        public float ForwardX, ForwardZ;
        public float HalfWidthM, HalfDepthM;

        public bool Contains(float wx, float wz)
        {
            float dx = wx - CenterX;
            float dz = wz - CenterZ;
            float r = dx * RightX + dz * RightZ;
            if (r < -HalfWidthM || r > HalfWidthM) return false;
            float f = dx * ForwardX + dz * ForwardZ;
            return f >= -HalfDepthM && f <= HalfDepthM;
        }
    }

    public enum SnowBladeProfileKind
    {
        /// <summary>직선. 양 끝이 열려 있어 넘친 눈이 좌우로 똑같이 빠진다.</summary>
        Straight = 0,

        /// <summary>양 끝을 앞으로 꺾은 날개. 눈이 끝단을 돌아 나가려면 꺾인 날개를 넘어야 한다.</summary>
        Winged = 1,

        /// <summary>왼쪽만 날개. 왼쪽이 막히므로 <b>오른쪽으로만</b> 뱉는다.</summary>
        LeftWing = 2,

        /// <summary>오른쪽만 날개. <b>왼쪽으로만</b> 뱉는다.</summary>
        RightWing = 3
    }

    /// <summary>
    /// 블레이드의 평면 형상. 가운데 직선 구간에 좌우 <b>날개</b>가 앞으로 꺾여 붙는다.
    ///
    /// 날개가 하는 일은 하나다 — <b>넘친 눈이 끝단을 돌아 나가는 길을 막는 것.</b> 직선 블레이드는
    /// 양 끝이 열려 있어서, 각도를 줘도 더미가 자라면 양쪽으로 똑같이 샌다(실측: 직진 30°에서
    /// 좌측 1.5%, 선회하면 그보다 크다). 한쪽에만 날개를 달면 그쪽이 막히므로 반대쪽으로만 뱉는다.
    ///
    /// <b>퇴적 가중을 손으로 한쪽에 몰아주는 방식은 쓰지 않는다.</b> 그것도 되기는 하지만 형상과
    /// 무관한 보정값이 하나 생기고, 이 설계는 거동을 전부 형상과 규칙에서 파생시키기로 했다.
    /// 날개는 컷 상자이자 배리어이므로 <b>그려진 실루엣이 그대로 충돌면</b>이라는 성질도 유지된다.
    /// </summary>
    [System.Serializable]
    public struct SnowBladeShape
    {
        /// <summary>가운데 직선 구간의 반폭.</summary>
        public float HalfWidthM;

        /// <summary>진행 방향 두께. 스윕 세그먼트 수가 이 값에서 나온다.</summary>
        public float HalfDepthM;

        public SnowBladeProfileKind Profile;

        /// <summary>날개 하나의 길이. 0 이면 프로파일과 무관하게 직선이다.</summary>
        public float WingLengthM;

        /// <summary>날개가 블레이드 선에서 앞으로 꺾인 각도. 0 이면 그냥 폭이 늘어난 직선이다.</summary>
        public float WingAngleDeg;

        public static SnowBladeShape Default => new SnowBladeShape
        {
            HalfWidthM = 1.15f,
            HalfDepthM = 0.175f,
            Profile = SnowBladeProfileKind.Straight,
            WingLengthM = 0.45f,
            WingAngleDeg = 35f
        };

        public bool HasLeftWing
            => WingLengthM > 1e-3f
               && (Profile == SnowBladeProfileKind.Winged || Profile == SnowBladeProfileKind.LeftWing);

        public bool HasRightWing
            => WingLengthM > 1e-3f
               && (Profile == SnowBladeProfileKind.Winged || Profile == SnowBladeProfileKind.RightWing);

        public int SegmentCount => 1 + (HasLeftWing ? 1 : 0) + (HasRightWing ? 1 : 0);

        /// <summary>블레이드가 닿을 수 있는 최대 반경. 스윕 AABB 여유를 잡는 데 쓴다.</summary>
        public float ReachM
        {
            get
            {
                float r = (float)System.Math.Sqrt(HalfWidthM * HalfWidthM + HalfDepthM * HalfDepthM);
                if (SegmentCount > 1)
                {
                    float tip = HalfWidthM + WingLengthM;
                    float d = (float)System.Math.Sqrt(tip * tip + HalfDepthM * HalfDepthM);
                    if (d > r) r = d;
                }
                return r;
            }
        }

        /// <summary>
        /// 세그먼트 하나를 월드 좌표로. 0 은 항상 가운데, 그다음이 왼쪽 날개(있으면), 오른쪽 날개.
        /// 순서가 고정이어야 배리어 판정이 결정론적이다.
        /// </summary>
        public SnowObb Segment(int index, in SnowBladePose pose)
        {
            float rx = pose.RightX, rz = pose.RightZ;
            float fx = pose.ForwardX, fz = pose.ForwardZ;

            if (index == 0)
            {
                return new SnowObb
                {
                    CenterX = pose.CenterX, CenterZ = pose.CenterZ,
                    RightX = rx, RightZ = rz, ForwardX = fx, ForwardZ = fz,
                    HalfWidthM = HalfWidthM, HalfDepthM = HalfDepthM
                };
            }

            bool left = HasLeftWing && index == 1;
            float sign = left ? -1f : 1f;

            // 날개는 블레이드 선에서 앞으로 꺾인다. 뿌리는 가운데 구간의 끝.
            float a = WingAngleDeg * 0.017453292f;
            float ca = (float)System.Math.Cos(a);
            float sa = (float)System.Math.Sin(a);

            float dirX = sign * ca * rx + sa * fx;      // 날개가 뻗는 방향
            float dirZ = sign * ca * rz + sa * fz;

            float rootX = pose.CenterX + sign * HalfWidthM * rx;
            float rootZ = pose.CenterZ + sign * HalfWidthM * rz;
            float half = WingLengthM * 0.5f;

            return new SnowObb
            {
                CenterX = rootX + dirX * half,
                CenterZ = rootZ + dirZ * half,
                RightX = dirX, RightZ = dirZ,
                ForwardX = -dirZ, ForwardZ = dirX,      // 날개 자신의 법선
                HalfWidthM = half,
                HalfDepthM = HalfDepthM
            };
        }

        public bool Contains(in SnowBladePose pose, float wx, float wz)
        {
            int n = SegmentCount;
            for (int i = 0; i < n; i++)
                if (Segment(i, pose).Contains(wx, wz)) return true;
            return false;
        }
    }
}

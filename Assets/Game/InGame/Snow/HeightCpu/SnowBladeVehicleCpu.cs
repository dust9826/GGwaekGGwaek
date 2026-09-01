namespace PPack
{
    /// <summary>한 프레임의 운전 의도.</summary>
    public struct SnowVehicleInput
    {
        public float Throttle;    // -1 후진 / +1 전진
        public float Steer;       // -1 좌 / +1 우
        public bool BladeDown;
    }

    /// <summary>
    /// 아케이드 컨트롤러 한 페이지. v7 <c>SnowPileCarV7</c> 의 드리프트 · 부스트 · 차체 피치롤 ·
    /// 적재질량 커플링 · 캐스트 요는 <b>전부 뺐다</b>. 이 스파이크의 질문은 조작감이 아니라 시뮬이다.
    ///
    /// <c>dt</c> 를 인자로 받고 <c>Time</c> 을 읽지 않는다. 필드는 눈 깊이를 <b>읽기만</b> 한다.
    /// </summary>
    public sealed class SnowBladeVehicleCpu
    {
        public const float DefaultAccelMps2 = 6f;
        public const float DefaultTopSpeedMps = 10f;
        public const float ReverseAccelMps2 = 4f;
        public const float ReverseTopSpeedMps = 4f;
        public const float CoastDecelMps2 = 3f;
        public const float SteerRateDegPerSec = 120f;
        public const float SteerMinSpeedMps = 0.3f;
        public const float DefaultBladeOffsetM = 1.6f;

        /// <summary>블레이드 앞 이 거리의 눈 깊이를 읽어 최고속을 깎는다.</summary>
        public const float SnowProbeAheadM = 0.5f;

        /// <summary>눈 깊이 1 m 당 최고속 배수 = 1 / (1 + 이 값 x 깊이). 물성에서 받는다.</summary>
        public const float DefaultDragPerMetre = 3f;

        /// <summary>차 중심에서 블레이드 선까지의 거리. 블레이드를 넓히면 보통 같이 키운다.</summary>
        public float BladeOffsetM { get; set; } = DefaultBladeOffsetM;

        /// <summary>
        /// 차체 전방 대비 블레이드의 요각. 음수가 좌향, 양수가 우향이다.
        ///
        /// 이 값 하나가 세 곳에 동시에 걸린다 — 컷 상자의 방향, 퇴적 밴드의 방향, relax 배리어의
        /// 방향. 셋이 같이 돌기 때문에 <b>비스듬한 블레이드는 눈을 진행 방향이 아니라 자기 법선
        /// 방향으로 민다</b>. 그 결과 더미가 한쪽 끝으로 밀려가고 그 끝에서 넘쳐 나가면서
        /// 한쪽에만 둔덕이 생긴다 — 실제 제설 블레이드가 윈드로를 만드는 방식 그대로다.
        ///
        /// 차체가 받는 횡력은 아직 모델링하지 않았다. v7 은 <c>CastYawDegPerM3s</c> 로 그걸 넣었고,
        /// "왼쪽으로 꺾으면 오른쪽으로 밀린다"는 학습 가능한 단서가 됐다. 이 스파이크의 질문은
        /// 조작감이 아니라 시뮬이므로 남겨둔다.
        /// </summary>
        public float BladeAngleDeg { get; set; }

        /// <summary>
        /// 엔진 출력. 가속도와 최고속에 <b>같이</b> 곱해진다.
        ///
        /// 계측을 위해 있는 노브다. 더미가 얼마나 자라는지 · 둔덕이 어디로 가는지 · 스텝이 몇 ms
        /// 걸리는지가 전부 속도에 딸려 있는데, 속도를 손으로 유지하는 것은 재현이 안 된다.
        /// 출력을 낮추면 같은 코스를 천천히 훑으며 형상을 볼 수 있고, 높이면 스윕 세그먼트가
        /// 상한 8 에 걸리는 지점을 찾을 수 있다.
        /// </summary>
        public float EnginePower01 { get; set; } = 1f;

        /// <summary>눈의 저항. <see cref="SnowMaterialCpu.DragPerMetre"/> 에서 온다.</summary>
        public float DragPerMetre { get; set; } = DefaultDragPerMetre;

        public float TopSpeedMps => DefaultTopSpeedMps * EnginePower01;
        public float AccelMps2 => DefaultAccelMps2 * EnginePower01;

        public float PosX { get; private set; }
        public float PosZ { get; private set; }
        public float HeadingDeg { get; private set; }
        public float SpeedMps { get; private set; }
        public float SnowDepthAheadM { get; private set; }
        public bool BladeDown { get; private set; } = true;

        public SnowBladePose BladePose { get; private set; }
        public SnowBladePose PrevBladePose { get; private set; }

        public SnowBladeVehicleCpu(float x, float z, float headingDeg)
        {
            PosX = x;
            PosZ = z;
            HeadingDeg = headingDeg;
            BladePose = ComputeBladePose();
            PrevBladePose = BladePose;
        }

        private void Forward(out float fx, out float fz)
        {
            float rad = HeadingDeg * 0.017453292f;
            fx = (float)System.Math.Sin(rad);
            fz = (float)System.Math.Cos(rad);
        }

        private SnowBladePose ComputeBladePose()
        {
            // 위치는 차체 전방으로 오프셋, 방향은 거기서 요각만큼 더 돌린 것.
            // 둘을 분리해야 비스듬한 블레이드가 제자리에 붙어 있으면서 다른 방향으로 민다.
            Forward(out float fx, out float fz);
            float rad = (HeadingDeg + BladeAngleDeg) * 0.017453292f;
            return new SnowBladePose
            {
                CenterX = PosX + fx * BladeOffsetM,
                CenterZ = PosZ + fz * BladeOffsetM,
                ForwardX = (float)System.Math.Sin(rad),
                ForwardZ = (float)System.Math.Cos(rad)
            };
        }

        public void Integrate(in SnowVehicleInput input, float dt, SnowHeightFieldCpu field)
        {
            PrevBladePose = BladePose;
            BladeDown = input.BladeDown;

            // ---- 눈이 깊을수록 느리다. 치운 차선이 곧 도로다 --------------------------
            SnowDepthAheadM = ProbeDepthM(field);
            float depthFactor = 1f / (1f + DragPerMetre * SnowDepthAheadM);
            float top = TopSpeedMps * depthFactor;
            float rev = -ReverseTopSpeedMps * EnginePower01 * depthFactor;

            // ---- 세로 ------------------------------------------------------------------
            if (input.Throttle > 0.01f) SpeedMps += AccelMps2 * input.Throttle * dt;
            else if (input.Throttle < -0.01f) SpeedMps += ReverseAccelMps2 * EnginePower01 * input.Throttle * dt;
            else
            {
                float drop = CoastDecelMps2 * dt;
                if (SpeedMps > drop) SpeedMps -= drop;
                else if (SpeedMps < -drop) SpeedMps += drop;
                else SpeedMps = 0f;
            }
            if (SpeedMps > top) SpeedMps = top;
            if (SpeedMps < rev) SpeedMps = rev;

            // ---- 조향. 서 있으면 안 돈다 -----------------------------------------------
            float abs = SpeedMps < 0f ? -SpeedMps : SpeedMps;
            if (abs > SteerMinSpeedMps)
            {
                // 속도가 붙을수록 회전율이 준다. 제자리 팽이와 고속 급선회를 동시에 막는다.
                float fade = 1f - 0.5f * (abs / System.Math.Max(TopSpeedMps, 0.1f));
                if (fade < 0.35f) fade = 0.35f;
                float dir = SpeedMps < 0f ? -1f : 1f;
                HeadingDeg += input.Steer * SteerRateDegPerSec * fade * dir * dt;
            }

            Forward(out float fx, out float fz);
            PosX += fx * SpeedMps * dt;
            PosZ += fz * SpeedMps * dt;

            ClampToField(field);
            BladePose = ComputeBladePose();
        }

        private float ProbeDepthM(SnowHeightFieldCpu field)
        {
            // 블레이드 법선이 아니라 진행 방향으로 재야 한다 - 비스듬한 블레이드로 직진할 때
            // "앞에 눈이 얼마나 깊은가"는 차가 가는 쪽의 이야기다.
            var p = BladePose;
            Forward(out float fx, out float fz);
            float wx = p.CenterX + fx * SnowProbeAheadM;
            float wz = p.CenterZ + fz * SnowProbeAheadM;
            if (!field.Geo.TryWorldToCell(wx, wz, out int cx, out int cz)) return 0f;
            return field.Get(cx, cz) * 1e-3f;
        }

        /// <summary>블레이드까지 필드 안에 남긴다. 밖으로 나가면 컷도 퇴적도 조용히 사라진다.</summary>
        private void ClampToField(SnowHeightFieldCpu field)
        {
            var geo = field.Geo;
            float margin = BladeOffsetM + 2f;
            float minX = geo.OriginXM + margin;
            float minZ = geo.OriginZM + margin;
            float maxX = geo.OriginXM + geo.ResX * SnowFieldGeometry.CellSizeM - margin;
            float maxZ = geo.OriginZM + geo.ResZ * SnowFieldGeometry.CellSizeM - margin;

            bool hit = false;
            if (PosX < minX) { PosX = minX; hit = true; }
            else if (PosX > maxX) { PosX = maxX; hit = true; }
            if (PosZ < minZ) { PosZ = minZ; hit = true; }
            else if (PosZ > maxZ) { PosZ = maxZ; hit = true; }
            if (hit) SpeedMps *= 0.4f;
        }
    }
}

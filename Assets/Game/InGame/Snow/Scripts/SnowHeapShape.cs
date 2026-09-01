using System;

namespace PPack
{
    /// <summary>
    /// 블레이드 앞에 밀려 쌓인 <b>더미의 모양</b>. 메시가 아니라 <b>필드 높이</b>다 —
    /// 블레이드를 가로질러 누운 평평한 정상의 능선이고, 양 끝이 우진각으로 깎여 있다.
    ///
    /// <b>순수 기하다.</b> <c>UnityEngine</c> 을 참조하지 않고, 필드도 원장도 모른다.
    /// 그래서 데디 서버(<c>-batchmode -nographics</c>)에서 그대로 돈다 —
    /// AnyTest v7 은 이 프로파일을 컴퓨트 셰이더 함수로 갖고 있었고, 그것이 이식이 아니라
    /// 재구현이어야 했던 이유다(루트 <c>AGENTS.md</c>: RenderTexture 는 권위가 될 수 없다).
    ///
    /// 네 면의 각도가 서로 다르고, 그 차이가 "밀리고 있다"를 만든다:
    /// <list type="bullet">
    /// <item><b>앞면</b>은 안식각보다 <b>급하다</b> — 블레이드가 능동적으로 압축하고 있으니
    ///   가라앉은 더미보다 서 있다.</item>
    /// <item><b>뒷면</b>은 앞면보다 더 급하다 — 블레이드 판이 물리적으로 받치고 있다.</item>
    /// <item><b>측면</b>은 정확히 안식각이다 — 아무것도 받치지 않으므로 평형이고,
    ///   그래서 <see cref="SnowRepose"/> 가 여기서 옮길 초과 기울기를 찾지 못한다.</item>
    /// <item><b>정상</b>은 평평하고 블레이드 선보다 조금 <b>앞</b>에 있다.</item>
    /// </list>
    ///
    /// <b>정상의 길이는 상수이고 블레이드 폭과 같다.</b> 부피가 늘어도 넓어지지 않는다 —
    /// 늘어난 부피는 전부 <b>높이</b>로 가고, 용량을 넘으면 넘친 만큼이 소반 폭 <b>바깥으로</b>
    /// 벽이 되어 흐른다(<see cref="SnowPlowBlade"/> 의 release). v7 은 여기에
    /// <c>widthPerHeight</c> 노브를 두어 정상이 블레이드보다 넓어질 수 있었지만, 이 프로젝트에서는
    /// <b>노브 자체를 두지 않는다</b> — 인스펙터에서 끌 수 있는 제약은 제약이 아니고,
    /// 폭이 자라면 소반 폭이 조용히 넓어져 "블레이드 폭만큼 치운다"가 거짓이 된다.
    /// </summary>
    public readonly struct SnowHeapShape
    {
        private readonly float _peakM;
        private readonly float _halfCrestM;
        private readonly float _tanFront;
        private readonly float _tanBack;
        private readonly float _tanRepose;

        /// <summary>정상 높이(m).</summary>
        public float PeakM => _peakM;

        /// <summary>정상의 반길이(m) — <b>블레이드 반폭과 같고 부피와 무관하다.</b></summary>
        public float HalfCrestM => _halfCrestM;

        /// <summary>정상선 기준 앞쪽 지지 반경(m). 앞면이 0 에 닿는 거리다.</summary>
        public float AheadM => _tanFront <= 0f ? 0f : _peakM / _tanFront;

        /// <summary>정상선 기준 뒤쪽 지지 반경(m).</summary>
        public float BehindM => _tanBack <= 0f ? 0f : _peakM / _tanBack;

        /// <summary>좌우 지지 반경(m) — 정상 반길이 + 안식각 우진각의 폭.</summary>
        public float AcrossHalfM => _halfCrestM + (_tanRepose <= 0f ? 0f : _peakM / _tanRepose);

        public SnowHeapShape(float peakM, float halfCrestM,
                             float tanFront, float tanBack, float tanRepose)
        {
            _peakM = peakM > 0f ? peakM : 0f;
            _halfCrestM = halfCrestM > 0f ? halfCrestM : 0f;
            _tanFront = tanFront > 1e-3f ? tanFront : 1e-3f;
            _tanBack = tanBack > 1e-3f ? tanBack : 1e-3f;
            _tanRepose = tanRepose > 1e-3f ? tanRepose : 1e-3f;
        }

        /// <summary>
        /// 힙 로컬 좌표에서의 높이(m). <paramref name="alongM"/> 은 정상선 기준 <b>앞이 양수</b>,
        /// <paramref name="acrossM"/> 은 블레이드 방향 좌우다. 지지 밖에서는 정확히 0 이다 —
        /// 그래야 발끝이 주변 눈과 연속으로 만나고 relax 가 거기서 초과 기울기를 못 찾는다.
        /// </summary>
        public float HeightM(float alongM, float acrossM)
        {
            float drop = alongM >= 0f ? alongM * _tanFront : -alongM * _tanBack;

            float over = MathF.Abs(acrossM) - _halfCrestM;
            if (over > 0f) drop += over * _tanRepose;

            float h = _peakM - drop;
            return h > 0f ? h : 0f;
        }

        /// <summary>
        /// 이 모양의 부피(m³). 닫힌 형태다:
        /// <code>
        ///     c    = (1/tanFront + 1/tanBack) / 2
        ///     V(H) = c·H² · (2·Lc + 2H / (3·tanRepose))
        /// </code>
        /// 앞뒤 두 기울기의 삼각형 단면이 <c>c·H²</c> 이고, 그것을 정상 길이 <c>2Lc</c> 에 걸쳐
        /// 밀어낸 것이 첫 항, 양쪽 우진각을 안식각으로 적분한 것이 둘째 항이다.
        ///
        /// v7 의 식과 같지만 <c>Lc</c> 가 <c>H</c> 의 함수가 아니다 — 그것이 폭 고정의 전부다.
        /// </summary>
        public static float VolumeM3(float peakM, float halfCrestM,
                                     float tanFront, float tanBack, float tanRepose)
        {
            if (peakM <= 0f) return 0f;

            float c = 0.5f * (1f / tanFront + 1f / tanBack);
            return c * peakM * peakM * (2f * halfCrestM + 2f * peakM / (3f * tanRepose));
        }

        /// <summary>
        /// <paramref name="volumeM3"/> 를 담는 정상 높이(m). 위 삼차식을 뉴턴으로 뒤집는다 —
        /// <c>V</c> 가 <c>H</c> 에 대해 단조 증가라 해가 하나뿐이고 12 회면 배정도까지 붙는다.
        ///
        /// <paramref name="maxPeakM"/> 에서 잘린다. 잘렸다는 사실은 여기서 알려주지 않는다 —
        /// 부르는 쪽이 <see cref="VolumeM3"/> 로 용량을 따로 재고, 넘친 만큼을 <b>측면 벽</b>으로
        /// 흘린다. 높이를 조용히 잘라 버리면 그 부피가 사라지고, 그것이 곧 누출이다.
        /// </summary>
        public static float PeakForVolumeM(float volumeM3, float maxPeakM, float halfCrestM,
                                          float tanFront, float tanBack, float tanRepose)
        {
            if (volumeM3 <= 0f) return 0f;

            float c = 0.5f * (1f / tanFront + 1f / tanBack);
            float a = 2f * c * halfCrestM;              // H² 계수
            float b = 2f * c / (3f * tanRepose);        // H³ 계수

            // 첫 추정은 이차항만 본 값이다. 낮은 더미에서는 그것이 지배항이라 거의 정답이고,
            // 높은 더미에서는 과대추정이라 뉴턴이 아래로 수렴한다.
            float h = a > 1e-6f
                ? MathF.Sqrt(volumeM3 / a)
                : MathF.Pow(volumeM3 / b, 1f / 3f);

            for (int i = 0; i < 12; i++)
            {
                float f = a * h * h + b * h * h * h - volumeM3;
                float d = 2f * a * h + 3f * b * h * h;
                if (d <= 1e-9f) break;

                float next = h - f / d;
                if (next < 0f) next = 0f;
                if (MathF.Abs(next - h) < 1e-6f) { h = next; break; }
                h = next;
            }

            return h > maxPeakM ? maxPeakM : h;
        }
    }
}

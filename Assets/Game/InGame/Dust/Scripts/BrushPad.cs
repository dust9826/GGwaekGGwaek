using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 표면에 붙은 사각 청소 패드의 붓질 한 번.
    ///
    /// 마스크를 지우는 패스와 지운 양을 기록하는 패스가 <b>같은 값</b>을 받아야 둘이 갈라지지
    /// 않으므로 하나로 묶는다. 도구(<c>InGame/Vacuum</c>)가 생기면 자기 트랜스폼으로 이걸 채워
    /// 넘긴다 — 그때까지는 <see cref="DustMousePainter"/> 가 마우스로 흉내낸다.
    /// </summary>
    public readonly struct BrushPad
    {
        /// <summary>월드 → 패드 로컬. 패드 평면은 로컬 XZ, 두께는 로컬 Y.</summary>
        public readonly Matrix4x4 WorldToPad;

        /// <summary>패드 로컬 XZ 의 반쪽 크기. x = 좌우 폭, y = 진행 방향 길이.</summary>
        public readonly Vector2 HalfExtents;

        /// <summary>패드가 닿는 두께. 이게 없으면 사각 기둥이 무한히 뻗어 아래층까지 지운다.</summary>
        public readonly float Thickness;

        /// <summary>경계가 풀어지는 폭. 월드 단위.</summary>
        public readonly float Feather;

        /// <summary>한 번에 지울 양. 0.002 아래는 8비트 마스크에서 무효다.</summary>
        public readonly float Strength;

        /// <summary>붓 자국의 울퉁불퉁함. 0 이면 완벽한 직사각형이 되어 스탬프처럼 보인다.</summary>
        public readonly float Unevenness;

        /// <summary>울퉁불퉁함의 결 크기. 월드 좌표 기준이라 붓을 따라 움직이지 않는다.</summary>
        public readonly float UnevennessScale;

        public BrushPad(Vector3 position, Quaternion rotation, Vector2 halfExtents,
                        float thickness, float feather, float strength,
                        float unevenness, float unevennessScale)
        {
            WorldToPad = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
            HalfExtents = halfExtents;
            Thickness = thickness;
            Feather = feather;
            Strength = strength;
            Unevenness = unevenness;
            UnevennessScale = unevennessScale;
        }
    }
}

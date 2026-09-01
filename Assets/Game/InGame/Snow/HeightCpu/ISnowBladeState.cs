namespace PPack
{
    /// <summary>
    /// 블레이드 자세를 <b>소유한 쪽</b>이 구현한다.
    ///
    /// <para><b>V7Spike 에서 여기로 옮겼다(2026-08-20).</b> 현세대(<see cref="SnowCpuStage"/>)와
    /// 멀티 차량이 이것을 쓰는데 파일이 스파이크 폴더 안에 있어서, <b>스파이크를 지우려면 현세대가
    /// 깨지는</b> 상태였다. 네임스페이스도 <c>SnowSpike.PileV7</c> 에서 <c>PPack</c> 으로 바꿨다 -
    /// 이 프로젝트는 평평한 이름공간 하나를 쓴다(루트 <c>AGENTS.md</c>).</para> 시각물(<see cref="SnowV7BladeVisual"/>)은 이 인터페이스만
    /// 보고 그린다.
    ///
    /// <para>인터페이스를 만든 이유는 <b>소유자가 둘이 됐기</b> 때문이다 — 싱글에서는 눈 리그
    /// (<see cref="SnowV7MapRig"/>)가 키 입력으로 정하고, 멀티에서는 네트워크 차량의 <c>[Networked]</c>
    /// 값이 진실이다. 시각물이 리그만 알고 있으면 <b>남의 화면에서 내 날이 늘 올라가 있다.</b></para>
    /// </summary>
    public interface ISnowBladeState
    {
        /// <summary>블레이드가 내려가 있는가.</summary>
        bool BladeDown { get; }

        /// <summary>배출 방향. -1 좌 · 0 정면 · +1 우.</summary>
        int AngleState { get; }
    }
}

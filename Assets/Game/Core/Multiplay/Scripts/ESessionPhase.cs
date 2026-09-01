namespace PPack
{
    /// <summary>
    /// 세션이 지나가는 단계. <b>UI 가 읽는 유일한 상태</b>이고, 화면 전환은 이것만 보고 한다.
    ///
    /// 로비와 게임플레이는 서로를 참조하지 않으므로(어셈블리가 그것을 컴파일 에러로 만든다) 경계를 넘는
    /// 것은 이 열거형과 <see cref="SessionLauncher"/> 뿐이다.
    /// </summary>
    public enum ESessionPhase
    {
        /// <summary>런너가 없다. 메인 메뉴.</summary>
        Offline,

        /// <summary>세션에 붙었고 사람을 기다린다. 방 코드가 유효하다.</summary>
        Lobby,

        /// <summary>세션을 만들거나 찾는 중. 방 코드로 조회한다.</summary>
        Matchmaking,

        /// <summary>씬 권위가 게임플레이 씬을 올리는 중. 모든 피어가 같이 기다린다.</summary>
        Loading,

        /// <summary>게임플레이 씬이 떴고 아바타가 돈다.</summary>
        Playing,
    }
}

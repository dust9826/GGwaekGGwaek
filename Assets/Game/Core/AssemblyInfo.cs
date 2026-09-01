using System.Runtime.CompilerServices;

// StageSession 의 순수 판정(Resolve · SceneOwnsSession)을 EditMode 테스트에 연다.
// public 으로 올리지 않는 이유는 그 둘이 호출부용 API 가 아니기 때문이다 — 호출부는 For 만 쓴다.
[assembly: InternalsVisibleTo("PPack.Multiplay.EditModeTests")]

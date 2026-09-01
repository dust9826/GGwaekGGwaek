using System.Runtime.CompilerServices;

// WorldMessageQueue 의 순서·시간 판정을 EditMode 테스트에 연다. public 으로 올리지 않는 이유는
// 그것이 호출부용 API 가 아니기 때문이다 — 호출부는 WorldMessagePresenter.Post 만 쓴다.
[assembly: InternalsVisibleTo("PPack.WorldMessage.EditModeTests")]

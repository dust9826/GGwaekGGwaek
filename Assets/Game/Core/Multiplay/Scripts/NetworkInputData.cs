using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 클라이언트가 매 틱 서버로 보내는 입력. <b>원인만 보낸다</b> — 위치도 효과도 보내지 않는다.
    /// 결과는 서버가 계산하고 각 클라이언트가 자기 화면에 다시 그린다(루트 AGENTS.md 규약).
    /// </summary>
    public struct NetworkInputData : INetworkInput
    {
        /// <summary>이동 축. x 는 좌우, y 는 전후. 각 성분은 -1..1.</summary>
        public Vector2 Move;

        /// <summary>
        /// 이동 기준이 되는 카메라의 요(도). <b>월드 방향 벡터를 보내지 않는 이유가 여기 있다</b> —
        /// 방향을 보내면 <c>PenguinLocomotion.CameraRelativeDirection</c> 의 크기 클램프를 서버가
        /// 강제할 수 없고(클라가 길이 3 짜리 벡터를 보내면 그대로 믿는다), 전후 성분과 좌우 성분을
        /// 서버가 다시 분해할 수도 없다. 각도는 그 조작 여지가 구조적으로 없다.
        ///
        /// <para>데디 서버에는 원격 플레이어의 카메라가 없으므로 이 값 없이는 이동 방향을 만들 수 없다.</para>
        /// </summary>
        public float CameraYawDeg;

        /// <summary>버튼 비트. <see cref="EInputButton"/> 로 색인한다.</summary>
        public NetworkButtons Buttons;
    }

    /// <summary>
    /// <see cref="NetworkInputData.Buttons"/> 의 비트 위치. <b>순서를 바꾸면 안 된다</b> — 비트가 곧
    /// 와이어 포맷이라 피어 사이에서 의미가 어긋난다.
    /// </summary>
    public enum EInputButton
    {
        /// <summary>
        /// 달리기 홀드(좌시프트). 2026-08-23 조작 개편에서 Shift 는 달리기이고, 슬라이딩 진입은
        /// Shift+Space 다. <b>비트 값 0 은 처음부터 바뀐 적이 없다.</b>
        /// </summary>
        Sprint = 0,

        /// <summary>좌클릭 홀드. 누르는 동안만 눈덩이에 붙어 힘을 준다.</summary>
        Action = 1,

        // 2~5 는 구멍이다. 제설차의 BladeToggle · AngleLeft · AngleStraight · AngleRight 가
        // 있던 자리인데 2026-08-24 에 차량을 버리면서 지웠다. <b>값을 재사용하지 않는다</b> —
        // 비트가 곧 와이어 포맷이라, 다시 쓰면 옛 빌드와 의미가 어긋난다. 새 버튼은 뒤에 붙인다.

        /// <summary>
        /// 매치 시작 요청. <b>RPC 를 쓰지 않는 이유가 여기 있다</b> — Fusion 위버가 사용자 어셈블리에
        /// <c>Fusion.Runtime</c> 의 internal <c>CheckInvokeRpc</c> 호출을 심는데 그 어셈블리의
        /// <c>InternalsVisibleTo</c> 에 우리가 없어서 런타임에 MethodAccessException 이 난다(실측).
        /// 입력은 이미 매 틱 서버로 가고 서버가 <c>TryGetInputForPlayer</c> 로 읽을 수 있으므로,
        /// 요청도 원인 채널에 실어 보낸다.
        /// </summary>
        RequestStartMatch = 6,

        /// <summary>들고 있는 눈덩이를 터뜨린다. 누른 순간만 의미가 있다.</summary>
        Burst = 7,

        /// <summary>새 눈덩이를 만든다. E의 누른 순간만 의미가 있다.</summary>
        CreateSnowball = 8,

        /// <summary>클라이언트가 우클릭 타이밍을 성공으로 판정했다.</summary>
        CoopShoveSuccess = 9,

        /// <summary>클라이언트가 우클릭 타이밍을 실패로 판정했다.</summary>
        CoopShoveFailure = 10,

        /// <summary>
        /// 점프. <b>눌림 상태만 보낸다</b> — 싱글의 <c>JumpPressedThisFrame</c> 은 읽는 순간 비워지는
        /// 래치라 그대로 보내면 재시뮬레이션에서 두 번 먹거나 씹힌다. 에지 판정은 받는 쪽이
        /// <c>[Networked] PreviousButtons</c> 와 비교해서 한다(폴더 AGENTS.md 규약).
        /// </summary>
        Jump = 11,

        /// <summary>운반 의도(F). 가까운 눈덩이·선물을 등에 메거나 내려놓고, 접근 중이면 취소한다.
        /// <b>뒤에 붙였다</b> — 비트가 곧 와이어 포맷이라 기존 값 사이에 끼워 넣지 않는다.</summary>
        Pickup = 12,

        /// <summary>
        /// 결과 화면의 "다시 하기". <see cref="RequestStartMatch"/> 와 같은 이유로 입력에 싣는다 —
        /// 재시작만 <c>[Rpc]</c> 로 남아 있었고, 그래서 <b>누르면 아무 일도 일어나지 않았다</b>:
        /// <c>MethodAccessException: Fusion.NetworkBehaviourUtils.CheckInvokeRpc ... is inaccessible
        /// from PPack.MissionNetHub.RpcRequestRestart</c> (2026-08-29 클론 로그 실측).
        /// </summary>
        RequestRestartMatch = 13,

        /// <summary>
        /// 증강 카드 0·1·2 를 골랐다. <b>뒤에 붙였다</b> — 비트가 곧 와이어 포맷이라 기존 값 사이에
        /// 끼워 넣지 않는다.
        ///
        /// <para>⚠ <b>비트가 셋이므로 카드 수가 3으로 고정된다.</b>
        /// <c>AugmentSelectionDirector._cardCount</c> 를 4로 올리면 넷째 카드에 실을 비트가 없어
        /// <b>멀티에서만 조용히 못 고르는 카드</b>가 생긴다. 늘리려면 여기에 비트를 더 붙이는
        /// 것이지 기존 셋을 재해석하는 것이 아니다.</para>
        /// </summary>
        AugmentPick0 = 14,

        /// <inheritdoc cref="AugmentPick0"/>
        AugmentPick1 = 15,

        /// <inheritdoc cref="AugmentPick0"/>
        AugmentPick2 = 16,
    }

    /// <summary>
    /// InGame의 로컬 타이밍 판정을 Core의 Fusion 입력 수집기로 넘기는 짧은 펄스. 판정 결과만 보내며
    /// 서버가 클라이언트 화면의 마커 시간을 다시 추측하지 않는다.
    /// </summary>
    public static class CoopShoveInputRelay
    {
        private const float PulseSeconds = 0.2f;
        private static float _successUntil = -1f;
        private static float _failureUntil = -1f;

        public static bool SuccessActive => Time.unscaledTime <= _successUntil;
        public static bool FailureActive => Time.unscaledTime <= _failureUntil;

        public static void Queue(bool success)
        {
            _successUntil = success ? Time.unscaledTime + PulseSeconds : -1f;
            _failureUntil = success ? -1f : Time.unscaledTime + PulseSeconds;
        }
    }

    /// <summary>
    /// 증강 카드 클릭을 Fusion 입력 수집기로 넘기는 짧은 펄스. <see cref="CoopShoveInputRelay"/> 와
    /// 같은 모양이고 같은 이유다 — 클릭은 한 프레임짜리 사건인데 입력은 틱마다 모아 보내므로,
    /// 짧게 유지해서 다음 수집에 반드시 한 번 실리게 한다.
    ///
    /// <para>펄스가 여러 틱에 걸쳐 실려도 해롭지 않다 — 서버의
    /// <c>AugmentSelectionDirector.SubmitVote</c> 는 같은 표를 다시 받아도 같은 값을 덮어쓸 뿐이다.</para>
    ///
    /// <para><b>호스트도 이 길로 간다.</b> 서버가 자기 입력도 <c>TryGetInputForPlayer</c> 로 읽으므로
    /// 표를 걷는 경로가 하나로 유지된다.</para>
    /// </summary>
    public static class AugmentPickInputRelay
    {
        private const float PulseSeconds = 0.25f;
        private static float _activeUntil = -1f;
        private static int _index = -1;

        /// <summary>지금 실어야 할 카드 인덱스. 없으면 -1.</summary>
        public static int ActiveIndex => Time.unscaledTime <= _activeUntil ? _index : -1;

        public static void Queue(int cardIndex)
        {
            _index = cardIndex;
            _activeUntil = Time.unscaledTime + PulseSeconds;
        }

        /// <summary>도메인 리로드가 꺼져 있어 지난 Play 의 값이 살아남는다.</summary>
        public static void Reset()
        {
            _index = -1;
            _activeUntil = -1f;
        }
    }
}

# StartScreen UI Audio

- Runtime file: `CasualHover.ogg`
- Original file: `pluck_002.ogg`
- Source pack: [Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds)
- License: Creative Commons Zero (CC0)
- Duration: approximately 0.165 seconds

짧고 둥근 플럭 음색이라 캐주얼 게임 메뉴에서 자주 쓰이는 가벼운 선택 피드백에 가깝고, 0.18초 DOTween 호버 안에 거의 마무리되는 변형을 선택했습니다. 재생 볼륨은 코드에서 0.28로 낮추고 버튼별로 약간의 피치 차이를 주어 같은 샘플의 반복감을 줄입니다.

StartScreen은 빠르게 버튼 사이를 이동할 때 이전 호버음을 끊지 않고 `AudioSource.PlayOneShot`으로 중첩 재생합니다. 경계 떨림에 의한 과도한 연타만 막도록 0.05초 재생 제한은 유지합니다.

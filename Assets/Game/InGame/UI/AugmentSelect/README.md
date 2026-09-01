# AugmentSelect — 증강 선택 화면

일차가 넘어갈 때 뜨는 카드 3장. 설계는 `docs/specs/2026-08-31-run-augments.md`.

## 구성

| | |
|---|---|
| `AugmentSelect.uxml` | 베일 + 머리말(DAY n / CHOOSE AN AUGMENT) + 빈 `card-row` |
| `AugmentSelect.uss` | 카드와 이득·패널티 줄 |
| `AugmentSelectPanelSettings.asset` | 1200x800 ScaleWithScreenSize, **SortingOrder 40** (StageHUD·StageOutro 위) |
| `Scripts/AugmentSelectionView.cs` | 카드를 코드로 만들고 클릭을 돌려준다 |

카드는 `.uxml` 템플릿이 아니라 **코드로 만든다.** 장수가 런타임에 정해지고(풀이 모자라면 3장보다
적다) 줄 수도 증강마다 달라서, 템플릿을 두면 빈 슬롯을 숨기는 코드가 따로 생긴다.

## 규칙

- **이득 줄과 패널티 줄은 같은 크기·같은 굵기다.** 패널티를 흐리게 처리하지 않는다 —
  "패널티가 있는 것이 정상" 이 문장을 읽기 전에 형태로 먼저 읽혀야 한다.
- 좋고 나쁨은 **부호로 추론하지 않는다.** `AugmentDefinition` 의 `Benefits` / `Penalties` 배열이
  어느 쪽인지 말해 주고, 이 화면은 색만 고른다.
- **표시 문자열은 영어다.** `StageHUD`(SCORE·ORDERS)와 `StageOutro`(ROUTE COMPLETE)가 이미 그렇다.
  스탯 이름 표기는 `AugmentSelectionView.Label(EAugmentStat)` 한 곳에 있다.
- 확률 축(`ExtraGiftChance`)만 `%p` 로 쓴다. 나머지는 배율이라 `%` 다.

## 이 화면은 판정하지 않는다

무엇을 눌렀는지만 `AugmentSelectionDirector.Confirm` 에 넘긴다. 그래서 화면 없이도 흐름을
PlayMode 로 검증할 수 있고, 실제로 `AugmentSelectionPlayModeTests` 는 뷰 없이 돈다.

## 정지는 여기 없다

`Time.timeScale = 0` 은 `AugmentSelectionDirector` 가 소유한다. 멀티에서 갚아야 할 빚이라
고칠 자리를 한 파일에 모아 뒀다.

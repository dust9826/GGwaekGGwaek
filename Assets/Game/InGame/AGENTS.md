# InGame — stage gameplay

Assembly `PPack.InGame`. References `PPack.Core` only — **never `PPack.OutGame`**. That is enforced by the assembly definition, not by convention: a stage scene has to be playable on its own, without entering through the lobby.

## Folder map

| | |
|---|---|
| `Player/` | third-person character, movement, tool-active vs idle camera |
| `Vacuum/` | 차량 원뿔 흡입이 사용하는 대상 마커와 VFX 프레젠테이션 |
| `Mop/` | push, pull, charged slam |
| `Trash/` | small / medium / large trash props, buried co-op haul prop |
| `Dust/` | all floor contamination — wide soft dust and hard-edged stains alike |
| `Insects/` | insect enemies, including the large elite that breaks apart |
| `Cleanliness/` | how clean the stage is, player-requested end, pass/fail check |
| `Map/` | the stage's physical space and room layout |
| `UI/` | in-stage HUD and result screen |
| `Delivery/` | factory requests, curved road graph, snow clearance and truck conflict resolution |
| `Interaction/` | 충격 반응 프롭·NPC·Delivery 도로 그래프를 읽는 주변 차량 상호작용 |
| `Prop/` | 재사용 가능한 인게임 픽업 프롭과 펭귄 임시 부스터 효과 |

Each folder carries its own `AGENTS.md`. Read the one for the folder you are working in.

## Rules

- A feature owns its Scripts, Prefabs, Materials, Audio and VFX. Do not add a shared `Prefabs/` or `Materials/` here.
- Anything a second feature needs moves up to `../Core/`. The co-op overload gauge is already there because both `Trash/` and `Insects/` drive it.
- Editor-only code goes in an `Editor/` subfolder with its own asmdef named `PPack.InGame.Editor`, platform-limited to Editor.
- Feel's `MMFeedbacks` has no assembly definition, so it compiles into `Assembly-CSharp` and **cannot be referenced from here**. Drive feedbacks from the inspector (an `MMF_Player` on the prefab, fired by a UnityEvent) rather than from C#.

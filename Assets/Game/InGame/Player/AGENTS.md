# InGame/Player — third-person character

Movement, and the camera switch the design calls for: while a tool is firing the player turns with the camera so the tool direction is pinned; while idle the camera orbits freely and the player keeps facing where they were.

Boundary: 도보 청소기 기능은 폐기됐다. Player는 이동과 카메라만 소유하며, 현재 청소 도구는
`../Vehicle/` 프로토타입에 있다.

- **`Santa/`는 이 폴더가 소유하는 도보 캐릭터가 아니다.** raw FBX(Tripo AI 변환) + 텍스처 한
  장뿐이고 `Prefabs/PF_Player`와는 무관하다 — "산타 장난감차" 모델로, `../Vehicle/`의
  `PF_VehicleProto`(현재 메인 플레이 주체)의 `Body` 메시로 2026-08-14에 배선됐다. 여기 그대로
  둔 이유는 원본 위치를 보존하는 것뿐 — 이 폴더에서 이 모델을 참조하는 코드는 없다.

## Folder map

| | |
|---|---|
| `Model/` | the gnome's rigged FBX (Humanoid avatar) |
| `Santa/` | 산타 장난감차 raw FBX·텍스처. 실제로는 `../Vehicle/`의 `Body` 메시로 쓰인다(위 참조) — 이 폴더는 원본 보관 위치일 뿐 |
| `Materials/`, `Texture/` | the gnome's material/texture set |
| `Prefabs/` | `PF_Player` — the playable character |
| `Scripts/` | `InputReader`, `PlayerCameraController`, `PlayerAnimationController`, `PlayerObjectLockOn`, `Controls` — the forked locomotion/camera/input stack (see decision below); `EPlayerState`, `PlayerState`, `PlayerToolSwitcher` — player state and F-key tool switching |
| `Scenes/` | `Player_TestScene` — standalone scene for iterating on movement/camera without a full stage |

## Decisions

- **Movement, animation and camera code is forked from `Assets/Synty/AnimationBaseLocomotion`'s sample scripts into `Scripts/` as `PPack`-owned code** (`InputReader`, `Controls`/`Controls.inputactions`, `PlayerAnimationController` — was `SamplePlayerAnimationController`, `PlayerCameraController` — was `SampleCameraController`, `PlayerObjectLockOn` — was `SampleObjectLockOn`), superseding the earlier "Inspector-only, never called from PPack.InGame" decision. The vendor package ships without an `.asmdef` so its scripts compile into `Assembly-CSharp`, which `PPack.InGame` cannot reference — the same constraint documented for Feel's `MMFeedbacks` in the root `AGENTS.md`. Forking removes that boundary entirely: `PlayerState`-driven behaviour (tool-active/idle camera switch, and any future state) can now call into these components directly instead of going through Inspector-wired UnityEvents. The cost is that `Assets/Synty/...` and this fork are no longer connected — future updates to the vendor package won't reach the fork and have to be re-applied by hand if ever needed. The original vendor files are untouched and still present; only `PF_Player`'s components were swapped to point at the fork. `PF_Player` (`Prefabs/PF_Player.prefab`) is a duplicate of Synty's `PF_PolygonPlayer` sample with its `AC_Polygon_Masculine` animator controller kept as-is, so the masculine locomotion state machine (walk/run/sprint/crouch/jump/turns) and the mouse-orbit third-person camera come for free.
- **The gnome model (`Model/garden_gnome_new_humanoid_rigged.fbx`) is imported as a Humanoid avatar** (was Generic on import) so Synty's masculine animation clips retarget onto it directly — bone naming (`Hips`, `LeftUpperLeg`, `Spine`, `Chest`, `Neck`, `Head`, ...) already matched Unity's Humanoid convention closely enough for auto-mapping to succeed cleanly.
- **`PF_Player`'s `CharacterController`, capsule fields on `PlayerAnimationController`, and the `SyntyPlayer_LookAt`/`FrontRayPos`/`RearRayPos` transforms were re-tuned for the gnome's ~1.0 m rounded proportions** (vs. the ~1.8 m human the Synty defaults assume). Camera distance/height/tilt on `PlayerCameraController` were scaled down to match, then `_cameraDistance` was bumped back up to `2.2` after it read as too close in testing. These are starting values, not final feel — tune further in the Inspector as needed.
- **`PlayerAnimationController._alwaysStrafe` is set to `false`** (Synty's sample default is `true`). With it `true`, the character's `transform.rotation` is Slerp'd toward the camera's forward every frame regardless of movement, so orbiting the camera with the mouse also spun the player — wrong for the idle case in the design note above ("camera orbits freely and the player keeps facing where they were"). With it `false`, the character only rotates to face its actual movement velocity, and holds its rotation when standing still, exactly matching idle behaviour. The design's tool-active behaviour (player turns to pin the tool direction to camera) still needs to be driven by `PlayerState`-reacting code — e.g. toggling `_isStrafing` via `PlayerAnimationController`'s aim/lock-on hooks, now directly callable since the fork — since `_alwaysStrafe` alone can't express "strafe only while a tool fires."
- **기존 도보 청소기 연결은 `PF_Player`에서 제거했다(2026-08-12).** `VacuumAimController`,
  `VacuumSuction`, 손의 청소기 모델, 조준 UI와 캡처 인디케이터가 더 이상 없다.
- **The cursor is always locked and hidden, unconditionally (2026-08-11).** `PlayerCameraController` used
  to gate `Cursor.visible = false; Cursor.lockState = CursorLockMode.Locked;` behind a `_hideCursor`
  inspector toggle (Synty's sample default: off, and it was never turned on here) — the OS pointer moved
  freely around the screen while `Look` still read raw mouse delta, so the crosshair and the visible
  pointer drifted apart with no way to reconcile them. The toggle was removed rather than just flipped to
  `true`, since there is no scenario in this project where the cursor should be free during gameplay and a
  never-touched inspector field is a trap for future confusion. No menu system exists yet to need the
  cursor back — when one lands, it will need to explicitly unlock/show the cursor itself (e.g. on
  pause/open), not rely on this field.
- **Player state is a single `EPlayerState` enum (`PlayerState.cs`), not a narrow "current tool" type** — it covers whatever the player is doing (`Vacuum`, `Mop` today), because a future co-op carry state will sit on the same axis (mutually exclusive with tool use) rather than as a separate bool bolted on. `PlayerState` only holds `Current` and fires `StateChanged`; it has no idea what triggers a change. `PlayerToolSwitcher` is the only current trigger — it reads a single raw `InputAction` bound to `<Keyboard>/f` (not a shared `.inputactions` asset, since one button doesn't warrant a whole asset) and toggles between `Vacuum`/`Mop`. This is a skeleton only: `Vacuum/` and `Mop/` don't yet read `PlayerState.StateChanged` or own their own per-tool input — that lands with each tool's actual input/behaviour.

# InGame/Mop — mop tool

The mop is the **dust tool**. It is a vacuum mop: a pad held against the floor and pushed along, suction-driven rather than swept. Left-click enters a cleaning mode where the camera drops to a top-down angle and W/S drive while A/D turn in place; the pad in front of the player erases dust from whatever surface it touches.

Boundary: `../Vacuum/` owns suction on things that get pulled in — trash, insects, props. This folder owns floor contamination. The pad, the erase contract and the cleaning VFX belong to `../Dust/`; this folder fills a `BrushPad` from the tool's transform and hands it over. It never touches the mask.

Full reasoning: `docs/specs/2026-08-11-mop-cleaning-mode.md`.

## Decisions

**The mop is the dust tool, not a sweeper (2026-08-11).** This folder previously described push, pull and a charged floor slam, with `../Vacuum/` owning everything suction. That split does not survive contact with the design: the thing that cleans dust is shaped like a mop and works by suction, so it is one tool, not two halves of two.

`../Vacuum/` keeps suction on *objects* — trash, insects, props that get pulled toward the player. The slam for killing insects is unclaimed again; it belongs wherever insect combat lands.

**Left-click is a toggle, not a hold (2026-08-11).** Left-click is a **mode** switch, not a suction switch. Holding would flip the camera and the controls back every time the button came up — a single pass across a floor would invert the view several times.

Hold still fits `../Vacuum/`, where suction happens in place without taking over the camera.

**The mode swaps components instead of asking `../Player/` to change (2026-08-11).** `MopMode` disables `InputReader`, `PlayerAnimationController` and `PlayerCameraController`, then enables the cleaning ones. Disabling `InputReader` also disables the `Player` action map, because its `OnDisable` does that — no separate call is needed.

The reason is merge cost, not design purity. `PlayerCameraController` clamps pitch to `(-10, 45)` and keeps every field private, so 50° is unreachable from outside; adding a public API to it would mean editing a file another author was actively tuning.

**This is a scaffold, not the final shape.** The mode changes camera, locomotion and input — all of which `../Player/` owns — so `MopMode` currently reaches into another feature's internals. The coupling exists either way; it just runs through `enabled` flags instead of an interface. When `../Player/` is settled, the mode belongs in `PlayerState` and the player components should be *told* rather than toggled.

**Cinemachine drives the cleaning camera (2026-08-11).** The first version tracked the target by hand and set the camera yaw straight from the player's yaw. Turning then span the whole world instantly and it made people dizzy. World rotation is wanted — what was missing was interpolation.

`CinemachineFollow` with `BindingMode.LockToTargetWithWorldUp` gives the orbit, and `TrackerSettings.RotationDamping` gives the delay. The hand-written camera script was deleted; it was reimplementing what Cinemachine already does.

**Damping is per-axis on purpose.** `PositionDamping` is `(0.8, 0.4, 1.2)` in target-local space: Z highest so the camera falls behind on acceleration and gives a sense of speed, Y lowest so floor bumps do not make the camera bob — a large Y brings the dizziness straight back.

**The camera aims at the pad, not the player (2026-08-11).** `CinemachineHardLookAt.LookAtOffset` is `(0, 0.6, 0.8)`. Z 0.8 matches the pad offset so the spot being cleaned sits near the centre of frame; Y 0.6 lifts the aim so the character sits around 63% down the screen and headroom is left above.

**`LookAtOffset.z` duplicates `MopPad._localOffset.z`.** Move the pad and this has to move with it. Left as two values because there is only one tool; wire one to read the other at the second one.

**The pad erases every target it overlaps, not the one the ray hit (2026-08-12).** `MopPad` used to paint only the `DustPaintTarget` on the collider its downward ray struck. With a floor tiled from panels, the moment the pad straddles a seam **half the brush is silently discarded** — the cleaned trail comes out with one side cut dead straight, and the effect is worst exactly where the player drives across a seam. It now uses `Physics.OverlapBoxNonAlloc` over the pad's footprint and paints all of them.

The "no erasing through walls" guarantee survives: the box is only as thick as the pad, so it catches floors on the same plane, and the brush shader re-clips anything outside the thickness anyway.

This was found by chasing a straight edge that appeared on **one side only**. An asymmetric artifact cannot come from a symmetric brush — that asymmetry is what pointed at target selection instead of at the brush.

**A wide feather is what makes a cleaned edge look torn (2026-08-12).** `MopPad._feather` was `0.06 m`. At `12 m/s` the same texels get stamped about eight times per pass, and eight passes erase a 6 cm feather down to nothing — the mask ends up binary, and a shader cannot soften an edge that has no gradient left. `0.3 m` survives repeated stamping because the outermost band erases by an amount that tends to zero.

Strength went `0.35 → 0.22 → 0.4` on the way. `0.22` gave a beautiful frayed edge and left the middle of the trail dirty; in a cleaning game a pass that does not clean is worse than a hard edge.

**The vehicle prototype that lived here has moved to `../Vehicle/` (2026-08-13).** `MopVehicle`,
`PF_MopVehicle`, `MopVehicleAudio`, `MopVehicleCameraFeel`, `MopDriveProbe`, `MopDriveAutopilot` and
`Mop_Driving_Test.unity` are gone from this folder. The driving model they carried — lateral velocity
decayed rather than discarded, steering and grip as functions of speed, the drift hold key, and the
split between the speed the model reads and the speed the presentation reads — now lives in
`../Vehicle/VehicleController`, and its reasoning lives in `../Vehicle/AGENTS.md`.

Keeping both was the actual cost. The same model existed twice and only one copy got fixed: the
rotation jitter found on 2026-08-13 was repaired in `../Vehicle/` and silently left behind here.
**Two copies of a tuned model diverge on the first bug.** To read the deleted files, check out
anything before `cs:138`.

What stays here is the walking cleaning mode — `MopMode`, `MopLocomotion` and the pad itself.
Whether it survives alongside the vehicle is still undecided.

## Open

- **`EPlayerState` still says `{ Vacuum, Mop }` and `PlayerToolSwitcher` flips between them on F.** Nothing here reads that yet — the cleaning mode is its own toggle on left-click. Deciding how the two relate is the next question, and it touches `../Player/`.
- **Animation freezes during cleaning.** Disabling `PlayerAnimationController` stops the `Animator` mid-pose. There is no cleaning animation yet, so nothing is lost — but this is the first thing to fix when one exists.
- **Returning from cleaning may snap the camera.** `PlayerCameraController` keeps `_lastAngleX` / `_lastPosition` internally and resumes interpolating from them. Not yet seen in practice; if it shows up, that is the concrete reason to give that file a public API.
- **`MopPad` is now used by the vehicle too.** `../Vehicle/VehicleMopPad` was a stale copy of this file and has been deleted; `PF_VehicleProto` carries this component and the minimap subscribes to `SurfacePainted` on it. Two consumers means the folder it lives in is now a fair question — it is still here because the vehicle carries a mop, not because that was decided.
- **`MopControls`' `Move` composite must stay `2DVector(mode=1)` (`Digital`).** The default, `DigitalNormalized`, returns `(-0.707, +0.707)` for W+A, so steering costs throttle. Measured, not theoretical.

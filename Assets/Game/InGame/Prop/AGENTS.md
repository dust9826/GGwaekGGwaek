# InGame / Prop

## Scope

This folder owns reusable in-game pickup props and their runtime effects. Runtime
scripts stay in the parent `PPack.InGame` assembly; editor-only asset builders live
under `Editor/` in their own editor assembly.

## Penguin booster contract

- `PenguinBoosterPickup` owns availability, presentation bob/rotation, collection,
  and respawn.
- `PenguinBoostReceiver` owns the temporary locomotion multiplier and the VFX
  instance attached to a penguin. Re-collecting refreshes the timer and never stacks
  duplicate VFX instances.
- The active effect references the third-party source prefab at
  `Assets/Plugins/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/Fire Bullet.prefab`.
  Never edit that source prefab. The builder may reserialize its legacy `Bullet.mesh`
  dependency without changing visual data so it remains loadable in Unity 6.
- `PenguinLocomotion.SetSpeedBoostMultiplier` is the only supported way for props
  to modify locomotion speed. Always restore it to `1` when an effect ends or its
  receiver is disabled.
- Current scene placement is for local `SinglePlay`. A future networked pickup must
  make collection and expiration server-authoritative rather than reusing the local
  trigger verbatim.

## Asset generation

Run `PPack/Prop/Build Booster And Install In SinglePlay` after changing the visual
recipe. The builder creates owned materials and `Prefabs/PF_PenguinBooster.prefab`,
then places one prefab instance near the player spawn in `SinglePlay` via Unity APIs.

# Penguin Booster Prop

The booster is a respawning pickup for SinglePlay. Touching it grants a 1.6x movement
boost for five seconds and attaches the existing **Fire Bullet** effect to the
penguin's `BodyPivot`. The pickup returns eight seconds after collection.

Owned assets are generated from the Unity menu:

`PPack > Prop > Build Booster And Install In SinglePlay`

Runtime tuning is serialized on `PF_PenguinBooster`; the third-party Fire Bullet
prefab is referenced, not copied or modified.


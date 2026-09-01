# Event Playground

`Event_Playground_Test.unity` is the shared manual-play scene for rapid gameplay-event prototypes.
It is intentionally excluded from Build Settings.

## Layout contract

- `SharedPreviewAssets/Player_Penguin`: the normal playable penguin prefab and its camera.
- `EventSlots/EventSlot_01_RabbitTrap`: rabbit, trap-install, and gift-result anchors for the first animal event.
- `EventSlots/EventSlot_02_Empty`: a clean bay to duplicate when the next event needs a test area.
- Cyan markers are actor spawns, orange markers are event origins or interaction points, and pink markers are rewards.
- Event-owned runtime objects and scripts still belong under `evenT/animal`, `evenT/monster`, or `evenT/weather`.

Open the scene with `PPack > Events > Open Event Playground`. The editor creates the scene once if it is missing.

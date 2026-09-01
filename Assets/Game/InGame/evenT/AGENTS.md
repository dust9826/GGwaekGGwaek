# In-Game Event Rules

## Folder ownership

- `evenT` is the project's existing canonical event folder spelling. Do not create sibling `Event`, `event`, or `Events` folders.
- Keep animal events in `animal/`, monster events in `monster/`, and weather events in `weather/`.
- Put authored runtime scripts, prefabs, and tests inside the owning event feature folder.

## Event flow

- Gameplay events use the readable lifecycle `Warning -> Active -> Recovery -> Cooldown`.
- Warning state must expose the affected world area before gameplay consequences begin.
- UI and minimap are presentation readers. They may subscribe to event state, but must not own timing or gameplay consequences.
- Remove or hide world/minimap markers after recovery so stale event locations cannot remain.
- The first prototype permits only one major threat event at a time; introduce an explicit scheduler before allowing overlaps.

## Weather authority and presentation

- Snow accumulation is authoritative gameplay data and must use positive CPU-field deltas. The active
  height-field stack routes moving weather through `SnowCpuStage.ApplyBlizzardSweep`; it must remain
  headless-safe and may not depend on a render texture.
- Particle density, wind, fog, sound, camera, and UI are presentation derived from event intensity; disabling the event must restore their prior values.
- Reuse the local player-follow snowfall and wind systems instead of spawning map-wide particles.
- Synchronize shared WinterVillage environment/event changes between ConceptMap and HillsideMap unless a scene-specific exception is documented.

## Scheduled blizzard (2026-08-26)

- `Cleanliness/Scripts/StageDateCoordinator` starts and pauses `TimeOfDayDirector` with the authoritative
  `GameManager` phase, evaluates the current day when play starts, and forwards `DayAdvanced` to
  `ScheduledBlizzardDirector.NotifyDateStarted(dayIndex)`. Map and weather do not reference Cleanliness.
  The scheduler owns only the configured day list and the once-per-runtime-day guard;
  `BlizzardEvent` owns timing and snow.
- Region snow usage is exactly `sum(initial depth at game start - current depth)`. Do not clamp each cell:
  snow moved into another cell in the same region must offset snow removed there.
- The route starts at the highest-use region, points through the second-highest separated region, and
  continues in that straight direction until its feathered footprint has left the CPU field.
- One event can contribute at most 120 mm to a cell, and resulting depth is capped at 300 mm. A per-event
  exposure buffer prevents overlapping sweep samples from spending the 120 mm budget more than once.
- `BlizzardAlertPresenter` shows the top-center warning for three unscaled seconds. A
  `VehicleRouteMinimapController`, when present, reads `BlizzardEvent` registration and affected bounds;
  scenes without that UI controller intentionally show no map marker.

## Verification

- Test the complete warning, active, recovery, and cleanup cycle with accelerated timings.
- Verify that active snowfall increases authoritative snow depth and that the minimap marker follows the declared affected bounds.
- Finish with Play Mode off, no temporary objects, no dirty unrelated scenes, and no new console errors.

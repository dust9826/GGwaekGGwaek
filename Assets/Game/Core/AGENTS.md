# Core — shared types and cross-feature systems

Assembly `PPack.Core`. **References nothing of ours.** Everything else references this, so a dependency pointing outward from here would be a cycle.

## What belongs here

- Data types and events that cross the InGame / OutGame boundary — match settings, player identity, stage result.
- Systems a second feature has *already* started needing. The **co-op overload gauge** is the current example: both `InGame/Trash/` and `InGame/Insects/` drive it, so it does not belong to either.
- Presentation-independent shared UI resources used by both assembly domains may live under `UI/`; Lilita One and its OFL license are the current example. Screen layouts and drawing logic still stay in the owning feature.

## What does not

- Anything only one feature uses. It stays in that feature until a second consumer is confirmed.
- Presentation. Core holds state and rules; drawing belongs to the feature that owns the screen.

Editor-only code goes in `Editor/` with its own asmdef named `PPack.Core.Editor`, platform-limited to Editor.

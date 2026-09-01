# Delivery house presentation

`GiftDeliveryHouseHelpDisplay` draws a camera-facing HELP bubble above the announced house and keeps it visible while
that order is active. It removes the bubble only when the order completes, fails, or the run ends. Roof tinting, the
house pin, and the Stage HUD all continue to start from the same `OrderStarted` event.

`GiftDeliveryHouseQuestSmoke` reuses the mapped `ChimneySmoke_XX` system instead of spawning a separate roof effect.
While the order is active, current and future smoke puffs use the roof's resolved quest display colour, emission rises
by 35%, and puff size rises by 10%. Completing, failing, or ending the run restores the original colour, emission, and
size. When a house has no pre-placed smoke object, the system derives the chimney top from the readable roof mesh's
highest compact vertex cluster, clones the existing winter-smoke setup there, and deletes that clone with the order.
It does not mutate the shared chimney-smoke material or place a generic emitter at the roof centre.

The bubble is built from layered URP unlit primitive meshes at runtime, so it has no raster texture dependency. A dark
rear volume, thick outline, raised paper face, dimensional rays, and a slight camera-facing yaw make it read as a small
3D world sign instead of a flat screen-space card. Its type uses the project's
`Assets/Game/Core/UI/Fonts/LilitaOne-Regular.ttf` asset, whose license is documented with the font asset.

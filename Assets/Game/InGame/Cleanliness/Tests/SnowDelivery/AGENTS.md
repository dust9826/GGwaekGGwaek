# SnowDelivery SinglePlay verification

This folder owns the tests for the production scene
`../../../Scenes/SinglePlay.unity` and the remaining test-only wrapping probe.

- `PF_SnowGiftMachine` consumes a supported `SnowBallCarrier` after its intake animation commits.
  The production SinglePlay instance is explicitly wired to `SnowCpuStage`.
- Production SinglePlay uses `PF_Penguin_MomentumHandling` and owns one `SnowballGrowthHud` under
  `SnowDeliveryRig`. Tests must verify the momentum controller, snowball binder, growth controller,
  `UIDocument`, panel settings, and UXML are present in the generated scene.
- The SnowDelivery instance accepts a snowball when either its center or collider surface reaches
  the front intake volume. Keep the surface tolerance on this scene instance; rear and side contact
  must remain rejected.
- Its physical gift follows the pop from the top chute to `GiftLandingAnchor` beside the intake, so
  the player who supplied the snowball can select it with F without circling the machine.
- The fixed stage mapping is Stage 1 blue, Stage 2 green, Stage 3 yellow, and Stage 4 red.
- The production SinglePlay scene removes the shared rig's `GiftSpawner`. Do not remove the
  production spawner script or change other delivery scenes unless their owner also adopts this flow.
- The derived scene lowers the complete map-owned `Geometry/Routes` hierarchy until every
  `Road_*` renderer top is at Y=0.01m. This keeps the visible road within 1cm of the flat ground,
  moves the curbs by the same offset, and leaves the source ConceptMap unchanged. Because there is
  no longer a road-to-ground rise, the 24 map-owned curb-ramp objects remain for traceability but
  their GameObject and colliders are disabled in this derived scene. The scene owns a separately
  baked `../../Data/SnowGroundMap_SinglePlay.asset`; never point
  it back at the ConceptMap ground map after changing route height, or the displaced snow will retain
  the old 0.315m step.
- The plaza collider replacement, four cardinal access ramps, and snow gift machine instance are
  authored by `Cleanliness/Editor/SnowDeliverySceneBuilder.cs`; do not hand-edit the scene YAML.
- The plaza ramps are 6m wide, extend 1.1m onto the inner surface, and start 4m beyond the outer
  ring. Their lower edge is buried 0.12m under the local approach surface rather than using the
  plaza mesh's lower bound; route overlays that will be opened are ignored when sampling that
  surface. The upper overlap lets the carried-snowball collision proxy cross the other seam.
- Curb and road renderers stay intact, but each collider crossing a plaza ramp is split around that
  ramp's full bounds plus 0.5m clearance. Test the actual collider's horizontal distance to samples
  along the access centerline rather than world AABB overlap, because rotated long route pieces'
  AABBs overlap unrelated ramps. Never disable an entire long route collider to open one route.
- The derived scene retains the map-owned `Geometry/Routes/VehicleCurbRamps` set, disabling only the
  small rendererless ramp colliders that overlap a plaza access corridor. A second
  `SnowDeliveryRig/CurbRamps` set must not exist because overlapping static contacts can catch the
  penguin or a rolling snowball at a road edge.
- The builder clones the complete `SnowCpuStage` GameObject from
  `Snow/Tests/Snow_BallPush_Test.unity`, then overrides only the WinterVillage bounds, initial
  depth, and baked ground map. Snow view and gathering tuning, including vertex spacing, belongs to
  the source scene and must not be duplicated in the SnowDelivery builder.
- Conversion is local-only while `Gift` is not a networked object. Keep the snow mass ledger closed
  through `SnowCpuStage.TryConsumeBallForLocalConversion`; do not destroy a tracked ball directly.

The PlayMode tests must cover all four mappings, an actual E-key snowball creation after the stage
intro enables input, real prefab-trigger surface contact for all four ball stages, one supported-ball
machine conversion including the snow ledger result, a real machine gift carried and dropped with F
from the intake side,
the single map-owned curb-ramp set, all four plaza approaches, and a real `PF_Penguin` carrying a
Stage 2 snowball up one complete plaza ramp.

- On Unity 6000.6.0b7, keep **Enter Play Mode Options disabled** so Domain Reload and Scene Reload
  both run. Repeated SnowDelivery PlayMode tests with Domain Reload disabled crash natively in
  `GPUResidentDrawer` / `ObjectDispatcher` instead of producing a managed test failure. Re-enable
  the optimization only after an editor upgrade and an explicit repeat-run verification.
  `Cleanliness/Editor/SnowDeliveryPlayModeStabilityGuard.cs` removes `DisableDomainReload` again at
  editor startup and immediately before Play Mode if another branch or a manual setting restores it.

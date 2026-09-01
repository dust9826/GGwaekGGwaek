# Winter Village Concept Map

`Scenes/WinterVillage_ConceptMap.unity` is the production map scene built from the approved winter-village concept.

## Ground and snow policy

- The terrain itself is intentionally snow-free. Snow is limited to vendor winter props such as roofs, fir trees, rocks, and the snowman.
- Ground is split into eight independent panels around the river. Every panel has unique UV0 coordinates in the 0-1 range so a later snow or contamination mask can be authored per panel without tiled-UV conflicts.
- There is no walkable terrain mesh beneath the open river. The two bridges provide the intended crossings.

## Layout

- The concept-art road topology is preserved as plain asphalt roads. The coloured guide lines and geometric route markers were intentionally removed because they were concept annotations, not world decoration.
- Road pieces stop before bends and junctions, then use project-owned rounded asphalt/shoulder transition meshes. Straight continuation nodes have no round patch, and cul-de-sac ends use a road-width cap rather than a large gameplay-marker shape.
- All eleven village houses now face their access route. Ten houses use 1.45-metre-wide warm-grey stone entry walkways authored as EasyRoads3D splines and baked into project-owned `Meshes/Paths/SM_EntryWalkway_*` assets. Each entrance is fitted to the measured centre of the actual lowest stair, doorstep, or porch opening, with a short perpendicular lead before the route begins to curve. House 5 uses a compact triangular apron that widens `NorthBank_Promenade_East` to the full stair mouth; its redundant EasyRoads dead-end spur is disabled. House 8 adds a project-owned raised landing and four stone steps from its side-porch door to the ground-level curve.
- A Christmas-tree plaza is the primary landmark. House clusters, lamps, fences, benches, parked vehicles, winter tools, and small story props form secondary landmarks.
- Riverside benches, a village well, small house patios, information posts, hydrants, and irregular bush clusters provide readable local detail without narrowing the playable roads.
- Both bridges use low wooden guard rails made from short posts and two thin horizontal rails; the former solid-looking full-height beams were removed.
- Trees and rocks are placed in irregular clusters with open navigation space around roads, junctions, bridges, and house entrances. The concept reinforcement pass adds nine small asymmetrical fir/bush/rock/grass clusters at outer curves, river approaches, and map edges rather than distributing props uniformly.

## Lighting and reactive prop prefabs

- `Prefabs/Lighting/PF_WinterLantern_Glow.prefab` wraps the vendor lamp with a project-owned HDR emissive bulb, warm point light, and `LanternFlicker`. Perlin noise supplies subtle low-frequency variation, while short randomized dips occur at wider intervals.
- `Prefabs/Houses/` contains nine project-owned lit house wrappers. Their shared emission mask lights only the warm-yellow window region of the source atlas, and each wrapper adds a low-intensity 2850 K point light plus `WarmWindowLight`.
- `Prefabs/Nature/` contains ten project-owned swaying tree wrappers used by all 98 scene trees. `TreeWindSway` rotates only an internal visual pivot for ambient wind and spring-damped contact response, leaving the root capsule collider upright. Layered waves and short shared gust pulses make the motion readable from the vehicle camera without producing discontinuous snapping.
- A solid vehicle/tree collision now drives a speed-scaled spring kick up to a larger impact-only angle. `Prefabs/VFX/PF_WinterTreeImpactFeedback.prefab` combines a canopy-local powder-and-clump snow burst with a short Feel `MMF_CameraShake`; the main vehicle camera consumes that Feel event after its follow pass so the shake is not overwritten. Per-tree and global cooldowns prevent repeated contact from spamming the response.
- `Prefabs/VFX/PF_WinterWindGusts.prefab` follows the gameplay camera independently of the snowfall volume. It uses three coordinated layers: velocity-stretched streaks, readable wind-carried flakes, and low-opacity powder wisps. A long eased envelope, subtle pulse, and small per-gust direction variation avoid the former scratch-like pop while keeping all layers and the tree sway on one shared world-space wind vector.
- `Lighting/Profiles/WinterVillage_LightingProfile.asset` provides the global URP Bloom override. The scene camera has HDR and post-processing enabled.
- The scene uses cool blue-grey exponential-squared distance fog (`RGB 0.25, 0.31, 0.40`, density `0.0155`). Nearby vehicle routes remain clear while distant houses and trees merge softly into the snowfall; the player-following `GroundSnowMist` remains a separate local layer.
- Runtime components are in `Scripts/`; shared emission materials and the generated window mask are in `Materials/` and `Textures/`.

## Gameplay handoff

- `WinterVillageMap` owns `MapMinimapBounds`; its trigger volume covers 120 x 110 metres.
- Ground panels, roads, bridges, buildings, trees, rocks, lamps, and fences provide colliders where appropriate.
- The ten dedicated house-entry centre lines and the house-5 promenade apron were checked with a 0.48-metre player radius and contain no blocking prop colliders. Their baked strips, the house-5 apron, and the house-8 door landing have upward normals and matching mesh colliders.
- The refined road centre lines and pedestrian network pass continuity checks with no surface gaps or blocking prop colliders.
- The final eleven-house entrance audit reports exact stair, doorstep, or porch contact and no fence, tree, or story-prop collider blocking any walkway.
- Invisible six-metre boundary walls prevent the player from leaving the authored area.
- `SetDressing/Wildlife` contains five instances of `Prefabs/Animals/PF_WinterRabbit_Flee.prefab`. The project-owned wrapper uses the brightened Santa-village white material, idles and looks around normally, then runs away from `PlayerVehicle` within 9 metres until it reaches a 15-metre safe distance. It uses local collision and ground probes rather than requiring a baked NavMesh.

## EasyRoads3D authoring source

- `EasyRoads3D Free v3` is imported under `Assets/EasyRoads3D` from the official Unity Asset Store package. Vendor files remain unmodified.
- The editable source network is the disabled `WinterVillageMap/Authoring/EasyRoads3D_EntryWalkways_Authoring` hierarchy. Enable it only while editing control points; its child terrain is an invisible authoring support surface backed by `Authoring/EasyRoads_AuthoringTerrain.asset`.
- Runtime rendering and collision use the baked project-owned `Surface` and `Border` meshes under `Geometry/PedestrianPaths`, so the vendor authoring network stays disabled in play mode. Short door-contact sections are endpoint-fitted after the EasyRoads bake so spline smoothing cannot pull a narrow entrance away from its stair or porch centre.

Preview captures are in `Preview/`. The approved paint-over and reusable ImageGen prompt are in `Concept/`. Vendor prefabs under `Low Poly Locations Ultimate Pack` are referenced only and were not modified.

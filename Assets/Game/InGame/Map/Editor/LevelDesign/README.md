# Level Design Placement Tool — Phase 1

This Editor-only tool keeps normal Unity Prefab instances in the active Scene. It does not replace Terrain editing, create level JSON, or generate a procedural world.

Open `PPack > Level Design > Open Level Design Hub` (or `Window > PPack > Level Design Hub`) to access Prefab placement, road drawing, Terrain templates, road maintenance, and the Road-First World generator from one window. The original dedicated windows and menu commands remain available.

## Files and responsibilities

- `Palette/PrefabPalette.cs`: ScriptableObject containing named Prefab categories.
- `Palette/PrefabPaletteWindow.cs`: Palette editing, thumbnails, Prefab selection, and tool activation.
- `Placement/PlacementTool.cs`: Scene View input, single-click placement, Prefab instantiation, and Undo.
- `Placement/PlacementPreview.cs`: transient mesh preview drawn directly in the Scene View; it creates no Scene GameObject.
- `Common/SceneRaycaster.cs`: Scene GUI ray conversion and collider raycast.

## Use

1. Open `PPack > Level Design > Open Prefab Palette` or `Window > PPack > Prefab Palette`.
2. `Palettes/WinterVillagePrefabPalette.asset` is restored automatically when the saved palette link is missing. Use `PPack > Level Design > Restore Winter Village Prefab Palette` to force the default palette back, or assign another `PrefabPalette` asset.
3. Expand `Edit Palette Contents` and register Project Prefab assets under categories.
4. Click a thumbnail. The Level Placement EditorTool activates automatically.
5. Move the pointer over a non-trigger Collider in the Scene View.
6. Confirm the cyan mesh preview and surface marker.
7. Categories using `Flatten Terrain` also show a green predicted Terrain surface below the Prefab.
8. Left-click once to apply the Terrain grade and place a connected Prefab instance.
9. Use `Cmd+Z` / `Ctrl+Z` to undo both the Terrain change and placement together.
10. Hold `Alt` or use the middle/right mouse buttons for normal Scene View navigation. Press `Esc` to leave the tool.

Phase 1 intentionally excludes random rotation/scale, normal alignment, continuous placement, and erase brushing.

## Included Winter Village palette

`Palettes/WinterVillagePrefabPalette.asset` contains the project-ready Winter Village Prefabs currently used by the maps: Houses, Nature, Lighting, Animals, and VFX. Raw source-pack terrain, roads, vehicles, and whole-map composite Prefabs are intentionally excluded.

The Houses category uses `Flatten Terrain`. Its renderer bounds define the building footprint automatically, and the surrounding 2.5 metres blend smoothly into the original Unity Terrain. Other categories keep the surface unchanged. Mesh-based ground can still receive a Prefab, but it cannot be reshaped by this Terrain-only feature.

## Road building

Open `PPack > Level Design > Open Road Builder`.

- **EasyRoads Template Road** reuses an `EasyRoadTemplate` asset as a prefab-like road recipe. The default `ERT_WinterVillagePackedSnow` template matches the current Winter Village packed-snow material, 4.8 m default width, collider, Terrain snap, and contour following.
- Choose a template and width, click **Draw EasyRoads Template Road**, click Scene points, and press Enter. The tool keeps a `TerrainRoadPath` spline plus `EasyRoadSource` metadata and generates only the new EasyRoads3D road mesh/collider. It does not regrade the Terrain or rebuild existing roads.
- Snapping measures from the visible road edge rather than only its centerline, so wide roads connect reliably. While the road is still an uncommitted preview, hover an existing road and press `C` to pin that connection point without creating anything. Continue drawing more points or press Enter/double-click to create. Creation rechecks both endpoints, then only the new road receives a width-scaled 0.65-1.2 m hidden overlap and a 2.25 m terminal alignment; the target road and its Terrain remain unchanged.
- Template rebuilds only own splines carrying `EasyRoadSource`. Older Road First World splines may share the same hierarchy root, but the template tool never rebuilds or moves them.
- Edit the Unity Spline later and click **Rebuild EasyRoads Splines**. The command rebuilds EasyRoads meshes against the current Terrain without writing the heightmap. EasyRoads template sources are deliberately excluded from the Terrain-painted road rebuild, preventing duplicate road rendering.
- If an older Road First World connection already lost its overlap, run **Repair Existing EasyRoads Connections Without Terrain Changes**. It extends only each branch's first marker, supports Undo, and leaves Terrain heights and every other road marker untouched.
- To clean up a Road Builder road that was already created with a short or exposed endpoint, select its `TerrainRoadPath` object and run **Refinish Selected EasyRoad Connections**. It resnaps only that road's endpoints using the wider hidden overlap and leaves the target road and Terrain untouched.
- Duplicate the template asset to create another reusable style, then change its Road Type name, material, width, contour, and collider settings in the Inspector.
- **Connected Main Road** draws Terrain-conforming road ribbons. Moving within 1.8 m of any existing road centerline snaps the new route to it, including T-junctions.
- **House Entrance Road** starts at the selected house's editable Entrance Anchor and keeps its first metre perpendicular to the door. Its width is manually controlled by the designer.
- Change widths in the Road Builder window or press `[` / `]` while drawing.
- Click to add control points, press Enter (or double-click) to build, Backspace to remove the last point, and Esc to cancel/exit.
- Generated roads keep serialized center points and rebuild their render/collision mesh through `RoadPath`. Creation is a single Undo operation.
- Corners use width-preserving miter joins. Snapped endpoints receive a slightly raised radial junction cap so separate road meshes overlap without a visible crack.
- For roads created before junction caps were added, run `PPack > Level Design > Repair All Road Junctions` once. The repair supports Undo and does not save the Scene automatically.

## Winter Terrain templates

Open `PPack > Level Design > Open Terrain Templates`.

- `TP_WinterVillageBasin` creates a flat village core surrounded by gentle rolling terrain.
- `TP_AlpineHillside` creates a continuous low-to-high district slope with a broad buildable shelf.
- `TP_SkiSlope` creates a pronounced mountain slope with a smooth central piste and raised side ridges.
- Creating a template writes a new, independently editable `TerrainData` asset and places a centered Unity Terrain in the active Scene.
- Snow, packed snow, shadow snow, rock, and pine-ground paint layers are assigned and blended automatically. They remain editable through Unity's normal Paint Terrain inspector.
- A Road Builder baseline is captured immediately, so Connected Main Road can be drawn without another setup pass.
- The generated hierarchy separates `Geometry`, `Landmarks`, `RouteGuides`, `BoundaryNature`, `SetDressing`, `Gameplay`, and `Lighting`.

The profile asset is only a reusable starting recipe. Editing the generated Terrain does not change the profile or another map. Use the Terrain Inspector for sculpting and painting, Prefab Palette for buildings and props, and Road Builder after the large landform is approved.

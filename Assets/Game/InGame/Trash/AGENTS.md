# InGame/Trash — trash props

Small trash streams in continuously, medium resists, large needs several players. Also owns the buried haul prop that only lifts once a minimum number of players suck at once, and drifts back down when they don't.

Boundary: The shared co-op overload gauge lives in `../../Core/` because `../Insects/` uses it too.

Decisions:

- The imported legacy trash art set is stored as eight standalone prefabs under `Prefabs/`. Old networking, inventory and interaction scripts were removed because those systems do not belong to this project; the visual hierarchy, URP materials, Rigidbody and collider setup were retained.
- Trash keeps the dust system's warm beige, muted green and oxidized orange family, but separates itself through brighter values and material response: paper is matte, plastic/food coating has a restrained highlight, and metal keeps a low rough glint. The supporting grime, stain and rust textures are seamless 512 px assets rather than the original 64 px swatches, so close inspection does not turn the props into flat dark blocks.
- `Scenes/TrashPropShowcase.unity` is a permanent art-review scene for the eight trash prefabs. It includes a real `Dust/M_Dust` comparison strip but is not a build scene; its only cross-feature reference is that visual benchmark.
- The eight trash prefabs carry a uniform three-pixel coral `#F15B42` silhouette. Each source renderer has a stencil mask and a paired `__TrashOutline` child using a generated mesh under `Meshes/`; coincident vertex normals are smoothed into the outline mesh so low-poly hard edges do not change the apparent line width. The outline children are paired into the source renderer's existing LOD level. No label, prompt or scanner UI belongs in these prefabs.
- `Trash_PaperStack` is made from open, nearly flat mesh islands, so its generated `Meshes/Trash_PaperStack_Outline.mesh` stores an outward direction per disconnected paper island instead of a smoothed surface normal. Keep every generated outline mesh paired with its source renderer; regenerating or replacing a source model requires regenerating its matching outline mesh too.
- `TrashMapTarget` is the Trash → Map UI display contract (2026-08-13). Every runtime trash prefab carries it on the root. It exposes only position and size plus registration events; UI removal or routing never owns collection logic.
- All eight prefabs also carry `../Vacuum/`'s `SuctionTarget` and sit on the `Suckable` layer (2026-08-14), so `../Vehicle/`'s `VehiclePullAbility` can sector-highlight and pull them. `_visualRoot` points at each prefab's `Visual` child — its stretch-on-capture scaling carries the outline mesh along for free since both live under the same node. Collection accounting still does not belong here: `SuctionTarget` is a marker, and the `Destroy` on arrival is `VehiclePullAbility`'s test-only placeholder, not this feature's.
- One instance of each of the eight prefabs is placed along the street in `../Map/Neighborhood/Scenes/Neighborhood_ConceptMap.unity` (2026-08-14) — the first real map to use them, as opposed to the flat test cubes `Vehicle_Prototype_Test.unity` still uses.

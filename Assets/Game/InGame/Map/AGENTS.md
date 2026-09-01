# InGame/Map — stage space

The physical level and its room layout.

Boundary: What is placed in it belongs to `../Trash/`, `../Dust/` and `../Insects/`.

## Contaminable surfaces

Any mesh that can get dirty must be **uniquely unwrapped into UV0 across 0–1** — no overlapping
islands, no UV tiling. `../Dust/` paints its contamination mask in UV0, so overlapping islands make
one wipe clean two places at once. Tile the floor material inside the shader
(`uv0 * _BaseMapTiling`), never by scaling the mesh UVs.

### Neither test map is a workable contamination surface (measured 2026-08-12)

The two test prefabs sit at opposite extremes and both fail, for opposite reasons.

| | renderers | consequence for `../Dust/` |
|---|---|---|
| `PF_TestMap` | **109** | 109 masks, one per renderer — the scaling problem `../Dust/AGENTS.md` opens with |
| `PF_TestMapWithoutCar` | **1** | one mask for the entire map, floor and walls and props together |

Measured on `PF_TestMapWithoutCar`: a single 1.88 M-triangle mesh, **3235 m²** of surface, UV0 inside
0–1 with a UV area sum of **0.458** — so the unwrap is unique and the mask contract above is actually
satisfied. What fails is resolution and granularity:

- **Texel density.** A 1024 mask gives one texel per **8.2 cm**; even 4096 only reaches 2.1 cm. The
  test plane that produces the look we signed off on is **0.98 cm**. The brush's ragged edge and
  residual flecks — half of what reads as "wiped" — dissolve at that scale.
- **No way to select the floor.** Walls, ceiling and props share the mesh, the UV and the mask, so
  the floor cannot be dirtied alone nor given more of the mask.
- **Cost.** `CaptureErased` and `Paint` each draw the whole mesh, so a stroke costs 3.76 M triangles
  per frame.

**A panel laid over the floor removes all three.** Verified 2026-08-12 in
`Map_TestSceneWithoutCar`: a 16×16 m plane at `y = 0.72` (the mesh floor sits at `y = 0.70`, not at
the `Floor_Collider` box at `y = 0`) with `M_Dust` and a 2048 mask gives **0.78 cm per texel**, four
triangles, and covers the floor only. Dust rendered and erased exactly as in the Dust test scene, and
the panel's edge is nearly invisible because the dust tone matches the scanned floor.

Two things that surfaced and are not solved: the revealed clean surface is `M_Dust`'s test tile, so a
garage needs its own `_BaseMap`; and this was an in-memory experiment — **the scene was never saved**,
so nothing of it is on disk.

### `Neighborhood_ConceptMap` is the first real, saved instance (2026-08-14)

`Neighborhood/ConceptFill/ConceptGround_FillsModelHoles` is a single quad (2 triangles, UV0 exactly
0–1) that IS the map's actual continuous floor — not a separate overlay panel like the experiment
above, because it happens to already satisfy every constraint that made the two test maps fail
(one mesh, floor only, few triangles). Its material was duplicated from `../Dust/Materials/M_Dust.mat`
(so it inherits `PPack/DustSurface`) with `_BaseMap` swapped to `TX_Neighborhood_GroundPlate` — this
answers the "garage needs its own `_BaseMap`" gap noted above: swap the base texture, keep the shader
and the dirt-layer tuning. Mask resolution was raised to 2048 (~2 cm/texel, close to the 0.78 cm the
2026-08-12 experiment measured). **`_DirtMask` must be cleared to null on the duplicated material** —
copying `M_Dust` wholesale carries over its pre-cleared test ellipse (`T_Dust_Mask_Cleared`), which
reads as a random dirty/clean patch with a harsh edge on a real map; with it cleared, `DustPaintTarget`
falls back to a uniform full-dirty start (`Vacuum/AGENTS.md`'s `Dust/AGENTS.md`-referenced fallback).

**Superseded 2026-08-15 — that map now uses `../Snow/`, not dust.** The dust setup above was removed
from `Neighborhood_ConceptMap` (material back to `M_Neighborhood_ConceptGround`, `DustPaintTarget` and
the `CleanVfx` rig gone) and replaced with a `SnowStage` + `SnowPanel` pair. The findings above still
stand as the reference for *building a dust surface* — keep them for the next dust map. Two things
learned in the swap that generalise:

- **A snow map needs per-scene wiring that a dust map does not.** `SnowVehiclePad._stage` and
  `SnowVehicleDrag._stage` point at a scene `SnowStage`, and a prefab cannot reference a scene object,
  so every scene that wants snow must wire them itself. Dust needed none of this — `MopPad` finds its
  targets by physics overlap.
- **Do not leave both systems on one floor.** With dust still active under the snow, plowing exposed
  the mop's green cleaned streaks on the revealed asphalt.

## Folder map

| | |
|---|---|
| `TestMap/` | raw Tripo-generated FBX + textures for the temporary test garage, as originally imported |
| `Prefabs/` | `PF_TestMap` — the walkable version of `TestMap/`'s model |
| `Scenes/` | `Map_TestScene` — `PF_TestMap` + a `PF_Player` instance, for walking the map before any real stage exists |
| `Neighborhood/` | 연속 바닥 위에 독립 루트로 만든 집·나무·조경 모듈을 직접 배치한 교외 주택가. 기존 단일 FBX는 씬에서 사용하지 않고 롤백용으로만 보존한다. 상세 사용법은 `Neighborhood/README.md`를 따른다. |

`MapMinimapBounds` is the Map → UI projection contract. Put it on the actual playable floor or map root and assign the floor renderer/collider; UI reads `WorldBounds` instead of guessing from an object name.

## 건물–보행로–도로 정렬 계약

맵의 동선은 **건물 문 ↔ 진입 보행로 ↔ 주 보행로/도로**가 하나의 연속된 체인으로
보여야 한다. 건물과 길을 각각 보기 좋게 놓은 뒤 눈대중으로 가까이 붙이는 방식은 금지한다.

- 건물 쪽 기준점은 프리팹 피벗이나 Renderer bounds 중심이 아니라, 실제 문·현관 계단·포치가
  열리는 지점이다. 진입 보행로 중심선은 그 지점에서 좌우 오차 `0.05 m` 이내로 맞춘다.
- 문 앞 첫 `0.6–1.2 m`는 문턱/계단 정면에 거의 수직으로 뻗게 한다. 정면 오차는 `5°`
  이내를 기본값으로 하고, 장식 때문에 문 앞에서 바로 꺾지 않는다.
- 반대쪽 끝은 주 보행로 또는 도로의 실제 렌더 메시 안으로 `0.10–0.25 m` 겹쳐 넣는다.
  바닥이 사이로 보이는 틈, 접점 직전의 끝 캡, 메시끼리 닿기만 하는 접선 접속은 실패다.
- 평면 접속의 높이 차이는 `0.03 m` 이하로 맞춘다. 연석처럼 의도한 단차가 있으면 보행로를
  연석 아래에 숨겨 끝내지 말고, 보이는 계단·랜딩·완만한 램프 또는 연석 절개를 둔다.
- 일반 T자 접속은 길 중심선이 대상 도로 가장자리에 수직으로 들어가게 한다(`15°` 이내).
  곡선 합류가 의도된 경우에도 마지막 `1–2 m`에서 위치와 접선을 함께 블렌딩해 꺾임을 없앤다.
- 열린 접속부에는 가로 방향 Border/끝 캡을 두지 않는다. Border는 양옆만 감싸고, 표지판·울타리·
  장식 프롭과 Collider가 실제 보행 폭을 막지 않는지 확인한다.

작업 순서는 `도로/주 보행로 블록아웃 → 건물 문 방향 확정 → 진입 보행로 제작 → 프롭 배치`다.
건물, 도로, spline 또는 bake된 메시 중 하나라도 움직였으면 양 끝 정렬을 다시 검사한다. 완료 전에는
조감도만 보지 말고 각 문 앞 사선 캡처, 각 도로 접속부 탑다운 캡처, 전체 맵 탑다운 캡처를 남기며,
spline 핸들이 아니라 **최종 렌더 메시와 Collider**를 기준으로 간격·높이·겹침을 측정한다.

## Decisions

- **`PF_TestMap` is `TestMap/`'s raw FBX rotated -90° on X, then uniformly scaled so wall height = 4× the player's height (`Player/Prefabs/PF_Player`'s capsule height, 1.0 m → walls 4.0 m).** The raw import comes in "standing up" — its floor faces the camera like a picture instead of lying flat underfoot — so without the rotation the room reads as a vertical wall and nothing you'd walk across. Confirmed visually (Scene View screenshot) that the rotated orientation lies flat with the floor facing up before locking it in.
- **The raw FBX ships with `addColliders` off and no floor mesh of its own.** `PF_TestMap` adds a `MeshCollider` to all 109 `tripo_part_*` sub-meshes (walls, shelves, the car, clutter) so the player collides with the dressing, plus one extra `Floor_Collider` `BoxCollider` spanning the model's full footprint at its lowest point — the source scan has no continuous floor slab, only disconnected low-lying props, so without this the character free-falls through the gaps.
- Uniform scaling was used (not stretched per-axis) so the room keeps its scanned proportions; this made the floor footprint large (~18×20 m) relative to the props on it. That's expected from scaling a small AI-generated diorama up by ~21× to hit a 4 m wall height, not a sign anything is broken.
- **`Neighborhood`의 FBX는 UV0가 없는 94만 삼각형 단일 메시이므로 현재 씬에서 제외한다.** ground-only plate는 연속 `MeshCollider`를 제공하고, 집·나무·관목·울타리는 이동 가능한 개별 모듈 루트로 직접 구성한다. 전체 초기 배치는 Editor 메뉴 `PPack/Map/Rebuild Modular Neighborhood`로 재생성한다.
- **맵 외곽 추락 방지는 `ModularNeighborhood/InvisibleBoundary`의 Renderer 없는 `BoxCollider` 4개가 담당한다.** 바닥보다 약간 바깥에 두고 6m 높이로 유지하며, 재생성 메뉴가 이 경계도 함께 복구한다.

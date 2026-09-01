# InGame/Dust — floor contamination

All floor contamination lives here. Wide soft dust and localised hard-edged stains are the same
system with different brush and texture parameters, not two features.

Removable by vacuum suction and by mop push/pull.

## Mask contract

- Mesh **UV0**. A contaminable mesh must be uniquely unwrapped across 0–1. Tile the base material
  inside the shader (`uv0 * _BaseMapTiling`), never by tiling the mesh UVs. See `../Map/AGENTS.md`.
- **1 = dirty, 0 = clean.**
- **R channel only.** All sampling goes through `SampleDirt()` in `Shaders/DustSurface.hlsl`, so
  widening to RGBA touches one place.
- **Erase only.** Dirt never moves. The mop's push is carried by VFX, not by transporting mask value.
- The mask arrives as a `Texture2D` today and as a `RenderTexture` once painting lands — same
  `_DirtMask` slot, same contract.
- **The starting pattern belongs to the surface, not to the material (2026-08-12).**
  `DustPaintTarget._startMask` overrides what `ResetMask()` seeds from; leave it empty and it falls
  back to the material's `_DirtMask` exactly as before. The reason is arithmetic: one floor material
  with sixteen different starting patterns is one material, but sixteen materials if the pattern
  lives in the material. Found while tiling a 64×64 m arena — every panel started with `M_Dust`'s
  pre-cleared ellipse in the middle because `M_Dust._DirtMask` is `T_Dust_Mask_Cleared`.

## Open — mask storage does not scale yet

`DustPaintTarget` allocates **one RenderTexture per Renderer**. That is fine for a test plane and
wrong for a real stage: the current test map alone is 109 renderers, which would be 109 masks.

The direction chosen (2026-08-11, not yet built) is **a shared mask atlas** — one large RT that many
surfaces carve a UV rect out of, instead of one RT each.

Why the atlas and not per-type materials: contaminable surfaces are expected to be *most* of a
stage, not a handful, and the shaders will diversify — glass with water streaks needs a transparent
shader that can never share a material with opaque floor dust. An atlas **decouples mask storage
from the material**, so adding a shader adds no masks. Grouping by material alone does not solve
that, because the mask is per-surface state: two meshes sharing a material share a mask, and
cleaning one would clean the other.

What still has to be decided: how rects are allocated (packing by world area, or a `Texture2DArray`
with fixed slices and wasted space on small meshes), what happens when the atlas fills, and whether
painting writes to the atlas directly or to a scratch RT that is then copied in.

Note for whoever picks this up: **draw calls are not the reason to do this.** The SRP Batcher
batches by shader variant, not material — every non-texture property of `DustSurface` sits in
`UnityPerMaterial`, so many dirt materials cost about the same as one. `MaterialPropertyBlock` would
make it *worse* by removing those renderers from the batcher. The cost being solved here is mask
memory.

## Brush contract

A stroke is a **`BrushPad`** — an oriented rectangle lying on the surface, not a sphere. Pad-local XZ
is the footprint, pad-local Y is thickness, and pad-local +Z is the direction of travel.

- The judgement lives in `Shaders/DustBrush.hlsl` and **two shaders share it**: `DustPaint` subtracts
  from the mask, `DustErased` records what was subtracted. Split them and the two numbers drift apart.
- **`DustErased` must run before `DustPaint`.** It reads the mask *before* the subtraction; afterwards
  the erased amount is unrecoverable.
- `_BrushStrength` has a hard floor of `0.002` — see the 8-bit note below.

## VFX Graph property names — no underscore, and never let a mismatch pass quietly

`DustCleanVfx` feeds four exposed properties: **`ErasedMap`, `ErasedThreshold`, `PadCenter`,
`Travel`.** The blackboard names must match character for character.

**No leading underscore.** These are not shader uniforms; the convention does not apply, and the
inspector shows them as plain labels.

A mismatch is silent — `SetVector3` on a name the graph does not have simply does nothing — and the
symptom never points at the cause. When three of these four were misnamed the effects were
"particles never die" (threshold never arrived, so nothing was culled) and "dust is sucked into the
floor" (the suction target stayed at its default, the world origin). Hours went into a nozzle-height
theory that had nothing to do with it. `DustCleanVfx.Bind` therefore logs an **error** at startup
naming every missing property. Do not soften that back into a silent skip.

## Decisions

**The brush is a directed rectangle (2026-08-11).** The tool is a steam-cleaner pad that sticks to
the surface and is pushed along, so the mark it leaves is a band, not a disc. Two of the three
cleaning VFX depend on that shape — pushed dust comes off the *leading edge* and the puff is a strip
— so the shape could not be deferred past the VFX work.

**Update (2026-08-13):** push is gone, so only the puff argument is still live. The rectangle
stays — the cleaned footprint is a band whatever the VFX does — but it now rests on one leg.

**A rectangular prism needs an explicit thickness test.** A sphere was clipped top and bottom by its
own distance term; a box is infinite along its normal. Without `abs(padLocal.y) > _BrushThickness`,
wiping a floor also wipes the ceiling of the storey below. This failure mode did not exist before and
is easy to miss because a single-storey test scene never shows it.

**The erased amount lives only on the GPU (2026-08-11).** `DustErased` renders the mesh into
**pad space** — the vertex shader emits pad-local XZ as clip coordinates — and writes
`(worldPosition, min(previousMask, brushAmount))`. VFX Graph reads that texture as a position map and
places particles itself. The CPU never learns where anything was erased.

Two consequences worth keeping:

- The render target is **per tool, not per surface.** Every surface under the pad draws into the same
  one, so this does not add to the mask-storage problem above. 64×64 RGBAFloat ≈ 64 KB per tool.
- Baking into pad space rather than UV space is what makes it affordable. In UV space a pad covers
  well under 1% of a floor's UV, so almost every particle would sample an empty texel and die.

Rejected: a CPU mirror (needs mesh Read/Write and can only spawn inside a smooth rectangle, losing
the brush's ragged edge), `AsyncGPUReadback` (1–3 frames of latency, and a single scalar cannot say
*where*), and a world-space grid (blind to whatever started out clean).

**The recorded position is lifted along the surface normal (2026-08-11, raised 2026-08-13).**
`_ErasedLift` (**0.15**, was 0.04) offsets the stored world position off the surface. Writing the
exact surface point spawns particles coplanar with an opaque floor, where they are invisible or
flicker depending on the view angle — it looks like the graph is broken. The lift follows the
**normal**, not up, so it stays correct on walls. The brush judgement still uses the true surface
position; lifting that too would skew the thickness test.

**0.04 was not enough (measured 2026-08-13).** At 4 cm the graph's gravity pulled particles under the
floor within a few frames, and the opaque surface then cut them into crescents — see
`docs/images/verify/particle_lift_before_004.png`. It reads as "the dust falls through the panel".
At 0.15 the puffs render as whole discs, including where they cross the dirt boundary
(`particle_lift_after_015.png`). `DustCleanVfx._nozzleHeight` is **not** the knob for this — raising
it to 0.9 and 1.8 only changed the suction direction and made the puffs vanish sooner; the crescents
stayed.

**The lift is a headroom fix, not a cure.** What actually decides whether dust hangs or sinks is the
gravity/force balance inside the graphs, and that is not reachable from code — `_erasedMaterial` is
built at runtime with `new Material(_erasedShader)` and `ApplyBrush` never sets `_ErasedLift`, so the
shader default *is* the value. A particle that lives long enough still ends up under the floor. Fix
the balance in the graph; until then this buys the time the particle needs.

**Judge the VFX with `DustPadSweep`, not with the mouse (2026-08-11).** The success criterion is a
*difference* — what happens over dirt versus over floor that is already clean — and a hand-dragged
mouse cannot reproduce the two conditions closely enough to compare them. `DustPadSweep` drives the
pad around a fixed circle instead: the first lap crosses dirt, a few laps later the same track is
spotless, so one run yields both readings. Every number in the tables below came from it.

It sits on `DustPainter` in `Tests/Dust_Look_Test.unity`, **disabled**; tick it in the inspector to
use it. Enabling resets the mask so each run starts identically. Leaving it on alongside
`DustMousePainter` is fine — the mouse only paints while the button is held.

**Both effects read the same map; only the surviving texels differ (2026-08-11).** Puff and sparkle
are the same graph up to the point where `Set Lifetime` is fed. Each adds one condition:

| | which texels live | measured, dirty → clean track |
|---|---|---|
| Puff | erased ≥ threshold | 112 → 1 |
| Sparkle | …and erased *below* an upper bound | 43 → 0 |

There was a third, `VFX_DustPush` (…and in front of the pad along `Travel`; 17 → 1). It was
**deleted on 2026-08-13** — see below.

**The sparkle's "edge" is not extra data — it falls out of the map.** Erased amount is high in the
middle of a stroke (~0.3 at `strength` 0.35) and falls toward zero across the feathered, noise-thinned
rim. Band-passing it therefore selects exactly the boundary that was just revealed. The single knob
is the upper bound: raise it and sparkles bleed inward, lower it and they hug the outer rim.

**The push effect was deleted (2026-08-13). It was never seen.** The spec asked for pushed dust at
15% of puff so the tool would read as suction — dust that spills rather than piles — and that ratio
was built and measured (17 particles on a dirty track). What the ratio did not survive is the
vehicle. Two things kill it and they compound:

- 15% of puff is below the threshold of noticing at third-person distance. cs:124 had already
  measured the same effect from the other side: puff and push are *"불투명한 원판, 바닥에 반원으로
  잘림. 개수가 적고 평평해 눈에 안 걸린다"*.
- Push emits **along the travel direction**, which on a driven vehicle is exactly where the body
  is. The player is behind the car looking forward, so the few particles there are get occluded by
  the thing that made them.

The second point is what makes this a deletion and not a tuning job. The mop was a walked tool when
the ratio was chosen; a vehicle puts a box between the camera and the leading edge, and no value of
the ratio moves the particles out from behind it.

Judged by driving, not by screenshot. Reopen only if a tool appears whose leading edge the camera
can actually see — and re-measure before rebuilding, because the 17-particle figure was taken with
the old pad and the old threshold and means nothing now.

Two consequences to know before touching this area:

- **`Travel` has no known consumer.** Push was its only documented one. The plumbing is left in
  `DustCleanVfx` because cs:124 hand-edited the graphs without verifying their contents, so "puff
  ignores travel" is a claim in this file rather than something anyone has checked. Confirm it in
  the graph before removing the property.
- **The directed rectangle lost one of its two justifications.** The brush is a rectangle because
  the pad is a band and because pushed dust came off the leading edge; only the first reason
  survives. The shape does not change — the footprint still needs it — but do not cite push for it.

`PadCenter` is per-graph optional: sparkle ignores it.
`DustCleanVfx.Bind` errors only on the two required properties and warns on the rest, so a typo is
still loud but "this graph does not use it" is not treated as a fault.

**Still weak at third-person distance (2026-08-11).** The effect is visible but modest — see
`docs/images/verify/vfx_three_thirdperson.png`, and note it competes with the floor's specular
highlight. Spec §8.5 passes rather than passes well. Size and count are the levers; this is graph
tuning, not a pipeline problem.

**VFX Graph over the built-in particle system (2026-08-11).** Both were built and swept over the
same circular path with the same pad. Measured on the sixth lap, by which point the track is
completely clean:

| | live particles |
|---|---|
| VFX Graph | **1** |
| Built-in particle system | **2000** (saturated at `maxParticles`) |

The built-in version was given every advantage — `Emit(EmitParams)` set each particle's position and
suction velocity individually, so it loses nothing on look. What it cannot do is know **where dust
actually was**, because the mask lives on the GPU. It therefore keeps puffing dust on a floor that
has none left; see `docs/images/verify/ab_builtin_puffs_on_clean.png`, a solid clump of dust over
spotless tile.

That is not a tuning gap, it is the requirement in `docs/specs/2026-08-11-dust-clean-vfx.md` §8.2,
and no amount of CPU-side work closes it while the mask stays on the GPU.

The losing side (`PS_DustPuff` and its `EmitFallback` path) was deleted once the comparison was
recorded, same as `M_Dust_Film`. The evidence is the screenshots, not the asset.

**Cleaning VFX is presentation, never authority (2026-08-11).** It is driven by what was actually
applied to the mask, not by local input, so a client renders every player's cleaning without extra
wiring. The whole path is client-local and survives the arrival of Fusion untouched. See
`docs/specs/2026-08-11-dust-clean-vfx.md` §6 — and note that a dedicated server has no GPU, so none
of this exists there and cleanliness scoring cannot be built on it.

**Dust and Stain are one system (2026-08-11).** The design doc treats "먼지·바닥 오염" as a single
item (`docs/Game_Concept.md:106`); the split came from v1 implementation constraints that were
never re-confirmed for v2, and there was no code to migrate.

Rejected: a counting grid (cannot express an arbitrary outline); world-space XZ projection (walls
and second floors impossible, and extending it means building the mesh-UV path anyway); URP decal
projectors (pixel-level erase needs a mask sample inside the decal, so the structure gains a layer
for nothing).

Reopen if dust and stains ever need genuinely different removal rules that one mask channel cannot
express.

**Hand-written `.shader`, not Shader Graph (2026-08-11).** `.shadergraph` is GUID-bearing JSON that
Plastic cannot merge and that cannot be authored outside the editor. The original reason for
choosing Shader Graph — "writing HLSL means reimplementing URP lighting" — was wrong: URP exposes
`UniversalFragmentPBR(InputData, SurfaceData)`. Fill the structs and URP's lighting runs.

What this gives up: dragging nodes to see a value change. Most tuning happens on material
properties in the inspector anyway.

**Grain comes from a real material, not from noise (2026-08-11).** A pure-procedural preview of the
compositing formula showed that noise-modulated brightness reads as blur, never as depth. Dust needs
an actual normal map. See `Textures/AGENTS.md` for the source.

**Granular over film (2026-08-11).** `M_Dust_Granular` and `M_Dust_Film` were built as an A/B and
rendered side by side under one light in `Tests/Dust_Look_Test.unity`. Granular wins and is the
default. At full coverage the film preset reads as flat brown paint; the granular preset reads as a
material with depth, and the difference survives at third-person camera distance. It also matches
the art targets, where the dust is unambiguously granular
(`docs/images/dust-target-grain.png`). Screenshots: `docs/images/verify/`.

`M_Dust_Film` was deleted on 2026-08-11 once the test scene stopped referencing it. The evidence
that settled this lives in `docs/images/verify/`, not in the material. The scene's two floor planes
survive as `Floor_Left` and `Floor_Right` — same material now, kept apart only because they meet at
`x = 0` and that seam is the one place a brush can straddle two surfaces.

**Update (2026-08-11):** the `_Layer0_` rename below wiped its values and it was deliberately not
migrated, so it no longer renders the film look at all. It is dead weight now, not a comparison.
`M_Dust_Granular` was renamed to `M_Dust` at the same time — the GUID survives, so scene references
held.

**The brush is uneven, and unevenness falls off with strength (2026-08-11).** A perfectly circular
falloff reads as a stamp, not as a tool. `DustPaint.shader` perturbs the distance test with
world-space value noise so the outline is ragged, and multiplies the result by a second noise so the
interior clears patchily. `DustMousePainter` scales that amount down as strength rises — a light
wipe leaves residue, a hard one clears evenly, and repeated passes converge. The noise is sampled in
**world space, not UV**: sampled in UV it would travel with the brush and the residue would look
stuck to the tool rather than to the floor.

**Mask precision is 8-bit, and it rounds (measured 2026-08-11).** `RenderTextureFormat.R8` means a
stamp subtracts exactly `round(strength × 255)` steps — not `strength × 255`. Two consequences, and
the first one is a trap:

- **`strength` below ~0.00196 does nothing at all, forever.** The rounded step is 0, so the texel
  never changes no matter how many times you pass over it. There is a dead band at the bottom of
  the slider.
- Above that the rounding overshoots: `0.002` erases at 1.96× its nominal rate, `0.01` at 1.18×. A
  light stroke therefore cleans *faster* than the float maths suggests, not slower.

At the working value `0.35` the error is 0.28% and the mask reaches 0 in three stamps, so this does
not constrain normal use. It constrains **tuning** — do not spend time on values below 0.002.

Measured against the real `DustPaint` shader with brush noise off; an `RFloat` mask reproduced the
float maths exactly, which locates the error in storage rather than in the shader. The earlier
version of this note had the direction backwards — it said light strokes build up *more coarsely*,
when they actually erase faster than intended until they fall off the cliff.

**Normal strength scales the tangent XY, it does not lerp (2026-08-11).** The first version did
`lerp(float3(0,0,1), grainN, saturate(cover * strength))`, which `saturate` clamps at 1 — so
`_GrainStrength` above 1.0 did nothing and the normal map could never be pushed past the source
map's own steepness. It read as "the normal map is barely applied". `ScaleDustNormal` multiplies
the tangent-space XY instead, so values above 1 genuinely deepen the relief. The range now goes to
4 and the material sits at 3.2 — well past what the old formula could reach.

**Macro colour variation is separate from grain (2026-08-11).** Detail alone still reads as one
flat brown, because all the variation lives at the grain frequency. `_DirtColor` / `_DirtColorB`
are blended by the dissolve noise at a very low tiling (`_MacroTiling`), giving lighter and darker
regions across the floor. Setting both tints to the same colour turns it off.

**Grain tiling is 3, not 12 (2026-08-11).** At 12 the debris in the source material lands at
roughly a millimetre on screen and the floor reads as a flat wash — see
`docs/images/verify/grain_tiling12.png` against `grain_tiling3.png`. At 3 the pebbles and twigs
carry their own shading and the surface reads as material with things in it. This is deliberately
larger than physically correct: the art direction is toy-like exaggeration, not photographic scale.
Grain strength goes to 1.8 to match — **the shader default. The material sits at 3.2**, which is the
value that actually ships; 1.8 was the figure from the tiling-12 era and was never updated here.

**The dirt material needs debris, not just grain (2026-08-11).** Two uniform materials were tried
and rejected — a Sampler-authored sand and Poly Haven's `brown_mud_dry`. Both are single-scale, and
no parameter recovers what a single scale cannot express. The art target's dust is two scales: a
fine base plus scattered discrete clumps that catch light. Judge a candidate material by whether it
has that second scale before importing it.

**Stochastic tiling is on by default (2026-08-11).** Tiling one 2K grain texture 12× showed an
obvious diagonal lattice across the whole floor — see `docs/images/verify/tiling_near_off.png` next
to `tiling_near_on.png`. The shader now splits UV into a triangle lattice, offsets the texture
randomly per cell and blends the three neighbours (Heitz & Neyret 2018). Blending three samples
evenly would collapse contrast toward the mean, so the weights are raised to `_StochasticContrast`
so one sample dominates outside narrow transition bands.

Cost is 3× the texture samples on the three dirt maps, behind the `_STOCHASTIC_TILING` keyword.
Turn it off for surfaces small enough that repetition never shows.

**Dirt maps drive albedo and roughness, and `_DirtColor` is a tint (2026-08-11).** Originally only
the normal was sampled and dirt was one flat colour, which left the downloaded albedo and roughness
maps unused. With a map bound, `_DirtColor` multiplies it — a dark source material can be lifted
toward the art-target tone without re-exporting from Sampler.

**The dissolve threshold is remapped, not raw (2026-08-11).** `cover` compares the noise against
`d * (1 + 2·edgeSoftness) − edgeSoftness`, not against `d`. Comparing against raw `d` leaves texels
whose noise sits near 1.0 uncovered even at `d = 1`, so "completely dirty" and "completely clean"
are both unreachable — it showed up as white specks on a floor that was supposed to be fully
covered. Do not simplify this back.

**Layer-scoped properties carry a `_Layer0_` prefix (2026-08-11).** The 16 properties that will
differ per contamination layer were renamed (`_DirtColor` → `_Layer0_ColorA`, and so on); the 10 that
are surface or shared across layers kept their names. **The prefix is the layer/shared distinction** —
`_Layer0_NormalMap` is the dirt grain, `_NormalMap` is the clean surface.

Renaming a shader property drops the value stored in every material, so this was done while only two
materials used the shader. Only `M_Dust` was migrated. Costs nothing in the shader: logic, CBUFFER
order, types and defaults are untouched, and no C# changed — `Assets/Game/` only ever referenced
`_DirtMask`, which kept its name.

**A second contamination type needs no shader change (2026-08-11).** `M_Sand` and `M_GreyDust` run
the same `DustSurface` with different textures and numbers. This is the first hard evidence for the
spec's rejection of per-type shader keywords: the types differ in values, not in maths.

**Grey cannot be made by tinting a brown map (2026-08-11).** `dirtAlbedo = map * tint` is a multiply,
so it cannot remove saturation. Measured: neutralising `T_Dirt_Albedo` needs a **7.1× multiplier on
blue**, which blows out highlights and amplifies JPEG noise. `T_GreyDust_Albedo.jpg` is therefore a
luminance conversion of that map (mean lifted 94.7 → 150), and the tint works normally on top of it.
Its source is CC0, so the derivative is clean — see `Textures/AGENTS.md`.

## Open — clean tile shows at a plane edge

A small patch at the **right edge of the first floor plane** renders as clean base map instead of
contamination. Visible as a teal sliver in `docs/images/verify/rename_before.png` and
`variants_three.png`.

It predates the `_Layer0_` rename. Narrowed by probing: disabling that plane removes it, disabling
the neighbouring plane does not, and deleting the brush indicator does not — so the plane itself
draws it and it is not a stray object. The mask texture has no edge blemish. **Cause unknown.**
Anyone touching `ComposeDust` or the mask sampling should check whether this moves.

Full reasoning, rejected alternatives and the art targets: `docs/specs/2026-08-11-dust-surface-shader.md`
and `docs/specs/2026-08-11-contamination-variants.md`.

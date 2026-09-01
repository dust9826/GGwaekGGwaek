# 먼지 표면 셰이더 구현 계획

> **에이전트용:** 태스크 단위로 실행한다. 스텝은 `- [ ]` 체크박스다.
> **스펙:** `docs/specs/2026-08-11-dust-surface-shader.md` — 결정의 근거는 전부 거기 있다.

**목표:** 바닥 오염 마스크로 청소 전후 대비를 만드는 URP 셰이더와, 그것을 눈으로 확인하는 테스트 씬.

**아키텍처:** 손으로 쓴 `.shader`가 URP의 `UniversalFragmentPBR`을 호출한다. 합성 로직은 `.hlsl`로 분리해 다음 브랜치의 페인팅 패스가 재사용한다. 먼지 양은 텍스처 마스크(R 채널, 1=더러움)로 들어오고, 지금은 손으로 만든 텍스처, 나중에 RenderTexture가 같은 슬롯에 꽂힌다.

**기술 스택:** Unity 6000.6.0b7 · URP 17.6.0 · HLSL · Plastic SCM · Unity CLI(`~/.unity/bin/unity cmd`)

## 전역 제약

- **브랜치**: `/main/dust-surface-shader`. `/main`에서 작업하지 않는다
- **버전 관리는 Plastic이다. git 명령을 쓰지 않는다.** 새 파일은 `Private`으로 뜨므로 `cm add -R` 후 체크인하고, 체크인 뒤 `cm status`로 남은 `Changed`를 명시 경로로 다시 넣는다
- **`.unity` / `.prefab` / `.asset` / `.mat` / `.meta` YAML을 손으로 편집하지 않는다.** 전부 Unity CLI나 에디터로 만든다
- **에셋 이동·삭제는 Unity를 통해서만.** Finder·쉘 금지
- 네임스페이스는 평평한 `PPack` 하나. 열거형만 `E` 접두사
- 마스크 규약: **UV0 · R 1채널 · 1 = 더러움**
- 테스트 씬은 피처 폴더 안 `Tests/`에 두고 **Build Settings에 넣지 않는다**
- **작업 전 에디터 상태**: `Assets/Scenes/SampleScene.unity` 하나만 열림, dirty 아님. 끝나면 이 상태로 복원한다

---

## 파일 구조

```
Assets/Game/InGame/Dust/
  AGENTS.md                          수정 — 통합 결정, 마스크 규약, A/B 결론
  Shaders/
    DustSurface.hlsl                 신규 — 합성 로직. 다음 브랜치가 재사용
    DustSurface.shader               신규 — URP Lit 패스 + 프로퍼티
  Materials/
    M_Floor_Tile.mat                 신규 — 깨끗한 바닥 (URP Lit)
    M_Dust_Granular.mat              신규 — A/B 알갱이형
    M_Dust_Film.mat                  신규 — A/B 막형
  Textures/
    AGENTS.md                        신규 — 외부 텍스처 출처·라이선스
    T_Dirt_Albedo.jpg/png            조달 — CC0
    T_Dirt_Normal.png                조달 — CC0
    T_Dirt_Rough.png                 조달 — CC0
    T_Dust_DissolveNoise.png         생성 — 타일링 밸류 노이즈
    T_Dust_Mask_Uniform.png          생성
    T_Dust_Mask_Blotchy.png          생성
    T_Dust_Mask_Path.png             생성
    T_Dust_Mask_Corner.png           생성
  Tests/
    Dust_Look_Test.unity             신규 — 반으로 갈린 바닥 + 조명 + 3인칭 카메라
```

**분담**: `.hlsl`·`.shader`·파이썬 생성기는 **Codex**(`codex-orchestrate` 스킬). `.mat`·`.unity`·임포트 설정·검증 스크린샷·Plastic은 **직접**. 이 경계는 파일 포맷에서 그대로 나온다 — 텍스트는 위임되고 YAML은 안 된다.

---

## Task 1: 폴더 정리와 문서

Stain 통합을 구조에 반영한다. 셰이더와 독립이라 먼저 끝내둔다.

**Files:**
- Delete: `Assets/Game/InGame/Stain/` (폴더째)
- Modify: `Assets/Game/InGame/AGENTS.md`, `Assets/Game/InGame/Map/AGENTS.md`, `Assets/Game/InGame/Dust/AGENTS.md`, `docs/Glossary.md`, `AGENTS.md`(루트)

- [ ] **Step 1: Stain 폴더 제거**

```bash
cm remove Assets/Game/InGame/Stain
```

디스크에서 먼저 지우면 `Removed locally`로 물려 체크인이 "there are no changes"로 실패한다. `cm remove`가 먼저다.

- [ ] **Step 2: `InGame/AGENTS.md` 폴더 맵에서 Stain 줄 제거**

지울 줄:
```
| `Stain/` | localised hard-edged stains (separate system from Dust) |
```

`Dust/` 줄을 이렇게 바꾼다:
```
| `Dust/` | all floor contamination — wide dust and hard-edged stains alike |
```

- [ ] **Step 3: `Dust/AGENTS.md` 재작성**

```markdown
# InGame/Dust — floor contamination

All floor contamination lives here. Wide soft dust and localised hard-edged stains are the same
system with different brush and texture parameters, not two features.

Removable by vacuum suction and by mop push/pull.

## Mask contract

- Mesh **UV0**. A contaminable mesh must be uniquely unwrapped into 0–1. Tile the base material
  inside the shader (`uv0 * _BaseMapTiling`), never by tiling the mesh UVs.
- **1 = dirty, 0 = clean.**
- **R channel only.** All sampling goes through `SampleDirt()` in `Shaders/DustSurface.hlsl` so
  widening to RGBA touches one place.
- **Erase only.** Dirt never moves; the mop's push is carried by VFX, not by transporting mask value.

## Decisions

**Dust and Stain are one system (2026-08-11).** The design doc treats "먼지·바닥 오염" as a single
item (`docs/Game_Concept.md:106`); the split came from v1 implementation constraints that were never
re-confirmed for v2. Rejected: a counting grid (cannot express an arbitrary outline), world-space XZ
projection (walls and second floors impossible), URP decals (pixel-level erase needs a mask sample
inside the decal anyway). Reopen if dust and stains ever need genuinely different removal rules that
one mask channel cannot express.

**Hand-written `.shader`, not Shader Graph (2026-08-11).** `.shadergraph` is GUID-bearing JSON that
Plastic cannot merge, and it cannot be authored outside the editor. URP exposes
`UniversalFragmentPBR(InputData, SurfaceData)`, so writing HLSL does not mean reimplementing lighting.

Full reasoning and the rejected alternatives: `docs/specs/2026-08-11-dust-surface-shader.md`.
```

- [ ] **Step 4: `Map/AGENTS.md`에 UV 규칙 추가**

Boundary 줄 아래에 붙인다:
```markdown
## Contaminable surfaces

Any mesh that can get dirty must be **uniquely unwrapped into UV0 0–1** — no overlapping islands,
no UV tiling. `../Dust/` paints its mask in UV0 and overlapping islands make one wipe clean two
places at once. Tile the floor material inside the shader, not by scaling mesh UVs.
```

- [ ] **Step 5: `docs/Glossary.md` 갱신**

`| 얼룩 | \`Stain\` | 국소·경계가 뚜렷한 바닥 오염 |` 줄을 지우고 `먼지` 줄을 이렇게:
```
| 먼지·바닥 오염 | `Dust` | 바닥 오염 전부. 넓은 먼지와 경계 뚜렷한 얼룩이 같은 시스템 |
```

"기획서와 코드가 갈리는 지점"의 첫 항목을 해소된 것으로 바꾼다:
```
- ~~기획서는 "먼지·바닥 오염"을 한 항목으로 묶지만 코드는 `Dust`와 `Stain`으로 나눈다~~
  **2026-08-11 해소** — `Dust` 하나로 통합했다. 근거는 `docs/specs/2026-08-11-dust-surface-shader.md`
```

- [ ] **Step 6: 루트 `AGENTS.md`에 테스트 씬 절 신설**

기존 `## 6. Asset editing`을 `## 7. Asset editing`으로 바꾸고 그 앞에 삽입:

```markdown
## 6. Test scenes

A verification scene lives in a `Tests/` folder inside the feature that owns it, and it gets checked
in — `Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`.

- Scene files are YAML and **cannot be merged**. When two branches touch the same scene, one side
  has to be thrown away whole. One scene per feature, with branches split per feature, means that
  never comes up.
- Name it `<Feature>_<WhatItVerifies>_Test.unity`.
- **Never add a test scene to Build Settings.** That list is one file,
  `ProjectSettings/EditorBuildSettings.asset`, and every branch edits the same lines in it. Open
  test scenes from the Project window instead.
- A test scene owns the objects inside it. Do not modify a production prefab to make a test work.
- When a feature goes away its test scene goes with it. Delete the folder with `cm remove`.
```

- [ ] **Step 7: 확인**

```bash
test ! -d Assets/Game/InGame/Stain && echo "Stain 제거됨"
grep -c "Stain" Assets/Game/InGame/AGENTS.md docs/Glossary.md
grep -n "## 6. Test scenes" AGENTS.md
```
기대: Stain 폴더 없음, `InGame/AGENTS.md`의 Stain 언급 0, 루트에 §6 존재.

- [ ] **Step 8: 체크인**

```bash
cm add -R Assets/Game/InGame/Dust
cm ci Assets/Game/InGame AGENTS.md docs/Glossary.md --commentsfile=<msg>
cm status   # Changed 남았으면 명시 경로로 재체크인
```

---

## Task 2: 텍스처 조달과 생성

**Files:**
- Create: `Assets/Game/InGame/Dust/Textures/` 아래 흙 3장 + 노이즈 1장 + 마스크 4장
- Create: `Assets/Game/InGame/Dust/Textures/AGENTS.md`

**Interfaces:**
- Produces: Task 4의 머티리얼이 참조할 텍스처 에셋 경로. 노멀맵은 **반드시 Texture Type = Normal map**으로 임포트돼 있어야 한다

- [ ] **Step 1: Poly Haven에서 CC0 흙 텍스처 고르기**

```bash
curl -s "https://api.polyhaven.com/assets?t=textures&c=floor" | python3 -c "
import json,sys
d=json.load(sys.stdin)
for k,v in d.items():
    tags=' '.join(v.get('tags',[]))+' '+' '.join(v.get('categories',[]))
    if any(w in tags.lower() for w in ('dirt','soil','ground','sand','mud')):
        print(k, '|', v['name'], '|', v.get('categories'))
"
```

고른 슬러그로 파일 목록을 받는다:
```bash
curl -s "https://api.polyhaven.com/files/<slug>" | python3 -m json.tool | head -60
```

**2K, jpg 또는 png**의 `Diffuse`(또는 `albedo`) / `nor_gl` / `rough`를 받는다. Poly Haven은 전부 CC0다.

- [ ] **Step 2: 다운로드**

```bash
mkdir -p /tmp/dirt_dl
curl -sL "<diffuse url>" -o /tmp/dirt_dl/T_Dirt_Albedo.jpg
curl -sL "<nor_gl url>"  -o /tmp/dirt_dl/T_Dirt_Normal.png
curl -sL "<rough url>"   -o /tmp/dirt_dl/T_Dirt_Rough.jpg
ls -la /tmp/dirt_dl
```
기대: 3개 파일, 각 수백 KB 이상.

- [ ] **Step 3: 노이즈와 마스크 생성**

`docs/images/generate_dust_preview.py`의 밸류 노이즈를 재사용해 8비트 그레이스케일 PNG를 만든다.
새 스크립트 `docs/images/generate_dust_textures.py`, 순수 stdlib(zlib + struct), 1024×1024, **타일링 가능**해야 한다(격자 좌표를 해상도로 wrap).

| 파일 | 내용 |
|---|---|
| `T_Dust_DissolveNoise.png` | 옥타브 3~4개 밸류 노이즈, 전 범위 사용 |
| `T_Dust_Mask_Uniform.png` | 전체 255 |
| `T_Dust_Mask_Blotchy.png` | 저주파 노이즈를 0.55~1.0으로 리맵 |
| `T_Dust_Mask_Path.png` | 255 바탕에 2차 베지어 스트로크를 0으로, 폭 ~120px, 부드러운 어깨 |
| `T_Dust_Mask_Corner.png` | 가장자리로 갈수록 255, 중앙은 ~60 |

실행 후 8개 PNG가 존재하고 각 10KB 이상인지 확인한다.

- [ ] **Step 4: Unity로 임포트**

```bash
U=~/.unity/bin/unity
$U cmd import_asset --source /tmp/dirt_dl/T_Dirt_Albedo.jpg --destination Game/InGame/Dust/Textures/T_Dirt_Albedo.jpg
# 나머지도 동일. 노이즈/마스크는 docs/images/ 산출물 경로에서 가져온다
```

`import_asset`의 인자 이름은 `$U cmd import_asset --help`로 확인하고 맞춘다.

- [ ] **Step 5: 임포트 설정 교정**

노멀맵은 Texture Type을 바꿔야 한다. 마스크·노이즈는 **sRGB를 꺼야** 값이 왜곡되지 않는다.

```bash
$U cmd eval --code '
var paths = new[]{
  ("Assets/Game/InGame/Dust/Textures/T_Dirt_Normal.png", UnityEditor.TextureImporterType.NormalMap, true),
  ("Assets/Game/InGame/Dust/Textures/T_Dirt_Rough.jpg", UnityEditor.TextureImporterType.Default, false),
  ("Assets/Game/InGame/Dust/Textures/T_Dust_DissolveNoise.png", UnityEditor.TextureImporterType.Default, false),
};
foreach (var (p, t, srgb) in paths) {
  var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(p);
  if (ti == null) { UnityEngine.Debug.LogError("no importer: " + p); continue; }
  ti.textureType = t;
  ti.sRGBTexture = (t == UnityEditor.TextureImporterType.NormalMap) ? true : srgb;
  ti.wrapMode = UnityEngine.TextureWrapMode.Repeat;
  ti.SaveAndReimport();
}
return "done";'
```

마스크 4장도 같은 방식으로 `Default` + sRGB off + Repeat.

- [ ] **Step 6: 확인**

```bash
$U cmd get_import_settings --path Assets/Game/InGame/Dust/Textures/T_Dirt_Normal.png --format json
```
기대: `textureType`이 `NormalMap`.

- [ ] **Step 7: 라이선스 기록**

`Assets/Game/InGame/Dust/Textures/AGENTS.md`:
```markdown
# InGame/Dust/Textures

## External assets

| File | Source | License |
|---|---|---|
| `T_Dirt_Albedo` `T_Dirt_Normal` `T_Dirt_Rough` | Poly Haven — `<slug>` (https://polyhaven.com/a/<slug>) | CC0 |

**CC0 only.** Anything with redistribution limits (textures.com and the like) does not come into
this repository. Record source and license here before importing.

## Generated

`T_Dust_DissolveNoise` and the `T_Dust_Mask_*` set come from `docs/images/generate_dust_textures.py`,
which is re-runnable. Masks are placeholders for the painting system landing in the next branch.
```

- [ ] **Step 8: 체크인**

```bash
cm add -R Assets/Game/InGame/Dust/Textures
cm add -R docs/images
cm ci Assets/Game/InGame/Dust/Textures docs/images --commentsfile=<msg>
cm status
```

---

## Task 3: 셰이더 (Codex 위임)

**Files:**
- Create: `Assets/Game/InGame/Dust/Shaders/DustSurface.hlsl`
- Create: `Assets/Game/InGame/Dust/Shaders/DustSurface.shader`

**Interfaces:**
- Produces: 셰이더 이름 `PPack/DustSurface`, 아래 프로퍼티 전부. Task 4의 머티리얼이 이 이름으로 붙는다
- Produces: `ComposeDust(...)` — 다음 브랜치의 페인팅 패스가 재사용

- [ ] **Step 1: `DustSurface.hlsl` 작성**

```hlsl
#ifndef PPACK_DUST_SURFACE_INCLUDED
#define PPACK_DUST_SURFACE_INCLUDED

// 오염 마스크 샘플 — RGBA 확장 시 고치는 유일한 지점.
// 규약: R 채널, 1 = 더러움.
float SampleDirt(TEXTURE2D_PARAM(dirtMask, dirtSampler), float2 uv)
{
    return SAMPLE_TEXTURE2D(dirtMask, dirtSampler, uv).r;
}

// 탄젠트 공간 노멀 블렌드 (whiteout)
half3 BlendDustNormal(half3 baseN, half3 detailN)
{
    return SafeNormalize(half3(baseN.xy + detailN.xy, baseN.z * detailN.z));
}

struct DustSurfaceResult
{
    half3 albedo;
    half  smoothness;
    half3 normalTS;
};

// d            : 먼지 양 0..1 (마스크 * amount)
// dissolveN    : 디졸브 노이즈 0..1
DustSurfaceResult ComposeDust(
    half3 cleanAlbedo, half cleanSmoothness, half3 cleanNormalTS,
    float d, float dissolveN, half3 grainNormalTS,
    half3 dirtColor, half dirtSmoothness,
    float edgeSoftness, float thinOpacity, float fullDirtAt,
    float grainStrength, float edgeRim, half3 edgeRimColor)
{
    // 핵심: d를 알파로 쓰지 않고 노이즈와 비교한다.
    // 알파로 쓰면 원이 투명해지고, 비교하면 얼룩덜룩 걷힌다.
    float cover = smoothstep(dissolveN - edgeSoftness, dissolveN + edgeSoftness, d);

    // 옅어짐: cover가 1이어도 d가 낮으면 먼지색 자체가 옅다.
    // 이게 없으면 "얇아진다"가 아니라 "구멍이 뚫린다"로 읽힌다.
    float opacity = lerp(thinOpacity, 1.0, saturate(d / max(fullDirtAt, 1e-4)));
    half3 dirtA   = lerp(cleanAlbedo, dirtColor, opacity);

    // 갓 드러난 경계가 밝게 튄다 (cover 0.5에서 최대)
    float rim = edgeRim * (1.0 - abs(cover * 2.0 - 1.0));

    DustSurfaceResult o;
    o.albedo     = lerp(cleanAlbedo, dirtA, cover) + edgeRimColor * rim;
    o.smoothness = lerp(cleanSmoothness, dirtSmoothness, cover);
    o.normalTS   = BlendDustNormal(cleanNormalTS,
                       lerp(half3(0,0,1), grainNormalTS, saturate(cover * grainStrength)));
    return o;
}
#endif
```

- [ ] **Step 2: `DustSurface.shader` 작성**

`Shader "PPack/DustSurface"`. 프로퍼티 블록은 정확히 이것:

```
_BaseMap, _BaseColor, _BaseMapTiling, _NormalMap, _Smoothness
_DirtMask, _DirtAmount, _DirtColor, _DirtSmoothness
_DirtGrainNormal, _GrainTiling, _GrainStrength
_DissolveNoise, _DissolveNoiseTiling, _EdgeSoftness
_ThinOpacity, _FullDirtAt
_EdgeRim, _EdgeRimColor
```

기본값: `_Smoothness 0.7`, `_DirtSmoothness 0.05`, `_DirtColor (0.753,0.541,0.314,1)`(아트 타겟의 `#C08A50`), `_DirtAmount 1`, `_EdgeSoftness 0.04`, `_GrainTiling 12`, `_GrainStrength 1`, `_DissolveNoiseTiling 6`, `_ThinOpacity 0.35`, `_FullDirtAt 0.6`, `_EdgeRim 0.3`.

패스 요구사항:
- `Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }`, `LightMode"="UniversalForward"`
- `#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"`
- `#include "DustSurface.hlsl"`
- 표준 URP 키워드: `_MAIN_LIGHT_SHADOWS`, `_MAIN_LIGHT_SHADOWS_CASCADE`, `_ADDITIONAL_LIGHTS`, `_ADDITIONAL_LIGHT_SHADOWS`, `_SHADOWS_SOFT`, `LIGHTMAP_ON`, `DIRLIGHTMAP_COMBINED`
- 프래그먼트에서 `SurfaceData`와 `InputData`를 채우고 **`UniversalFragmentPBR(inputData, surfaceData)`** 호출
- `ShadowCaster`와 `DepthOnly` 패스 포함
- **UV**: `_DirtMask`는 `uv0` 그대로. `_BaseMap`은 `uv0 * _BaseMapTiling`, 노이즈는 `uv0 * _DissolveNoiseTiling`, 그레인은 `uv0 * _GrainTiling`
- `SurfaceData.metallic = 0`, `occlusion = 1`, `alpha = 1`

- [ ] **Step 3: 컴파일 확인**

```bash
U=~/.unity/bin/unity
$U cmd recompile && sleep 5 && $U cmd recompile_status --format json
$U cmd console --level error --tail 40 --format json
```
기대: 에러 0.

- [ ] **Step 4: 프로퍼티 노출 확인**

```bash
$U cmd get_shader_properties --shader "PPack/DustSurface" --format json
```
기대: 위 19개 프로퍼티가 전부 나온다. 하나라도 빠지면 Step 2로 돌아간다.

- [ ] **Step 5: 체크인**

```bash
cm add -R Assets/Game/InGame/Dust/Shaders
cm ci Assets/Game/InGame/Dust/Shaders --commentsfile=<msg>
cm status
```

---

## Task 4: 머티리얼 3종

**Files:**
- Create: `Assets/Game/InGame/Dust/Materials/M_Floor_Tile.mat`, `M_Dust_Granular.mat`, `M_Dust_Film.mat`

**Interfaces:**
- Consumes: Task 2의 텍스처, Task 3의 `PPack/DustSurface`
- Produces: Task 5의 씬이 바닥에 물릴 머티리얼

`.mat`은 YAML이라 손으로 쓰지 않는다. `create_asset` 또는 `eval`로 만든다.

- [ ] **Step 1: 머티리얼 생성**

```bash
$U cmd eval --code '
var sh = UnityEngine.Shader.Find("PPack/DustSurface");
if (sh == null) return "shader not found";
foreach (var n in new[]{"M_Dust_Granular","M_Dust_Film"}) {
  var m = new UnityEngine.Material(sh);
  UnityEditor.AssetDatabase.CreateAsset(m, "Assets/Game/InGame/Dust/Materials/" + n + ".mat");
}
UnityEditor.AssetDatabase.SaveAssets();
return "created";'
```

- [ ] **Step 2: 텍스처와 값 물리기**

```bash
$U cmd set_material_properties \
  --material Assets/Game/InGame/Dust/Materials/M_Dust_Granular.mat \
  --properties '{
    "_DirtMask": {"path":"Assets/Game/InGame/Dust/Textures/T_Dust_Mask_Blotchy.png"},
    "_DissolveNoise": {"path":"Assets/Game/InGame/Dust/Textures/T_Dust_DissolveNoise.png"},
    "_DirtGrainNormal": {"path":"Assets/Game/InGame/Dust/Textures/T_Dirt_Normal.png"},
    "_BaseMap": {"path":"<타일 텍스처 또는 흰색>"},
    "_GrainStrength": 1.0, "_GrainTiling": 16, "_EdgeSoftness": 0.04, "_ThinOpacity": 0.35
  }'
```

`M_Dust_Film`은 같은 텍스처에 `_GrainStrength 0.05`, `_EdgeSoftness 0.2`, `_ThinOpacity 0.15`.

인자 형식은 `$U cmd set_material_properties --help`로 확인한다.

- [ ] **Step 3: 확인**

```bash
$U cmd get_material_properties --material Assets/Game/InGame/Dust/Materials/M_Dust_Granular.mat --format json
```
기대: 셰이더가 `PPack/DustSurface`, 텍스처 슬롯이 null이 아님.

- [ ] **Step 4: 체크인**

---

## Task 5: 테스트 씬

**Files:**
- Create: `Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`

**Interfaces:**
- Consumes: Task 4의 머티리얼

- [ ] **Step 1: 씬 생성**

```bash
$U cmd create_scene --path Game/InGame/Dust/Tests/Dust_Look_Test.unity
```

- [ ] **Step 2: 바닥 두 장 · 조명 · 카메라**

Quad나 Plane 두 개를 나란히 놓고 각각 `M_Dust_Granular` / `M_Dust_Film`을 물린다.
Directional Light 하나를 **깨끗한 면에 스페큘러가 보이는 각도**로 (대략 `rotation (35, -140, 0)`부터 시작해 스크린샷 보며 조정).
카메라는 3인칭 높이·부감 (대략 `position (0, 4.5, -6)`, `rotation (32, 0, 0)`).

`create_gameobject`, `set_serialized_field`, `add_component`로 만든다.

- [ ] **Step 3: 저장**

```bash
$U cmd save_scene --path Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity
```

**Build Settings에 추가하지 않는다.**

- [ ] **Step 4: 체크인**

---

## Task 6: 룩 검증과 A/B 결론

- [ ] **Step 1: `_DirtAmount` 스윕 스크린샷**

`1.0 / 0.75 / 0.5 / 0.25 / 0.0` 각각에서:
```bash
$U cmd set_material_properties --material <각 머티리얼> --properties '{"_DirtAmount": <값>}'
$U cmd capture_game_view --save_path /tmp/dust_verify/amount_<값>.png
```

- [ ] **Step 2: 스펙 8절 기준으로 판정**

1. 0과 1의 스크린샷이 한눈에 다른가
2. 깨끗한 면에 스페큘러·반사가 보이는가
3. 0→1에서 원형이 아니라 얼룩덜룩 걷히는가
4. `T_Dust_Mask_Path`에서 자국 경계가 아트 타겟처럼 읽히는가
5. 근거리에서 먼지가 판이 아니라 알갱이 물질로 읽히는가
6. Granular / Film 중 어느 쪽인가
7. 마젠타 없음, 콘솔 에러 0

- [ ] **Step 3: 결론을 `Dust/AGENTS.md`에 기록**

이긴 프리셋과 **왜** 이겼는지, 진 쪽 파라미터의 기본값을 적는다.

- [ ] **Step 4: 에디터 상태 복원 (루트 `AGENTS.md` §5)**

```bash
$U cmd editor_stop                                  # 플레이 모드였다면
$U cmd menu --path "File/Open Scene" ...            # 또는 eval로 SampleScene 열기
$U cmd list_open_scenes --format json               # SampleScene 하나, dirty 아님 확인
```
`/tmp/dust_verify/`의 임시 스크린샷 중 문서에 안 쓸 것은 지운다.

- [ ] **Step 5: `docs/INDEX.md` 갱신과 최종 체크인**

현재 상태 절에 "먼지 셰이더 완료, 페인팅은 다음"을 적고 Plans에 이 계획을 등록한다.

---

## 다음 브랜치로 넘어가는 것

페인팅(메시를 RT에 UV 공간으로 렌더), 청소 VFX, 청소도 집계(`Cleanliness/`), Fusion 복제.
근거는 스펙 9절.

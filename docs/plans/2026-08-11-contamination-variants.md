# 오염 배리언트 구현 계획

> **에이전트용:** 태스크 단위로 실행한다. 스텝은 `- [ ]` 체크박스다.
> **스펙:** `docs/specs/2026-08-11-contamination-variants.md` — 결정의 근거는 전부 거기 있다.

**목표:** 오염 1층짜리 `DustSurface`를 층 구조가 들어올 수 있는 이름으로 정리하고, 머티리얼 배리언트 계층(부모=오염, 자식=표면)을 한 쌍 세워 전파와 잠금이 실제로 동작하는지 확인한다.

**아키텍처:** 셰이더 로직은 **한 줄도 바꾸지 않는다.** 층별로 갈릴 16개 프로퍼티에 `_Layer0_` 접두사만 붙이고, 층과 무관한 10개는 그대로 둔다. 그 위에 `M_Dust`(부모)와 `M_Dust_OnTile`(자식)을 세워 표면 5개만 자식이 오버라이드하게 한다.

**기술 스택:** Unity 6000.6.0b7 · URP 17.6.0 · HLSL · Plastic SCM · Unity CLI(`~/.unity/bin/unity`)

## 전역 제약

- **브랜치**: `/main/contamination-variants`. 워크스페이스는 이미 여기 올라가 있다(cs:39). `/main`에서 작업하지 않는다
- **버전 관리는 Plastic이다. git 명령을 쓰지 않는다.** 새 파일은 `Private`으로 뜨므로 `cm add -R` 후 체크인하고, 체크인 뒤 `cm status`로 남은 `Changed`를 명시 경로로 다시 넣는다
- **`.unity` / `.prefab` / `.asset` / `.mat` / `.meta` YAML을 손으로 편집하지 않는다.** 전부 Unity 에디터나 Unity CLI로 만든다
- **에셋 이동·이름변경·삭제는 Unity Project 창에서만.** Finder·쉘 금지 — `.meta` GUID가 깨진다
- **셰이더 합성 로직을 바꾸지 않는다.** 이번 작업은 이름과 자산 구조뿐이다. `DustSurface.hlsl`은 프로퍼티 이름을 참조하지 않으므로 **건드릴 파일이 아니다**
- 마스크 규약은 그대로: **UV0 · R 1채널 · 1 = 더러움**
- 테스트 씬은 `Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`. **Build Settings에 넣지 않는다**
- **작업 전 에디터 상태를 먼저 기록하고 끝나면 복원한다**(루트 `AGENTS.md` §5). 열린 씬, 활성 씬, Play Mode, dirty 여부

## 사전 확인

이 계획은 **C# 변경이 없다.** `Assets/Game/`의 C#이 참조하는 셰이더 프로퍼티는 `_DirtMask`(이름 안 바뀜)와 `DustPaint` 전용 브러시 6개뿐이다. 확인 명령:

```bash
grep -rn "PropertyToID" --include="*.cs" Assets/Game/ | grep -o '"_[A-Za-z]*"' | sort -u
```

기대 출력: `_BrushHardness` `_BrushNoiseAmount` `_BrushNoiseScale` `_BrushPosition` `_BrushRadius` `_BrushStrength` `_DirtMask` — 이 목록에 `_Layer0_`로 바뀔 이름이 하나도 없어야 한다.

---

## 파일 구조

```
Assets/Game/InGame/Dust/
  AGENTS.md                       수정 — 배리언트 계층·접두사 결정 기록
  Shaders/
    DustSurface.shader            수정 — 프로퍼티 16개 리네임 (Properties · CBUFFER · 프래그먼트)
    DustSurface.hlsl              건드리지 않음 — 프로퍼티 이름을 참조하지 않는다
  Materials/
    M_Dust.mat                    이름변경 — M_Dust_Granular 에서. GUID 유지되므로 씬 참조가 산다
    M_Dust_OnTile.mat             신규 — M_Dust 의 Material Variant
    M_Dust_Film.mat               그대로 둔다 — 마이그레이션 대상 아님(아래 참조)
  Tests/
    Dust_Look_Test.unity          수정 — 바닥이 M_Dust_OnTile 을 쓰게
docs/
  images/verify/                  신규 스크린샷 2장 — 리네임 전후 회귀 증거
  INDEX.md                        수정 — 계획 한 줄
```

**`M_Dust_Film`을 마이그레이션하지 않는 이유**: `Dust/AGENTS.md`가 *"참조가 없어지면 삭제"*라고 적어둔 A/B 비교용이다. 리네임 후 값이 날아가도 잃을 것이 없고, 오히려 삭제 시점을 앞당기는 근거가 된다. 값을 다시 넣지 않는다.

---

## 프로퍼티 리네임 표

**바꾸는 것 16개** — 층별로 갈릴 것:

| 현재 | 새 이름 |
|---|---|
| `_DirtAmount` | `_Layer0_Amount` |
| `_DirtAlbedoMap` | `_Layer0_AlbedoMap` |
| `_DirtGrainNormal` | `_Layer0_NormalMap` |
| `_DirtRoughMap` | `_Layer0_RoughMap` |
| `_DirtColor` | `_Layer0_ColorA` |
| `_DirtColorB` | `_Layer0_ColorB` |
| `_DirtSmoothness` | `_Layer0_Smoothness` |
| `_DirtRoughInfluence` | `_Layer0_RoughInfluence` |
| `_GrainTiling` | `_Layer0_GrainTiling` |
| `_GrainStrength` | `_Layer0_GrainStrength` |
| `_DissolveNoiseTiling` | `_Layer0_DissolveTiling` |
| `_EdgeSoftness` | `_Layer0_EdgeSoftness` |
| `_ThinOpacity` | `_Layer0_ThinOpacity` |
| `_FullDirtAt` | `_Layer0_FullDirtAt` |
| `_EdgeRim` | `_Layer0_EdgeRim` |
| `_EdgeRimColor` | `_Layer0_EdgeRimColor` |

**그대로 두는 것 10개** — 표면 5개와 오염 공통 5개:

`_BaseMap` `_BaseColor` `_BaseMapTiling` `_NormalMap` `_Smoothness`
`_DirtMask` `_MacroTiling` `_UseStochasticTiling` `_StochasticContrast` `_DissolveNoise`

`_DirtGrainNormal` → `_Layer0_NormalMap`이 표면의 `_NormalMap`과 헷갈릴 수 있다. 접두사가 있는 쪽이 오염, 없는 쪽이 표면이다 — 이것이 접두사를 붙이는 이유다.

## `M_Dust_Granular`의 현재 값

리네임하면 이 값들이 전부 날아간다. **Task 1 시작 전에 이 표가 맞는지 대조하고, Task 2에서 다시 넣는다.**

| 프로퍼티 (새 이름) | 값 |
|---|---|
| `_Layer0_Amount` | `1` |
| `_Layer0_AlbedoMap` | `T_DirtGround_Albedo.jpg` |
| `_Layer0_NormalMap` | `T_DirtGround_Normal.jpg` |
| `_Layer0_RoughMap` | `T_DirtGround_Rough.jpg` |
| `_Layer0_ColorA` | `(1.05, 0.98, 0.86, 1)` |
| `_Layer0_ColorB` | `(0.72, 0.62, 0.5, 1)` |
| `_Layer0_Smoothness` | `0.05` |
| `_Layer0_RoughInfluence` | `0.35` |
| `_Layer0_GrainTiling` | `3` |
| `_Layer0_GrainStrength` | `3.2` |
| `_Layer0_DissolveTiling` | `6` |
| `_Layer0_EdgeSoftness` | `0.04` |
| `_Layer0_ThinOpacity` | `0.35` |
| `_Layer0_FullDirtAt` | `0.6` |
| `_Layer0_EdgeRim` | `0.96` |
| `_Layer0_EdgeRimColor` | `(1, 1, 1, 1)` |

이름이 안 바뀌는 것들(값이 살아남으므로 재입력 불필요, 대조용):

| 프로퍼티 | 값 |
|---|---|
| `_BaseMap` | `T_Floor_Tile.png` |
| `_BaseMapTiling` | `4` |
| `_BaseColor` | `(1, 1, 1, 1)` |
| `_NormalMap` | 없음 |
| `_Smoothness` | `0.75` |
| `_DirtMask` | `T_Dust_Mask_Cleared.png` |
| `_MacroTiling` | `0.55` |
| `_UseStochasticTiling` | `1` |
| `_StochasticContrast` | `7` |
| `_DissolveNoise` | `T_Dust_DissolveNoise.png` |

**`_Layer0_GrainStrength`가 3.2다.** `Dust/AGENTS.md`는 "그레인 강도 1.8"이라고 적고 있는데 그건 그레인 타일링 12 시절의 값이고, 머티리얼의 실제 값은 3.2다. **머티리얼 값이 정답이다** — 문서 쪽이 뒤처져 있다. Task 3에서 이 불일치를 고친다.

**죽은 데이터**: `M_Dust_Granular.mat`에 `_GrainBlendTiling: 0.7`과 `_GrainTilingB: 5.3`이 남아 있는데 현재 셰이더의 `Properties`에 없는 이름들이다. 이전 셰이더 버전의 잔재다. **이번 작업과 무관하므로 손대지 않는다.** 리네임과 함께 자연히 정리되는지만 Task 2에서 확인하고 기록한다.

---

## Task 1: 셰이더 프로퍼티 리네임

**Files:**
- Modify: `Assets/Game/InGame/Dust/Shaders/DustSurface.shader`
- 건드리지 않음: `Assets/Game/InGame/Dust/Shaders/DustSurface.hlsl`

**Interfaces:**
- Consumes: 없음 (첫 태스크)
- Produces: 위 리네임 표의 새 이름 16개. Task 2가 이 이름으로 머티리얼 값을 넣는다

- [ ] **Step 1: 작업 전 에디터 상태를 기록한다**

```bash
~/.unity/bin/unity status
```

열린 씬, 활성 씬, Play Mode, dirty 여부를 적어둔다. Task 3 끝에 이 상태로 되돌린다. Play Mode가 켜져 있으면 끄고 시작한다.

- [ ] **Step 2: 리네임 전 스크린샷을 찍는다 (회귀 기준선)**

`Dust_Look_Test.unity`를 열고 카메라를 움직이지 않은 채 스크린샷을 `docs/images/verify/rename_before.png`로 저장한다.

에디터 구동은 **`unity-start` 스킬**을 쓴다. 명령 이름을 추측하지 말고 연결된 에디터가 실제로 제공하는 목록에서 고른다:

```bash
~/.unity/bin/unity status   # 이 워크스페이스의 에디터가 연결돼 있는지
~/.unity/bin/unity cmd      # 인자 없이 = 사용 가능한 명령 목록
```

`unity status`에 이 워크스페이스(`.../PPackPPack_v2-branchB`)가 안 보이면 **다른 워크스페이스의 에디터로 명령을 보내지 않는다.** 루트 `AGENTS.md`대로 중단하고 보고한다. CLI가 막히면 에디터 GUI에서 직접 씬을 열고 Game 뷰를 캡처해도 된다 — 중요한 것은 전후 스크린샷의 카메라가 같다는 것뿐이다.

**이 스크린샷이 Task 2의 합격 기준이다.** 카메라 위치를 바꾸면 비교가 무의미해지므로 이후 절대 건드리지 않는다.

- [ ] **Step 3: 현재 값이 계획의 표와 맞는지 대조한다**

```bash
awk '/m_SavedProperties/,0' Assets/Game/InGame/Dust/Materials/M_Dust_Granular.mat \
  | grep -E "^\s+- _|m_Texture|\{r:|: [0-9.-]+$"
```

위 "현재 값" 표와 다르면 **표가 아니라 파일이 정답이다.** 다른 값을 발견하면 계획의 표를 고치고 진행한다.

- [ ] **Step 4: `Properties` 블록의 16개 이름을 바꾼다**

`DustSurface.shader` 5–45행. 이름만 바꾸고 타입·범위·기본값·주석은 그대로 둔다. 예:

```hlsl
        _Layer0_Amount("Dirt Amount", Range(0, 1)) = 1
        _Layer0_AlbedoMap("Dirt Albedo", 2D) = "white" {}
        _Layer0_ColorA("Dirt Tint A", Color) = (1, 1, 1, 1)
        _Layer0_ColorB("Dirt Tint B", Color) = (0.82, 0.74, 0.62, 1)
```

- [ ] **Step 5: `TEXTURE2D`/`SAMPLER` 선언 3개를 바꾼다**

56–69행. 텍스처 3개가 리네임 대상이다. 샘플러 이름은 `sampler_` + 텍스처 이름 규칙을 따라야 한다:

```hlsl
        TEXTURE2D(_Layer0_NormalMap);
        SAMPLER(sampler_Layer0_NormalMap);
        TEXTURE2D(_Layer0_AlbedoMap);
        SAMPLER(sampler_Layer0_AlbedoMap);
        TEXTURE2D(_Layer0_RoughMap);
        SAMPLER(sampler_Layer0_RoughMap);
```

- [ ] **Step 6: `CBUFFER_START(UnityPerMaterial)` 안의 이름을 바꾼다**

71–91행. **선언 순서를 바꾸지 않는다** — SRP Batcher는 레이아웃에 민감하다. 이름만 교체한다.

- [ ] **Step 7: 프래그먼트 셰이더의 참조를 바꾼다**

`DustForwardFragment` 165–245행. `_DirtAmount` `_DissolveNoiseTiling` `_GrainTiling` `_DirtColor` `_DirtColorB` `_DirtSmoothness` `_DirtRoughInfluence` `_EdgeSoftness` `_ThinOpacity` `_FullDirtAt` `_GrainStrength` `_EdgeRim` `_EdgeRimColor`와 `TEXTURE2D_ARGS(...)`에 넘기는 텍스처·샘플러 쌍이 대상이다. `ComposeDust(...)` 호출의 **인자 순서는 바꾸지 않는다.**

- [ ] **Step 8: 프로퍼티 목록을 통째로 대조한다**

옛 이름을 grep으로 찾는 방식은 **쓰지 않는다.** `_GrainTiling` 같은 패턴에 식별자 경계가 없어서 새 이름 `_Layer0_GrainTiling`에도 매칭되고, "출력 없음"이 합격 기준인데 항상 출력이 나온다. 대신 `Properties` 블록의 식별자를 전부 뽑아 예상 26개와 대조한다 — 누락과 오타를 동시에 잡는 더 강한 검사다.

```bash
sed -n '/^    Properties/,/^    }/p' Assets/Game/InGame/Dust/Shaders/DustSurface.shader \
  | grep -oE '(^|[[:space:]])_[A-Za-z0-9_]+\(' | tr -d ' (' | sort
```

기대: **정확히 26줄**이고 아래와 일치한다.

```
_BaseColor _BaseMap _BaseMapTiling _DirtMask _DissolveNoise _MacroTiling _NormalMap
_Smoothness _StochasticContrast _UseStochasticTiling
_Layer0_AlbedoMap _Layer0_Amount _Layer0_ColorA _Layer0_ColorB _Layer0_DissolveTiling
_Layer0_EdgeRim _Layer0_EdgeRimColor _Layer0_EdgeSoftness _Layer0_FullDirtAt
_Layer0_GrainStrength _Layer0_GrainTiling _Layer0_NormalMap _Layer0_RoughInfluence
_Layer0_RoughMap _Layer0_Smoothness _Layer0_ThinOpacity
```

- [ ] **Step 9: 컴파일을 확인한다**

**`recompile`은 셰이더를 검사하지 않는다.** C# 스크립트 전용이라 셰이더만 고친 상태에서는 `up_to_date`가 뜨고, 그것은 아무것도 증명하지 않는다. 셰이더는 이렇게 확인한다:

```bash
~/.unity/bin/unity cmd get_console_logs --project-path . --severity Error --limit 20
~/.unity/bin/unity cmd get_material_properties --project-path . \
  --material Assets/Game/InGame/Dust/Materials/M_Dust.mat
```

기대: 콘솔 에러 **0건**이고, 머티리얼이 `PPack/DustSurface`에 바인딩된 채 **새 이름들을 프로퍼티로 돌려준다.** 셰이더가 깨졌다면 둘 다 무너진다.

이 시점에 `M_Dust_Granular`는 값이 날아가 기본값으로 렌더되므로 **화면이 달라 보이는 것이 정상이다.** 특히 알갱이가 사라지고 먼지가 밋밋한 갈색으로 깔린다 — `_Layer0_GrainStrength`가 3.2에서 기본값 1.8로 떨어지고 텍스처 슬롯이 비었기 때문이다. Task 2에서 되돌린다.

- [ ] **Step 10: 체크인**

```bash
cm status
cm ci Assets/Game/InGame/Dust/Shaders/DustSurface.shader --commentsfile=<파일>
cm status
```

커밋 메시지 요지: 층별로 갈릴 프로퍼티 16개에 `_Layer0_` 접두사. 로직 변경 없음. 머티리얼 값은 다음 태스크에서 복구.

---

## Task 2: 머티리얼 배리언트 계층

**Files:**
- Rename: `Assets/Game/InGame/Dust/Materials/M_Dust_Granular.mat` → `M_Dust.mat`
- Create: `Assets/Game/InGame/Dust/Materials/M_Dust_OnTile.mat`
- Modify: `Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`

**Interfaces:**
- Consumes: Task 1의 새 프로퍼티 이름 16개
- Produces: `M_Dust`(부모, 오염 정체성 잠금) · `M_Dust_OnTile`(자식, 표면 5개만 오버라이드)

**전부 Unity Project 창에서 한다.** `.mat` YAML을 손으로 쓰지 않는다.

- [ ] **Step 1: `M_Dust_Granular`를 `M_Dust`로 이름 바꾼다**

Project 창에서 우클릭 → Rename. **GUID가 유지되므로 `Dust_Look_Test.unity`의 참조가 살아 있다.** Finder에서 하면 `.meta`가 깨진다.

- [ ] **Step 2: `M_Dust`에 값 16개를 다시 넣는다**

Inspector에서 위 "현재 값" 표대로 입력한다. 이름이 안 바뀐 10개는 살아 있으므로 건드리지 않고, 표의 두 번째 표와 대조만 한다.

- [ ] **Step 3: 값이 복구됐는지 스크린샷으로 확인한다**

`Dust_Look_Test.unity`에서 **카메라를 건드리지 않은 채** `docs/images/verify/rename_after.png`를 찍는다.

**합격 기준: `rename_before.png`와 눈으로 구분되지 않을 것.** 다르면 값 하나가 틀린 것이다. 두 파일을 나란히 놓고 확인한다:

```bash
open docs/images/verify/rename_before.png docs/images/verify/rename_after.png
```

차이가 보이면 Step 2로 돌아간다. **여기서 통과하지 못하면 다음 스텝으로 가지 않는다.**

- [ ] **Step 4: `M_Dust`에서 오염 정체성을 잠근다**

Inspector에서 프로퍼티 옆 자물쇠를 켠다. 잠글 것 — `_Layer0_AlbedoMap` `_Layer0_NormalMap` `_Layer0_RoughMap` `_Layer0_ColorA` `_Layer0_ColorB` `_Layer0_Smoothness` `_Layer0_RoughInfluence` `_Layer0_GrainTiling` `_Layer0_GrainStrength` `_Layer0_DissolveTiling` `_Layer0_EdgeSoftness` `_Layer0_ThinOpacity` `_Layer0_FullDirtAt` `_Layer0_EdgeRim` `_Layer0_EdgeRimColor`.

**잠그지 않을 것** — `_DirtMask`와 `_Layer0_Amount`. 스펙 §5: 이 둘은 "그 표면에서 얼마나, 어디가" 더러운지라 자식이 정한다. `DustPaintTarget`이 `_DirtMask`를 초기 분포로 읽으므로 잠그면 모든 바닥이 같은 자리에서 더러워진다.

- [ ] **Step 5: `M_Dust_OnTile` 배리언트를 만든다**

`M_Dust` 우클릭 → Create → Material Variant. 이름 `M_Dust_OnTile`.

- [ ] **Step 6: 자식에서 표면 5개만 오버라이드한다**

`_BaseMap` = `T_Floor_Tile.png`, `_BaseMapTiling` = `4`, `_BaseColor` = `(1,1,1,1)`, `_NormalMap` = 없음, `_Smoothness` = `0.75`.

Inspector가 오버라이드된 프로퍼티를 표시한다. **오버라이드 표시가 이 5개(+ 아래 Step 7의 2개)에만 붙어야 한다.** 오염 쪽에 붙었으면 잠금이 안 걸린 것이다 — Step 4로 돌아간다.

- [ ] **Step 7: 자식에서 분포와 양을 정한다**

`_DirtMask` = `T_Dust_Mask_Cleared.png`, `_Layer0_Amount` = `1`. 잠기지 않았으므로 오버라이드된다.

- [ ] **Step 8: 전파를 확인한다 — 이 태스크가 사려는 것**

`M_Dust`(부모)의 `_Layer0_GrainTiling`을 `3` → `8`로 바꾸고 씬을 본다.
**기대: `M_Dust_OnTile`을 쓰는 바닥의 알갱이 크기가 같이 변한다.**
확인 후 **`3`으로 되돌린다.**

변하지 않으면 배리언트가 아니라 복사본이 만들어진 것이다. Step 5로 돌아간다.

- [ ] **Step 9: 테스트 씬의 바닥을 자식 머티리얼로 바꾼다**

`Dust_Look_Test.unity`에서 바닥 오브젝트의 머티리얼을 `M_Dust` → `M_Dust_OnTile`로 교체하고 저장한다.

- [ ] **Step 10: 부모를 표면 중립으로 만든다 — 계층이 이름뿐이 되지 않게**

여기까지 오면 부모와 자식의 표면 값이 **똑같다.** Unity는 부모와 같은 값을 오버라이드로 기록하지 않을 수 있고, 그러면 "자식이 표면을 정한다"는 구조가 이름만 남는다. 실제로 갈라놓는다.

`M_Dust`(부모)에서 `_BaseMap`을 비우고(`None`) `_BaseMapTiling`을 `1`로 되돌린다. 부모는 특정 바닥에 매이지 않은 **오염의 정의**이므로 이것이 옳은 상태다.

- [ ] **Step 11: 자식이 타일을 지켰는지 확인한다**

씬을 본다. 두 갈래다.

- **바닥에 타일이 그대로 보인다** → 자식이 진짜 오버라이드를 들고 있다. 통과
- **바닥이 흰색이 됐다** → 자식은 오버라이드가 없었고 부모 값을 물려받고 있었다. `M_Dust_OnTile`에서 `_BaseMap` = `T_Floor_Tile.png`, `_BaseMapTiling` = `4`를 **지금 다시 넣는다.** 이제는 부모와 다르므로 확실히 오버라이드로 기록된다

어느 쪽이든 이 스텝을 마치면 자식만 타일을 안다.

- [ ] **Step 12: 최종 스크린샷**

`docs/images/verify/variant_ontile.png`. **여전히 `rename_before.png`와 구분되지 않아야 한다** — 계층을 세웠을 뿐 룩은 바뀐 것이 없다.

- [ ] **Step 13: 죽은 데이터가 정리됐는지 확인한다**

```bash
grep -nE "_GrainBlendTiling|_GrainTilingB" Assets/Game/InGame/Dust/Materials/M_Dust.mat
```

남아 있으면 그대로 둔다 — 이번 작업 범위가 아니다. 결과만 Task 3에 기록한다.

- [ ] **Step 14: 체크인**

```bash
cm status
cm add -R Assets/Game/InGame/Dust/Materials docs/images/verify
cm status --private
cm ci Assets/Game/InGame/Dust/Materials Assets/Game/InGame/Dust/Tests docs/images/verify --commentsfile=<파일>
cm status
```

`.meta`가 같이 들어갔는지 확인한다. 머티리얼 이름을 바꿨으므로 Plastic에 **이동으로 잡히는지 삭제+추가로 잡히는지** 확인하고, 후자면 그대로 체크인한다.

---

## Task 3: 문서 갱신

**Files:**
- Modify: `Assets/Game/InGame/Dust/AGENTS.md`
- Modify: `docs/INDEX.md`

**Interfaces:**
- Consumes: Task 1·2의 결과
- Produces: 없음 (마지막 태스크)

- [ ] **Step 1: `Dust/AGENTS.md`에 결정을 기록한다**

"Decisions" 절에 항목을 더한다. 담을 것:

- **층별 프로퍼티에 `_Layer0_` 접두사 (2026-08-11).** 셰이더 프로퍼티를 나중에 이름 바꾸면 머티리얼 값이 날아간다. `DustSurface`를 쓰는 머티리얼이 2장뿐인 지금이 가장 쌌다. 접두사 유무가 곧 층별/공통 구분이다
- **머티리얼 계층은 부모=오염, 자식=표면 (2026-08-11).** 잠그는 기준은 정체성이냐 분포냐. `_DirtMask`와 `_Layer0_Amount`는 자식이 정한다 — `DustPaintTarget`이 `_DirtMask`를 초기 분포로 읽기 때문
- 전문은 `docs/specs/2026-08-11-contamination-variants.md`

- [ ] **Step 2: 그레인 강도 불일치를 고친다**

`Dust/AGENTS.md`의 *"그레인 강도를 1.8로 맞춘다"*를 실제 값 **3.2**로 고친다. 1.8은 그레인 타일링 12 시절의 값이고 머티리얼은 3.2로 살아 있다.

- [ ] **Step 3: `M_Dust_Film` 처리를 기록한다**

`Dust/AGENTS.md`의 `M_Dust_Film` 항목에 **리네임 때 값이 날아갔다**는 것과, 따라서 A/B 비교용으로도 더는 쓸 수 없으니 삭제 후보라는 것을 적는다. 삭제 자체는 이번 작업에서 하지 않는다 — 참조가 남았는지 확인이 필요하고 그건 별건이다.

- [ ] **Step 4: Step 11의 죽은 데이터 결과를 기록한다**

`_GrainBlendTiling`·`_GrainTilingB`가 남았는지 여부를 `Dust/AGENTS.md`에 한 줄로 적는다.

- [ ] **Step 5: `docs/INDEX.md`를 갱신한다**

"Plans" 절의 이 계획 줄은 이미 있다. **`미착수` → `완료`로 바꾼다.**

"현재 상태" 절에 한 줄 더한다 — 오염 배리언트 계층이 섰고, 층 2·3은 스펙에만 있으며, 다음 할 일이 무엇인지.

- [ ] **Step 6: 에디터 상태를 복원한다**

Task 1 Step 1에서 기록한 상태로 되돌린다. 루트 `AGENTS.md` §5 기준으로 확인할 것:

- Play Mode 꺼짐
- 원래 활성 씬이 활성
- dirty인 씬 없음
- `(Clone)` · `[RuntimeSpawned]` · `__TEST__` 이름이 남아 있지 않음
- 검증용으로만 만든 오브젝트·카메라가 남아 있지 않음

- [ ] **Step 7: 체크인**

```bash
cm status
cm ci Assets/Game/InGame/Dust/AGENTS.md docs/INDEX.md docs/plans/2026-08-11-contamination-variants.md --commentsfile=<파일>
cm status
```

---

## 완료 기준

- `DustSurface.shader`에 옛 이름 16개가 하나도 없다
- 셰이더 컴파일 에러 0건, C# 변경 0건
- `variant_ontile.png`이 `rename_before.png`와 눈으로 구분되지 않는다 — **룩이 하나도 안 변했다는 것이 이 작업의 성공 조건이다**
- `M_Dust`의 `_Layer0_GrainTiling`을 바꾸면 `M_Dust_OnTile`에 전파된다
- `M_Dust_OnTile`이 `_BaseMap`과 `_BaseMapTiling`을 **오버라이드로** 들고 있다 (부모는 각각 `None`·`1`이므로 값이 달라 확실히 기록된다)
- `M_Dust_OnTile`에서 오염 룩 15개가 **잠겨서 편집되지 않는다.** `_DirtMask`와 `_Layer0_Amount`는 편집된다

> 부모와 값이 같은 프로퍼티(`_BaseColor` `_NormalMap` `_Smoothness` 등)에는 오버라이드 표시가 안 붙을 수 있다. **정상이다.** Unity Material Variant는 부모와 다른 값만 오버라이드로 기록한다. 계층이 동작하는지는 위 세 줄로 판단하지, 오버라이드 개수로 판단하지 않는다.
- `cm status`가 깨끗하다 (기존 `ProjectSettings/Packages` Private 제외 — 이 작업의 산출물이 아니다)
- Play Mode 꺼짐, 원래 씬 활성, dirty 없음

## 실제로 어떻게 되었나 (2026-08-11 실행 기록)

Task 1은 계획대로다. Task 2는 **도중에 방향이 바뀌었다.**

계획은 표면 축(부모 `M_Dust` + 자식 `M_Dust_OnTile`)을 세워 전파와 잠금을 확인하는 것이었다. 실제로는 **오염 축**을 먼저 늘렸다 — 모래 텍스처가 들어오면서 `M_Sand`를, 이어 `M_GreyDust`를 만들어 오염 종류 3종이 됐고, 이들을 나란히 보는 `Dust_MaterialVariants_Test.unity`를 새로 만들었다.

그래서 **스펙 §5의 머티리얼 배리언트 계층은 아직 검증되지 않았다.** 지금 있는 것은 형제 머티리얼 세 장이지 부모-자식이 아니다. 전파(부모를 고치면 자식이 따라오는가)와 잠금(자식이 오염 룩을 못 건드리는가)은 미확인 상태다. 다음에 표면 축을 늘릴 때 Task 2의 Step 4~11을 그대로 쓰면 된다.

얻은 것도 있다. 오염 종류를 늘리는 데 **셰이더 변경이 0**이라는 것이 실물로 확인됐다 — 스펙 §7이 "오염 종류를 키워드로 나누지 않는다"고 기각한 근거가 추측이 아니게 됐다.

Task 3의 문서 갱신은 완료했다.

## 하지 않는 것

스펙이 자리만 잡아둔 것들이다. 이번 계획에 들어오면 범위를 벗어난 것이다.

- 층 2·3 구현, `_DIRT_LAYERS_*` 키워드, 마스크 채널 확장
- 투명 셰이더, 카펫 등 표면별 특수 셰이딩
- 오염 종류의 런타임 데이터(ScriptableObject)
- 마스크 아틀라스
- VFX·사운드·청소도 집계
- 두 번째 표면(나무 등) 머티리얼 — 전파 확인에는 자식 하나면 충분하다

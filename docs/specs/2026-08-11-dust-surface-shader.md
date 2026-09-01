# 먼지 표면 셰이더 — 설계

> **작성일** 2026-08-11 · **브랜치** `/main/dust-surface-shader` · **피처** `Assets/Game/InGame/Dust/`

청소 전후의 시각적 대비를 만드는 바닥 오염 셰이더와, 그것을 눈으로 확인하는 테스트 씬을 만든다.
**"청소한 느낌"이 나는지가 이 작업의 유일한 성공 기준이다.**

---

## 1. 선행 결정 — Dust와 Stain을 하나로 합친다

`InGame/Stain/`을 없애고 `InGame/Dust/` 하나가 모든 바닥 오염을 소유한다.

**근거**

- 기획서는 애초에 나누지 않는다. `Game_Concept.md:106`이 "먼지·바닥 오염"을 한 항목으로 두고, 도구별 구분은 "오염 종류에 따라 적합한 도구를 구분할 수 있다"까지만 말한다.
- 분리 근거는 v1의 구현 사정(격자 vs 렌더 텍스처)이었고 `Stain/AGENTS.md`에 "v2에서 아직 재확인되지 않았다"로 명시돼 있었다. `docs/Glossary.md:27`과 `Session_Summary_20260811.md:60`도 같은 항목을 미결로 남겨두고 있었다.
- 코드가 0줄인 지금이 되돌리는 비용이 가장 싸다.

**기각한 대안**

| 대안 | 기각 이유 |
|---|---|
| 격자 기반 먼지 + UV 얼룩 (v1 방식) | 격자는 임의 외곽선을 표현하지 못한다. 레퍼런스의 유기적인 경계가 안 나온다 |
| 기반만 `Core/`에 두고 폴더는 유지 | 소비자가 하나뿐이다. "두 번째 호출부에서 승격"(루트 `AGENTS.md`) 규칙 위반 |
| 월드 XZ 톱다운 투영 | 벽·수직면·2층 바닥이 원리적으로 불가. 확장 시 메시 UV 경로를 어차피 새로 만들어야 한다 |
| URP 데칼 프로젝터 | 픽셀 단위 지우기를 하려면 데칼 안에서 다시 마스크를 샘플해야 해서 구조가 한 겹 늘어난다 |

**뒤집는 조건**: 먼지와 얼룩이 서로 다른 *제거 규칙*(예: 얼룩만 런타임 생성, 얼룩만 대걸레 전용)을 실제로 갖게 되고 그게 마스크 채널 하나로 표현이 안 될 때.

---

## 2. 오염 마스크 규약

이번 브랜치는 셰이더만 만들지만, 다음 브랜치의 페인팅이 여기 그대로 꽂혀야 한다. 그래서 규약을 먼저 고정한다.

| 항목 | 결정 |
|---|---|
| 좌표 | **메시 UV0**. 오염 대상 메시는 0–1 유니크 언랩이어야 한다 |
| 바닥 재질 타일링 | 메시 UV가 아니라 셰이더 안에서 `uv0 * _BaseMapTiling` |
| 값 방향 | **1 = 더러움, 0 = 깨끗함** |
| 채널 | **R 1채널** |
| 슬롯 | `_DirtMask` 하나. 지금은 `Texture2D`, 나중에 `RenderTexture` — 같은 자리 |
| 페인팅 방향 | **지우기 전용.** 먼지가 이동하지 않는다 |

**지우기 전용의 대가와 대안**: 대걸레로 밀 때 먼지가 앞에 모이는 총량 보존 동작을 포기한다. 밀리는 느낌은 파티클과 물기 궤적 VFX가 대신한다. 대신 마스크가 쓰기 전용이라 RT 더블버퍼가 불필요하고, 청소도 집계가 단조 감소라 정확하며, Fusion 복제 시 붓질 이벤트만 보내면 된다.

**UV0을 고른 이유**: UV1은 라이트맵과 충돌할 수 있으나, 매 메시에 두 번째 UV 세트를 저작하게 만드는 비용이 더 크다. 라이트맵을 실제로 켤 때 재확인한다.

### RGBA 확장 경로

오염 종류가 늘어 채널이 더 필요해지면 바꿔야 하는 곳은 **넷뿐이다.** 이 목록이 유지되도록 구현한다.

1. RT 포맷 선언 — C# 한 곳
2. 브러시의 채널 마스크 파라미터
3. 표면 셰이더의 채널별 블렌드
4. 청소도 집계의 채널별 합산

이를 위해 마스크 샘플링을 **Shader Graph 서브그래프 `SampleDirt` 하나**로 묶는다. 셰이더 어디서도 `_DirtMask`를 직접 샘플하지 않는다.

---

## 3. 이번 브랜치의 범위

**한다**: 먼지 표면 셰이더, 머티리얼 2종(A/B용), 마스크 텍스처 4장, 테스트 씬, 문서 갱신.

**안 한다** — 각각 어느 브랜치 소관인지 명시한다.

| 안 하는 것 | 소관 |
|---|---|
| 마스크에 실제로 칠하는 페인팅 시스템 | 다음 `Dust` 브랜치 |
| 청소 VFX (먼지 퍼프, 흡입 궤적) | 다음 `Dust` 브랜치 |
| 청소도 집계와 % 표시 | `InGame/Cleanliness/` |
| 청소기·대걸레 조작 | `InGame/Vacuum/`, `InGame/Mop/` |
| 3D 먼지 덩어리·자갈 프롭 | `InGame/Trash/` |
| Fusion 복제 | Fusion 설치 후 |

집계를 `Dust/`에 넣지 않는 이유: 한번 여기 생기면 `Cleanliness/`로 옮길 이유를 나중에 아무도 찾지 못한다.

### 이 브랜치가 답할 수 없는 것

"청소한 느낌"은 네 갈래인데 이 브랜치는 그중 셋만 답한다.

| | 이번에 답하나 |
|---|---|
| 전후 차이가 확연한가 | **예** |
| 먼지가 어떤 형태로 걷히는가 | **예** |
| 닦인 자국이 어떻게 보이는가 | **부분** — 자국이 그려진 마스크를 손으로 만들어 꽂아서 룩만 본다 |
| 닦는 손맛 (반응성·지연·타이밍) | **아니오** — 페인팅이 있어야 나온다 |

네 번째 항목으로 이 브랜치를 판정하지 않는다.

---

## 4. 레퍼런스 판독

`~/Documents/소프트웨어마에스트로/KimOhOh/` 의 예상 플레이 화면 5장. 이 중 **1번과 3번이 같은 차고의 더러운 상태와 깨끗한 상태**이고, 5번은 62% 청소된 중간 상태다.

`docs/INDEX.md`가 경고한 대로 이 이미지들은 콘셉트 목업이다. HUD·레이아웃·구도는 스펙으로 쓰지 않고, **먼지 룩에 대해서만** 읽었다.

| 관찰 | 설계 반영 |
|---|---|
| 더러운 면이 **불투명**하다 — 바닥 타일 무늬가 완전히 가려진다 | 먼지는 반투명 베일이 아니라 덮어쓰기 |
| 깨끗한 면은 **강한 반사·스페큘러**, 더러운 면은 **완전 무광** | 광택이 전후 대비의 최대 레버 (기획서 116줄) |
| 경계가 **크게 넘실대지만 폭은 좁다** | 노이즈로 녹이되 소프트니스 기본값은 작게 |
| 경계 안쪽 깨끗한 면에 **잔여 얼룩이 점점이** 남는다 | 노이즈 디졸브가 만드는 상태 |
| 갓 닦인 자리 가장자리에 **밝은 림** | 디졸브 경계 하이라이트 파라미터 |
| 더러운 면에 **알갱이 입체감** | 먼지 노멀 + 두께 변화 |
| 자갈·볼트·종이는 **별도 3D 프롭** | 셰이더 아님 → `Trash/` |
| 깨끗한 바닥이 채도 높은 민트/블루 타일 | 테스트 씬 바닥은 무늬가 읽히는 광택 타일로 |

스코프 판단이 레퍼런스로 검증됐다 — 5번 이미지의 "62% Cleaned" HUD와 노즐의 흰 먼지 퍼프 VFX는 둘 다 이번에 미룬 것들이다.

### 생성한 아트 타겟

원본 레퍼런스는 방 전체 샷이라 먼지를 근거리에서 볼 수 없다. 그래서 위 두 장을 조건으로 `gpt-image-1`(`/v1/images/edits`)에 근접 샷 3장을 생성했다. **이건 콘셉트 타겟이지 스펙이 아니다** — 셰이더가 맞춰야 할 목표 이미지로만 쓴다.

| 파일 | 무엇을 정하나 |
|---|---|
| `dust-target-boundary.png` | 경계가 어떻게 끝나는가 |
| `dust-target-grain.png` | 먼지 알갱이의 스케일과 질감 |
| `dust-target-stroke.png` | 닦인 자국의 가장자리 |

![경계](../images/dust-target-boundary.png)

![알갱이](../images/dust-target-grain.png)

![닦인 자국](../images/dust-target-stroke.png)

**타겟이 확인해준 것** — 5절 개정판의 항이 전부 실제로 보인다.

| 항 | 타겟에서 보이는 모습 |
|---|---|
| `cover` (노이즈 디졸브) | 경계가 직선이 아니라 너덜너덜하고, 그 너머로 알갱이가 흩뿌려져 있다 |
| `opacity` (`_ThinOpacity`) | 끝나기 전에 **먼지가 옅어지는 구간**이 분명히 있다. 구멍이 뚫리는 게 아니다 |
| `_DirtGrainNormal` | 잔 알갱이 + 그림자를 지닌 **입체 덩어리**. 매끈한 판이 절대 아니다 |
| `_EdgeRim` | 갓 드러난 타일 가장자리가 실제로 밝게 튄다 (`stroke` 우상단) |
| 광택 대비 | 타일의 스페큘러 하이라이트 vs 먼지의 완전 무광 |

**색 타겟**: 먼지는 따뜻한 황토색(대략 `#C08A50` 계열)으로, 내 프리뷰의 `#705040`보다 **밝고 따뜻하다.** 타일은 창백한 민트/오프화이트에 강한 반사.

**미결 A/B에 대한 시사** — 타겟의 먼지는 명백히 **알갱이 있는 물질**이지 막이 아니다. 8절 6번이 `Granular` 쪽으로 기울 가능성이 높다. 다만 타겟은 목표지 결과가 아니므로 비교 자체는 그대로 수행한다.

**이번 범위 밖 관찰**: 자국 가장자리의 덩어리가 field보다 크고 뭉쳐 있다 — 먼지가 밀려 둔덕이 된 모습이다. 이건 표면 셰이더가 아니라 페인팅 시점의 축적 효과이고, 우리는 지우기 전용을 택했으므로 이번에 안 한다. 다음 브랜치에서 재검토할 후보로만 남긴다.

---

## 5. 셰이더 설계

**`DustSurface`** — 손으로 쓴 `.shader` (HLSL), URP Lit 패스.

> **정정 (2026-08-11).** 초판은 Shader Graph를 골랐고 근거는 "HLSL로 쓰면 URP 라이팅을 다시 구현해야 한다"였다. **그 전제가 틀렸다.** URP 17.6은 `UniversalFragmentPBR(InputData, SurfaceData)`를 공개 API로 제공한다(`Library/PackageCache/com.unity.render-pipelines.universal@.../ShaderLibrary/Lighting.hlsl:302`). 구조체를 채워 넘기면 URP의 라이팅이 그대로 돈다 — 재구현이 아니다.

HLSL을 고른 실제 이유:

- **`.shadergraph`는 GUID가 박힌 JSON이라 Plastic이 병합하지 못한다.** 씬과 같은 문제를 셰이더에서 또 겪게 된다
- **Codex가 저작할 수 있다.** 그래프는 에디터에서 손으로 이어야 하고, 텍스트 파일은 위임된다
- 디프가 읽힌다 — 파라미터 하나 바뀐 걸 리뷰할 수 있다

포기하는 것: 그래프에서 노드를 끌어 값을 보는 반복. 다만 조정 대상은 대부분 머티리얼 프로퍼티라 인스펙터에서 그대로 만진다.

파일은 둘로 나눈다. 합성 로직은 `DustSurface.hlsl`에, URP 패스 보일러플레이트는 `DustSurface.shader`에. 다음 브랜치의 페인팅 패스가 같은 `.hlsl`을 재사용한다.

### 입력

| 프로퍼티 | 용도 |
|---|---|
| `_BaseMap` `_BaseColor` `_NormalMap` `_Smoothness` `_BaseMapTiling` | 깨끗한 바닥 재질 |
| `_DirtMask` | 텍셀별 먼지 양. 1 = 더러움 |
| `_DirtAmount` | 전역 세기 0–1. 테스트 씬에서 전후를 훑는 손잡이 |
| `_DirtColor` `_DirtSmoothness` | 먼지 재질 |
| `_DissolveNoise` `_DissolveNoiseTiling` | 잔가는 노이즈 — 걷히는 모양을 만든다 |
| `_DirtGrainNormal` `_GrainTiling` `_GrainStrength` | **먼지 알갱이 노멀맵.** 먼지가 입체로 읽히게 하는 유일한 수단 |
| `_ThinOpacity` `_FullDirtAt` | 옅어짐. `d`가 낮을 때 먼지색이 얼마나 옅어지는지, 어느 값부터 완전한 먼지색인지 |
| `_EdgeSoftness` | 디졸브 경계 폭 |
| `_EdgeRim` `_EdgeRimColor` | 갓 닦인 경계의 밝은 림 세기 |

### 합성

```
d       = SampleDirt(_DirtMask, uv) * _DirtAmount
n_f     = _DissolveNoise 샘플
grainN  = _DirtGrainNormal 샘플 (uv * _GrainTiling)

cover   = smoothstep(n_f - _EdgeSoftness, n_f + _EdgeSoftness, d)
opacity = lerp(_ThinOpacity, 1, saturate(d / _FullDirtAt))
dirtA   = lerp(cleanAlbedo, _DirtColor, opacity)
rim     = _EdgeRim * (1 - abs(cover * 2 - 1))

BaseColor  = lerp(cleanAlbedo, dirtA, cover) + _EdgeRimColor * rim
Smoothness = lerp(cleanSmooth, _DirtSmoothness, cover)
Normal     = BlendNormal(clean 노멀, grainN, cover * _GrainStrength)
```

**요점은 `d`를 알파로 쓰지 않고 노이즈와 비교한다는 것이다.** 그냥 알파로 쓰면 원이 투명해지며 사라지고, 노이즈와 비교하면 얼룩덜룩 걷힌다. 레퍼런스 5번의 잔여 점들이 이 식에서 나온다.

`d`가 `_FullDirtAt` 이상이면 `opacity`가 1이 되어 `_DirtColor`가 그대로 나오므로 레퍼런스의 불투명한 더러움이 재현된다. **`opacity`가 따로 있는 이유**는 아래 프리뷰 검증에 있다 — 커버리지만 줄이면 먼지가 "얇아지는" 게 아니라 "구멍이 뚫린다".

### 느낌을 만드는 레버 (우선순위)

1. **광택 대비** `_Smoothness` ↔ `_DirtSmoothness`. 깨끗한 면에 스페큘러가 살아 있고 더러운 면이 완전 무광일 때 대비가 가장 크다
2. **알갱이 노멀** `_DirtGrainNormal`. 먼지가 판이 아니라 물질로 읽히게 하는 유일한 수단
3. **노이즈 디졸브** — 유기적으로 걷히는 느낌
4. **경계 림** — 갓 닦인 자리가 반짝하는 juice

### 프리뷰 검증 (2026-08-11)

셰이더를 짜기 전에 위 공식을 순수 파이썬으로 구현해 렌더했다. 생성기와 결과는 `docs/images/`에 있고 파라미터를 바꿔 재실행할 수 있다.

![디졸브 스윕](../images/dust-dissolve-sweep.png)

![닦인 자국](../images/dust-wiped-path.png)

**살아남은 것** — 디졸브 수식. `d`를 알파가 아니라 노이즈와 비교하니 실제로 유기적인 덩어리로 걷힌다. 원이 투명해지는 현상이 없다.

**죽은 것** — 초판의 A/B 축. `_ThicknessRange`가 노이즈로 **밝기만** 흔들게 돼 있었는데, `dust-wiped-path.png`에서 두 프리셋이 같은 그림에 블러만 다른 상태로 나온다. 밝기 변조는 어떤 조명에서도 깊이가 되지 않는다. 죽은 것은 "두께"라는 개념이 아니라 그것을 밝기로 표현하려 한 파라미터 구성이다. 그래서 `_ThicknessNoise` / `_ThicknessRange`를 빼고 **실제 노멀맵 `_DirtGrainNormal`**로 교체했다.

**드러난 것** — 중간 세기에서 먼지가 "얇아진다"가 아니라 "판에 구멍이 뚫린다"로 읽힌다. 커버리지 하나로는 옅음을 표현할 수 없어서 `_ThinOpacity` / `_FullDirtAt`을 추가했다.

**이 프리뷰의 한계** — 순수 파이썬이라 진짜 노멀맵도, PBR 스페큘러도, 그림자도 없다. 판정에서 제외해야 하는 것: 대각선 스페큘러 띠(가짜 조명 아티팩트), 먼지색 `#705040`(레퍼런스보다 어둡고 차다), 알갱이 질감의 부재. 이 셋은 프리뷰의 한계지 설계의 결론이 아니다.

### 텍스처 조달

`_DirtGrainNormal`은 절차적 노이즈로 만들지 않는다. 프리뷰가 보여준 대로 **먼지가 물질로 읽히려면 실제 흙 재질의 그레인이 필요**하고, 그건 노이즈 함수로 근사되지 않는다.

| 맵 | 조달 |
|---|---|
| `_DirtGrainNormal` (+ 알베도·러프니스 참고) | **CC0 PBR 흙 머티리얼**을 받아 쓴다. Poly Haven / ambientCG는 CC0라 상업 이용에 제약이 없다 |
| `_DissolveNoise` | 자체 생성. `docs/images/generate_dust_preview.py`의 밸류 노이즈를 타일링 PNG로 뽑으면 된다 |

**라이선스는 CC0만 쓴다.** textures.com 등 재배포 제한이 있는 소스는 받지 않는다. 받은 것의 출처와 라이선스는 `Dust/Textures/AGENTS.md`에 기록한다.

Feel의 `MMTools/.../MMNoise/`에 노이즈 텍스처가 있지만 **벤더 에셋이라 의존하지 않는다.** 재임포트로 사라질 수 있고 루트 `AGENTS.md`가 벤더 폴더 의존을 금한다.

`Adobe Substance 3D Sampler`가 설치돼 있다(Painter·Designer 아님). 사진에서 PBR 맵을 뽑는 용도로는 맞지만 GUI 전용이라 자동화할 수 없다. CC0 머티리얼로 부족하면 그때 수동으로 쓴다.

### 미결 — 구현 후 실물로 판단한다

먼지가 **알갱이가 보이는 물질**로 읽혀야 하는지, **매끈하게 깔린 막**으로 읽혀야 하는지 아직 모른다. 레퍼런스는 근거리에서는 알갱이, 원거리에서는 막으로 보인다.

프리뷰에서 이미 한 번 축을 잘못 잡았으므로(위 프리뷰 검증), 이번 축은 **실제로 시각적으로 갈리는 것**으로 잡는다 — 노멀맵 그레인의 유무다.

- `M_Dust_Granular` — `_GrainStrength` 높게, `_GrainTiling` 촘촘하게, `_EdgeSoftness` 작게
- `M_Dust_Film` — `_GrainStrength` 0에 가깝게, `_EdgeSoftness` 크게, `_ThinOpacity` 낮게

결론이 나면 `Dust/AGENTS.md`에 기록하고 진 쪽 파라미터는 기본값으로 눕힌다.

이건 "안 쓸 설정을 미리 만드는 것"(루트 `AGENTS.md` §2)이 아니다. **성공 기준 자체가 미지수**라서 비교 장치가 산출물의 일부다.

---

## 6. 테스트 씬

`Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`

- **바닥을 반으로 갈라 같은 마스크를 물린다.** 왼쪽 `M_Dust_Granular`, 오른쪽 `M_Dust_Film`. 같은 조명·같은 카메라에서 나란히 봐야 5절의 판단이 선다
- 바닥 재질은 **채도 높은 광택 타일**. 무늬가 읽혀야 덮였을 때 대비가 산다
- 조명은 Directional 1개 + 스카이박스 반사. **깨끗한 면에 스페큘러 스윕이 보이는 각도**로 맞춘다. 레퍼런스에서 대비의 절반이 반사다
- 카메라는 3인칭 높이·각도. 먼지는 그 거리에서 읽혀야 한다
- `_DirtAmount` 0→1 슬라이더

마스크 텍스처 4장 (`Dust/Textures/`):

| | 무엇을 검증하나 |
|---|---|
| 균일 | 전역 세기와 디졸브 성격 |
| 얼룩덜룩 | 자연스러운 분포 |
| **닦인 길** | 지나간 자국의 경계가 어떻게 읽히는가 |
| 구석 뭉침 | 진한 곳의 알갱이 그레인 |

테스트 중 에디터 상태 복원은 루트 `AGENTS.md` §5를 따른다.

---

## 7. 문서 변경

| 파일 | 변경 |
|---|---|
| `InGame/Dust/AGENTS.md` | 통합 결정 + 기각 대안 + 마스크 규약 |
| `InGame/Dust/Textures/AGENTS.md` | 외부 텍스처의 출처·라이선스 기록 (CC0만 허용) |
| `InGame/Stain/` | 폴더째 `cm remove` |
| `InGame/AGENTS.md` | 폴더 맵에서 Stain 줄 제거 |
| `InGame/Map/AGENTS.md` | UV0 유니크 언랩 규칙 |
| `docs/Glossary.md` | 얼룩 항목 정리, "기획서와 코드가 갈리는 지점" 1번 해소 |
| `docs/INDEX.md` | 이 스펙 등록, 현재 상태 갱신 |
| 루트 `AGENTS.md` | 테스트 씬 규칙 신설 (§6, 기존 "Asset editing"을 §7로) |

### 루트 AGENTS.md에 추가할 절

기존 문서가 영어라 영어로 쓴다.

> ## 6. Test scenes
>
> A verification scene lives in a `Tests/` folder inside the feature that owns it, and it gets checked in — `Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`.
>
> - Scene files are YAML and **cannot be merged**. When two branches touch the same scene, one side has to be thrown away whole. One scene per feature, with branches split per feature, means that never comes up.
> - Name it `<Feature>_<WhatItVerifies>_Test.unity`.
> - **Never add a test scene to Build Settings.** That list is one file, `ProjectSettings/EditorBuildSettings.asset`, and every branch edits the same lines in it. Open test scenes from the Project window instead.
> - A test scene owns the objects inside it. Do not modify a production prefab to make a test work.
> - When a feature goes away its test scene goes with it. Delete the folder with `cm remove`.

---

## 8. 검증 기준

1. **3인칭 거리에서 `_DirtAmount` 0과 1의 스크린샷이 한눈에 다르다** ← 가장 중요
2. 깨끗한 면에 스페큘러·반사가 실제로 보인다
3. 0→1로 훑을 때 원형으로 투명해지지 않고 얼룩덜룩 걷힌다
4. 닦인 길 마스크에서 자국 경계가 레퍼런스처럼 읽힌다
5. 근거리에서 **먼지가 판이 아니라 알갱이 있는 물질로** 읽힌다 (프리뷰가 실패한 지점)
6. **알갱이형 / 막형 중 어느 쪽인지 결론이 난다**
7. URP에서 마젠타 없음, 콘솔 에러 0

6번의 결론은 `Dust/AGENTS.md`에 결정으로 기록한다.

---

## 9. 다음 브랜치로 넘기는 것

1. **페인팅** — 메시를 RT에 UV 공간으로 렌더(버텍스 셰이더가 UV를 클립 좌표로 출력)하고 프래그먼트에서 월드 거리로 브러시를 판정한다. 레이캐스트의 `textureCoord`를 UV로 쓰는 방식보다 나은 이유: 브러시 크기가 UV 왜곡과 무관하게 월드 기준으로 일정하고, UV 이음새가 자연스럽게 처리되며, Read/Write MeshCollider가 필요 없다. 이음새 필터링 문제는 페인팅 후 딜레이션 패스로 처리한다
2. **청소 VFX** — 먼지 퍼프, 흡입 궤적
3. **청소도 집계** (`Cleanliness/`) — 마스크를 다운샘플 체인으로 줄여 `AsyncGPUReadback`으로 읽는다. 전체 RT를 그대로 읽으면 비싸다

---

## 10. 미해결

- **알갱이형 vs 막형** — 8절 6번으로 결론
- **흙 머티리얼 선정** — CC0 후보를 받아 근거리에서 비교한 뒤 하나로 고정. 출처·라이선스는 `Dust/Textures/AGENTS.md`에 기록
- **라이트맵 UV 충돌** — 라이트맵을 실제로 켤 때 UV0 결정 재확인
- **텍셀 밀도** — 바닥이 넓어지면 마스크 해상도가 부족해진다. 타일 분할이 필요해지는 시점은 실제 맵 크기가 정해질 때 판단

# 먼지 청소 VFX 구현 계획

> **에이전트용:** 태스크 단위로 실행한다. 스텝은 `- [ ]` 체크박스다.
> **스펙:** `docs/specs/2026-08-11-dust-clean-vfx.md` — 결정의 근거는 전부 거기 있다.

**목표:** 닦는 동안 "지금 빨아들이고 있다"가 보이게 만든다. 붓을 사각 패드로 바꾸고, 실제로
지워진 양을 GPU 텍스처로 남겨 파티클이 그것을 직접 읽는다.

**아키텍처:** 붓질 한 번이 드로우콜 **두 개**가 된다. 먼저 메시를 **패드 공간**에 렌더해
`(월드 좌표, 실제로 지워질 양)`을 RT 에 적고, 그다음 기존대로 **UV 공간**에 렌더해 마스크에서
뺀다. VFX Graph 가 첫 번째 RT 를 포지션 맵으로 읽어 파티클을 뿌린다. CPU 는 이 경로에 개입하지
않는다 — 그래서 지연이 0 이고, 붓의 너덜너덜한 노이즈 모양을 파티클이 그대로 따라간다.

**기술 스택:** Unity 6000.6.0b7 · URP 17.6.0 · Visual Effect Graph 17.6.0 · HLSL · Plastic SCM ·
Unity CLI (`~/.unity/bin/unity`)

## 전역 제약

- **브랜치**: `/main/dust-clean-vfx` (cs:47 부터). `/main` 에서 작업하지 않는다
- **버전 관리는 Plastic 이다. git 명령을 쓰지 않는다.** 에디터 밖에서 만든 파일은 `Private` 로
  뜨므로 `cm add -R` 후 체크인하고, 체크인 뒤 `cm status` 로 남은 `Changed` 를 명시 경로로 다시 넣는다
- **`.unity` / `.prefab` / `.asset` / `.mat` / `.meta` / `.vfx` YAML 을 손으로 편집하지 않는다**
- **에셋 이동·삭제는 Unity 를 통해서만.** Finder·쉘 금지 (`delete_asset` / `move_asset`)
- **Unity CLI 인자 이름을 추측하지 않는다.** 인자 없이 `$U cmd --project-path "$P"` 로 전체 목록을
  받아 확인한다. 틀린 인자는 400 으로 돌아온다
- **`--project-path` 를 항상 붙인다.** 이 머신에 에디터가 둘 떠 있다 (7800 = 이 워크스페이스,
  7801 = branchB). 빼면 엉뚱한 에디터에 간다
- 네임스페이스는 평평한 `PPack` 하나. private 필드 `_camelCase`, 열거형만 `E` 접두사
- 직렬화된 Unity Object 필드는 `== null` / `!= null` (fake-null 때문에 `is null` 금지)
- 마스크 규약: **UV0 · R 1채널 · 1 = 더러움 · 지우기 전용**
- **`strength` 하한은 0.002.** 마스크가 `R8` 이라 매 스탬프 `round(strength × 255)` 단계씩 빠지고,
  그 아래는 반올림이 0 이라 영원히 아무 일도 일어나지 않는다 (스펙 §3 실측)
- **Fusion 은 설치돼 있지 않고 이번에 네트워크 코드를 쓰지 않는다.** 대신 두 줄을 지킨다 —
  VFX 는 "내 입력"이 아니라 "마스크에 실제로 적용된 붓질"에서 나오고, 붓질은 프레임당 한 번만
  적용한다 (스펙 §6)
- **테스트 전 에디터 상태를 기록하고 끝나면 복원한다** (루트 `AGENTS.md` §5)

**분담**: `.hlsl` · `.shader` · `.cs` 는 텍스트라 **Codex 위임 가능**(`codex-orchestrate`).
`.vfx` 그래프 · `.prefab` · `.unity` · 검증 스크린샷 · Plastic 은 **직접**. 셰이더 브랜치와 같은
경계지만 **`.vfx` 가 위임 불가 쪽에 새로 들어왔다** — 그래프는 에디터에서만 저작된다.

---

## 파일 구조

```
Assets/Game/InGame/Dust/
  Shaders/
    DustBrush.hlsl          신규 — 사각 브러시 판정 + 노이즈. 두 셰이더가 공유
    DustPaint.shader        수정 — 구 → 방향 있는 사각형
    DustErased.shader       신규 — 패드 공간에 (월드좌표, 지운양) 기록
  Scripts/
    BrushPad.cs             신규 — 패드 포즈 + 브러시 파라미터
    DustPaintTarget.cs      수정 — Paint(in BrushPad), CaptureErased(...)
    DustMousePainter.cs     수정 — 패드 구성, VFX 호출
    DustCleanVfx.cs         신규 — 지운양 RT 소유, VisualEffect 구동
  VFX/
    VFX_DustPuff.vfx        신규 — 직접 저작
    VFX_DustPush.vfx        신규 — 직접 저작
    VFX_CleanSparkle.vfx    신규 — 직접 저작
    PS_DustPuff.prefab      신규 — 내장 파티클 비교판
  Materials/
    M_Dust_Film.mat         삭제
  Tests/
    Dust_Look_Test.unity    수정 — 바닥 통일, VFX 배치
  AGENTS.md                 수정 — 브러시 모양·지운양 RT·A/B 결론·8비트 정정
```

**검증 방식**: 이 브랜치의 산출물은 셰이더와 파티클이라 단위 테스트가 성립하지 않는다. 셰이더
브랜치와 같이 **스크린샷과 CLI 조회로 판정**하고, 각 태스크는 "무엇을 보면 통과인가"를 명시한다.

---

## Task 1: 무대 정리 — `M_Dust_Film` 제거

VFX 를 판정할 무대가 `Dust_Look_Test.unity` 인데 바닥 절반이 **기각된 프리셋**으로 깔려 있다.
퍼프가 먼지와 어떻게 어울리는지 보는 데 노이즈가 되므로 먼저 치운다. 나머지 작업과 독립이다.

**Files:**
- Modify: `Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`
- Delete: `Assets/Game/InGame/Dust/Materials/M_Dust_Film.mat`
- Modify: `Assets/Game/InGame/Dust/AGENTS.md`

- [ ] **Step 1: 작업 전 에디터 상태 기록**

```bash
U=~/.unity/bin/unity; P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 5
```

열려 있는 씬과 dirty 여부를 적어둔다. dirty 인 씬이 있으면 **건드리지 말고 사용자에게 묻는다**
(루트 `AGENTS.md` §5).

- [ ] **Step 2: 테스트 씬을 열고 `M_Dust_Film` 을 쓰는 렌더러 찾기**

```bash
$U cmd --project-path "$P" --no-banner open_scene --path Game/InGame/Dust/Tests/Dust_Look_Test.unity
$U cmd --project-path "$P" --no-banner find_gameobjects --type MeshRenderer --format json
```

각 렌더러의 머티리얼을 조회해 `M_Dust_Film` 을 물고 있는 것을 특정한다.

- [ ] **Step 3: 해당 렌더러를 `M_Dust_Granular` 로 교체**

```bash
$U cmd --project-path "$P" --no-banner set_component_properties \
  --target "<하이어라키 경로>" --type MeshRenderer \
  --properties '{"m_Materials":["Assets/Game/InGame/Dust/Materials/M_Dust_Granular.mat"]}'
```

인자 형식은 전체 목록으로 먼저 확인한다. 씬을 저장한다.

- [ ] **Step 4: 참조가 사라졌는지 확인한 뒤 삭제**

```bash
FILM=$(grep -m1 "guid:" Assets/Game/InGame/Dust/Materials/M_Dust_Film.mat.meta | awk '{print $2}')
grep -rl "$FILM" Assets/ ProjectSettings/ | grep -v "M_Dust_Film"
```

기대: **출력 없음.** 남아 있으면 Step 3 으로 돌아간다. 없으면:

```bash
$U cmd --project-path "$P" --no-banner delete_asset \
  --asset "Assets/Game/InGame/Dust/Materials/M_Dust_Film.mat" --confirm true
```

- [ ] **Step 5: `Dust/AGENTS.md` 정리**

"Granular over film" 결정문의 마지막 두 줄을 지운다:

```
`M_Dust_Film` stays in the repository as the comparison that settled this. Delete it once nothing
references it.
```

대신 결정문 끝에 한 줄을 붙인다:

```
`M_Dust_Film` was deleted on 2026-08-11 once the test scene stopped referencing it. The evidence
that settled this lives in `docs/images/verify/`, not in the material.
```

- [ ] **Step 6: 8비트 정밀도 서술 정정**

`Dust/AGENTS.md` 의 "Mask precision is 8-bit" 문단 전체를 아래로 교체한다. 기존 서술은 방향이
반대다 — 실측 결과는 스펙 §3 에 있다.

```markdown
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
float maths exactly, which locates the error in storage rather than in the shader.
```

- [ ] **Step 7: 확인**

```bash
test ! -f Assets/Game/InGame/Dust/Materials/M_Dust_Film.mat && echo "삭제됨"
grep -c "M_Dust_Film" Assets/Game/InGame/Dust/AGENTS.md
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 5
```

기대: 파일 없음 / 언급 1회(결정문의 회고) / 에러 0.

- [ ] **Step 8: 체크인**

```bash
cm status
cm ci Assets/Game/InGame/Dust --commentsfile=<msg>
cm status   # Changed 남았으면 명시 경로로 재체크인
```

---

## Task 2: VFX Graph 가정 검증 ← **여기서 실패하면 설계를 바꾼다**

스펙 §5 가 남긴 유일한 미확인 가정이다. **텍스처를 포지션 맵으로 읽고 임계값으로 파티클을 죽이는
패턴이 URP 17.6 / Unity 6000.6.0b7 에서 실제로 도는가.** 이게 안 되면 §3 의 GPU 경로 전체가
성립하지 않으므로 구현을 더 진행하기 전에 답을 낸다.

진짜 지운양 RT 는 아직 없다. **손으로 만든 가짜 맵**으로 충분하다 — 검증 대상은 데이터가 아니라
VFX Graph 의 능력이다.

**Files:**
- Create: `Assets/Game/InGame/Dust/VFX/VFX_DustPuff.vfx` (이 태스크에서 뼈대만)
- Create: 임시 검증 스크립트 (검증 후 삭제)

**Interfaces:**
- Produces: 그래프가 읽을 **노출 프로퍼티 이름 두 개** — Task 4 의 C# 이 이 이름으로 바인딩한다
  - `_ErasedMap` : `Texture2D`
  - `_ErasedThreshold` : `float`

- [ ] **Step 1: 가짜 맵을 만드는 임시 컴포넌트**

`Assets/__TEST__VfxProbe/VfxProbe.cs` (임시. 검증 후 폴더째 삭제):

```csharp
using UnityEngine;
using UnityEngine.VFX;

namespace PPackTest
{
    /// __TEST__ 임시. VFX Graph 가 텍스처를 포지션 맵으로 읽는지 확인만 한다.
    public sealed class VfxProbe : MonoBehaviour
    {
        [SerializeField] private VisualEffect _vfx;
        [SerializeField] private float _extent = 1f;

        private RenderTexture _map;

        private void Start()
        {
            const int Size = 64;
            _map = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat,
                                     RenderTextureReadWrite.Linear) { enableRandomWrite = false };
            _map.Create();

            // xyz = 월드 좌표, w = 지운 양. UV 공간의 중앙 원 안만 w = 1.
            Texture2D src = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
                Vector3 world = transform.position
                              + new Vector3((u - 0.5f) * 2f * _extent, 0f, (v - 0.5f) * 2f * _extent);
                float inside = new Vector2(u - 0.5f, v - 0.5f).magnitude < 0.3f ? 1f : 0f;
                src.SetPixel(x, y, new Color(world.x, world.y, world.z, inside));
            }
            src.Apply();
            Graphics.Blit(src, _map);
            Destroy(src);

            _vfx.SetTexture("_ErasedMap", _map);
            _vfx.SetFloat("_ErasedThreshold", 0.5f);
        }

        private void OnDestroy()
        {
            if (_map != null) { _map.Release(); Destroy(_map); }
        }
    }
}
```

- [ ] **Step 2: 컴파일**

```bash
$U cmd --project-path "$P" --no-banner recompile
# completed 될 때까지 recompile_status 폴링
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 10
```
기대: 에러 0.

- [ ] **Step 3: 그래프 저작 (직접, 에디터에서)**

`Assets/Game/InGame/Dust/VFX/VFX_DustPuff.vfx` 를 만들고 이 구조로 짠다:

| | |
|---|---|
| 노출 프로퍼티 | `_ErasedMap` (Texture2D), `_ErasedThreshold` (float) |
| Spawn | 고정 레이트, 우선 2000/s |
| Initialize | 파티클마다 **랜덤 UV** 로 `_ErasedMap` 을 샘플 → `rgb` 를 위치로, `a` 를 임계값 판정에 |
| 죽이기 | `a < _ErasedThreshold` 이면 이 파티클은 살지 않는다 |

**죽이는 방법을 두 가지 중 되는 쪽으로 잡는다.** 어느 쪽이 되는지가 이 태스크의 답이다:

1. Initialize 에서 `lifetime` 을 0 으로 — 가장 단순하고 버전 의존이 적다
2. Update 에서 `Set Alive` 를 false 로 — 의도가 명확하지만 블록 이름·동작이 버전마다 다르다

위치는 **월드 공간**이어야 한다. 시스템의 좌표계 설정이 Local 이면 샘플한 월드 좌표가 두 번
변환되어 엉뚱한 데 뜬다 — 여기서 틀리기 쉽다.

- [ ] **Step 4: 붙여서 돌리기**

테스트 씬에 빈 GameObject 를 만들어 `VisualEffect`(에셋 = `VFX_DustPuff`)와 `VfxProbe` 를 붙이고
플레이한다.

```bash
$U cmd --project-path "$P" --no-banner editor_play
$U cmd --project-path "$P" --no-banner capture_game_view --output /tmp/vfx_probe/circle.png
$U cmd --project-path "$P" --no-banner editor_stop
```

- [ ] **Step 5: 판정**

| 통과 | 실패 |
|---|---|
| 파티클이 **원 모양으로만** 뜬다. 원 밖에는 하나도 없다 | 전 영역에 고르게 뜬다 → 임계값 죽이기가 안 먹는다 |
| 원이 프로브 오브젝트 위치를 따라간다 | 원점이나 엉뚱한 곳에 뜬다 → 좌표계가 Local 이다 |

**실패하면 여기서 멈추고 사용자에게 보고한다.** Task 3 이후는 이 가정 위에 서 있다. 대안은
스펙 §3 표의 나머지 셋(AsyncGPUReadback / CPU 미러 / 월드 그리드)이고, 선택은 사용자 몫이다.

- [ ] **Step 6: 임시물 정리 (루트 `AGENTS.md` §5)**

```bash
rm -rf Assets/__TEST__VfxProbe
cm status --private   # 자동 add 로 잡혔으면 cm undo 로 해제
```

테스트 씬에 만든 프로브 오브젝트를 지우고, 씬이 dirty 로 남지 않게 한다. `/tmp/vfx_probe/` 중
문서에 안 쓸 것은 지운다.

- [ ] **Step 7: 결과를 스펙에 반영**

되든 안 되든 스펙 §10 의 "VFX Graph 의 텍스처 기반 파티클 배치가 실제로 되는가"를 결과로
바꾸고, **어느 죽이기 방법이 통했는지**를 적는다. 다음 두 그래프가 같은 방법을 쓴다.

- [ ] **Step 8: 체크인**

---

## Task 3: 붓을 방향 있는 직사각형으로

**Files:**
- Create: `Assets/Game/InGame/Dust/Shaders/DustBrush.hlsl`
- Modify: `Assets/Game/InGame/Dust/Shaders/DustPaint.shader`
- Create: `Assets/Game/InGame/Dust/Scripts/BrushPad.cs`
- Modify: `Assets/Game/InGame/Dust/Scripts/DustPaintTarget.cs`
- Modify: `Assets/Game/InGame/Dust/Scripts/DustMousePainter.cs`

**Interfaces:**
- Produces: `struct BrushPad` — Task 4 의 `CaptureErased` 가 같은 값을 받는다
- Produces: `DustPaintTarget.Paint(in BrushPad pad)` — 반환 없음. 지운 양은 Task 4 의 RT 로 나온다
- Produces: 셰이더 프로퍼티 `_BrushWorldToPad`(float4x4) `_BrushHalfExtents`(vector)
  `_BrushThickness` `_BrushFeather` `_BrushStrength` `_BrushNoiseAmount` `_BrushNoiseScale`

- [ ] **Step 1: `DustBrush.hlsl` — 두 셰이더가 공유할 브러시 판정**

기존 `DustPaint.shader` 안의 노이즈 함수를 여기로 옮기고 사각 판정을 추가한다. Task 4 의
`DustErased.shader` 가 **같은 파일을 include** 해서, 지우는 양과 기록하는 양이 갈라지지 않는다.

```hlsl
#ifndef PPACK_DUST_BRUSH_INCLUDED
#define PPACK_DUST_BRUSH_INCLUDED

// 두 셰이더가 같은 레이아웃으로 선언해야 하므로 CBUFFER 를 여기 둔다.
CBUFFER_START(UnityPerMaterial)
    float4x4 _BrushWorldToPad;
    float4   _BrushHalfExtents;   // xy = 패드 로컬 XZ 반쪽 크기
    float    _BrushThickness;     // 패드 로컬 Y 허용 범위
    float    _BrushFeather;       // 경계 폭 (월드 단위)
    float    _BrushStrength;
    float    _BrushNoiseAmount;
    float    _BrushNoiseScale;
CBUFFER_END

float BrushHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float BrushValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = BrushHash(i);
    float b = BrushHash(i + float2(1.0, 0.0));
    float c = BrushHash(i + float2(0.0, 1.0));
    float d = BrushHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// 두 옥타브 — 큰 얼룩과 잔 결이 같이 있어야 붓질로 읽힌다.
float BrushFbm(float2 p)
{
    return BrushValueNoise(p) * 0.65 + BrushValueNoise(p * 2.7 + 13.1) * 0.35;
}

float3 BrushToPadLocal(float3 positionWS)
{
    return mul(_BrushWorldToPad, float4(positionWS, 1.0)).xyz;
}

// 이 텍셀에서 지울 양. 패드 밖이거나 두께 밖이면 0.
// 노이즈는 **월드 좌표**로 뽑는다 — UV 로 뽑으면 무늬가 붓을 따라 움직여서
// 얼룩이 바닥이 아니라 도구에 붙어 있는 것처럼 보인다.
float BrushAmount(float3 positionWS, float3 padLocal)
{
    // 사각 프리즘은 무한히 길다. 이걸 안 자르면 바닥을 닦을 때 아래층 천장까지 지워진다.
    if (abs(padLocal.y) > _BrushThickness) return 0.0;

    float2 noiseUV = positionWS.xz * _BrushNoiseScale;
    float edgeNoise = BrushFbm(noiseUV);
    float patchNoise = BrushFbm(noiseUV * 1.9 + 41.0);

    // 2D 박스 SDF — 음수면 패드 안.
    float2 q = abs(padLocal.xz) - _BrushHalfExtents.xy;
    float outside = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);

    // 외곽선을 흔든다.
    outside += (edgeNoise - 0.5) * _BrushFeather * _BrushNoiseAmount * 2.0;

    float falloff = 1.0 - smoothstep(-_BrushFeather, 0.0, outside);

    // 안쪽도 균일하지 않게 — 한 번 지나가면 얼룩이 남고, 겹쳐 문지르면 고르게 지워진다.
    float patch = lerp(1.0 - _BrushNoiseAmount, 1.0, patchNoise);

    return falloff * _BrushStrength * patch;
}
#endif
```

- [ ] **Step 2: `DustPaint.shader` 를 공유 헤더 쓰도록 수정**

`Properties` 블록을 교체한다:

```
_BrushHalfExtents("Brush Half Extents (XZ)", Vector) = (0.5, 0.15, 0, 0)
_BrushThickness("Brush Thickness", Float) = 0.25
_BrushFeather("Brush Feather", Float) = 0.06
_BrushStrength("Brush Strength", Range(0.002, 1)) = 0.35
_BrushNoiseAmount("Brush Unevenness", Range(0, 1)) = 0.55
_BrushNoiseScale("Brush Unevenness Scale", Float) = 6
```

`_BrushWorldToPad` 는 행렬이라 `Properties` 에 못 넣는다. C# 에서 `SetMatrix` 로만 넣는다.
`_BrushStrength` 의 하한이 **0.002** 인 것에 주의 — 그 아래는 8비트 반올림이 0 이라 무효다.

`HLSLPROGRAM` 안의 노이즈 함수와 `CBUFFER` 를 지우고 include 로 바꾼다:

```hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "DustBrush.hlsl"
```

`Varyings` 에 패드 로컬을 추가하고 프래그먼트를 단순화한다. 블렌드(`BlendOp RevSub`, `Blend One One`)와
UV → 클립 변환은 **그대로 둔다** — 마스크는 여전히 UV 공간에 그린다.

```hlsl
struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 padLocal   : TEXCOORD1;
};

Varyings BrushVertex(Attributes input)
{
    Varyings output;
    float2 clipXY = input.uv * 2.0 - 1.0;
    clipXY.y *= _ProjectionParams.x;
    output.positionCS = float4(clipXY, 0.0, 1.0);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.padLocal   = BrushToPadLocal(output.positionWS);
    return output;
}

half4 BrushFragment(Varyings input) : SV_Target
{
    return half4(BrushAmount(input.positionWS, input.padLocal), 0, 0, 0);
}
```

- [ ] **Step 3: `BrushPad.cs`**

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 표면에 붙은 사각 청소 패드 한 번의 붓질. 마스크를 지우는 패스와 지운 양을 기록하는 패스가
    /// 같은 값을 받아야 둘이 갈라지지 않으므로 구조체로 묶는다.
    ///
    /// 도구(<c>InGame/Vacuum</c>)가 생기면 자기 트랜스폼으로 이걸 채워 넘긴다.
    /// </summary>
    public readonly struct BrushPad
    {
        public readonly Matrix4x4 WorldToPad;
        public readonly Vector2 HalfExtents;
        public readonly float Thickness;
        public readonly float Feather;
        public readonly float Strength;
        public readonly float Unevenness;
        public readonly float UnevennessScale;

        public BrushPad(Vector3 position, Quaternion rotation, Vector2 halfExtents,
                        float thickness, float feather, float strength,
                        float unevenness, float unevennessScale)
        {
            WorldToPad = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
            HalfExtents = halfExtents;
            Thickness = thickness;
            Feather = feather;
            Strength = strength;
            Unevenness = unevenness;
            UnevennessScale = unevennessScale;
        }
    }
}
```

- [ ] **Step 4: `DustPaintTarget` 수정**

프로퍼티 ID 를 교체하고 `Paint` 시그니처를 바꾼다. `_BrushPosition` / `_BrushRadius` /
`_BrushHardness` 는 사라진다.

```csharp
private static readonly int BrushWorldToPadId = Shader.PropertyToID("_BrushWorldToPad");
private static readonly int BrushHalfExtentsId = Shader.PropertyToID("_BrushHalfExtents");
private static readonly int BrushThicknessId = Shader.PropertyToID("_BrushThickness");
private static readonly int BrushFeatherId = Shader.PropertyToID("_BrushFeather");
private static readonly int BrushStrengthId = Shader.PropertyToID("_BrushStrength");
private static readonly int BrushNoiseAmountId = Shader.PropertyToID("_BrushNoiseAmount");
private static readonly int BrushNoiseScaleId = Shader.PropertyToID("_BrushNoiseScale");

/// <summary>패드가 덮은 자리의 먼지를 지운다.</summary>
public void Paint(in BrushPad pad)
{
    if (_mask == null || _mesh == null) return;

    ApplyBrush(_paintMaterial, pad);

    _command.Clear();
    _command.SetRenderTarget(_mask);
    // DrawMesh 가 오브젝트→월드 행렬을 세팅해줘야 셰이더가 월드 위치를 계산할 수 있다.
    _command.DrawMesh(_mesh, transform.localToWorldMatrix, _paintMaterial, 0, 0);
    Graphics.ExecuteCommandBuffer(_command);
}

private static void ApplyBrush(Material material, in BrushPad pad)
{
    material.SetMatrix(BrushWorldToPadId, pad.WorldToPad);
    material.SetVector(BrushHalfExtentsId, new Vector4(pad.HalfExtents.x, pad.HalfExtents.y, 0f, 0f));
    material.SetFloat(BrushThicknessId, pad.Thickness);
    material.SetFloat(BrushFeatherId, pad.Feather);
    material.SetFloat(BrushStrengthId, pad.Strength);
    material.SetFloat(BrushNoiseAmountId, pad.Unevenness);
    material.SetFloat(BrushNoiseScaleId, pad.UnevennessScale);
}
```

- [ ] **Step 5: `DustMousePainter` 가 패드를 만들도록 수정**

`_radius` / `_hardness` 를 지우고 패드 크기와 두께를 넣는다. **회전이 새로 필요하다** — 표면
노멀이 위, 이동 방향이 앞이다.

```csharp
[Header("Pad")]
[Tooltip("패드의 반쪽 크기. x = 좌우(폭), y = 진행 방향 길이.")]
[SerializeField] private Vector2 _halfExtents = new Vector2(0.5f, 0.15f);
[Tooltip("패드가 닿는 두께. 이게 없으면 사각 기둥이 무한히 뻗어 아래층까지 지운다.")]
[SerializeField, Min(0.01f)] private float _thickness = 0.25f;
[SerializeField, Min(0.001f)] private float _feather = 0.06f;
[Tooltip("0.002 아래는 8비트 마스크의 반올림이 0 이라 아무 일도 일어나지 않는다.")]
[SerializeField, Range(0.002f, 1f)] private float _strength = 0.35f;

private Vector3 _lastHitPoint;
private Quaternion _padRotation = Quaternion.identity;
private bool _hasLastHit;
```

`Update` 의 붓질 부분:

```csharp
// 진행 방향은 이전 프레임의 접촉점에서 온다. 거의 안 움직였으면 직전 회전을 유지한다 —
// 매 프레임 새로 구하면 정지 상태에서 패드가 덜덜 떨린다.
Vector3 travel = _hasLastHit ? hit.point - _lastHitPoint : Vector3.zero;
Vector3 forward = Vector3.ProjectOnPlane(travel, hit.normal);
if (forward.sqrMagnitude > 1e-6f)
    _padRotation = Quaternion.LookRotation(forward.normalized, hit.normal);
else if (!_hasLastHit)
    _padRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(_camera.transform.forward, hit.normal).normalized, hit.normal);

_lastHitPoint = hit.point;
_hasLastHit = true;

if (!mouse.leftButton.isPressed) return;

float unevenness = _unevenness * Mathf.Lerp(1f, 1f - _evenOutWithStrength, _strength);
BrushPad pad = new BrushPad(hit.point, _padRotation, _halfExtents,
                            _thickness, _feather, _strength, unevenness, _unevennessScale);

if (hit.collider.TryGetComponent(out DustPaintTarget paintTarget))
    paintTarget.Paint(pad);
```

브러시 인디케이터도 패드를 따라야 한다:

```csharp
_brushIndicator.SetPositionAndRotation(hit.point, _padRotation);
_brushIndicator.localScale = new Vector3(_halfExtents.x * 2f, 0.02f, _halfExtents.y * 2f);
```

- [ ] **Step 6: 컴파일 확인**

```bash
$U cmd --project-path "$P" --no-banner recompile
# completed 폴링
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 10
$U cmd --project-path "$P" --no-banner get_shader_properties --shader "PPack/DustPaint" --format json
```
기대: 에러 0. 프로퍼티에 `_BrushHalfExtents` `_BrushThickness` `_BrushFeather` 가 있고
`_BrushRadius` `_BrushHardness` 는 **없다**.

- [ ] **Step 7: 눈으로 확인**

테스트 씬에서 플레이하고 마우스로 문지른다.

```bash
$U cmd --project-path "$P" --no-banner editor_play
$U cmd --project-path "$P" --no-banner capture_game_view --output /tmp/dust_vfx/brush_rect.png
$U cmd --project-path "$P" --no-banner editor_stop
```

| 통과 | 실패 |
|---|---|
| 자국이 **띠 모양**이고 진행 방향으로 정렬된다 | 원이 나온다 → 행렬이 안 들어갔다 |
| 외곽선이 너덜너덜하다 | 매끈한 직사각형 → 노이즈가 죽었다 |
| 마우스를 멈추면 패드가 안 떤다 | 덜덜 떤다 → 이동 방향 판정 임계값 문제 |

**두께 판정을 반드시 따로 확인한다.** 바닥을 닦을 때 다른 렌더러가 지워지지 않아야 한다. 테스트
씬에 확인할 두 번째 면이 없으면 임시로 벽 하나를 세워 확인하고 지운다.

- [ ] **Step 8: 체크인**

---

## Task 4: 지운 양을 GPU 텍스처로 남긴다

**Files:**
- Create: `Assets/Game/InGame/Dust/Shaders/DustErased.shader`
- Create: `Assets/Game/InGame/Dust/Scripts/DustCleanVfx.cs`
- Modify: `Assets/Game/InGame/Dust/Scripts/DustPaintTarget.cs`
- Modify: `Assets/Game/InGame/Dust/Scripts/DustMousePainter.cs`

**Interfaces:**
- Consumes: Task 3 의 `BrushPad`, `DustBrush.hlsl`
- Produces: `DustCleanVfx.ErasedMap` (`RenderTexture`, RGBAFloat 64×64) — 그래프의 `_ErasedMap`
- Produces: `DustPaintTarget.CaptureErased(RenderTexture target, in BrushPad pad)`
- Produces: `DustCleanVfx.BeginFrame()` / `DustCleanVfx.Play(in BrushPad pad, Vector3 travelDirection)`

- [ ] **Step 1: `DustErased.shader`**

핵심은 버텍스가 **패드 로컬 XZ 를 클립 좌표로** 내보낸다는 것이다. 그러면 패드의 발자국이 RT
전체를 채워서 텍셀이 낭비되지 않고, RT 가 표면당이 아니라 **도구당** 하나가 된다.

```hlsl
Shader "PPack/DustErased"
{
    Properties
    {
        _BrushHalfExtents("Brush Half Extents (XZ)", Vector) = (0.5, 0.15, 0, 0)
        _BrushThickness("Brush Thickness", Float) = 0.25
        _BrushFeather("Brush Feather", Float) = 0.06
        _BrushStrength("Brush Strength", Range(0.002, 1)) = 0.35
        _BrushNoiseAmount("Brush Unevenness", Range(0, 1)) = 0.55
        _BrushNoiseScale("Brush Unevenness Scale", Float) = 6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "DustErased"
            Cull Off
            ZTest Always
            ZWrite Off
            // 한 프레임에 스탬프가 겹치면 마지막 것이 이긴다. 위치가 거의 같으므로 무해하다.
            Blend Off

            HLSLPROGRAM
            #pragma vertex ErasedVertex
            #pragma fragment ErasedFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DustBrush.hlsl"

            TEXTURE2D(_DirtMask);
            SAMPLER(sampler_DirtMask);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 padLocal   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            Varyings ErasedVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 padLocal = BrushToPadLocal(positionWS);

                // 패드 발자국을 -1..1 로 정규화해 RT 전체에 펼친다.
                float2 clipXY = padLocal.xz / max(_BrushHalfExtents.xy, 1e-4);
                clipXY.y *= _ProjectionParams.x;

                output.positionCS = float4(clipXY, 0.0, 1.0);
                output.positionWS = positionWS;
                output.padLocal   = padLocal;
                output.uv         = input.uv;
                return output;
            }

            float4 ErasedFragment(Varyings input) : SV_Target
            {
                // 남아 있던 양보다 많이 지울 수는 없다. 이 min 이 "이미 깨끗한 자리에서
                // 퍼프가 안 나는" 동작의 전부다.
                float previous = SAMPLE_TEXTURE2D(_DirtMask, sampler_DirtMask, input.uv).r;
                float erased = min(previous, BrushAmount(input.positionWS, input.padLocal));
                return float4(input.positionWS, erased);
            }
            ENDHLSL
        }
    }
}
```

- [ ] **Step 2: `DustPaintTarget.CaptureErased` 추가**

**반드시 `Paint` 보다 먼저 불려야 한다** — 빼기 전 마스크를 읽어야 "실제로 지워질 양"이 나온다.

```csharp
[SerializeField] private Shader _erasedShader;
private Material _erasedMaterial;

// Awake 안:
if (_erasedShader == null) _erasedShader = Shader.Find("PPack/DustErased");
_erasedMaterial = new Material(_erasedShader) { hideFlags = HideFlags.HideAndDontSave };

/// <summary>
/// 이 붓질로 실제로 지워질 양과 그 월드 좌표를 <paramref name="target"/> 에 기록한다.
/// <see cref="Paint"/> 보다 <b>먼저</b> 부른다 — 빼고 나면 지워진 양을 알 수 없다.
/// </summary>
public void CaptureErased(RenderTexture target, in BrushPad pad)
{
    if (target == null || _mask == null || _mesh == null) return;

    ApplyBrush(_erasedMaterial, pad);
    _erasedMaterial.SetTexture(DirtMaskId, _mask);

    _command.Clear();
    _command.SetRenderTarget(target);
    _command.DrawMesh(_mesh, transform.localToWorldMatrix, _erasedMaterial, 0, 0);
    Graphics.ExecuteCommandBuffer(_command);
}
```

`OnDestroy` 에 `if (_erasedMaterial != null) Destroy(_erasedMaterial);` 를 추가한다.

- [ ] **Step 3: `DustCleanVfx.cs`**

```csharp
using UnityEngine;
using UnityEngine.VFX;

namespace PPack
{
    /// <summary>
    /// 청소 VFX. 이번 프레임에 실제로 지워진 양과 그 월드 좌표를 담은 RT 를 소유하고,
    /// VFX Graph 가 그것을 포지션 맵으로 읽어 스스로 파티클을 뿌린다. CPU 는 개입하지 않는다.
    ///
    /// RT 는 <b>표면당이 아니라 도구당</b> 하나다. 패드 공간에 굽기 때문에 패드 아래 표면이
    /// 여럿이어도 모두 같은 RT 에 그려 넣는다.
    ///
    /// 붓질을 미는 쪽(지금은 <c>DustMousePainter</c>, 나중에 <c>InGame/Vacuum</c>)이 소유한다.
    /// 그래서 나중에 Fusion 이 와도 원격 플레이어의 도구가 자기 것을 갖는다.
    /// </summary>
    public sealed class DustCleanVfx : MonoBehaviour
    {
        [SerializeField] private VisualEffect _puff;
        [SerializeField] private VisualEffect _push;
        [SerializeField] private VisualEffect _sparkle;
        [SerializeField, Min(8)] private int _resolution = 64;
        [Tooltip("이 값보다 적게 지워진 텍셀에서는 파티클이 뜨지 않는다.")]
        [SerializeField, Range(0f, 1f)] private float _threshold = 0.02f;

        private static readonly int ErasedMapId = Shader.PropertyToID("_ErasedMap");
        private static readonly int ErasedThresholdId = Shader.PropertyToID("_ErasedThreshold");
        private static readonly int TravelId = Shader.PropertyToID("_Travel");

        private RenderTexture _erasedMap;

        public RenderTexture ErasedMap => _erasedMap;

        private void Awake()
        {
            _erasedMap = new RenderTexture(_resolution, _resolution, 0,
                                           RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = name + "_ErasedMap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            _erasedMap.Create();

            Bind(_puff);
            Bind(_push);
            Bind(_sparkle);
        }

        private void Bind(VisualEffect effect)
        {
            if (effect == null) return;
            effect.SetTexture(ErasedMapId, _erasedMap);
            effect.SetFloat(ErasedThresholdId, _threshold);
        }

        /// <summary>이번 프레임의 기록을 비운다. 붓질을 그리기 전에 부른다.</summary>
        public void BeginFrame()
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _erasedMap;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        /// <summary>패드 포즈와 진행 방향을 그래프에 넘긴다. 파티클 위치는 RT 가 준다.</summary>
        public void Play(in BrushPad pad, Vector3 travelDirection)
        {
            SetTravel(_puff, travelDirection);
            SetTravel(_push, travelDirection);
            SetTravel(_sparkle, travelDirection);
        }

        private static void SetTravel(VisualEffect effect, Vector3 travelDirection)
        {
            if (effect == null) return;
            effect.SetVector3(TravelId, travelDirection);
        }

        private void OnDestroy()
        {
            if (_erasedMap != null) { _erasedMap.Release(); Destroy(_erasedMap); }
        }
    }
}
```

- [ ] **Step 4: `DustMousePainter` 가 순서대로 부르게 수정**

```csharp
[SerializeField] private DustCleanVfx _vfx;

// ... 붓질 부분:
if (_vfx != null) _vfx.BeginFrame();

if (hit.collider.TryGetComponent(out DustPaintTarget paintTarget))
{
    // 순서가 중요하다. CaptureErased 는 빼기 전 마스크를 읽어야 한다.
    if (_vfx != null) paintTarget.CaptureErased(_vfx.ErasedMap, pad);
    paintTarget.Paint(pad);
}

if (_vfx != null) _vfx.Play(pad, forward);
```

**이 호출 묶음이 프레임당 한 번 도는 자리에 있어야 한다.** 나중에 Fusion 이 오면 이 묶음이
`Render()` 로 그대로 옮겨간다 — `FixedUpdateNetwork` 에 두면 재시뮬레이션마다 중복으로 지워진다
(스펙 §6).

- [ ] **Step 5: 컴파일과 프로퍼티 확인**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 10
$U cmd --project-path "$P" --no-banner get_shader_properties --shader "PPack/DustErased" --format json
```
기대: 에러 0, 셰이더가 찾아진다.

- [ ] **Step 6: RT 내용을 눈으로 확인**

이 단계에서는 아직 그래프가 없으므로 RT 자체를 본다. 테스트 씬에 Quad 하나를 띄워
`_erasedMap` 을 Unlit 머티리얼에 물려 화면 구석에 보여주는 **임시** 오브젝트를 만든다
(`__TEST__ErasedMapPreview`, 확인 후 삭제).

| 통과 | 실패 |
|---|---|
| 문지르는 동안 사각 영역이 밝아진다 | 항상 검다 → 순서가 뒤바뀌었거나 RT 바인딩이 안 됐다 |
| **이미 깨끗한 자리를 문지르면 검은 채로 있다** | 밝아진다 → `min(previous, ...)` 가 안 먹는다 |
| 밝은 부분이 너덜너덜하다 | 매끈한 사각형 → `DustBrush.hlsl` 이 공유되지 않았다 |

두 번째 줄이 이 태스크 전체의 존재 이유다.

- [ ] **Step 7: 임시 프리뷰 제거 후 체크인**

---

## Task 5: 퍼프 — VFX Graph

**Files:**
- Modify: `Assets/Game/InGame/Dust/VFX/VFX_DustPuff.vfx` (Task 2 의 뼈대를 실물로)
- Modify: `Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`

**Interfaces:**
- Consumes: `_ErasedMap` `_ErasedThreshold` `_Travel`

- [ ] **Step 1: 그래프를 실물 퍼프로 (직접, 에디터에서)**

Task 2 에서 통한 죽이기 방법을 그대로 쓴다. 목표는 **아트 타겟
`docs/images/vfx-target-puff.png`** — 큰 구름이 아니라 **패드 가장자리에 붙은 얇은 띠**다.

| | |
|---|---|
| 위치 | `_ErasedMap` 의 `rgb`. 월드 공간 |
| 속도 | 살짝 위로 + **패드 안쪽으로** 수렴. 밖으로 흩어지면 흡입기로 안 보인다 |
| 수명 | 짧게 (0.3~0.6s). 길면 궤적이 남아 청소한 자리가 지저분해 보인다 |
| 크기 | 편차를 크게. 타겟이 블러로 나온 이유가 균일한 크기였을 가능성이 높다 (스펙 §10) |
| 색 | 먼지 재질과 같은 계열. `_DirtColor` 톤 (`#C08A50` 부근) |

- [ ] **Step 2: 씬에 배치**

`DustCleanVfx` 를 붙인 오브젝트에 `VisualEffect`(에셋 = `VFX_DustPuff`)를 자식으로 두고
`_puff` 필드에 물린다. `DustMousePainter._vfx` 도 연결한다.

- [ ] **Step 3: 판정 — 스펙 §8 의 1·2·3번**

```bash
$U cmd --project-path "$P" --no-banner editor_play
# 더러운 자리를 문지르며
$U cmd --project-path "$P" --no-banner capture_game_view --output /tmp/dust_vfx/puff_dirty.png
# 이미 닦은 자리를 다시 문지르며
$U cmd --project-path "$P" --no-banner capture_game_view --output /tmp/dust_vfx/puff_clean.png
$U cmd --project-path "$P" --no-banner editor_stop
```

| | 통과 기준 |
|---|---|
| 1 | 문지르는 동안 퍼프가 나고, 먼지가 걷히는 것과 눈에 띄는 시차가 없다 |
| 2 | **이미 깨끗한 자리에서는 퍼프가 안 난다** (`puff_clean.png` 가 비어 있다) |
| 3 | 퍼프가 사각 패드 모양과 붓의 너덜너덜한 경계를 따라간다. 매끈한 사각형이면 실패 |
| 5 | **3인칭 거리에서 읽힌다.** 근접에서만 보이면 실패 |

- [ ] **Step 4: 체크인**

---

## Task 6: 퍼프 — 내장 파티클 비교판과 결론

스펙 §5 의 A/B. **결론이 이 태스크의 산출물이다.**

**Files:**
- Create: `Assets/Game/InGame/Dust/VFX/PS_DustPuff.prefab`
- Modify: `Assets/Game/InGame/Dust/Scripts/DustCleanVfx.cs`
- Modify: `Assets/Game/InGame/Dust/AGENTS.md`

- [ ] **Step 1: Shuriken 퍼프 프리팹 (직접, 에디터에서)**

`ParticleSystem` 하나. 룩은 Task 5 의 그래프와 최대한 맞춘다 — 수명·색·크기 편차·중력.
Renderer 는 URP 파티클 셰이더(`Universal Render Pipeline/Particles/Unlit`)를 쓴다.

- [ ] **Step 2: `DustCleanVfx` 에 비교용 방출 경로 추가**

```csharp
[Header("비교용 — 내장 파티클")]
[Tooltip("스펙 5절의 A/B. 결론이 나면 진 쪽을 지운다.")]
[SerializeField] private ParticleSystem _puffFallback;

/// <summary>
/// 내장 파티클 비교판. CPU 는 어디가 지워졌는지 모르므로 패드 사각형 안에 무작위로 뿌리는
/// 것 말고 할 수 있는 게 없다 — 이 한계가 곧 A/B 의 내용이다.
/// </summary>
private void EmitFallback(in BrushPad pad, int count)
{
    if (_puffFallback == null) return;

    Matrix4x4 padToWorld = pad.WorldToPad.inverse;
    var emit = new ParticleSystem.EmitParams();
    for (int i = 0; i < count; i++)
    {
        Vector3 local = new Vector3(Random.Range(-pad.HalfExtents.x, pad.HalfExtents.x), 0f,
                                    Random.Range(-pad.HalfExtents.y, pad.HalfExtents.y));
        emit.position = padToWorld.MultiplyPoint3x4(local);
        emit.applyShapeToPosition = false;
        _puffFallback.Emit(emit, 1);
    }
}
```

`Play` 에서 `_puffFallback` 이 물려 있을 때만 부르게 한다.

- [ ] **Step 3: 나란히 비교**

같은 카메라·같은 조명에서 그래프판과 파티클판을 각각 켜고 두 장씩 찍는다 — **더러운 자리**와
**이미 닦은 자리**.

```bash
$U cmd --project-path "$P" --no-banner capture_game_view --output /tmp/dust_vfx/ab_vfxg_dirty.png
$U cmd --project-path "$P" --no-banner capture_game_view --output /tmp/dust_vfx/ab_vfxg_clean.png
$U cmd --project-path "$P" --no-banner capture_game_view --output /tmp/dust_vfx/ab_shuriken_dirty.png
$U cmd --project-path "$P" --no-banner capture_game_view --output /tmp/dust_vfx/ab_shuriken_clean.png
```

- [ ] **Step 4: 판정**

**"이미 닦은 자리" 두 장이 결론을 낸다.** 내장 파티클은 CPU 가 어디가 지워졌는지 모르므로
깨끗한 바닥에서도 퍼프가 난다 — 스펙 §8 의 2번을 **원리적으로** 만족할 수 없다. 그게 실제로
얼마나 거슬리는지를 눈으로 확인하는 것이 이 A/B 의 내용이다.

거슬리지 않는다면 그건 진짜 결과다. 그때는 "왜 GPU 경로가 필요한가"를 다시 따져야 한다.

- [ ] **Step 5: 결론을 `Dust/AGENTS.md` 에 기록**

이긴 쪽과 **왜** 이겼는지, 진 쪽 자산을 지웠는지 남겼는지. 남긴다면 그 이유도. `M_Dust_Film`
때와 같은 형식으로 쓰고, 검증 스크린샷은 `docs/images/verify/` 로 옮긴다.

- [ ] **Step 6: 진 쪽 정리와 체크인**

---

## Task 7: 밀림과 반짝

**Files:**
- Create: `Assets/Game/InGame/Dust/VFX/VFX_DustPush.vfx`, `VFX_CleanSparkle.vfx`
- Modify: `Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`

Task 6 에서 이긴 기술로 만든다. 셋 다 **같은 `_ErasedMap` 하나**에서 나온다.

- [ ] **Step 1: 밀림 — 흡입기로 보이게**

아트 타겟 `docs/images/vfx-target-push.png` 는 **형태만** 참고한다. 그 이미지의 둔덕 크기를
따라가면 도구가 대걸레로 보인다 (스펙 §4 의 설계 모순 항목).

| | |
|---|---|
| 방출 위치 | `_ErasedMap` 의 `rgb` 중 **진행 방향 앞쪽**만. `_Travel` 과 패드 로컬 위치로 거른다 |
| 양 | **퍼프보다 확실히 적게.** 둘의 비율이 "흡입기인가 대걸레인가"를 말한다 |
| 수명 | 짧게. **바닥에 쌓여 남으면 안 된다** — 마스크는 지워졌는데 화면에 먼지가 있는 상태가 된다 |

- [ ] **Step 2: 반짝**

아트 타겟 `vfx-target-sparkle.png` 는 **어휘만** 가져온다 (사방 십자 글린트, 크기 편차).
**위치는 타겟을 따르지 않는다** — 타겟은 타일 한가운데 박혔는데 실제로는 갓 드러난 **경계**에
몰려야 한다.

수명을 아주 짧게 잡는다. 길면 청소한 자리가 계속 반짝여서 정신없다.

- [ ] **Step 3: 판정 — 스펙 §8 의 4번**

```bash
$U cmd --project-path "$P" --no-banner capture_game_view --output /tmp/dust_vfx/push_direction.png
```

이동 방향을 여러 번 바꾸며 확인한다.

| 통과 | 실패 |
|---|---|
| 밀린 먼지가 앞 모서리를 따라오고, 도구가 **빨아들이는 물건**으로 보인다 | 앞에 먼지가 쌓여 남는다 → 대걸레로 보인다 |
| 반짝임이 갓 드러난 경계에 몰린다 | 닦인 자리 전체가 반짝인다 |

- [ ] **Step 4: 체크인**

---

## Task 8: 최종 검증 · 문서 · 복원

- [ ] **Step 1: 스펙 §8 전체를 순서대로 확인**

1. 문지르는 동안 퍼프가 나고 시차가 없다
2. **이미 깨끗한 자리를 문지르면 퍼프가 안 난다**
3. 퍼프가 사각 패드와 붓 노이즈 모양을 따라간다
4. 밀림이 앞 모서리를 따라오되 **흡입기로 보인다.** 바닥에 쌓여 남으면 실패
5. 3인칭 거리에서 읽힌다
6. VFX Graph / 내장 파티클 결론이 났다
7. **바닥을 닦을 때 아래층·반대편이 지워지지 않는다**
8. 마젠타 없음, 콘솔 에러 0

- [ ] **Step 2: 판정 스크린샷을 저장소로**

```bash
mkdir -p docs/images/verify
cp /tmp/dust_vfx/<판정 근거로 쓸 것들> docs/images/verify/
```

문서에 안 쓸 것은 옮기지 않는다.

- [ ] **Step 3: `Dust/AGENTS.md` 갱신**

새 결정들을 기록한다:

- **브러시는 방향 있는 직사각형이다** — 도구가 표면에 붙어 미는 스팀청소기라서. 두께 판정이
  없으면 사각 프리즘이 무한히 뻗어 아래층까지 지운다
- **지운 양은 GPU 에만 있다** — 패드 공간 RT, 표면당이 아니라 **도구당**. CPU 미러·Readback 을
  기각한 이유와, 이 경로가 붓 노이즈 모양을 그대로 따라간다는 것
- **밀림은 쌓이는 것이 아니라 넘치는 것** — 흡입과 밀림의 비율이 도구의 정체를 말한다
- Task 6 의 A/B 결론

- [ ] **Step 4: `docs/INDEX.md` 갱신**

현재 상태를 "먼지 청소 VFX 완료"로 바꾸고 Plans 에 이 계획을 등록한다. 다음 후보는
`Vacuum`(도구) 또는 `Cleanliness`(집계)다.

- [ ] **Step 5: 에디터 상태 복원 (루트 `AGENTS.md` §5)**

```bash
$U cmd --project-path "$P" --no-banner editor_stop
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 10
```

- Play Mode 꺼짐
- Task 1 Step 1 에 적어둔 원래 씬으로 복귀, dirty 아님
- `__TEST__` / `(Clone)` 이름의 오브젝트가 하나도 없음
- 임시 프리뷰·프로브 오브젝트 제거됨
- `/tmp/dust_vfx/`, `/tmp/vfx_probe/` 중 저장소로 안 옮긴 것 정리

- [ ] **Step 6: 최종 체크인과 `cm status` 확인**

```bash
cm add -R Assets/Game/InGame/Dust
cm status --private        # ProjectSettings/Packages 말고 남은 게 없어야 한다
cm ci <명시 경로들> --commentsfile=<msg>
cm status                  # Changed 가 남았으면 명시 경로로 재체크인
```

---

## 다음 브랜치로 넘기는 것

1. **도구** (`Vacuum/`) — 표면 부착, 온오프, 흡입 궤적. `BrushPad` 를 자기 트랜스폼으로 채워 넘긴다
2. **청소도 집계** (`Cleanliness/`) — CPU 커버리지 그리드. **`AsyncGPUReadback` 이 아니다** —
   데디케이티드 서버에는 GPU 가 없다 (스펙 §6)
3. **도장 보간** — 빠르게 움직일 때 자국에 구멍이 나는 문제. 이번에 보류했다
4. **Fusion** — 설치 전에 루트 `AGENTS.md` 의 "Read this before installing Fusion again" 을 읽는다

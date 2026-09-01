# 지면 눈 타일링 구현 계획

> **에이전트 작업자에게:** 이 계획은 태스크 단위로 실행한다. 단계는 체크박스(`- [ ]`)로 추적한다.

**목표:** 지면 눈 패널을 타일 N장으로 쪼개 프러스텀 컬링을 얻고, 높이 텍스처를 더러운 사각형만 올려 프레임 대역폭을 줄인다.

**구조:** 타일은 **순수한 뷰 개념**이다. 모든 타일이 같은 머티리얼·텍스처·UV 공간을 공유하므로 셰이더가 안 바뀌고 이음매가 생길 수 없다. 부분 업로드는 `SnowHeightFieldCpu` 에 렌더 전용 더러움 누적기를 하나 더 두고, 뷰가 그 바운딩 사각형만 스테이징 텍스처를 거쳐 `Graphics.CopyTexture` 로 박는다.

**기술:** Unity 6000.6.0b7 · URP 17.6.0 · C# · NUnit(EditMode) · Metal

**스펙:** `docs/specs/2026-08-25-snow-render-tiles.md`

## 전역 제약

- 네임스페이스는 **`PPack` 하나**. 폴더를 따르지 않는다.
- private 필드 `_camelCase`, 메서드·타입 `PascalCase`, `[SerializeField]` 선호, early return 선호.
- 직렬화된 Unity Object 필드는 `== null` / `!= null` (fake-null 때문). 그 외에는 `is null`.
- 셀 크기는 `SnowFieldGeometry.CellSizeM` = 0.125 m, 청크는 `ChunkCells` = 16 셀. **상수를 새로 만들지 않는다.**
- **셰이더를 한 줄도 바꾸지 않는다.** `SnowDisplace.shader` 는 이 계획의 대상이 아니다.
- **권위 격자 · 인덱스 공간 · 네트워크 블록 · 공개 조회 API(`HeightAtM` / `SurfaceYAt` / `DepthAt` / `ResolveZone`)를 바꾸지 않는다.**
- **모든 폴백은 현재 동작(전체 업로드)이다.** 어떤 경로로 빠져도 지금보다 나빠지지 않아야 한다.
- 버전 관리는 **Plastic**. `git` 명령을 쓰지 않는다. 체크인은 **사용자 승인 후**에만 한다(20 MB 맵 에셋 건이 미결이라 묶어서 한 번에 한다).
- 회귀 기준선: EditMode **222/222**, PlayMode 62 중 기존 빨간 **15개와 정확히 동일**(cs:660 에서 확인해 둔 목록).

---

## 파일 구조

| 파일 | 책임 |
|---|---|
| `Assets/Game/InGame/Snow/HeightCpu/View/SnowPanelTiling.cs` **(생성)** | 타일 분할과 더러운 사각형 계산 **전부**. 상태 없음 → 그래픽 없이 전수 검증 가능 |
| `Assets/Game/InGame/Snow/Tests/EditMode/SnowPanelTilingTests.cs` **(생성)** | 위의 테스트 |
| `Assets/Game/InGame/Snow/HeightCpu/SnowHeightFieldCpu.cs` **(수정)** | 렌더 전용 더러움 누적기 추가 |
| `Assets/Game/InGame/Snow/Tests/EditMode/SnowHeightFieldCpuTests.cs` **(수정)** | 누적기 테스트 추가 |
| `Assets/Game/InGame/Snow/HeightCpu/View/SnowDisplaceView.cs` **(수정)** | 타일 메시 생성 + 부분 업로드 + 계측 |
| `Assets/Game/InGame/Snow/AGENTS.md` **(수정)** | 규칙과 실측 기록 |
| `docs/INDEX.md` **(수정)** | 목차 갱신 |

계산을 `SnowPanelTiling` 으로 떼는 것이 이 계획의 핵심 결정이다. `SnowDisplaceView` 는 `GameObject`·`Texture2D` 를 만드므로 그래픽 장치 없이 테스트할 수 없지만, **틀리면 실금이 가는 부분은 전부 순수 계산**이라 그쪽만 떼면 EditMode 로 굳힐 수 있다.

---

## Task 1: `SnowPanelTiling` — 타일 분할과 더러운 사각형 계산

**파일:**
- 생성: `Assets/Game/InGame/Snow/HeightCpu/View/SnowPanelTiling.cs`
- 생성: `Assets/Game/InGame/Snow/Tests/EditMode/SnowPanelTilingTests.cs`

**인터페이스:**
- 사용: `SnowFieldGeometry`(`ResX` · `ResZ` · `ChunksX` · `ChunkCellBounds`)
- 제공: 아래 여섯 개의 `public static`. Task 3·4 가 전부 이것만 부른다.

```csharp
int   SnowPanelTiling.LatticeCount(float sizeM, float spacingM)
float SnowPanelTiling.LatticePos(float minM, float sizeM, int count, int index)
int   SnowPanelTiling.QuadsPerTile(float tileSizeM, float spacingM)
int   SnowPanelTiling.TileCountOnAxis(int latticeCount, int quadsPerTile)
void  SnowPanelTiling.TileVertexRange(int latticeCount, int quadsPerTile, int tile, out int lo, out int hi)
bool  SnowPanelTiling.TryDirtyCellRect(SnowFieldGeometry geo, IReadOnlyList<int> dirtyChunks,
                                       out int cx0, out int cz0, out int cx1, out int cz1)
int   SnowPanelTiling.StagingSizeFor(int width, int height, int maxCells)
```

- [ ] **Step 1: 실패하는 테스트를 쓴다**

생성: `Assets/Game/InGame/Snow/Tests/EditMode/SnowPanelTilingTests.cs`

```csharp
using System.Collections.Generic;
using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// 타일 분할의 계산만 검증한다. <b>틀리면 화면에 실금이 가는 부분이 전부 여기</b>이고,
    /// 순수 계산이라 그래픽 장치 없이 전수로 돌릴 수 있다.
    /// </summary>
    public sealed class SnowPanelTilingTests
    {
        private const float Spacing = 0.25f;

        [Test]
        public void LatticeCount_MatchesTheOldSinglePanelFormula()
        {
            // 120 m / 0.25 = 480 구간 → 481 정점. 지금 BuildGrid 가 쓰는 식과 같아야 한다.
            Assert.AreEqual(481, SnowPanelTiling.LatticeCount(120f, Spacing));
            Assert.AreEqual(441, SnowPanelTiling.LatticeCount(110f, Spacing));
            Assert.AreEqual(2, SnowPanelTiling.LatticeCount(0.01f, Spacing), "최소 2 정점");
        }

        [Test]
        public void 이웃_타일이_공유하는_모서리_정점은_비트단위로_같다()
        {
            // 이 테스트가 이 파일의 존재 이유다. 타일마다 로컬 원점으로 계산하면
            // 부동소수 결과가 갈려 타일 경계에 실금이 간다.
            const float min = -60f, size = 120f;
            int count = SnowPanelTiling.LatticeCount(size, Spacing);
            int quads = SnowPanelTiling.QuadsPerTile(16f, Spacing);
            int tiles = SnowPanelTiling.TileCountOnAxis(count, quads);

            for (int t = 0; t < tiles - 1; t++)
            {
                SnowPanelTiling.TileVertexRange(count, quads, t, out _, out int hi);
                SnowPanelTiling.TileVertexRange(count, quads, t + 1, out int nextLo, out _);
                Assert.AreEqual(hi, nextLo, $"타일 {t} 의 끝과 {t + 1} 의 시작이 같은 정점이어야 한다");

                float a = SnowPanelTiling.LatticePos(min, size, count, hi);
                float b = SnowPanelTiling.LatticePos(min, size, count, nextLo);
                Assert.IsTrue(a.Equals(b), $"공유 정점이 다르다: {a:R} vs {b:R}");
            }
        }

        [Test]
        public void 타일들이_격자를_빠짐없이_한_번씩_덮는다()
        {
            foreach (float tileM in new[] { 8f, 16f, 20f, 30f, 500f })
            {
                int count = SnowPanelTiling.LatticeCount(120f, Spacing);
                int quads = SnowPanelTiling.QuadsPerTile(tileM, Spacing);
                int tiles = SnowPanelTiling.TileCountOnAxis(count, quads);

                int covered = 0;
                int expectedLo = 0;
                for (int t = 0; t < tiles; t++)
                {
                    SnowPanelTiling.TileVertexRange(count, quads, t, out int lo, out int hi);
                    Assert.AreEqual(expectedLo, lo, $"타일 {t} 이 앞 타일과 안 붙는다 (tile {tileM} m)");
                    Assert.Greater(hi, lo, $"타일 {t} 에 quad 가 없다 (tile {tileM} m)");
                    covered += hi - lo;
                    expectedLo = hi;
                }

                Assert.AreEqual(count - 1, covered, $"quad 합이 전역과 다르다 (tile {tileM} m)");
                Assert.AreEqual(count - 1, expectedLo, $"마지막 타일이 끝에 안 닿는다 (tile {tileM} m)");
            }
        }

        [Test]
        public void TileCountOnAxis_IsAtLeastOne_EvenWhenTheTileIsBiggerThanTheField()
        {
            int count = SnowPanelTiling.LatticeCount(6f, Spacing);         // 25 정점
            int quads = SnowPanelTiling.QuadsPerTile(16f, Spacing);        // 64 quad
            Assert.AreEqual(1, SnowPanelTiling.TileCountOnAxis(count, quads));
            SnowPanelTiling.TileVertexRange(count, quads, 0, out int lo, out int hi);
            Assert.AreEqual(0, lo);
            Assert.AreEqual(count - 1, hi, "한 장짜리 타일은 격자 전체를 덮어야 한다");
        }

        [Test]
        public void TryDirtyCellRect_ReturnsFalse_WhenNothingIsDirty()
        {
            var geo = new SnowFieldGeometry(16f, 16f, 0f, 0f);
            Assert.IsFalse(SnowPanelTiling.TryDirtyCellRect(geo, new List<int>(),
                                                            out _, out _, out _, out _));
        }

        [Test]
        public void TryDirtyCellRect_CoversExactlyOneChunk()
        {
            var geo = new SnowFieldGeometry(16f, 16f, 0f, 0f);   // 128x128 셀, 8x8 청크
            int chunk = geo.ChunkIndex(3, 5);
            Assert.IsTrue(SnowPanelTiling.TryDirtyCellRect(geo, new List<int> { chunk },
                                                           out int cx0, out int cz0,
                                                           out int cx1, out int cz1));
            Assert.AreEqual(3 * 16, cx0);
            Assert.AreEqual(5 * 16, cz0);
            Assert.AreEqual(3 * 16 + 15, cx1);
            Assert.AreEqual(5 * 16 + 15, cz1);
        }

        [Test]
        public void TryDirtyCellRect_SpansTwoDistantChunks()
        {
            var geo = new SnowFieldGeometry(16f, 16f, 0f, 0f);
            var dirty = new List<int> { geo.ChunkIndex(1, 1), geo.ChunkIndex(6, 4) };
            Assert.IsTrue(SnowPanelTiling.TryDirtyCellRect(geo, dirty,
                                                           out int cx0, out int cz0,
                                                           out int cx1, out int cz1));
            Assert.AreEqual(1 * 16, cx0);
            Assert.AreEqual(1 * 16, cz0);
            Assert.AreEqual(6 * 16 + 15, cx1);
            Assert.AreEqual(4 * 16 + 15, cz1);
        }

        [Test]
        public void StagingSizeFor_RoundsUpToAPowerOfTwo_AndRefusesWhatIsTooBig()
        {
            Assert.AreEqual(16, SnowPanelTiling.StagingSizeFor(1, 1, 256));
            Assert.AreEqual(16, SnowPanelTiling.StagingSizeFor(16, 9, 256));
            Assert.AreEqual(32, SnowPanelTiling.StagingSizeFor(17, 9, 256));
            Assert.AreEqual(64, SnowPanelTiling.StagingSizeFor(40, 64, 256));
            Assert.AreEqual(256, SnowPanelTiling.StagingSizeFor(160, 160, 256));
            Assert.AreEqual(0, SnowPanelTiling.StagingSizeFor(257, 4, 256), "넘으면 0 = 전체 업로드");
            Assert.AreEqual(0, SnowPanelTiling.StagingSizeFor(4, 300, 256));
        }
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
~/.unity/bin/unity command recompile --timeout 280
```
기대: `recompile_status` 가 `failed: true`, 에러에 `SnowPanelTiling` 을 못 찾는다는 메시지.

- [ ] **Step 3: 최소 구현을 쓴다**

생성: `Assets/Game/InGame/Snow/HeightCpu/View/SnowPanelTiling.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <b>패널을 타일로 나누는 계산 전부.</b> 상태가 없다 — 그래서 그래픽 장치 없이 EditMode 로
    /// 전수 검증할 수 있고, <see cref="SnowDisplaceView"/> 는 이것을 부르기만 한다.
    ///
    /// <para><b>왜 떼어냈는가.</b> 틀리면 화면에 <b>실금</b>이 가는 부분이 전부 여기다 —
    /// 이웃 타일이 공유하는 모서리 정점이 한 비트라도 다르면 그 선이 보인다. 뷰 안에 두면
    /// <c>GameObject</c>·<c>Texture2D</c> 때문에 테스트가 안 된다.</para>
    /// </summary>
    public static class SnowPanelTiling
    {
        /// <summary>스테이징 텍스처의 최소 한 변(셀). 이보다 작게 만들 이유가 없다.</summary>
        public const int MinStagingCells = 16;

        /// <summary>
        /// 축 하나의 전역 정점 수. <b>지금 <c>BuildGrid</c> 가 쓰는 식과 같아야 한다</b> —
        /// 타일링 전후로 정점이 같은 자리에 있어야 화면이 안 바뀐다.
        /// </summary>
        public static int LatticeCount(float sizeM, float spacingM)
            => Mathf.Max(2, Mathf.RoundToInt(sizeM / spacingM) + 1);

        /// <summary>
        /// 전역 정점 <paramref name="index"/> 의 좌표.
        ///
        /// <para>⚠ <b>타일이 달라도 같은 index 는 반드시 같은 값을 내야 한다.</b> 타일마다 로컬
        /// 원점 기준으로 다시 계산하면 부동소수 결과가 미세하게 갈려 공유 모서리에 실금이 간다.
        /// 그래서 이 함수는 타일을 인자로 받지 않는다.</para>
        /// </summary>
        public static float LatticePos(float minM, float sizeM, int count, int index)
            => minM + index / (float)(count - 1) * sizeM;

        /// <summary>타일 하나가 갖는 quad 수(축 하나). 최소 1.</summary>
        public static int QuadsPerTile(float tileSizeM, float spacingM)
            => Mathf.Max(1, Mathf.RoundToInt(tileSizeM / spacingM));

        /// <summary>축 하나의 타일 수. 필드보다 타일이 커도 1 이다.</summary>
        public static int TileCountOnAxis(int latticeCount, int quadsPerTile)
            => Mathf.Max(1, Mathf.CeilToInt((latticeCount - 1) / (float)quadsPerTile));

        /// <summary>
        /// 타일 <paramref name="tile"/> 이 갖는 정점 인덱스 구간. <b>양 끝을 포함</b>하고,
        /// 끝 인덱스는 다음 타일의 시작 인덱스와 <b>같다</b>(경계 정점을 공유한다).
        /// </summary>
        public static void TileVertexRange(int latticeCount, int quadsPerTile, int tile,
                                           out int lo, out int hi)
        {
            lo = tile * quadsPerTile;
            hi = lo + quadsPerTile;
            if (hi > latticeCount - 1) hi = latticeCount - 1;
        }

        /// <summary>
        /// 더러운 청크들을 덮는 <b>셀 좌표</b> 사각형(양 끝 포함). 비어 있으면 <c>false</c>.
        /// 사각형은 청크 정렬이므로 항상 청크 배수 폭이다.
        /// </summary>
        public static bool TryDirtyCellRect(SnowFieldGeometry geo, IReadOnlyList<int> dirtyChunks,
                                            out int cx0, out int cz0, out int cx1, out int cz1)
        {
            cx0 = cz0 = int.MaxValue;
            cx1 = cz1 = int.MinValue;
            if (dirtyChunks == null || dirtyChunks.Count == 0) return false;

            for (int i = 0; i < dirtyChunks.Count; i++)
            {
                geo.ChunkCellBounds(dirtyChunks[i], out int x0, out int z0, out int x1, out int z1);
                if (x0 < cx0) cx0 = x0;
                if (z0 < cz0) cz0 = z0;
                if (x1 > cx1) cx1 = x1;
                if (z1 > cz1) cz1 = z1;
            }

            return true;
        }

        /// <summary>
        /// 사각형 <paramref name="width"/> × <paramref name="height"/> 를 담을 정사각 스테이징의
        /// 한 변. 2의 거듭제곱으로 올림한다 — 매 프레임 크기가 달라도 <b>몇 종류만</b> 만들어
        /// 재사용하기 위해서다. <paramref name="maxCells"/> 를 넘으면 <b>0</b>(= 전체 업로드).
        /// </summary>
        public static int StagingSizeFor(int width, int height, int maxCells)
        {
            int need = width > height ? width : height;
            if (need <= 0 || need > maxCells) return 0;

            int size = MinStagingCells;
            while (size < need) size <<= 1;
            return size > maxCells ? 0 : size;
        }
    }
}
```

- [ ] **Step 4: 통과를 확인한다**

```bash
~/.unity/bin/unity command recompile --timeout 280
~/.unity/bin/unity command run_tests --mode EditMode --test_filter "PPack.SnowPanelTilingTests" --timeout 400
```
기대: `Total 8, Passed 8, Failed 0`.

---

## Task 2: `SnowHeightFieldCpu` 렌더 전용 더러움 누적기

**파일:**
- 수정: `Assets/Game/InGame/Snow/HeightCpu/SnowHeightFieldCpu.cs` (필드 선언부 · 생성자 · `WakeChunk`)
- 수정: `Assets/Game/InGame/Snow/Tests/EditMode/SnowHeightFieldCpuTests.cs`

**인터페이스:**
- 사용: 없음(자기 안에서 끝난다)
- 제공: `IReadOnlyList<int> RenderDirtyChunks` · `void ClearRenderDirty()`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`SnowHeightFieldCpuTests.cs` 의 마지막 `}` 두 개 앞에 붙인다:

```csharp
        [Test]
        public void RenderDirty_누적된다_BeginStep이_비우지_않는다()
        {
            // 한 프레임에 FixedUpdate 가 여러 번 돌 수 있다. 스텝마다 비우면 렌더는
            // 마지막 스텝 것만 보게 되고, 그러면 앞 스텝이 바꾼 셀이 화면에 안 올라간다.
            var f = Small();
            f.BeginStep();
            f.WakeChunkOfCell(0, 0);
            f.BeginStep();
            f.WakeChunkOfCell(f.Geo.ResX - 1, f.Geo.ResZ - 1);

            Assert.AreEqual(2, f.RenderDirtyChunks.Count, "두 스텝의 청크가 둘 다 남아 있어야 한다");
            Assert.AreEqual(1, f.ChangedChunks.Count,
                            "ChangedChunks 는 스텝마다 비워지는 그대로여야 한다");
        }

        [Test]
        public void RenderDirty_같은_청크를_여러_번_깨워도_한_번만_들어간다()
        {
            var f = Small();
            f.BeginStep();
            for (int i = 0; i < 10; i++) f.WakeChunkOfCell(1, 1);
            Assert.AreEqual(1, f.RenderDirtyChunks.Count);
        }

        [Test]
        public void RenderDirty_ClearRenderDirty만_비운다()
        {
            var f = Small();
            f.BeginStep();
            f.WakeChunkOfCell(0, 0);
            Assert.AreEqual(1, f.RenderDirtyChunks.Count);

            f.ClearRenderDirty();
            Assert.AreEqual(0, f.RenderDirtyChunks.Count);

            // 비운 뒤에는 다시 들어갈 수 있어야 한다(플래그가 안 남아 있어야 한다).
            f.WakeChunkOfCell(0, 0);
            Assert.AreEqual(1, f.RenderDirtyChunks.Count);
        }

        [Test]
        public void RenderDirty_아무것도_안_건드리면_비어_있다()
        {
            var f = Small();
            f.BeginStep();
            Assert.AreEqual(0, f.RenderDirtyChunks.Count);
        }
```

- [ ] **Step 2: 실패를 확인한다**

```bash
~/.unity/bin/unity command recompile --timeout 280
```
기대: 컴파일 에러 — `RenderDirtyChunks` / `ClearRenderDirty` 가 없다.

- [ ] **Step 3: 최소 구현을 쓴다**

`SnowHeightFieldCpu.cs`, `public IReadOnlyList<int> ChangedChunks => _changed;` **바로 아래**에 추가:

```csharp
        /// <summary>
        /// 렌더가 아직 안 가져간 청크. <see cref="_changed"/> 와 목적이 다르다.
        ///
        /// <para>⚠ <b><see cref="BeginStep"/> 이 이것을 비우지 않는다.</b> 한 프레임에
        /// <c>FixedUpdate</c> 가 <b>0번일 수도 여러 번일 수도</b> 있으므로, 스텝마다 비우면
        /// <c>LateUpdate</c> 는 마지막 스텝 것만 보거나 아예 빈 목록을 본다 — 그러면 앞 스텝이
        /// 바꾼 셀이 화면에 안 올라간다. <b>렌더가 가져갈 때만</b>
        /// (<see cref="ClearRenderDirty"/>) 비운다.</para>
        /// </summary>
        private readonly List<int> _renderDirty = new List<int>(256);

        /// <summary>중복 방지. 스텝 스탬프가 아니라 <b>플래그</b>다 — 스텝을 가로질러 누적하므로.</summary>
        private readonly bool[] _renderDirtyFlag;

        /// <inheritdoc cref="_renderDirty"/>
        public IReadOnlyList<int> RenderDirtyChunks => _renderDirty;

        /// <summary>렌더가 다 올린 뒤에 부른다. 이것만이 <see cref="RenderDirtyChunks"/> 를 비운다.</summary>
        public void ClearRenderDirty()
        {
            for (int i = 0; i < _renderDirty.Count; i++) _renderDirtyFlag[_renderDirty[i]] = false;
            _renderDirty.Clear();
        }
```

생성자에서 `_changedStamp = new int[geo.ChunkCount];` 바로 아래에 추가:

```csharp
            _renderDirtyFlag = new bool[geo.ChunkCount];
```

`WakeChunk` 를 아래로 교체:

```csharp
        public void WakeChunk(int chunkIndex)
        {
            if (_chunkRest[chunkIndex] >= RestStepsToSleep) _dirtyIndex?.MarkDirty(chunkIndex);
            _chunkRest[chunkIndex] = 0;

            if (_changedStamp[chunkIndex] != _stepStamp)
            {
                _changedStamp[chunkIndex] = _stepStamp;
                _changed.Add(chunkIndex);
            }

            // 렌더용은 스텝을 가로질러 누적한다 - 위 주석 참고.
            if (!_renderDirtyFlag[chunkIndex])
            {
                _renderDirtyFlag[chunkIndex] = true;
                _renderDirty.Add(chunkIndex);
            }
        }
```

- [ ] **Step 4: 통과를 확인한다**

```bash
~/.unity/bin/unity command recompile --timeout 280
~/.unity/bin/unity command run_tests --mode EditMode --test_filter "PPack.SnowHeightFieldCpuTests" --timeout 400
```
기대: 기존 테스트 + 새 4개가 전부 통과.

---

## Task 3: 뷰의 메시 타일링

**파일:**
- 수정: `Assets/Game/InGame/Snow/HeightCpu/View/SnowDisplaceView.cs`

**인터페이스:**
- 사용: Task 1 의 `LatticeCount` · `LatticePos` · `QuadsPerTile` · `TileCountOnAxis` · `TileVertexRange`
- 제공: `int TileCount` (검증용 · 지금 그리는 타일 총수)

- [ ] **Step 1: 인스펙터 노브를 추가한다**

`_edgeProfile` 선언 **아래**에 추가:

```csharp
        [Tooltip("지면 패널을 이 크기로 쪼갠다(m). 쪼개면 프러스텀 컬링이 들어서 화면 밖 타일을 " +
                 "안 그린다. 작을수록 컬링이 촘촘하고 드로우 콜이 는다.\n\n" +
                 "0 이면 안 쪼갠다(2026-08-25 이전 동작).")]
        [SerializeField, Range(0f, 60f)] private float _tileSizeM = 16f;
```

- [ ] **Step 2: `Panel` 을 타일 여러 장으로 바꾼다**

`Panel` 클래스를 아래로 교체:

```csharp
        /// <summary>필드 하나를 그리는 한 벌. 지면과 상자가 같은 것을 쓴다.</summary>
        private sealed class Panel
        {
            public SnowHeightFieldCpu Field;

            /// <summary>타일들의 부모. 예전 단일 패널의 자리를 그대로 쓴다.</summary>
            public GameObject Root;

            /// <summary><b>타일이 공유한다.</b> 텍스처도 유니폼도 전부 같으므로 한 장이면 된다.</summary>
            public Material Material;

            public GameObject[] TileObjects;
            public Mesh[] TileMeshes;

            public Texture2D HeightTex;
            public Texture2D FloorTex;
            public Texture2D MaskTex;
        }
```

`PanelCount` 아래에 추가:

```csharp
        /// <summary>검증용 — 지금 그리는 타일 총수. 지면이 쪼개졌는지 이걸로 본다.</summary>
        public int TileCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _panels.Count; i++)
                    n += _panels[i].TileMeshes == null ? 0 : _panels[i].TileMeshes.Length;
                return n;
            }
        }
```

- [ ] **Step 3: `BuildPanel` 이 타일을 만들게 한다**

`BuildPanel` 안에서 `panel.Mesh = BuildGrid(...)` 부터 `return panel;` 까지를 아래로 교체:

```csharp
            panel.Root = new GameObject(name);
            panel.Root.transform.SetParent(parent, false);
            panel.Root.transform.localPosition = localPos;
            panel.Root.transform.localRotation = localRot;
            panel.Root.transform.localScale = Vector3.one;

            BuildTiles(panel, geo, ground, meshMinX, meshMinZ, meshW, meshD, name);
            return panel;
```

`BuildGrid` 를 아래 두 메서드로 교체:

```csharp
        /// <summary>
        /// 패널을 덮는 타일들을 만든다. <b>정점 격자는 전역 하나</b>이고 타일은 그 인덱스 구간만
        /// 가져간다 — 그래서 이웃과 공유하는 모서리 정점이 비트 단위로 같고 실금이 안 간다.
        /// </summary>
        private void BuildTiles(Panel panel, SnowFieldGeometry geo, SnowGroundFieldCpu ground,
                                float minX, float minZ, float w, float d, string name)
        {
            int nx = SnowPanelTiling.LatticeCount(w, _vertexSpacingM);
            int nz = SnowPanelTiling.LatticeCount(d, _vertexSpacingM);

            // 0 이면 안 쪼갠다 - 예전 동작으로 돌아가는 탈출구다.
            float tileM = _tileSizeM > 0f ? _tileSizeM : float.MaxValue;
            int quadsX = _tileSizeM > 0f ? SnowPanelTiling.QuadsPerTile(tileM, _vertexSpacingM) : nx - 1;
            int quadsZ = _tileSizeM > 0f ? SnowPanelTiling.QuadsPerTile(tileM, _vertexSpacingM) : nz - 1;

            int tilesX = SnowPanelTiling.TileCountOnAxis(nx, quadsX);
            int tilesZ = SnowPanelTiling.TileCountOnAxis(nz, quadsZ);

            panel.TileObjects = new GameObject[tilesX * tilesZ];
            panel.TileMeshes = new Mesh[tilesX * tilesZ];

            for (int tz = 0; tz < tilesZ; tz++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    SnowPanelTiling.TileVertexRange(nx, quadsX, tx, out int x0, out int x1);
                    SnowPanelTiling.TileVertexRange(nz, quadsZ, tz, out int z0, out int z1);

                    int index = tz * tilesX + tx;
                    Mesh mesh = BuildTileMesh(geo, ground, minX, minZ, w, d, nx, nz,
                                              x0, x1, z0, z1, $"{name}_Tile{tx}x{tz}");
                    panel.TileMeshes[index] = mesh;

                    var go = new GameObject($"{name}_Tile{tx}x{tz}");
                    go.transform.SetParent(panel.Root.transform, false);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;

                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = panel.Material;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.receiveShadows = true;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    panel.TileObjects[index] = go;
                }
            }
        }

        /// <summary>
        /// 전역 격자의 <c>[x0..x1] × [z0..z1]</c> 구간만 덮는 메시. 정점 좌표는 <b>전역 인덱스</b>로
        /// 계산한다(<see cref="SnowPanelTiling.LatticePos"/>) — 타일 로컬로 계산하면 공유 모서리가
        /// 갈려 실금이 간다.
        ///
        /// <para><b>바운즈를 손으로 넉넉히 준다.</b> 정점이 CPU 에서 움직이지 않으므로 유니티가 계산한
        /// 바운즈는 두께 0 인 판이고, 그러면 카메라가 눈 위를 볼 때 컬링이 판 전체를 잘라 <b>눈이
        /// 통째로 사라진다</b>. 정점 변위 경로의 고전적인 실수다. 바닥 범위는 <b>이 타일이 덮는
        /// 셀만</b> 훑어서 구한다 — 필드 전체를 쓰면 타일마다 바운즈가 같아져 컬링이 안 든다.</para>
        /// </summary>
        private Mesh BuildTileMesh(SnowFieldGeometry geo, SnowGroundFieldCpu ground,
                                   float minX, float minZ, float w, float d, int nx, int nz,
                                   int x0, int x1, int z0, int z1, string name)
        {
            int vx = x1 - x0 + 1;
            int vz = z1 - z0 + 1;

            var verts = new Vector3[vx * vz];
            for (int z = 0; z < vz; z++)
            {
                float wz = SnowPanelTiling.LatticePos(minZ, d, nz, z0 + z);
                for (int x = 0; x < vx; x++)
                {
                    float wx = SnowPanelTiling.LatticePos(minX, w, nx, x0 + x);
                    verts[z * vx + x] = new Vector3(wx, 0f, wz);
                }
            }

            var tris = new int[(vx - 1) * (vz - 1) * 6];
            int t = 0;
            for (int z = 0; z < vz - 1; z++)
            {
                for (int x = 0; x < vx - 1; x++)
                {
                    int i0 = z * vx + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + vx;
                    int i3 = i2 + 1;

                    tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                    tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
                }
            }

            var mesh = new Mesh
            {
                name = $"{name}_Grid{vx}x{vz}",
                indexFormat = verts.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0, false);

            float tileMinX = verts[0].x;
            float tileMinZ = verts[0].z;
            float tileMaxX = verts[verts.Length - 1].x;
            float tileMaxZ = verts[verts.Length - 1].z;

            TileFloorRange(geo, ground, tileMinX, tileMinZ, tileMaxX, tileMaxZ,
                           out float lowY, out float highY);

            float minY = lowY - 4f;
            float maxY = highY + 12f;
            mesh.bounds = new Bounds(
                new Vector3((tileMinX + tileMaxX) * 0.5f, (minY + maxY) * 0.5f,
                            (tileMinZ + tileMaxZ) * 0.5f),
                new Vector3(tileMaxX - tileMinX, maxY - minY, tileMaxZ - tileMinZ));
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>이 사각형이 덮는 셀의 바닥 최소·최대(월드 Y). 바닥이 없으면 둘 다 0 이다.</summary>
        private static void TileFloorRange(SnowFieldGeometry geo, SnowGroundFieldCpu ground,
                                           float minX, float minZ, float maxX, float maxZ,
                                           out float lowY, out float highY)
        {
            lowY = geo.OriginYM;
            highY = geo.OriginYM;
            if (ground == null) return;

            if (!geo.TryWorldRectToCellRect(minX, minZ, maxX, maxZ,
                                            out int cx0, out int cz0, out int cx1, out int cz1))
            {
                return;
            }

            int lo = int.MaxValue;
            int hi = int.MinValue;
            for (int cz = cz0; cz <= cz1; cz++)
            {
                int row = cz * geo.ResX;
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int v = ground.FloorMm[row + cx];
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
            }

            if (lo > hi) return;
            lowY = geo.OriginYM + lo * 0.001f;
            highY = geo.OriginYM + hi * 0.001f;
        }
```

- [ ] **Step 4: `Dispose` 가 타일을 정리하게 한다**

`Dispose` 안의 `if (panel.Go != null) ... if (panel.Mesh != null) ...` 두 줄을 교체:

```csharp
                if (panel.TileObjects != null)
                    for (int t = 0; t < panel.TileObjects.Length; t++)
                        if (panel.TileObjects[t] != null) Destroy(panel.TileObjects[t]);
                if (panel.TileMeshes != null)
                    for (int t = 0; t < panel.TileMeshes.Length; t++)
                        if (panel.TileMeshes[t] != null) Destroy(panel.TileMeshes[t]);
                if (panel.Root != null) Destroy(panel.Root);
```

- [ ] **Step 5: 컴파일과 회귀를 확인한다**

```bash
~/.unity/bin/unity command recompile --timeout 280
~/.unity/bin/unity command run_tests --mode EditMode --test_filter "PPack.Snow" --timeout 400
```
기대: 컴파일 에러 0, EditMode 전부 통과.

- [ ] **Step 6: 화면과 컬링을 실측한다**

`Snow_Terrain_Test` 를 열고 Play 한 뒤:

```bash
~/.unity/bin/unity command eval --code 'var v = UnityEngine.Object.FindFirstObjectByType<PPack.SnowDisplaceView>(); return $"panels={v.PanelCount} tiles={v.TileCount}";'
~/.unity/bin/unity command get_performance_stats
```

합격선 넷:
1. `tiles` 가 1 보다 크다.
2. **카메라를 돌리면 `triangles` 가 변한다**(지금은 상수 1,689,600).
3. 캡처한 화면에 **이음매·실금이 없다**. 타일링 전 캡처와 육안 비교.
4. **드로우 콜이 감당된다**(스펙 §7.8). `drawCalls` 와 프레임 시간을 타일링 전(드로우 콜 22 ·
   눈 0.86 ms)과 비교한다. 프레임 시간이 나빠지면 `_tileSizeM` 을 키운다 — 그래서 노브다.

---

## Task 4: 뷰의 부분 업로드

**파일:**
- 수정: `Assets/Game/InGame/Snow/HeightCpu/View/SnowDisplaceView.cs`

**인터페이스:**
- 사용: Task 1 의 `TryDirtyCellRect` · `StagingSizeFor`, Task 2 의 `RenderDirtyChunks` · `ClearRenderDirty`
- 제공: `long DebugUploadedBytesLastFrame` (검증용)

- [ ] **Step 1: 필드와 계측기를 추가한다**

`_panels` 선언 아래에 추가:

```csharp
        /// <summary>스테이징 한 변의 상한(셀). 넘는 사각형은 전체 업로드로 되돌아간다.</summary>
        private const int MaxStagingCells = 256;

        /// <summary>2의 거듭제곱 한 변마다 하나. 매 프레임 새로 만들지 않으려고 재사용한다.</summary>
        private readonly Dictionary<int, Texture2D> _staging = new Dictionary<int, Texture2D>(6);

        /// <summary>스테이징에 담을 때 쓰는 임시 버퍼. 가장 큰 스테이징에 맞춰 한 번만 만든다.</summary>
        private ushort[] _stagingScratch;

        private bool _partialUploadSupported;

        /// <summary>검증용 — 지난 프레임에 GPU 로 올린 높이 바이트.</summary>
        public long DebugUploadedBytesLastFrame { get; private set; }
```

`Awake` 를 교체:

```csharp
        private void Awake()
        {
            _stage = GetComponent<SnowCpuStage>();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) enabled = false;

            // 영역 복사가 없으면 부분 업로드를 할 수 없다. 그때는 예전처럼 통째로 올린다.
            _partialUploadSupported =
                (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;
        }
```

- [ ] **Step 2: `LateUpdate` 의 업로드 루프를 교체한다**

`LateUpdate` 안의 업로드 `for` 루프를 아래로 교체:

```csharp
            DebugUploadedBytesLastFrame = 0;
            for (int i = 0; i < _panels.Count; i++)
            {
                // 부분 업로드는 지면만 한다. Build() 가 지면을 항상 먼저 넣으므로 0번이 지면이다.
                UploadHeight(_panels[i], i == 0);
            }
```

- [ ] **Step 3: 업로드 메서드를 추가한다**

`LateUpdate` 아래에 추가:

```csharp
        /// <summary>
        /// 높이 텍스처를 갱신한다. <paramref name="partial"/> 이 참이면 <b>더러운 사각형만</b>
        /// 올리고, 아니면 통째로 올린다.
        ///
        /// <para><b>지면만 부분 업로드를 한다.</b> 상자는 6,144 셀 = 12 KB 라 사각형을 구하고
        /// 스테이징을 거치는 값이 아끼는 값보다 크다. 대신 <b>깨끗하면 아예 건너뛴다</b> —
        /// 조용한 상자 50개면 그것만으로 프레임당 600 KB 가 사라진다.</para>
        /// </summary>
        private void UploadHeight(Panel panel, bool allowPartial)
        {
            SnowHeightFieldCpu field = panel.Field;
            if (field == null || panel.HeightTex == null) return;

            // 세운 직후에는 텍스처가 빈 상태다. 더러운 사각형만 올리면 나머지가 쓰레기로 남는다.
            if (panel.NeedsFullUpload) { FullUpload(panel, field); return; }

            // 깨끗하면 아무것도 안 한다 - 지면도 상자도 같다.
            if (field.RenderDirtyChunks.Count == 0) return;

            if (allowPartial && _partialUploadSupported && TryUploadDirtyRect(panel, field))
            {
                field.ClearRenderDirty();
                return;
            }

            FullUpload(panel, field);
        }

        private void FullUpload(Panel panel, SnowHeightFieldCpu field)
        {
            panel.HeightTex.SetPixelData(field.HeightMm, 0);
            panel.HeightTex.Apply(false, false);
            DebugUploadedBytesLastFrame += (long)field.HeightMm.Length * 2;
            panel.NeedsFullUpload = false;
            field.ClearRenderDirty();
        }

        /// <summary>
        /// 더러운 청크의 바운딩 사각형만 올린다. 사각형이 없거나 스테이징 상한을 넘으면
        /// <c>false</c> 를 돌려주고, 호출자가 전체 업로드로 되돌아간다.
        /// </summary>
        private bool TryUploadDirtyRect(Panel panel, SnowHeightFieldCpu field)
        {
            IReadOnlyList<int> dirty = field.RenderDirtyChunks;
            if (dirty.Count == 0) return false;

            SnowFieldGeometry geo = field.Geo;
            if (!SnowPanelTiling.TryDirtyCellRect(geo, dirty,
                                                  out int cx0, out int cz0, out int cx1, out int cz1))
            {
                return false;
            }

            int w = cx1 - cx0 + 1;
            int h = cz1 - cz0 + 1;
            int size = SnowPanelTiling.StagingSizeFor(w, h, MaxStagingCells);
            if (size == 0) return false;                       // 너무 크다 - 전체가 싸다

            Texture2D staging = GetStaging(size);
            if (staging == null) return false;

            if (_stagingScratch == null || _stagingScratch.Length < size * size)
                _stagingScratch = new ushort[size * size];

            ushort[] src = field.HeightMm;
            for (int r = 0; r < h; r++)
            {
                System.Array.Copy(src, (cz0 + r) * geo.ResX + cx0, _stagingScratch, r * size, w);
            }

            staging.SetPixelData(_stagingScratch, 0);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, 0, 0, 0, 0, w, h, panel.HeightTex, 0, 0, cx0, cz0);
            DebugUploadedBytesLastFrame += (long)size * size * 2;
            return true;
        }

        private Texture2D GetStaging(int size)
        {
            if (_staging.TryGetValue(size, out Texture2D tex) && tex != null) return tex;

            tex = new Texture2D(size, size, TextureFormat.R16, false, true)
            {
                name = $"SnowUploadStaging{size}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _staging[size] = tex;
            return tex;
        }
```

- [ ] **Step 4: `Panel` 에 `NeedsFullUpload` 를 추가하고 `Build`·`Dispose` 를 맞춘다**

`Panel` 에 추가:

```csharp
            /// <summary>세운 직후에는 텍스처가 빈 상태다. 반드시 한 번은 통째로 올려야 한다.</summary>
            public bool NeedsFullUpload = true;
```

`Dispose` 마지막(`_panels.Clear();` 앞)에 추가:

```csharp
            foreach (KeyValuePair<int, Texture2D> kv in _staging)
                if (kv.Value != null) Destroy(kv.Value);
            _staging.Clear();
            _stagingScratch = null;
```

- [ ] **Step 5: 컴파일과 회귀를 확인한다**

```bash
~/.unity/bin/unity command recompile --timeout 280
~/.unity/bin/unity command run_tests --mode EditMode --test_filter "PPack.Snow" --timeout 400
```

- [ ] **Step 6: 업로드가 실제로 줄었는지 실측한다**

`Snow_Terrain_Test` 에서 Play 한 뒤 두 상태를 잰다:

```bash
~/.unity/bin/unity command eval --code 'var v = UnityEngine.Object.FindFirstObjectByType<PPack.SnowDisplaceView>(); return $"uploaded={v.DebugUploadedBytesLastFrame}";'
```

합격선 셋:
1. **정지 중 0 바이트.**
2. **눈덩이를 굴리는 중 100 KB 이하**(240 × 220 m 에서 전체는 6.45 MB).
3. **화면이 안 바뀐다** — 자국이 실시간으로 갱신되고, 안 올라간 자리가 없다.

---

## Task 5: 검증과 문서

**파일:**
- 수정: `Assets/Game/InGame/Snow/AGENTS.md`
- 수정: `docs/INDEX.md`

- [ ] **Step 1: PlayMode 회귀를 확인한다**

```bash
~/.unity/bin/unity command run_tests --mode PlayMode --test_filter "PPack.Snow" --async_tests true --timeout 300
```
기대: 62개 중 **기존 빨간 15개와 정확히 동일**. 새로 빨개진 것이 하나라도 있으면 **멈추고 원인을 찾는다.**

- [ ] **Step 2: 화면 증거를 남긴다**

`docs/images/verify/snow_tiles_culling.png`(카메라를 돌렸을 때 삼각형 수가 변하는 것)와
`docs/images/verify/snow_tiles_no_seam.png`(타일 경계에 실금이 없는 근접 화면).

- [ ] **Step 3: 폴더 규칙을 갱신한다**

`Assets/Game/InGame/Snow/AGENTS.md` 의 "규모 실측" 절 **앞**에 아래를 넣는다.
`{{ }}` 안은 Task 3·4 에서 실제로 잰 값으로 채운다 — **그 값이 아직 없어서 비워 둔 것이고,
다른 곳은 그대로 쓴다.**

```markdown
## 지면 눈은 타일로 그린다 (2026-08-25)

지면 패널 한 장이 타일 N장이 됐다. **타일은 순수한 뷰 개념이다** — 머티리얼도 텍스처도 UV 공간도
한 벌을 공유하고, 타일은 같은 삼각형 집합의 부분집합일 뿐이다. 그래서 **셰이더가 한 줄도 안 바뀌었고
이음매가 물리적으로 생길 수 없다.** 크기는 `SnowDisplaceView.Tile Size M`(기본 16 m, 0 이면 안 쪼갬).

- ⚠ **정점 좌표는 전역 인덱스로 계산한다**(`SnowPanelTiling.LatticePos`). 타일마다 로컬 원점으로
  다시 계산하면 부동소수 결과가 갈려 **공유 모서리에 실금**이 간다. `SnowPanelTilingTests` 의
  `이웃_타일이_공유하는_모서리_정점은_비트단위로_같다` 가 그것을 굳힌다.
- **바운즈는 타일별로 그 타일이 덮는 셀의 바닥 범위로 잡는다.** 필드 전체 범위를 쓰면 타일이
  여러 장이어도 바운즈가 전부 같아 **컬링이 안 든다** — 쪼갠 의미가 없어진다.
- 실측: 타일 {{타일 수}}장 · 삼각형 {{전}} → {{후}}(카메라 방향에 따라 변한다) ·
  드로우 콜 {{전}} → {{후}} · 눈의 프레임 비용 {{전}} → {{후}}.

### 높이 텍스처는 더러운 사각형만 올린다

- ⚠ **`RenderDirtyChunks` 는 `BeginStep` 이 비우지 않는다.** 한 프레임에 `FixedUpdate` 가 0번일 수도
  여러 번일 수도 있어서, 스텝마다 비우면 `LateUpdate` 는 마지막 스텝 것만 본다. **렌더가 가져갈 때만**
  (`ClearRenderDirty`) 비운다. 네트워크가 쓰는 `ChangedChunks` 와 목적이 다르므로 목록이 둘이다.
- 절차: 더러운 청크의 바운딩 사각형 → 2의 거듭제곱 스테이징 텍스처 → `Graphics.CopyTexture`.
- **폴백 셋. 최악이 지금과 같다** — 사각형이 256 셀을 넘으면 · `copyTextureSupport` 에 `Basic` 이
  없으면 · 패널을 방금 세웠으면 전체 업로드.
- **상자는 부분 업로드를 안 하되 깨끗하면 건너뛴다.** 12 KB 라 기계값이 아낌값보다 크다.
- ⚠ **사각형이 하나다.** 두 플레이어가 맵 양 끝에서 밀면 바운딩이 맵 전체가 되어 폴백한다.
  사각형 K개로 쪼개는 것은 **실측 전에는 안 한다**.
- 실측: 정지 중 {{바이트}} · 굴리는 중 {{바이트}}(전체는 {{전체}}).

설계 문서: `docs/specs/2026-08-25-snow-render-tiles.md` · 계획: `docs/plans/2026-08-25-snow-render-tiles.md`.
```

- [ ] **Step 4: `docs/INDEX.md` 를 갱신한다**

현재 상태 절에 결과 한 문단을 추가하고 계획 문서를 목차에 넣는다.

- [ ] **Step 5: 체크인 — 사용자 승인 후**

⚠ **`cm ci` 를 임의로 하지 않는다.** 20.3 MB 맵 에셋 건이 미결이라 묶어서 한 번에 한다.
승인이 나면 루트 `AGENTS.md` 의 "Version control" 절대로:

1. `cm status --changed --private` 로 **전체 경로 목록을 먼저** 만든다.
2. 수정된 파일은 `cm checkout` 을 먼저 한다(안 하면 "is not changed in current workspace" 로 거절).
3. `.meta` 를 자산과 **같이** 이름 짓는다.
4. 삭제와 수정을 **같은 체크인에 섞지 않는다**.
5. 체크인 뒤 `cm status --changed --private` 로 남은 것이 없는지 확인한다.

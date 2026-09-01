# Neighborhood Modular Map

새로 만든 연속 바닥을 기준으로 직접 구성한 로우폴리 교외 주택가 맵입니다. 기존 FBX와 원본 컨셉 아트 투영은 현재 씬에서 사용하지 않습니다.

- 씬: `Scenes/Neighborhood_ConceptMap.unity`
- `ConceptGround_FillsModelHoles`는 ImageGen으로 만든 `TX_Neighborhood_GroundPlate.png`를 사용하는 연속 바닥입니다.
- `ModularNeighborhood/Houses` 아래에 집 8채가 구획별로 독립된 루트로 배치되어 있어 사용자가 Scene View에서 직접 이동·회전·교체할 수 있습니다.
- `ModularNeighborhood/Vegetation`에는 나무 20그룹과 관목 16그룹이 있고, `StreetDetails`에는 울타리와 돌이 있습니다.
- `GrassDioramaBase`가 외곽과 아래쪽 틈을 완전히 막습니다.
- 연속 바닥에는 `MeshCollider`가 있어 차량 또는 플레이어 테스트에서 빈 곳으로 빠지지 않습니다.
- 집과 조경은 매트한 URP Lit 재질과 Unity 기본 메시를 조합한 실제 3D 모듈입니다. 배치된 장식 프리미티브의 자동 Collider는 제거해 차량 동선 테스트를 방해하지 않습니다.
- `ModularNeighborhood/InvisibleBoundary`에는 Renderer가 없는 `BoxCollider` 벽 4개가 바닥 외곽을 둘러싸고 있어 차량과 플레이어가 맵 밖으로 떨어지지 않습니다. 벽 높이는 6m이며 Trigger가 아닙니다.
- **플레이 가능한 시스템이 이 씬에 들어와 있습니다(2026-08-14).** `Vehicle/Tests/Vehicle_Prototype_Test.unity`에서 검증된 요소를 옮겨왔고, `NeighborhoodMap`의 자식이 아니라 씬 루트에 나란히 있습니다 — `PF_VehicleProto`(차량/플레이어), `Main Camera`(`VehicleCamera`), `CleanVfx`, `JumpObstacle_0~2`, `Autopilot`(비활성), `ReflectionProbe`, `PostFX_Bloom`. **`Rebuild Modular Neighborhood`는 `ModularNeighborhood`와 `Model`만 지우고 다시 만들어서 이 루트 오브젝트들은 건드리지 않습니다** — 안심하고 재생성을 돌려도 됩니다. `ConceptCamera`는 `VehicleCamera`와 MainCamera 태그가 겹쳐 삭제했습니다.
- **청소 대상은 먼지가 아니라 눈입니다(2026-08-15).** 이 맵은 `../../Snow/` 시스템을 씁니다 — `SnowStage`(권위 격자 `SnowField` 소유) + `SnowPanel`(`SnowPanelBuilder` 로 만든 그리드 메시 + `M_Snow`)을 씬 루트에 두고, 차량 프리팹에 이미 들어 있는 눈 리그(`SnowPad`·`SnowProbe_*`·`SnowVehicleDrag`)의 `_stage` 를 이 `SnowStage` 에 물렸습니다. **`_stage` 배선은 씬마다 해야 합니다** — 프리팹은 씬 오브젝트를 참조할 수 없어 비어 있는 채로 옵니다.
  - `SnowStage`: origin `(-36, -40)`, size `72 × 80`(바닥 플레이트 73 × 81 안쪽), cell `0.0625`, 최대·시작 깊이 30cm. `SnowPanel` 은 같은 72 × 80, 정점 간격 0.25, y = 0.
  - 실측: 첫 주행에서 `covered = 1` · `speedFactor = 0.55`(눈 저항 감속 정상), 제설 자국이 차폭보다 넓게 남고 스프레이 파티클 약 120개.
- **먼지 설정은 제거했습니다.** 2026-08-14 에 `ConceptGround_FillsModelHoles` 를 `M_NeighborhoodGround_Dust` + `DustPaintTarget` 으로 먼지 표면으로 만들었다가, 눈으로 교체하며 원래 머티리얼(`M_Neighborhood_ConceptGround`)로 되돌리고 `DustPaintTarget` 과 `CleanVfx`(먼지 VFX 리그)를 뺐습니다. 두 청소 시스템이 같은 바닥에 겹치면 제설로 드러난 노면에 먼지 걸레 자국이 같이 보입니다. `Materials/M_NeighborhoodGround_Dust.mat` 은 되돌릴 때를 위해 남겨뒀지만 현재 씬에서는 쓰지 않습니다.
- **Trash 프롭 8종이 거리를 따라 배치돼 있습니다.** `../../Trash/Prefabs/`의 실제 프리팹 인스턴스(테스트 큐브 아님)이고, 이미 `SuctionTarget` + `Suckable` 레이어가 붙어 있어 `VehiclePullAbility`로 바로 조준·흡입됩니다.
- **차량 외형이 플레이스홀더 박스에서 산타 장난감차 모델로 바뀌었습니다.** `../../Vehicle/AGENTS.md` 참조.
- **맵 전체 스케일을 2배로 키웠습니다(2026-08-15).** `NeighborhoodMap` 루트의 `localScale` 을 `1 → 2` 로 준 것이고, 배치를 늘린 게 아니라 **말 그대로 전체를 확대**한 것입니다 — 집·나무·울타리도 같이 2배가 되고 바닥은 36.5 × 40.5 → 73 × 81 이 됩니다. 집 8채·나무 20그룹 같은 구성 수는 그대로이므로 `Rebuild Modular Neighborhood` 를 다시 돌려도 안전합니다(빌더는 로컬 좌표로 짓고, 스케일은 루트가 쥡니다).
  - **차량은 `NeighborhoodMap` 밖의 씬 루트라 같이 커지지 않습니다** — 의도된 결과로, 산타 장난감차가 실제 크기 주택가를 달리는 비율이 됩니다.
  - 스케일을 바꾸면 **`SnowStage`·`SnowPanel` 은 따로 맞춰야 합니다.** 둘 다 씬 루트라 같이 확대되지 않고, 크기도 트랜스폼이 아니라 `sizeMeters`(월드 미터) 로 정해집니다. 눈 깊이 30cm 는 그대로 두었습니다 — 차가 안 커졌으므로 차 기준 눈 깊이가 유지돼야 주행감이 같습니다.
  - Trash 프롭 8개도 씬 루트라 넓어진 도로에 맞춰 다시 흩었습니다.

## 편집과 재생성

- 집을 옮길 때는 `Houses/House_Lot_*` 루트를 이동합니다.
- 나무와 관목은 `Vegetation`의 개별 루트를 이동합니다.
- 전체 배치를 초기 상태로 다시 만들려면 Unity 메뉴 `PPack > Map > Rebuild Modular Neighborhood`를 실행합니다.
- 배치를 직접 수정한 뒤에는 재생성 메뉴를 실행하면 수정 사항이 초기화되므로 주의합니다.

## 아트 방향

- 청록·크림·적갈색·머스터드 주택과 짙은 청회색 지붕
- 밝은 황록색 잔디, 차콜 아스팔트, 따뜻한 아이보리 보도
- 낮은 거칠기보다 매트한 디오라마 표면을 우선하고 강한 금속성·젖은 광택은 사용하지 않습니다.
- 직교 카메라와 부드러운 방향광으로 원본의 보드게임형 아이소메트릭 구도를 유지합니다.

## 바닥 생성 자산

`TX_Neighborhood_GroundPlate.png`는 built-in ImageGen으로 제작한 집과 조경이 없는 바닥 전용 이미지입니다. 기존 모델용 `SM_Neighborhood_Diorama.fbx`, `NeighborhoodObjectProjection.shader`, `TX_Neighborhood_ConceptProjection.png`는 비교와 롤백을 위해 보존하지만 현재 씬 계층에는 포함하지 않습니다.

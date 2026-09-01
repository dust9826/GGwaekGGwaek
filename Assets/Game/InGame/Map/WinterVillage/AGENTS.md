# Winter map design reference

이 폴더는 겨울 맵의 콘셉트, 레이아웃 판단, 프롭 사용 계획을 보관한다. 실제 플레이용
씬과 프리팹을 만들 때는 이 문서뿐 아니라 `Assets/Game/InGame/Map/AGENTS.md`를 반드시
함께 읽는다.

## 기준 자산과 변경 경계

기준 팩은 다음 위치의 `Low Poly Locations Ultimate Pack`이다.

- 샘플 씬: `Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Scenes/Low Poly Winter/`
- 개별 프리팹: `Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/`
- 원본 모델: `Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Models/`

샘플 씬, 벤더 프리팹, FBX, 머티리얼은 **참고 전용**이다. 직접 수정하지 않는다. 필요한
조합은 개별 프리팹을 실제 맵에 인스턴스하거나, 프로젝트 소유 폴더인
`Assets/Game/InGame/Map/` 아래에 변형 프리팹을 만들어 사용한다. 완성된 프로덕션 씬을
이 `map design` 폴더에 두지 않는다.

## 분석한 겨울 샘플

2026-08-15에 아래 10개 씬의 실제 카메라 구도, 루트 직계 배치, 위치, 높이, 스케일을
확인했다. 각 씬은 약 45–50m 크기의 디오라마이고 루트 직계 배치는 43–74개다. 이 숫자는
플레이 맵의 할당량이 아니라 상대적인 밀도 참고값이다.

| 씬 | 핵심 구성 | 배치에서 배울 점 |
|---|---|---|
| `Location christmas village` | 목조 주택 3채, 중앙 크리스마스트리, 개울, 우물 | 건물을 삼각형으로 두고 가운데를 비운다. 갈런드는 건물 사이의 중경을 연결하고, 큰 전나무와 바위는 외곽 프레임을 만든다. |
| `Location christmass fair` | 아이스링크와 트리, 장터 오두막, 깃발·기둥 링 | 하나의 활동 공간을 중심에 두고 반복 프롭으로 원형 동선을 읽힌다. 삽, 썰매, 눈사람은 주 동선 밖에 생활 흔적으로 흩어진다. |
| `Location country winter house` | 단독주택, 차도·보도, 울타리, 전신주 | 도로를 1차 축으로 만들고 주택 대지를 울타리로 분리한다. 자동차, 쓰레기통, 소화전, 표지판은 기능적으로 맞는 도로변 위치에 둔다. |
| `Location crystal mountain` | 거대한 수정 산, 수정 군집, 눈 덮인 바위 | 강한 수직 랜드마크 하나를 두고 작은 군집을 방사형으로 낮춘다. 인공 생활 프롭을 섞지 않아 주제를 선명하게 유지한다. |
| `Location life north pole` | 단차 지형, 이글루 3채, 모닥불 3개, 전경 물 | 각 테라스에 이글루와 모닥불을 한 쌍의 생활 노드로 둔다. 나무와 바위는 단차 경계를 강조하고 열린 설원은 남긴다. |
| `Location river winter house` | 강변 주택, 부두, 보트, 숲 | 집에서 부두와 물로 이어지는 방향성이 가장 먼저 읽힌다. 통·썰매·가로등은 출입구와 부두 주변에 묶고, 연기는 지붕 위에만 둔다. |
| `Location santa claus house` | 큰 산타 하우스, 작은 주택, 트리, 썰매·순록 | 큰 건물을 상단 중심 랜드마크로 두고 작은 집을 하단 외곽에 분산한다. 선물, 순록, 썰매는 트리 주변의 이야기 군집이 된다. |
| `Location ski resort` | 대각선 스키 슬로프, 리프트, 상단 롯지 | 높이 차와 사선 동선이 핵심이다. 깃발은 코스를 표시하고 바위·눈더미는 슬로프 가장자리를 잡되 활강면은 비운다. |
| `Location winter road forest` | 사선 도로, 차량 2대, 안내판, 평행한 강 | 도로를 비워 둔 강한 이동 축으로 사용한다. 차량은 축 위, 표지판은 굽이나 진입점, 나무와 바위는 도로 바깥에 둔다. |
| `Location winter town` | 강을 둘러싼 집 5채, 중앙 다리, 굴뚝 연기 | 다리가 분리된 구역을 연결하는 중심 랜드마크다. 집은 물을 둘러보게 회전하고, 연기는 지붕 위치를 반복해 마을의 리듬을 만든다. |

## 공통 공간 문법

겨울 프롭을 고르게 흩뿌리지 않는다. 모든 배치는 아래 계층을 따른다.

1. **바닥과 이동 축** — 눈 지형, 도로, 강, 다리, 경사로로 먼저 큰 흐름을 만든다.
2. **주 랜드마크 1개** — 큰 집, 링크, 수정 산, 다리, 롯지처럼 멀리서 방향을 잡아 주는
   대상을 정한다.
3. **보조 노드 2–5개** — 작은 집, 이글루, 장터 부스, 캠프처럼 플레이 공간을 구획한다.
4. **경로 안내 반복물** — 가로등, 울타리, 깃발, 기둥, 갈런드, 표지판을 실제 동선에
   맞춰 반복한다.
5. **생활 군집** — 서로 관련된 소형 프롭 2–5개를 한 묶음으로 놓아 사용 흔적을 만든다.
6. **외곽 프레임** — 큰 나무와 바위로 실루엣, 낙하 경계, 시선 차폐를 만든다.
7. **여백** — 걷기, 청소, 카메라 회전, 멀티플레이 교차에 필요한 빈 공간을 마지막까지
   보존한다.

샘플의 나무와 바위는 주로 중심에서 반경 18–24m의 외곽에 있고, 랜드마크와 생활
프롭은 중심과 중경에 있다. 이 원리는 사용하되 원형으로 균일 배치하지 않는다. 길,
지형 단차, 건물 시선축에 따라 한쪽은 조밀하고 반대쪽은 열어 둔다.

## 프롭 선택표

### 바닥과 지형

- 설원 베이스: `Prefabs/Lands/Low Poly Winter/`
- 물과 얼음: `Prefabs/Waters/Low Poly Winter/`
- 도로와 보도: `Prefabs/Roads/`
- 자연 절벽·가장자리: `Prefabs/Stones/Stones winter/`
- 수정 지형: `Prefabs/Stones/Crystal stones/`

팩의 완성형 `land ...` 프리팹은 디오라마 형태와 단차를 참고하는 데 좋지만, 실제
청소 맵의 플레이 바닥으로 그대로 채택하지 않는다. 바닥은 연속 Collider와 청소용
UV0 조건을 충족하는 프로젝트 소유 메시로 다시 판단한다.

### 건축 랜드마크

- 일반 마을: `Prefabs/Houses/Winter houses/Winter houses/`
- 목조 휴양 마을: `Prefabs/Houses/Winter houses/Wooden winter houses/`
- 단독주택: `Prefabs/Houses/Winter houses/country winter house.prefab`
- 강변: `Prefabs/Houses/Winter houses/river winter house.prefab`
- 축제 장터: `Prefabs/Houses/Winter houses/Fairground christmas houses/`
- 산타 마을: `Prefabs/Houses/Winter houses/Christmas houses/`
- 북극 캠프: `Prefabs/Houses/Winter houses/igloo house.prefab`
- 스키장: `Prefabs/Houses/Winter houses/ski house.prefab`

건물은 정면을 모두 카메라 쪽으로 맞추지 않는다. 출입구는 실제 접근 경로나 광장을
향하게 하고, 주변 건물은 주 랜드마크 또는 중심 활동 공간을 바라보도록 약간씩
회전한다.

### 겨울 마을 진입로 정렬 체크리스트

`../AGENTS.md`의 건물–보행로–도로 정렬 계약을 이 폴더의 모든 집에 적용한다. 특히 눈이나
장식으로 틈을 가릴 예정이라는 이유로 정렬 검사를 생략하지 않는다. 눈이 없는 기본 메시 상태에서
아래 항목을 먼저 통과해야 한다.

1. 실제 문, 가장 낮은 계단, 포치 개구부를 기준으로 집 쪽 끝점과 중심선을 잡는다.
2. 집 쪽 중심 오차 `≤ 0.05 m`, 문 정면 접선 오차 `≤ 5°`를 수치와 사선 캡처로 확인한다.
3. 도로/산책로 쪽은 최종 Surface 메시 안으로 `0.10–0.25 m` 겹치고, 평면 접속 높이 차이는
   `≤ 0.03 m`로 맞춘다. 의도한 연석 단차는 별도의 계단·랜딩·램프/절개로 명시한다.
4. T자 접속은 도로 가장자리에 거의 수직(`≤ 15°`)이어야 한다. 끝점을 옮길 때는 마지막
   `1–2 m`에 변위를 분산해 완만한 곡선을 유지하고 마지막 한 행만 당겨 뾰족하게 만들지 않는다.
5. 접속부의 가로 Border/끝 캡과 보행을 막는 프롭·Collider가 없는지 확인한다.
6. 집이나 도로를 이동하거나 EasyRoads spline을 다시 bake한 뒤에는 1–5를 전부 반복한다.

서로 다른 두 도로의 끝과 끝을 이어야 할 때는 생성 mesh를 당겨 겹치지 말고 두
`TerrainRoadPath` 원본을 선택한 뒤 Road Builder의 `Connect Selected Road Ends`를 사용한다.
이 도구는 가장 가까운 끝점을 공통 중심으로 맞추고 양쪽 마지막 spline 구간의 위치·접선·높이를
함께 블렌딩한다. 한쪽의 마지막 점만 움직여 삼각형 틈, 겹침 돌기 또는 직각 끝 캡을 숨기는 방식은
금지한다. 연결 뒤에는 해당 Scene의 도로 재생성 명령을 실행하고 최종 mesh/collider에서 이음매를
탑다운과 낮은 사선으로 다시 확인한다. 같은 방향으로 갈라지는 두 지선은 end-to-end 연결이 아니므로
이 도구로 강제하지 말고 별도 교차로/분기 구조를 사용한다.

### Road First World EasyRoads 작업 계약

`Scenes/WinterVillage_RoadFirstWorld.unity`의 도로는 씬에 남겨 둔 `TerrainRoadPath` spline을
편집 원본으로 사용하고, 메뉴 `PPack/Map/Road First World/Rebuild Roads With EasyRoads3D Pro`로
EasyRoads 표시 메시와 `MeshCollider`를 다시 만든다. 생성된 EasyRoads 메시 정점을 직접 옮기거나
스케일해 고치지 않는다. 재생성기는 매번 `TD_RoadFirstWinterWorld_PreEasyRoads`의 원본 높이맵에서
시작해 누적 지형 변형을 막고, 도로 중심선을 최대 약 `10.5°` 경사로 제한한다. 차로 폭을 평탄화한
뒤 높이 차이에 따라 약 `6–44 m`의 동적 shoulder/grading band를 주변 원지형에 `smoothstep`으로
섞는다. 도로 옆 지형이 수직 절벽처럼 남지 않도록 저각도에서 확인하고,
`Audit EasyRoads Terrain Profiles`의 roadside bank가 `30°`를 넘는 상태로 저장하지 않는다. 여러 길이 겹치는 곳은
주 간선의 목표 높이를 우선하고 지선의 영향을 점진적으로 섞어 교차부 단차와 울퉁불퉁함을 만들지 않는다.

도로 변경 뒤에는 반드시 아래 순서로 확인한다.

1. `Validate EasyRoads3D Roads`로 도로 수, 표시 메시, `MeshCollider`, 지면 관통/부유, Missing
   Script를 검사한다.
2. `Audit EasyRoads Terrain Profiles`로 종단 경사, 횡경사, 국소 요철, 양쪽 지형보다 꺼진 정도를
   검사한다. 목표 상한은 종단 경사 `≤ 12°`, 교차 접속을 포함한 횡경사 `≤ 6°`, 국소 요철 `≤ 0.04 m`, 양측 대비
   함몰 `≤ 0.20 m`다.
3. `Capture District Grounding QA`로 전체 탑뷰, 모든 구역의 낮은 사선 뷰, 스키장 역방향 뷰를
   새로 찍는다. 수치만 통과해도 교차부 원형 보정 메시, 막다른 길 끝, 다리 접속, 집 진입부,
   카메라에 가까운 도로에서 급격한 높이 변화·삼각형 틈·겹침 깜빡임이 보이면 저장하지 않는다.
4. 플레이 모드에서 실제 차량으로 직선, 굽이, 교차로, 진입로를 통과하며 바퀴가 걸리거나 차체가
   튀는 곳이 없는지 확인한다. 주행 수치를 바꿔 지형 문제를 가리지 않는다.

막다른 길 종단은 도로 폭보다 큰 원판으로 덮지 않는다. spline 끝의 접선을 기준으로 길 바깥쪽에만
반원형 종단을 만들고, 직선 지름은 기존 도로 끝단과 겹쳐 하나의 연속 실루엣으로 읽혀야 한다.
교차로 보정 원판도 분기 연결에 필요한 범위만 사용하며 독립된 원형 광장처럼 보이면 실패다.

도로를 재생성하거나 프롭을 추가한 뒤에는 `Buildings_Individual`, `Nature_Individual`,
`Props_Individual`을 모두 다시 검사한다. 집·나무·바위·가로등·표지판·장식 프롭의 Renderer
footprint가 차로와 shoulder 보호 폭을 침범하면 도로 접선의 바깥 방향으로 옮기고 지형에 다시
grounding한다. 가로등과 표지판은 길을 안내하되 바퀴가 지나가는 면에는 두지 않으며, 교차로와
종단에서는 한 길을 피해서 다른 길 위로 옮기는 일이 없도록 모든 연결 도로에 대한 여유를 재검사한다.

EasyRoads가 만드는 UV 축은 메시 청크에 따라 달라질 수 있으므로, 도로 본체에 UV 기반 가장자리
띠를 다시 켜지 않는다. 도로는 균일한 흙길 재질을 사용하고 눈 가장자리는 주변 지형과 종단 캡으로
표현한다. UV 기반 띠가 켜져 흰 격자나 회색 테두리가 보이는 상태는 잘못된 결과다.

현재 콘셉트 맵의 `VillageHouse_05`처럼 산책로와 계단 입구 사이가 짧으면 억지로 가느다란 길을
만들지 않고 열린 apron/랜딩으로 연결할 수 있다. `VillageHouse_08`처럼 문턱이 높으면 지면 길을
문 아래에 묻지 말고 문 폭에 맞춘 랜딩과 보이는 계단을 별도 메시로 둔다. 둘 다 예외가 아니라
같은 정렬 계약을 지형 조건에 맞게 푼 사례다.

### 경사면 주택 부지 성형 계약

경사에 집을 배치할 때 집을 기울이거나, 집 아래의 빈 공간을 계단·큰 바위·검은 쐐기 메시로
가리지 않는다. `WinterVillage_HillsideMap`에서 사용한
`Generated/HouseGrading/MSH_MountainGround_UnifiedContinuous.asset` 방식처럼 **지형 자체를 먼저
평탄화하고 주변 경사와 연속적으로 다시 잇는 것**을 기본 해법으로 한다.

1. 처마와 장식은 제외하고 벽·기초가 실제로 닿는 Renderer/Collider footprint를 구한다. 사방에
   최소 `0.35–0.80 m`의 평탄 여유를 더해 집 전체를 받치는 수평 pad 범위를 정한다.
2. pad의 높이는 문턱, 연결될 진입로와 원지형 높이를 함께 샘플링해 정한다. 집 Transform을 경사에
   맞춰 기울이지 않으며, 구조 footprint 안의 바닥은 하나의 수평면이어야 한다.
3. pad 바깥에는 보통 `2–4 m` 폭의 grading band를 두고 `smoothstep` 계열 가중치로 원지형에
   완만하게 복귀시킨다. 경계 정점의 위치와 노멀을 원지형에 맞춰 절벽, 검은 틈, 얇은 겹침선,
   떠 있는 모서리를 남기지 않는다.
4. 집 문 앞에는 수평 landing을 먼저 확보하고, landing에서 진입로까지 높이와 방향을 여러 정점에
   나눠 연결한다. 마지막 정점 한 줄만 꺾어 급경사나 삼각형 쐐기를 만들지 않는다.
5. 보이는 지형 메시와 주행 Collider는 같은 성형 결과를 사용한다. 별도의 수직 BoxCollider,
   도로를 침범하는 받침, 차량 바퀴가 걸리는 얇은 경계 Collider로 외형 문제를 숨기지 않는다.
6. 저장 전 눈 표현을 잠시 끈 상태에서도 집 아래가 모두 지면과 접하는지 확인한다. 위에서 한 장,
   네 방향 사선에서 각각 확인하고 문–landing–진입로–본길을 차량 크기의 gizmo 또는 실제 차량으로
   횡단해 걸림과 급격한 피치 변화가 없어야 한다.

집·문·도로 위치가 바뀌면 기존 grading mesh를 그대로 늘이거나 돌려 쓰지 말고 footprint와 주변
지형을 다시 샘플링해 생성한다. 생성물은 `Generated/HouseGrading/` 아래에 두고, 집 이름과 대응
관계를 추적할 수 있게 이름을 붙인다.

### 외곽 실루엣과 자연 채움

- 침엽수: `Prefabs/Trees/Winter trees/Fir trees winter/`
- 소나무: `Prefabs/Trees/Winter trees/Fir trees winter/Pine trees winter/`
- 마른나무: `Prefabs/Trees/Winter trees/Dry trees winter/`
- 쓰러진 나무: `tree trunk winter`
- 눈 바위: `Prefabs/Stones/Stones winter/`
- 눈더미: `Prefabs/Environment/Winter env/snowdrift.prefab`

나무는 큰 것, 중간, 작은 것, 휘거나 기운 변형을 섞는다. 같은 모델의 복제본을 같은
간격과 회전으로 세우지 않는다. 샘플처럼 보통 0.75–1.0 범위의 균일 스케일 변화를
사용하고, 한 나무의 XYZ를 따로 늘이지 않는다.

프로젝트 소유 `PF_WinterTreeSway_*` 프리팹의 충돌 변형은 `TreeWindSway`가
`SwayPivot`의 회전과 균일 스케일을 함께 움직인다. 밑동 위치와 Collider는 고정하며 비균일 squash는 사용하지 않는다.
충돌 최대 기울기 `10°`, 킥 `4.2°`, spring `16`, damping `2.1`로 앞뒤 감쇠 진동을 만들고,
스케일은 최대 `1.08`까지 커졌다가 약 `0.98`까지 작아진 뒤 복원한다. 공용 `PF_WinterTreeImpactFeedback`은 Feel `MMF_Sound`로 모노 목재 충격음 3종을
랜덤 선택하고 pitch `0.94–1.03`, 완전 3D logarithmic rolloff(`2–22 m`)로 충돌 위치에서
재생한다. 기존 눈 낙하와 약한 카메라 반응을 함께 쓰되, 만화식 스쿼시를 다시 섞지 않는다.

### 플레이어 차량은 Delivery와 같은 주행·충돌·리스폰 경로를 쓴다

`WinterVillage_ConceptMap`의 `PlayerVehicle`은 `Vehicle/Prefabs/PF_VehicleProto` 인스턴스다.
주행 수치나 `VehicleImpactRelay`를 씬에서 따로 덮지 않는다. 따라서 장애물 충돌, 차체 반응,
관성·드리프트는 `Delivery_RequestFlow_Test`와 같은 프리팹 구현을 그대로 따른다.

도로의 `Curb_NN` 콜라이더 윗면은 짝이 되는 `Road_NN` 윗면과 맞추고, 도로 바깥에는 Renderer 없는
`Geometry/Routes/VehicleCurbRamps` 경사 콜라이더를 둔다. 현재 12개 연석과 24개 경사면이다.
차량이 도로 가장자리 8.5cm 턱에 걸리거나, 눈밭으로 벗어난 뒤 31.5cm 수직면 때문에 도로로
복귀하지 못하는 현상을 막는다. 보이는 도로·연석 메시는 바꾸지 않는다.

Hillside처럼 도로가 여러 높이와 경사로 이어지는 맵도 `WinterVillage_ConceptMap`과 같은
`PF_VehicleProto` 및 주행 수치를 사용한다. 씬에서 가속·회전·그립·드리프트 값을 따로 조정해
도로 문제를 가리지 않는다. 모든 직선 연석은 구간 전체 길이에 걸쳐 좌우 한 쌍의 Renderer 없는
`VehicleCurbRamps`를 가지며, 기본 단면은 `1.24 x 0.40 m`, 로컬 롤은 `±14.7°`로 한다. 경사면의
안쪽 끝은 도로 콜라이더 안으로 최소 `0.10 m` 겹쳐 차량 바퀴가 수직 연석 면에 먼저 닿지 않게 한다.

원형 이음부나 고도 접합부의 연석 콜라이더는 시각 메시와 분리해 차량 진행을 막지 않게 하거나,
동일한 완만한 전이 콜라이더를 둔다. 도로·연석을 이동, 회전, 리스케일 또는 재생성했으면 램프도
같은 작업에서 다시 정렬한다. 검증은 차량으로 도로 양쪽 가장자리를 안→밖, 밖→안 두 방향으로
직접 횡단해 멈춤·튀어 오름·바퀴 끼임이 없는지 확인한다. **도로 외곽 및 도로 구간 이음부의
수직 Collider 면에 차량이 걸리는 상태로 맵을 저장하지 않는다.**

`Gameplay/PlayerSpawn`의 `VehicleRespawnPoint`가 시작·복귀 자세를 소유한다. 강의 `Water_01~04`
Renderer bounds 아래에는 `RiverRespawnVolumes` 네 개를 두고 수면보다 5cm 낮은 지점부터 차량을
잡는다. 복귀는 반드시 `VehicleController.RespawnAt`을 거쳐 위치·회전·선형속도·각속도·내부 yaw를
함께 초기화한다. 트랜스폼만 텔레포트하는 별도 리스폰 코드를 만들지 않는다.

### ConceptMap 변경은 Delivery 통합 씬까지 같은 작업에서 동기화한다

`Scenes/WinterVillage_ConceptMap.unity`가 맵과 공용 플레이 요소의 **유일한 원본**이다.
`../../Delivery/Tests/Delivery_RequestFlow_Test.unity`는 이 원본의 사본 위에 배송 전용 리그를 얹는
파생 씬이므로, 공용 맵 오브젝트를 그 씬에서 직접 수정하지 않는다.

ConceptMap의 지형·도로·연석·다리·강·집·진입로·콜라이더·차량·카메라·눈·조명·스폰·리스폰 중
하나라도 저장해서 바꿨다면, **같은 작업을 완료하기 전에** 메뉴
`PPack/Delivery/Build Request Flow Test Scene`를 실행해 Delivery 통합 씬을 다시 만든다. 생성된 씬도
ConceptMap과 같은 변경 묶음에 포함한다. 빌더는 맵이 소유한 `SnowStage`, `PlayerVehicle`,
`VehicleCamera`, `VehicleRespawnPoint`, 강의 `VehicleRespawnVolume`, `VehicleCurbRamps`를 재사용하고,
배송 노드·구간·공장·트럭·UI만 추가해야 한다.

재생성 뒤에는 최소한 다음을 확인한다.

- `VehicleController`, `VehicleCamera`, `SnowStage`, `VehicleRespawnPoint`가 각각 정확히 하나다.
- 강의 `VehicleRespawnVolume`은 `Water_01~04`에 대응하는 네 개이며 같은 스폰을 가리킨다.
- `Geometry/Routes/VehicleCurbRamps`의 24개 경사 콜라이더만 있고 Delivery 전용 `CurbRamps`가 중복되지 않는다.
- 맵의 집·진입로·도로 변경과 배송 정차점/그래프가 어긋나지 않으며 Missing Script가 없다.

새 공용 시스템을 추가했는데 빌더가 이를 또 만든다면 파생 씬에서 손으로 지우지 말고
`../../Delivery/Editor/DeliveryTestSceneBuilder.cs`를 수정해 맵 소유 컴포넌트를 찾아 재사용한다.
빌더는 테스트 씬 에셋을 삭제 후 복사하므로 Plastic에서 같은 경로가 `Removed` + `Added`로 보일 수 있다.

### 배송 맵 프리팹 카탈로그

`Prefabs/DeliveryMapKit/`는 `Delivery_RequestFlow_Test.unity`에 직접 배치된 원본 프리팹을 맵 구성용
Variant로 모아 둔 카탈로그다. 원본이나 벤더 프리팹을 이 폴더로 옮기지 않는다. 메뉴
`PPack/Map/Winter Village/Build Delivery Map Prefab Kit`로 건물·자연·프롭·차량·조명/VFX·게임플레이
카테고리를 다시 생성하며, Project 창에서 Variant를 드래그해 맵을 구성한다.

바위는 지형 경계에서 일부가 눈에 묻힌 듯 놓는다. 작은 바위를 단순히 크게 확대해
절벽을 만드는 샘플도 있지만, 플레이 공간 가까이에서는 충돌 크기와 시각 크기가
어긋나기 쉬우므로 큰 바위 전용 변형을 우선한다.

### 경로 안내 프롭

- 가로등: `Prefabs/Environment/Lighting/`
- 울타리: `Prefabs/Environment/Fences/`
- 전신주와 전선: `Prefabs/Environment/Electric poles/`
- 다리: `Prefabs/Environment/Bridges/winter bridge.prefab`
- 부두: `Prefabs/Environment/Wooden piers/wooden pier winter.prefab`
- 스키장: `ski lift`, `ski flag blue`, `ski flag red`
- 축제: `wooden post 5`, `flags`, `Garlands/`
- 도로: `information plate winter`, 일반 표지판, 소화전

이 프롭은 장식이 아니라 경로 문법이다. 가로등은 통행로 가장자리, 울타리는 사유지
경계, 깃발은 코스 양옆, 표지판은 선택 지점에 둔다. 서로 무관한 방향으로 흩어 놓지
않는다.

### 생활과 이야기 프롭

- 집 주변: 눈삽, 썰매, 눈사람, 쓰레기통, 통, 자동차
- 축제 중심: 아이스링크, 크리스마스트리, 선물 상자, 사탕 지팡이
- 산타 활동: 썰매, 순록, 선물 상자
- 야외 캠프: 이글루 + 모닥불
- 강변: 부두 + 보트 + 통
- 굴뚝: `smoke grey`를 굴뚝 위에 세로 간격으로 배치

이야기 프롭은 단품 랜덤 배치보다 관계를 우선한다.

- 눈삽은 집 벽, 울타리, 눈더미에 기대어 둔다.
- 썰매는 경사 시작점, 집 출입구 옆, 도착 지점에 둔다.
- 선물은 트리나 산타 하우스 주변에 2–5개씩 크기와 색을 섞어 묶는다.
- 자동차는 도로 방향과 평행하게 두고 보행 광장이나 숲 가운데 두지 않는다.
- 쓰레기통과 통은 서비스 출입구, 부두, 건물 측면에 둔다.
- 눈사람은 주 동선을 막지 않는 열린 눈밭 가장자리에 둔다.
- 연기와 고드름은 반드시 지붕·굴뚝·처마에 붙여 공중 소품처럼 보이지 않게 한다.

## 테마별 빠른 조합

| 원하는 분위기 | 주 랜드마크 | 보조 프롭 | 외곽 프레임 |
|---|---|---|---|
| 포근한 겨울 마을 | 목조 주택 또는 겨울 주택 | 트리, 우물, 갈런드, 눈사람 | 전나무 + 중형 눈 바위 |
| 크리스마스 장터 | 링크 또는 대형 트리 | 장터 집, 깃발, 기둥, 가로등, 썰매 | 외곽 주택 + 전나무 |
| 생활형 주택가 | 단독주택 | 도로, 보도, 울타리, 자동차, 전신주, 쓰레기통 | 마른나무 + 전나무 |
| 북극 캠프 | 이글루 군집 | 모닥불, 작은 바위 | 절벽형 바위 + 성긴 나무 |
| 강변 휴양지 | 강변 주택 | 부두, 보트, 통, 가로등, 굴뚝 연기 | 소나무 + 강변 바위 |
| 산타 마을 | 산타 하우스 | 트리, 썰매, 순록, 선물, 사탕 지팡이 | 작은 집 + 전나무 |
| 스키장 | 롯지 또는 리프트 | 슬로프, 깃발, 눈더미 | 바위 + 성긴 전나무 |
| 겨울 도로 | 도로와 안내판 | 차량, 전신주, 쓰러진 나무 | 도로 밖 나무·바위 + 강 |
| 겨울 도시 | 중앙 다리 | 물가를 향한 집, 보행로, 굴뚝 연기 | 강변 나무 + 모서리 바위 |
| 환상 설원 | 수정 산 | 크기별 수정 군집 | 큰 눈 바위, 인공 프롭 최소화 |

한 맵에 모든 조합을 섞지 않는다. 기본 테마 하나를 선택하고, 필요하면 보조 테마를
하나만 더한다. 예를 들어 `겨울 도시 + 크리스마스 장터`는 가능하지만 `북극 캠프 +
스키장 + 산타 마을 + 수정 산`을 동시에 넣으면 시각적 우선순위가 사라진다.

## 실제 PPack 맵으로 옮길 때

샘플은 고정 카메라용 디오라마이지 플레이 가능한 청소 맵이 아니다. 다음 조건을 먼저
만족해야 한다.

- 플레이어 크기와 카메라로 직접 걸어 보고 문, 다리, 울타리 틈, 경사 폭을 결정한다.
- 주 동선은 프롭 없는 연속 공간으로 유지하고 막다른 장식 틈을 만들지 않는다.
- 시각용 설원과 실제 청소 가능 바닥을 분리한다.
- 청소 가능 메시의 UV0는 0–1 안에서 유일해야 하며 겹치거나 타일링되면 안 된다.
- 큰 바닥 한 장에 맵 전체를 합치지 말고, 청소 해상도와 공간 구획에 맞는 패널로 나눈다.
- 연속 MeshCollider 또는 명확한 바닥 Collider를 제공하고 지형 구멍을 남기지 않는다.
- 장식 프롭의 Collider는 실제 형태보다 지나치게 넓지 않은지 확인한다.
- `MapMinimapBounds`는 실제 플레이 가능한 바닥 또는 맵 루트의 bounds를 사용한다.
- 렌더러 없는 외곽 충돌 경계를 두어 디오라마 바깥으로 추락하지 않게 한다.
- Trash, Dust, Insects 배치는 맵 장식에 합치지 않고 각 기능 소유 폴더의 프리팹으로 둔다.

## 권장 계층

프로덕션 겨울 맵은 최소한 아래 역할이 구분되게 구성한다. 이름은 기존 맵 규칙에
맞춰 조정할 수 있지만 역할을 한 루트에 뒤섞지 않는다.

```text
WinterMap
  Geometry          # 플레이 바닥, 벽, 물, 도로, 다리
  Landmarks         # 주 건물과 큰 시선 기준점
  RouteGuides       # 가로등, 울타리, 표지판, 깃발
  BoundaryNature    # 외곽 나무, 바위, 눈더미
  SetDressing       # 생활 프롭과 이야기 군집
  Gameplay          # 스폰, MapMinimapBounds, 경계, 기능별 배치 지점
  Lighting          # 조명, 프로브, Volume
```

## 제작 순서

1. 받은 콘셉트 이미지에서 플레이 경계, 주 출입점, 주 랜드마크, 보조 구역을 표시한다.
2. 위 10개 레퍼런스 중 기본 아키타입 하나와 필요 시 보조 아키타입 하나를 선택한다.
3. 프롭 없이 바닥, 도로, 물, 다리, 경사만 블록아웃한다.
4. `PF_Player`로 이동 폭, 경사, 카메라 가림, 추락 지점을 확인한다.
5. 랜드마크와 건물을 배치하고 출입구가 실제 동선을 향하게 한다.
6. 가로등, 울타리, 깃발, 표지판으로 길을 읽히게 한다.
7. 외곽에 크기와 형태가 다른 나무·바위를 비대칭으로 배치한다.
8. 마지막에 생활 프롭을 관계 있는 군집으로 추가한다.
9. Trash, Dust, Insects를 위한 비어 있는 플레이 공간과 접근성을 확인한다.
10. 게임 카메라, 조감도, 반대편 시점에서 스크린샷을 비교하고 과밀 구역을 덜어낸다.

## 피해야 할 배치

- 같은 나무를 동일 간격, 동일 회전, 동일 스케일로 반복하는 격자 숲
- 작은 프롭을 맵 전체에 같은 밀도로 뿌리는 배치
- 랜드마크가 여러 개 경쟁해 어디로 가야 할지 알 수 없는 구성
- 도로, 다리, 스키 슬로프 한가운데를 장식 프롭으로 막는 구성
- 모든 건물 정면이 한 방향만 바라보는 전시대식 배치
- 실제 기능과 무관한 위치의 전신주, 울타리, 표지판, 보트, 쓰레기통
- 하얀 눈·바위·프롭이 겹쳐 실루엣이 사라지는 구성
- 샘플 완성형 환경 프리팹 하나를 그대로 플레이 맵으로 사용
- 벤더 샘플 씬이나 원본 프리팹을 직접 수정

완성 판단은 “프롭이 많이 들어갔는가”가 아니라, 눈 위의 길과 랜드마크가 먼저 읽히고
프롭이 그 길의 용도와 장소의 이야기를 보강하는가로 한다.

## 눈 바닥 맵 베이크 (2026-08-26)

`Generated/Snow/SnowGroundMap_WinterVillage.asset` 하나가 셀별 바닥 높이, 적설 가능 여부, 시작
적설 배율을 소유한다. `SnowBakeSurface`를 지오메트리 계층 루트에 붙여 분류하며, 자식 콜라이더는
가장 가까운 부모 표식을 물려받는다.

- `GroundPanels_NoSnow`, `PedestrianPaths`: Ground, 시작 적설 100%
- 세 도로, 코너 전환, 커브 램프, `Bridges`: Road, 시작 적설 100%. 바닥 높이 차가 눈 표면에도 유지된다
- `Landmarks`, `VillageHouses`, `BoundaryNature`, `SetDressing`: Obstacle
- `SetDressing/Wildlife`: Ignore — 움직이는 토끼 위치를 영구 장애물로 굽지 않는다
- `River/Water_01..04`: Water 레이어. Ground도 Obstacle도 아니며, 지면 메시의 강 구멍 때문에
  바닥 맵에서도 빈 셀이다

도로·지면·장애물 배치를 바꾼 뒤에는 원본 ConceptMap을 열고 Ground Map을 다시 굽고,
`PPack/Delivery/Build Request Flow Test Scene`과 `PPack/Delivery/Build Snow Delivery Scene`을 다시 만든다.
지붕·경사처럼 같은 XZ에 여러 눈 표면이 겹치는 경우는 이 맵에 합치지 않고 `SnowZone`을 쓴다.

## Road First World 상호작용 프롭 (2026-08-26)

벤더 씬에서 풀린 정적 `barrel` 3개와 `hydrant` 1개는 각각 프로젝트 소유
`PF_RollingBarrel`, `PF_BreakableHydrant`로 교체한다. 메뉴
`PPack/Map/Road First World/Apply Interactive Barrel And Hydrant Prefabs`가 기존 월드 위치와 지면
접촉 높이를 보존해 교체하며, 반복 실행해도 이미 교체된 프리팹은 중복 생성하지 않는다.

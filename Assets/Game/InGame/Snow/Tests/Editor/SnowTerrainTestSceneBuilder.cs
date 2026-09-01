#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// <b>지면 자체가 평평하지 않을 때의 눈</b>을 검증하는 씬을 만든다. 경사·지붕은 <see cref="SnowZone"/> 이
    /// 맡으므로 이 씬이 묻는 것은 하나다 — <b>구운 바닥 맵(<see cref="SnowGroundMap"/>)으로 지형을 따라가는
    /// 눈이 되는가.</b>
    ///
    /// <para>지형은 코드로 만든다. 손으로 조각한 터레인은 씬처럼 머지가 안 되고, 무엇을 검증하는 지형인지가
    /// 파일 안에 안 남는다. 여기 식으로 두면 <b>완만한 언덕 · 마스크가 꺼야 하는 급경사 · 한 샘플짜리 절벽</b>
    /// 셋이 항상 같은 자리에 있다. 더 사실적인 지형이 필요하면 이 에셋을 에디터에서 덧칠하면 된다 —
    /// 다시 빌드하지 않는 한 덮어쓰지 않는다.</para>
    ///
    /// <para>⚠ <b>빌드 뒤에 굽기를 한 번 눌러야 한다.</b> 이 빌더는 맵 에셋을 만들고 스테이지에 물려 주기만
    /// 한다 — 굽기는 <c>Tools/PPack/Snow/Bake Ground Map (selected asset)</c> 이고, 씬이 열린 상태에서
    /// 콜라이더를 레이로 훑는다. 굽지 않은 맵은 스테이지가 거부하고 평지로 돈다(콘솔에 이유가 찍힌다).</para>
    ///
    /// <para>이 테스트 씬은 Build Settings 에 절대 추가하지 않는다.</para>
    /// </summary>
    public static class SnowTerrainTestSceneBuilder
    {
        private const string _scenePath = "Assets/Game/InGame/Snow/Tests/Snow_Terrain_Test.unity";
        private const string _sourceScenePath = "Assets/Game/InGame/Snow/Tests/Snow_Slope_Test.unity";
        private const string _terrainDataPath = "Assets/Game/InGame/Snow/Tests/Geometry/Terrain_SnowTest.asset";
        private const string _groundMapPath = "Assets/Game/InGame/Snow/Tests/SnowGroundMap_Terrain.asset";
        private const string _snowRootName = "SnowCpuStage";
        private const string _penguinPrefabPath = "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab";

        /// <summary>
        /// 펭귄이 서는 자리. 검증 대상 셋이 <b>한 화면에</b> 들어오는 곳으로 골랐다 — 정면에 완만한
        /// 언덕 <c>(-9, -6)</c>, 그 오른쪽에 급경사 언덕 <c>(8, 8)</c>, 더 오른쪽에 대지 모서리
        /// <c>x = 12.5</c>. Y 는 빌드할 때 터레인에 레이를 쏴서 정한다(식으로 계산하면 흩뿌린 돔이
        /// 그 자리에 걸렸을 때 땅속에서 시작한다).
        /// </summary>
        private static readonly Vector2 _penguinSpawnXZ = new Vector2(-6f, -26f);

        /// <summary>프리팹이 <c>y = 0.6</c> 을 지면 위 기준으로 쓴다(<c>Snow_BallPush_Test</c> 와 같은 값).</summary>
        private const float _penguinPivotAboveGroundM = 0.6f;

        /// <summary>
        /// 격자와 터레인이 <b>같은 사각형</b>이다. 달라지면 굽힌 맵이 스테이지에 안 맞는다.
        ///
        /// <para><b>실제 맵의 4배다</b>(2026-08-24). <c>SinglePlay</c> 가 120 × 110 m 이고 이 씬은
        /// 그 네 배인 240 × 220 m 다 — 지면 시트 경로가 <b>실제 맵보다 큰 데서 어떻게 무너지는지</b>를
        /// 재는 것이 이 크기의 목적이다. 셀 12.5 cm 기준 1920 × 1760 = 3,379,200 셀이고, 그 수치가
        /// 굽는 시간 · 에셋 크기 · 프레임 업로드를 전부 정한다(폴더 <c>AGENTS.md</c> "규모" 절).</para>
        /// </summary>
        private const float _fieldSizeXM = 240f;

        /// <inheritdoc cref="_fieldSizeXM"/>
        private const float _fieldSizeZM = 220f;

        /// <summary>
        /// 터레인의 Y 범위. 바닥은 mm 단위 R16 이라 65.535 m 가 상한이다. 여기서 쌓일 수 있는 최대는
        /// 굴곡 바닥(5.0) + 대지(2.2) + 가장 높은 돔(6.0) = 13.2 m 이므로 16 m 로 둔다 — 모자라면
        /// 하이트맵이 <c>Clamp01</c> 에서 잘려 언덕 꼭대기가 평평해지고, 그러면 "급경사" 검증 대상이
        /// 조용히 평지가 된다.
        /// </summary>
        private const float _terrainHeightM = 16f;

        /// <summary>
        /// 513 → 240 m 에 0.469 m/샘플(Z 는 220 m 에 0.430). 눈 셀(12.5 cm)보다 거칠지만 레이가
        /// 그 사이를 메운다. 터레인 해상도는 <c>2^n+1</c> 만 되므로 40 m 시절의 밀도(0.3125)에
        /// 정확히 대응하는 769 는 못 쓴다.
        /// </summary>
        private const int _heightmapResolution = 513;

        [MenuItem("Tools/PPack/Build Snow Terrain Test Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Light sun = BuildSun();
            GameObject terrain = BuildTerrain();
            BuildOverviewCamera();
            GameObject penguin = BuildPenguin();

            SnowGroundMap map = CreateOrLoadGroundMap();
            if (map == null)
            {
                Debug.LogError($"Snow 터레인 테스트 씬 빌드 중단: 바닥 맵 에셋을 만들 수 없다: {_groundMapPath}");
                return;
            }

            if (!TryCopySnowRig(scene, sun, map, out string rigError))
            {
                Debug.LogError($"Snow 터레인 테스트 씬 빌드 중단: {rigError}");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, _scenePath))
            {
                Debug.LogError($"Snow 터레인 테스트 씬 저장 실패: {_scenePath}");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = map;
            Debug.Log($"Snow 터레인 테스트 씬 빌드 완료: {_scenePath} · 터레인 {terrain.name} " +
                      $"({_fieldSizeXM} × {_terrainHeightM} × {_fieldSizeZM} m) · " +
                      $"펭귄 {(penguin == null ? "없음" : penguin.transform.position.ToString("F2"))}. " +
                      "다음: 선택된 SnowGroundMap_Terrain 에 대해 Tools/PPack/Snow/Bake Ground Map 을 누른다. " +
                      "조작은 WASD · 마우스 · Space · Shift, E 눈덩이 · Q 터뜨리기 · 좌클릭 눈 뭉치기. " +
                      "이 씬은 Build Settings 에 추가하지 않는다.");
        }

        private static Light BuildSun()
        {
            var go = new GameObject("Sun");
            go.transform.eulerAngles = new Vector3(35f, 200f, 0f);

            Light sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = Color.white;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            return sun;
        }

        /// <summary>
        /// 맵 전체가 한 화면에 들어오는 자리. <b>far clip 을 같이 키워야 한다</b> — 240 m 맵을 담으려면
        /// 카메라가 200 m 가까이 물러나므로 40 m 시절의 300 m 로는 먼 쪽 지형이 잘려 나간다.
        ///
        /// <para>⚠ <b>꺼진 채로 만든다. 그리고 <c>MainCamera</c> 태그도 <c>AudioListener</c> 도 안 붙인다.</b>
        /// 플레이할 카메라는 펭귄이 들고 오기 때문이다 — <c>PF_Penguin</c> 안의 <c>CameraRig/Camera</c> 가
        /// 이미 <c>MainCamera</c> + <c>AudioListener</c> 다. 둘 다 켜 두면 <c>Camera.main</c> 이 어느 쪽인지
        /// 갈리고 리스너가 둘이라 경고가 뜬다. 전경 스크린샷이 필요할 때만 이 오브젝트를 켠다
        /// (그때는 펭귄 카메라를 같이 끌 것).</para>
        /// </summary>
        private static void BuildOverviewCamera()
        {
            var go = new GameObject("OverviewCamera");
            go.transform.position = new Vector3(0f, 118f, -186f);
            go.transform.rotation = Quaternion.Euler(30f, 0f, 0f);

            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 900f;
            go.SetActive(false);
        }

        /// <summary>
        /// 펭귄을 지형 위에 세운다. <b>Y 는 터레인 콜라이더에 레이를 쏴서 정한다</b> — 하이트맵 식을
        /// 다시 푸는 것보다 정확하고, 흩뿌린 돔이 그 자리에 걸려도 땅속에서 시작하지 않는다.
        ///
        /// <para>⚠ <b>펭귄은 눈 <i>위</i>가 아니라 지형 위에 선다.</b> 눈에는 콜라이더가 없다(변위 셰이더로
        /// 그리는 높이장이다). 초기 적설 600 mm 에서는 발목쯤 잠긴 것처럼 보이는데 그것이 현재 사양이고,
        /// 눈덩이도 같은 규칙으로 구른다(폴더 <c>AGENTS.md</c> "펭귄 임시 대역" 절).</para>
        /// </summary>
        private static GameObject BuildPenguin()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_penguinPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"펭귄 프리팹이 없다: {_penguinPrefabPath}. 씬은 펭귄 없이 세운다.");
                return null;
            }

            // 에디트 모드에서는 방금 만든 터레인이 물리 씬에 아직 안 올라와 있을 수 있다.
            Physics.SyncTransforms();

            float groundY = 0f;
            var from = new Vector3(_penguinSpawnXZ.x, _terrainHeightM + 10f, _penguinSpawnXZ.y);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, _terrainHeightM + 20f))
            {
                groundY = hit.point.y;
            }
            else
            {
                Debug.LogWarning($"펭귄 자리 {_penguinSpawnXZ} 아래에서 지형을 못 맞혔다. y = 0 으로 둔다.");
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = "Penguin";
            go.transform.position = new Vector3(_penguinSpawnXZ.x,
                                                groundY + _penguinPivotAboveGroundM,
                                                _penguinSpawnXZ.y);
            // 정면(+Z)에 완만한 언덕 · 급경사 언덕 · 대지 모서리가 모두 들어온다.
            go.transform.rotation = Quaternion.identity;
            return go;
        }

        /// <summary>
        /// 터레인 하나를 만들어 씬에 놓는다. 원점은 <c>(-크기/2, 0, -크기/2)</c> 라 격자와 같은 사각형을 덮는다 —
        /// 터레인은 자기 위치에서 <b>+X · +Z 로만</b> 뻗으므로 중심에 놓으면 사분면 하나만 덮는다.
        /// </summary>
        private static GameObject BuildTerrain()
        {
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(_terrainDataPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, _terrainDataPath);
            }

            // <b>해상도를 먼저, 크기를 나중에.</b> heightmapResolution 을 바꾸면 size 가 초기화된다.
            data.heightmapResolution = _heightmapResolution;
            data.size = new Vector3(_fieldSizeXM, _terrainHeightM, _fieldSizeZM);
            data.SetHeights(0, 0, BuildHeights());
            EditorUtility.SetDirty(data);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain_SnowTest";
            go.transform.position = new Vector3(-_fieldSizeXM * 0.5f, 0f, -_fieldSizeZM * 0.5f);

            var terrain = go.GetComponent<Terrain>();
            // URP 는 터레인 전용 머티리얼이 필요하다. 비우면 기본이 붙지만 파이프라인이 주는 것을 명시로 쓴다.
            Material terrainMaterial = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial
                : null;
            if (terrainMaterial != null) terrain.materialTemplate = terrainMaterial;
            terrain.drawInstanced = true;

            return go;
        }

        /// <summary>
        /// 지형의 식. <b>검증할 것 셋은 40 m 시절과 같은 월드 좌표에 그대로 둔다</b> — 크기만 바뀌고
        /// 보는 것이 바뀌면 이전 실측과 비교가 안 된다. 그 위에 맵 전체를 덮는 것 둘을 더한다.
        ///
        /// <list type="bullet">
        /// <item><b>완만한 언덕</b> <c>(-9, -6)</c>(최대 21°) — 눈이 지형을 따라가는지. 마스크는 켜야 한다.</item>
        /// <item><b>급한 언덕</b> <c>(8, 8)</c>(최대 55°) — 굽기의 경사각 한계(기본 50°)가 꺼야 한다.
        /// XZ 격자로 깊이를 재면 셀이 <c>1/cos θ</c> 로 늘어나기 때문이다.</item>
        /// <item><b>한 샘플짜리 절벽</b> <c>x = 12.5</c>(2.2 m / 0.47 m = 78°) — 마스크 구멍의
        /// <b>가장자리 마감</b>이 아직 없다는 것을 화면에서 바로 보여 준다(굽기가 0/255 만 쓴다).
        /// 240 m 맵에서는 이 선이 Z 를 가로질러 220 m 짜리 <b>대지 모서리</b>가 된다.</item>
        /// <item><b>굴곡 바닥</b> — 저주파 사인 둘(최대 7°). 240 × 220 m 를 평면으로 두면 셀의 99% 가
        /// 바닥 0 이라 <b>규모만 크고 굽기는 아무것도 안 하는</b> 측정이 된다.</item>
        /// <item><b>흩뿌린 돔</b> — 격자 + 해시로 결정론적으로 놓는다. 절반은 완만(마스크 켜짐),
        /// 절반은 급경사(마스크 꺼짐)라 <b>마스크 경계가 맵 전역에 생긴다</b>. 중앙 ±25 m 는
        /// 비워 둔다 — 위의 검증용 셋을 덮지 않기 위해서다.</item>
        /// </list>
        /// </summary>
        private static float[,] BuildHeights()
        {
            var heights = new float[_heightmapResolution, _heightmapResolution];
            float stepX = _fieldSizeXM / (_heightmapResolution - 1);
            float stepZ = _fieldSizeZM / (_heightmapResolution - 1);
            float halfX = _fieldSizeXM * 0.5f;
            float halfZ = _fieldSizeZM * 0.5f;

            Dome[] scattered = BuildScatteredDomes();

            for (int z = 0; z < _heightmapResolution; z++)
            {
                float wz = -halfZ + z * stepZ;
                for (int x = 0; x < _heightmapResolution; x++)
                {
                    float wx = -halfX + x * stepX;

                    // 굴곡 바닥. 진폭 합이 2.4 이므로 +2.6 을 더해 0 아래로 안 내려가게 한다 —
                    // 음수는 Clamp01 에서 0 으로 눌려 넓은 평평한 자국을 남긴다.
                    float h = 2.6f
                            + 1.6f * Mathf.Sin(wx / 26f) * Mathf.Cos(wz / 22f)
                            + 0.8f * Mathf.Sin((wx + wz) / 13f);

                    h += DomeAt(wx, wz, -9f, -6f, 13f, 3.2f);   // 완만 — 21°
                    h += DomeAt(wx, wz, 8f, 8f, 6f, 5.5f);      // 급함 — 55°
                    if (wx > 12.5f) h += 2.2f;                  // 대지 모서리 — 한 샘플에서 78°

                    for (int i = 0; i < scattered.Length; i++)
                    {
                        Dome d = scattered[i];
                        h += DomeAt(wx, wz, d.X, d.Z, d.RadiusM, d.HeightM);
                    }

                    // 하이트맵은 [z, x] 순서이고 값은 size.y 에 대한 0~1 이다.
                    heights[z, x] = Mathf.Clamp01(h / _terrainHeightM);
                }
            }

            return heights;
        }

        private struct Dome
        {
            public float X;
            public float Z;
            public float RadiusM;
            public float HeightM;
        }

        /// <summary>
        /// 맵 전역에 놓는 돔. <b>결정론이다</b> — 해시만 쓰므로 다시 빌드해도 같은 지형이 나오고,
        /// 그래야 실측을 비교할 수 있다(<c>Random</c> 을 쓰면 시드를 저장해야 한다).
        ///
        /// <para>절반은 <c>H/R</c> 이 0.76 을 넘어 <b>50° 한계에 걸리는 급경사</b>다
        /// (돔의 최대 경사는 <c>atan(πH / 2R)</c>). 그래야 마스크가 맵 전역에서 일을 한다.</para>
        /// </summary>
        private static Dome[] BuildScatteredDomes()
        {
            var domes = new System.Collections.Generic.List<Dome>(64);
            const float latticeX = 34f;
            const float latticeZ = 32f;
            float halfX = _fieldSizeXM * 0.5f;
            float halfZ = _fieldSizeZM * 0.5f;

            int nx = Mathf.FloorToInt(_fieldSizeXM / latticeX);
            int nz = Mathf.FloorToInt(_fieldSizeZM / latticeZ);

            for (int iz = 0; iz <= nz; iz++)
            {
                for (int ix = 0; ix <= nx; ix++)
                {
                    if (Hash01(ix, iz, 1) < 0.28f) continue;      // 격자 티가 나지 않게 솎는다

                    float cx = -halfX + (ix + 0.5f) * latticeX + (Hash01(ix, iz, 2) - 0.5f) * 18f;
                    float cz = -halfZ + (iz + 0.5f) * latticeZ + (Hash01(ix, iz, 3) - 0.5f) * 16f;

                    // 검증용 셋의 자리는 비워 둔다.
                    if (Mathf.Abs(cx) < 25f && Mathf.Abs(cz) < 25f) continue;

                    bool steep = Hash01(ix, iz, 4) < 0.5f;
                    float radius = steep ? 5f + Hash01(ix, iz, 5) * 3f : 11f + Hash01(ix, iz, 5) * 7f;
                    float height = steep ? radius * (0.9f + Hash01(ix, iz, 6) * 0.25f)
                                         : radius * (0.18f + Hash01(ix, iz, 6) * 0.14f);
                    if (height > 6f) height = 6f;

                    domes.Add(new Dome { X = cx, Z = cz, RadiusM = radius, HeightM = height });
                }
            }

            return domes.ToArray();
        }

        /// <summary>정수 좌표만 먹는 결정론적 0~1 해시. 부동소수가 안 들어가므로 플랫폼이 갈리지 않는다.</summary>
        private static float Hash01(int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13;
                h *= 0x85EBCA6Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000;
            }
        }

        /// <summary>코사인 돔. 반지름 밖은 0 이고 가장자리에서 기울기도 0 이라 언덕이 지면에 매끄럽게 붙는다.</summary>
        private static float DomeAt(float x, float z, float centreX, float centreZ, float radiusM,
                                    float heightM)
        {
            float distance = Mathf.Sqrt((x - centreX) * (x - centreX) + (z - centreZ) * (z - centreZ));
            if (distance >= radiusM) return 0f;
            return heightM * 0.5f * (1f + Mathf.Cos(Mathf.PI * distance / radiusM));
        }

        /// <summary>
        /// 바닥 맵 에셋. <b>범위는 스테이지와 같아야 한다</b> — 다르면 <c>TryBuildField</c> 가 거부한다.
        /// 이미 있으면 범위만 다시 맞추고 굽힌 내용은 건드리지 않는다.
        /// </summary>
        private static SnowGroundMap CreateOrLoadGroundMap()
        {
            SnowGroundMap map = AssetDatabase.LoadAssetAtPath<SnowGroundMap>(_groundMapPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<SnowGroundMap>();
                AssetDatabase.CreateAsset(map, _groundMapPath);
            }

            var so = new SerializedObject(map);
            so.FindProperty("_originXZ").vector2Value = new Vector2(-_fieldSizeXM * 0.5f, -_fieldSizeZM * 0.5f);
            so.FindProperty("_sizeMeters").vector2Value = new Vector2(_fieldSizeXM, _fieldSizeZM);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);
            return map;
        }

        /// <summary>
        /// 눈 리그는 <b>Slope 테스트에서 복사</b>한다. 컴포넌트 넷(스테이지·마처 뷰·시스템·Displace 뷰)의
        /// 조합과 그 인스펙터 값이 이미 맞춰져 있고, 새로 붙이면 그 조합을 다시 발명하게 된다.
        /// </summary>
        private static bool TryCopySnowRig(Scene targetScene, Light sun, SnowGroundMap map, out string error)
        {
            error = null;
            Scene sourceScene = default;
            bool opened = false;
            bool configured = false;

            try
            {
                sourceScene = EditorSceneManager.OpenScene(_sourceScenePath, OpenSceneMode.Additive);
                opened = true;

                GameObject sourceRig = null;
                foreach (GameObject root in sourceScene.GetRootGameObjects())
                {
                    if (root.name != _snowRootName) continue;
                    if (sourceRig != null)
                    {
                        error = $"원본 씬에 루트 {_snowRootName} 가 둘 이상 있다: {_sourceScenePath}";
                        return false;
                    }

                    sourceRig = root;
                }

                if (sourceRig == null)
                {
                    error = $"원본 씬에 루트 {_snowRootName} 가 없다: {_sourceScenePath}";
                    return false;
                }

                GameObject rig = Object.Instantiate(sourceRig);
                rig.name = _snowRootName;
                if (rig.scene != targetScene) SceneManager.MoveGameObjectToScene(rig, targetScene);
                rig.transform.position = Vector3.zero;
                rig.transform.rotation = Quaternion.identity;

                var stage = rig.GetComponent<SnowCpuStage>();
                var system = rig.GetComponent<SnowSystem>();
                var marchView = rig.GetComponent<SnowCpuStageView>();
                var displaceView = rig.GetComponent<SnowDisplaceView>();
                if (stage == null || system == null || marchView == null || displaceView == null)
                {
                    error = $"{_snowRootName} 에 눈 컴포넌트 넷이 다 있지 않다.";
                    return false;
                }

                var stageObject = new SerializedObject(stage);
                stageObject.FindProperty("_originXZ").vector2Value =
                    new Vector2(-_fieldSizeXM * 0.5f, -_fieldSizeZM * 0.5f);
                stageObject.FindProperty("_sizeMeters").vector2Value =
                    new Vector2(_fieldSizeXM, _fieldSizeZM);
                stageObject.FindProperty("_initialDepthMm").intValue = 600;
                stageObject.FindProperty("_groundMap").objectReferenceValue = map;

                // ⚠ <b>잔량은 명시로 넣는다.</b> 0 으로 뭉치면 한 번에 지름 0.83 m 가 나와 "손으로 뭉친
                // 눈덩이" 로는 너무 크다 — 250 이 0.48 m 를 준다(폴더 AGENTS.md 의 Snow_BallPush_Test
                // 실측). 원본 씬(Snow_Slope_Test)에 0 이 직렬화돼 있어서 복사만 하면 그 값이 따라온다.
                // C# 기본값은 이미 저장된 컴포넌트를 고치지 않는다.
                stageObject.FindProperty("_gatherResidueMm").intValue = 250;
                stageObject.ApplyModifiedPropertiesWithoutUndo();

                // 경사·지붕은 상자가 맡는다. 이 씬이 묻는 것은 <b>지면 시트 + 굽힌 바닥</b> 하나뿐이다.
                var systemObject = new SerializedObject(system);
                SerializedProperty look = systemObject.FindProperty("_look");
                look.enumValueIndex = (int)ESnowLook.Displace;
                systemObject.ApplyModifiedPropertiesWithoutUndo();

                SetSun(marchView, sun);
                SetSun(displaceView, sun);
                configured = true;
            }
            catch (System.Exception exception)
            {
                error = $"원본 눈 리그를 복사하지 못했다: {exception.Message}";
            }
            finally
            {
                if (opened && sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    if (!EditorSceneManager.CloseScene(sourceScene, true))
                    {
                        error = $"원본 씬을 저장하지 않고 닫지 못했다: {_sourceScenePath}";
                        configured = false;
                    }
                }
            }

            return configured;
        }

        private static void SetSun(Object view, Light sun)
        {
            var so = new SerializedObject(view);
            SerializedProperty property = so.FindProperty("_sun");
            if (property == null) return;
            property.objectReferenceValue = sun;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif

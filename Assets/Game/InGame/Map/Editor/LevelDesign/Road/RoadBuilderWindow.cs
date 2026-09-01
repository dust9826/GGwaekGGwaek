using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    internal sealed class RoadBuilderWindow : EditorWindow
    {
        private RoadEntranceDatabase _database;
        private SerializedObject _serializedDatabase;
        private EasyRoadTemplate _easyRoadTemplate;
        private Vector2 _scroll;

        [MenuItem("PPack/Level Design/Open Road Builder")]
        public static void Open()
        {
            GetWindow<RoadBuilderWindow>("Road Builder");
        }

        private void OnEnable()
        {
            minSize = new Vector2(560f, 640f);
            LoadDatabase();
        }

        private void OnGUI()
        {
            if (_database == null) LoadDatabase();

            EditorGUILayout.LabelField("ROAD BUILDING TOOLS", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "EasyRoads Template Road는 현재 Winter Village 도로의 Road Type·재질을 프리셋처럼 " +
                "재사용합니다. 새 도로만 현재 Terrain을 따라 생성하며 기존 지형과 도로는 유지합니다.",
                MessageType.Info);

            DrawEasyRoadsTemplateTools();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("TERRAIN-PAINTED ROADS", EditorStyles.boldLabel);
            RoadBuilderPreferences.MainRoadWidth = EditorGUILayout.Slider(
                "Main Road Width", RoadBuilderPreferences.MainRoadWidth, 1.5f, 10f);
            RoadBuilderPreferences.EntranceRoadWidth = EditorGUILayout.Slider(
                "Entrance Road Width", RoadBuilderPreferences.EntranceRoadWidth, 0.6f, 5f);
            RoadBuilderPreferences.BorderWidth = EditorGUILayout.Slider(
                "Dark Border Width", RoadBuilderPreferences.BorderWidth, 0.5f, 3f);
            RoadBuilderPreferences.EdgeFeather = EditorGUILayout.Slider(
                "Edge Softness", RoadBuilderPreferences.EdgeFeather, 0.02f, 0.35f);
            RoadBuilderPreferences.MaximumGrade = EditorGUILayout.Slider(
                "Maximum Grade", RoadBuilderPreferences.MaximumGrade, 2f, 25f);
            RoadBuilderPreferences.GradingShoulder = EditorGUILayout.Slider(
                "Grading Shoulder", RoadBuilderPreferences.GradingShoulder, 0.5f, 10f);

            DrawTerrainResolution();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Connected Main Road", GUILayout.Height(32f)))
                ToolManager.SetActiveTool<MainRoadBuilderTool>();
            if (GUILayout.Button("House Entrance Road", GUILayout.Height(32f)))
                ToolManager.SetActiveTool<RoadBuilderTool>();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild Road Network"))
                TerrainRoadCommands.RebuildAllTerrainRoads();
            if (GUILayout.Button("Capture Terrain Baseline"))
                TerrainRoadCommands.CaptureCurrentTerrainAsRoadBaseline();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Upgrade Old Roads To Splines"))
                TerrainRoadCommands.UpgradeTerrainRoadsToSplines();
            if (GUILayout.Button("Convert Old Mesh Roads"))
                TerrainRoadCommands.ConvertLegacyMeshRoadsToTerrain();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Spline 매듭을 이동하거나 도로 폭을 바꾼 뒤 Rebuild Road Network를 누르세요. " +
                "Capture Terrain Baseline은 현재 높이를 새 원본으로 교체할 때만 사용합니다.",
                MessageType.None);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("House Entrance Anchors", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "현재 값은 집 메시 경계에서 자동 추정한 초깃값입니다. 문이 메시와 합쳐진 프리팹은 " +
                "Local Position의 X/Z와 Door Width를 눈으로 확인해 한 번 조정하면 이후 계속 재사용됩니다.",
                MessageType.Warning);

            if (_serializedDatabase == null) return;
            _serializedDatabase.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.PropertyField(_serializedDatabase.FindProperty("_entries"), true);
            EditorGUILayout.EndScrollView();
            if (_serializedDatabase.ApplyModifiedProperties())
                AssetDatabase.SaveAssetIfDirty(_database);
        }

        private void LoadDatabase()
        {
            _database = RoadBuilderAssets.GetOrCreateDatabase();
            _serializedDatabase = _database == null ? null : new SerializedObject(_database);
            _easyRoadTemplate = EasyRoadBuilderPreferences.Template;
        }

        private void DrawEasyRoadsTemplateTools()
        {
            EasyRoadTemplate selected = (EasyRoadTemplate)EditorGUILayout.ObjectField(
                "EasyRoad Template",
                _easyRoadTemplate,
                typeof(EasyRoadTemplate),
                false);
            if (selected != _easyRoadTemplate)
            {
                _easyRoadTemplate = selected == null
                    ? EasyRoadTemplateAssets.GetOrCreateDefaultTemplate()
                    : selected;
                EasyRoadBuilderPreferences.Template = _easyRoadTemplate;
            }

            EasyRoadBuilderPreferences.Width = EditorGUILayout.Slider(
                "EasyRoad Width", EasyRoadBuilderPreferences.Width, 0.5f, 12f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Draw Template Road", GUILayout.Height(34f)))
                ToolManager.SetActiveTool<EasyRoadDrawingTool>();
            if (GUILayout.Button("Rebuild EasyRoads", GUILayout.Height(34f)))
                EasyRoadAuthoring.RebuildActiveSceneFromMenu();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Template Asset") && _easyRoadTemplate != null)
            {
                Selection.activeObject = _easyRoadTemplate;
                EditorGUIUtility.PingObject(_easyRoadTemplate);
            }
            if (GUILayout.Button("Validate EasyRoads Setup"))
                EasyRoadTemplateAssets.ValidateFromMenu();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Scene에서 LMB로 지점을 찍고 Enter로 확정합니다. [ / ]는 폭, Backspace는 마지막 점, " +
                "Esc는 취소입니다. 기존 도로에 스냅하면 새 도로 끝만 정렬됩니다. Spline 수정 후 " +
                "Rebuild를 누르면 Terrain은 유지한 채 EasyRoads 메시와 Collider가 갱신됩니다.",
                MessageType.None);
        }

        private static void DrawTerrainResolution()
        {
            Terrain terrain = Selection.activeGameObject == null
                ? Terrain.activeTerrain
                : Selection.activeGameObject.GetComponentInParent<Terrain>();
            if (terrain == null || terrain.terrainData == null) return;

            TerrainData data = terrain.terrainData;
            float metersPerAlpha = data.size.x / Mathf.Max(1, data.alphamapWidth - 1);
            float metersPerHeight = data.size.x / Mathf.Max(1, data.heightmapResolution - 1);
            MessageType type = RoadBuilderPreferences.MainRoadWidth / metersPerAlpha < 3f
                ? MessageType.Warning
                : MessageType.None;
            EditorGUILayout.HelpBox(
                $"Terrain 정밀도 · Paint {metersPerAlpha:0.00} m/texel · Height {metersPerHeight:0.00} m/sample" +
                (type == MessageType.Warning
                    ? "\n현재 도로 폭이 페인트 3 texel보다 좁아 가장자리가 각져 보일 수 있습니다."
                    : string.Empty),
                type);
        }
    }

    internal sealed class LevelDesignHubWindow : EditorWindow
    {
        private enum HubSection
        {
            Prefabs,
            Roads,
            Terrain,
            Maintenance,
            WorldBuilder
        }

        private const float NavigationWidth = 184f;

        [SerializeField] private HubSection _section;
        private Vector2 _detailScroll;

        [MenuItem("PPack/Level Design/Open Level Design Hub", priority = 0)]
        [MenuItem("Window/PPack/Level Design Hub")]
        public static void Open()
        {
            LevelDesignHubWindow window = GetWindow<LevelDesignHubWindow>("Level Design Hub");
            window.minSize = new Vector2(720f, 470f);
            window.Show();
        }

        private void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            DrawNavigation();
            EditorGUILayout.Space(8f);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            DrawSelectedSection();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawHeader()
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("PPACK LEVEL DESIGN", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                scene.IsValid() ? $"Active Scene · {scene.name}" : "Active Scene · None",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawNavigation()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(NavigationWidth));
            EditorGUILayout.LabelField("TOOLS", EditorStyles.boldLabel);
            DrawNavigationButton(HubSection.Prefabs, "Prefab Placement");
            DrawNavigationButton(HubSection.Roads, "Road Building");
            DrawNavigationButton(HubSection.Terrain, "Terrain Templates");
            DrawNavigationButton(HubSection.Maintenance, "Road Maintenance");
            DrawNavigationButton(HubSection.WorldBuilder, "World Generator");
            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox(
                "기존 전용 창과 메뉴는 그대로 유지됩니다.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawNavigationButton(HubSection section, string label)
        {
            Color previous = GUI.backgroundColor;
            if (_section == section) GUI.backgroundColor = new Color(0.35f, 0.72f, 1f);
            if (GUILayout.Button(label, GUILayout.Height(34f)))
            {
                _section = section;
                _detailScroll = Vector2.zero;
                GUI.FocusControl(null);
            }
            GUI.backgroundColor = previous;
        }

        private void DrawSelectedSection()
        {
            switch (_section)
            {
                case HubSection.Prefabs:
                    DrawPrefabSection();
                    break;
                case HubSection.Roads:
                    DrawRoadSection();
                    break;
                case HubSection.Terrain:
                    DrawTerrainSection();
                    break;
                case HubSection.Maintenance:
                    DrawMaintenanceSection();
                    break;
                case HubSection.WorldBuilder:
                    DrawWorldBuilderSection();
                    break;
            }
        }

        private static void DrawPrefabSection()
        {
            DrawSectionTitle(
                "PREFAB PLACEMENT",
                "Winter Village Prefab을 고르고 Scene에 배치합니다. 집 카테고리는 배치 전 지형 평탄화 미리보기를 제공합니다.");

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Selected Prefab",
                    PlacementTool.SelectedPrefab,
                    typeof(GameObject),
                    false);
            }

            DrawActionCard(
                "Prefab Palette",
                "등록된 집, 나무, 조명, 동물, VFX를 썸네일로 선택합니다.",
                "Open Prefab Palette",
                PrefabPaletteWindow.Open);
            DrawActionCard(
                "Placement Tool",
                "현재 선택된 Prefab을 Collider 표면에 배치합니다. Q/E는 15° 회전, Shift+Q/E는 45° 회전, Alt는 Scene 탐색, Esc는 종료입니다.",
                "Activate Placement Tool",
                () => ToolManager.SetActiveTool<PlacementTool>());
        }

        private static void DrawRoadSection()
        {
            DrawSectionTitle(
                "ROAD BUILDING",
                "도로 설정은 Road Builder에서 조절하고 아래 Scene Tool로 바로 그릴 수 있습니다.");

            DrawActionCard(
                "Road Builder Settings",
                "EasyRoad 템플릿, 도로 폭, 가장자리, 경사 제한과 집 출입구 Anchor를 설정합니다.",
                "Open Road Builder",
                RoadBuilderWindow.Open);
            DrawActionCard(
                "EasyRoads Template Road",
                "기존 도로를 바꾸지 않고 Terrain 위에 새 EasyRoad를 만듭니다. C는 미리보기 연결 고정, Enter는 생성입니다.",
                "Draw EasyRoad Template",
                () => ToolManager.SetActiveTool<EasyRoadDrawingTool>());
            DrawActionCard(
                "Connected Main Road",
                "Terrain을 따라가는 큰 도로를 그리고 기존 Terrain 도로와 자동 연결합니다.",
                "Draw Connected Main Road",
                () => ToolManager.SetActiveTool<MainRoadBuilderTool>());
            DrawActionCard(
                "House Entrance Road",
                "선택한 집의 문 Anchor에서 시작하는 폭 조절형 진입로를 만듭니다.",
                "Draw House Entrance Road",
                () => ToolManager.SetActiveTool<RoadBuilderTool>());
        }

        private static void DrawTerrainSection()
        {
            DrawSectionTitle(
                "TERRAIN TEMPLATES",
                "마을 분지, 산비탈, 스키 슬로프 Terrain을 생성한 뒤 Unity Terrain 도구로 계속 편집합니다.");

            Terrain terrain = Selection.activeGameObject == null
                ? Terrain.activeTerrain
                : Selection.activeGameObject.GetComponentInParent<Terrain>();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Target Terrain", terrain, typeof(Terrain), true);
            }

            DrawActionCard(
                "Winter Terrain Templates",
                "독립적인 TerrainData와 눈 재질 레이어, 도로 baseline, 표준 hierarchy를 함께 만듭니다.",
                "Open Terrain Templates",
                WinterTerrainTemplateWindow.Open);
            DrawActionCard(
                "Ski Slope Preview",
                "포함된 스키장 지형 템플릿을 별도 테스트 Scene으로 만들어 확인합니다.",
                "Build Ski Slope Preview",
                WinterTerrainTemplatePreviewBuilder.BuildPreviewScene);
            if (GUILayout.Button("Close Preview And Return", GUILayout.Height(26f)))
                WinterTerrainTemplatePreviewBuilder.ClosePreviewAndReturn();
        }

        private static void DrawMaintenanceSection()
        {
            TerrainRoadPath[] terrainRoads = UnityEngine.Object.FindObjectsByType<TerrainRoadPath>();
            RoadPath[] meshRoads = UnityEngine.Object.FindObjectsByType<RoadPath>();
            DrawSectionTitle(
                "ROAD MAINTENANCE",
                $"Loaded Scene · Terrain roads {terrainRoads.Length} · Mesh roads {meshRoads.Length}");
            EditorGUILayout.HelpBox(
                "아래 기능은 Scene 데이터를 수정할 수 있습니다. 실행 전 Undo 기록과 현재 Scene 저장 상태를 확인하세요.",
                MessageType.Warning);

            DrawConfirmedAction(
                "Rebuild Terrain Road Network",
                "Terrain baseline에서 모든 Terrain 도로를 다시 합성합니다.",
                "Rebuild",
                TerrainRoadCommands.RebuildAllTerrainRoads);
            DrawConfirmedAction(
                "Capture Current Terrain Baseline",
                "현재 Terrain 높이를 이후 도로 재생성의 새로운 원본으로 저장합니다.",
                "Capture",
                TerrainRoadCommands.CaptureCurrentTerrainAsRoadBaseline);
            DrawConfirmedAction(
                "Repair Legacy Road Junctions",
                "이전 mesh 도로 접합부의 junction cap을 다시 계산합니다.",
                "Repair",
                RoadJunctionRepairUtility.RepairAllFromMenu);
            DrawConfirmedAction(
                "Rebuild EasyRoads From Splines",
                "현재 Spline을 기준으로 새 도로 mesh와 collider만 갱신합니다.",
                "Rebuild EasyRoads",
                EasyRoadAuthoring.RebuildActiveSceneFromMenu);
            DrawConfirmedAction(
                "Connect Selected Road Ends",
                "선택한 두 TerrainRoadPath의 가장 가까운 끝점과 마지막 구간을 함께 보정해 틈과 날카로운 꺾임을 없앱니다.",
                "Connect Selected Ends",
                RoadEndpointConnector.ConnectSelectedFromMenu);
            DrawConfirmedAction(
                "Refinish Selected EasyRoad",
                "선택한 EasyRoad의 양 끝 연결부만 다시 정리합니다.",
                "Refinish Selected",
                EasyRoadAuthoring.RefinishSelectedConnectionsFromMenu);
        }

        private static void DrawWorldBuilderSection()
        {
            DrawSectionTitle(
                "ROAD-FIRST WINTER WORLD",
                "큰 도로를 먼저 구성한 Winter Village 기준 맵을 프로젝트 에셋과 Scene으로 다시 생성합니다.");
            EditorGUILayout.HelpBox(
                "이 생성기는 전용 Road-First World Scene과 Generated 에셋을 갱신합니다. 일반적인 수동 레벨 편집에는 Prefab, Road, Terrain 탭을 사용하세요.",
                MessageType.Warning);

            if (GUILayout.Button("Build / Rebuild Road-First Winter World...", GUILayout.Height(38f)) &&
                EditorUtility.DisplayDialog(
                    "Build Road-First Winter World",
                    "전용 Winter World Scene과 생성 에셋을 다시 만들까요? 현재 다른 Scene의 저장되지 않은 변경은 먼저 저장해야 합니다.",
                    "Build",
                    "Cancel"))
            {
                RunSafely(RoadFirstWinterWorldBuilder.Build);
            }
        }

        private static void DrawSectionTitle(string title, string description)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(description, MessageType.Info);
            EditorGUILayout.Space(5f);
        }

        private static void DrawActionCard(
            string title,
            string description,
            string buttonLabel,
            Action action)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
            if (GUILayout.Button(buttonLabel, GUILayout.Height(30f))) RunSafely(action);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3f);
        }

        private static void DrawConfirmedAction(
            string title,
            string description,
            string buttonLabel,
            Action action)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
            if (GUILayout.Button(buttonLabel, GUILayout.Height(28f)) &&
                EditorUtility.DisplayDialog(title, description + "\n\n계속할까요?", buttonLabel, "Cancel"))
            {
                RunSafely(action);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3f);
        }

        private static void RunSafely(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Level Design Hub", exception.Message, "OK");
            }
        }
    }
}

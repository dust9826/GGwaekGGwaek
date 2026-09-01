#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// 오늘은 그림자 수신·계조 클리핑·정점 간격 비교를 검증하고, 필드에 Y축이 생긴 뒤에는 경사 위의 눈을 검증한다.
    /// 펭귄은 실제 기본 조작으로 눈 위를 돌아다니며 캐릭터 크기·접지·캐스트 그림자를 확인한다.
    /// 해의 yaw 200은 그림자를 카메라 쪽으로 보내기 위한 값이며, 미끼 점광원은 임의의 Light를 해로 고르는 회귀를 드러낸다.
    /// 이 테스트 씬은 Build Settings에 절대 추가하지 않는다.
    /// </summary>
    public static class SnowSlopeTestSceneBuilder
    {
        private const string _scenePath = "Assets/Game/InGame/Snow/Tests/Snow_Slope_Test.unity";
        private const string _sourceScenePath = "Assets/Game/InGame/Snow/Tests/Snow_BallPush_Test.unity";
        private const string _materialsFolderPath = "Assets/Game/InGame/Snow/Tests/Materials";
        private const string _snowRootName = "SnowCpuStage";
        private const string _penguinRootName = "Penguin";

        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int[] _slopeAngles = { 15, 30, 45, 60, 75 };
        private static readonly float[] _toneValues = { 0.2f, 0.4f, 0.6f, 0.8f, 1f };

        [MenuItem("Tools/PPack/Build Snow Slope Test Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Shader toneShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (toneShader == null)
            {
                Debug.LogError("Snow 경사 테스트 씬 빌드 중단: Universal Render Pipeline/Unlit 셰이더가 없다.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int createdGameObjectCount = 0;

            Light sun = BuildSun(ref createdGameObjectCount);
            BuildDecoyPointLight(ref createdGameObjectCount);
            BuildSlopes(ref createdGameObjectCount);
            BuildShadowCasters(ref createdGameObjectCount);
            BuildGround(ref createdGameObjectCount);

            if (!TryCopyAndConfigureSourceRigs(scene, sun, out GameObject snowRig, out GameObject penguin,
                    out string sourceRigError))
            {
                Debug.LogError($"Snow 경사 테스트 씬 빌드 중단: {sourceRigError}");
                return;
            }

            createdGameObjectCount += snowRig.GetComponentsInChildren<Transform>(true).Length;
            createdGameObjectCount += penguin.GetComponentsInChildren<Transform>(true).Length;

            if (!EnsureMaterialsFolder())
            {
                Debug.LogError($"Snow 경사 테스트 씬 빌드 중단: 머티리얼 폴더를 만들 수 없다: {_materialsFolderPath}");
                return;
            }

            BuildToneReference(toneShader, ref createdGameObjectCount);
            BuildCamera(ref createdGameObjectCount);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, _scenePath))
            {
                Debug.LogError($"Snow 경사 테스트 씬 저장 실패: {_scenePath}");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Snow 경사 테스트 씬 빌드 완료: 게임 오브젝트 {createdGameObjectCount}개, " +
                      $"씬 {_scenePath}. 이 씬은 Build Settings에 추가하지 않는다.");
        }

        private static Light BuildSun(ref int createdGameObjectCount)
        {
            GameObject sunObject = CreateObject("Sun", ref createdGameObjectCount);
            sunObject.transform.eulerAngles = new Vector3(35f, 200f, 0f);

            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = Color.white;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            return sun;
        }

        private static void BuildDecoyPointLight(ref int createdGameObjectCount)
        {
            // 임의의 Light를 해로 고르는 코드가 돌아오면 이 점광원이 선택되어 눈 음영이 틀어지도록 둔다.
            GameObject lightObject = CreateObject("Decoy_PointLight_DoNotUseAsSun", ref createdGameObjectCount);
            lightObject.transform.position = new Vector3(-14f, 3f, -6f);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = 8f;
            light.range = 12f;
            light.color = new Color(1f, 0.85f, 0.6f, 1f);
        }

        private static void BuildSlopes(ref int createdGameObjectCount)
        {
            GameObject parent = CreateObject("Slopes", ref createdGameObjectCount);

            // 스펙 §5의 다섯 각도다. 셀 실제 폭은 평지 대비 1.03 / 1.15 / 1.41 / 2.00 / 3.86배로 늘어난다.
            for (int index = 0; index < _slopeAngles.Length; index++)
            {
                int angle = _slopeAngles[index];
                GameObject slope = CreatePrimitive(PrimitiveType.Cube, $"Slope_{angle}deg", ref createdGameObjectCount);
                slope.transform.SetParent(parent.transform, false);
                slope.transform.localScale = new Vector3(6f, 0.4f, 10f);
                slope.transform.localEulerAngles = new Vector3(angle, 0f, 0f);

                // +X 회전에서 로컬 +Z가 내리막이다. 가장 낮은 모서리를 y=0에 맞춰 각도 비교의 기준을 고정한다.
                float radians = angle * Mathf.Deg2Rad;
                float centerY = 5f * Mathf.Sin(radians) + 0.2f * Mathf.Cos(radians);
                slope.transform.localPosition = new Vector3((index - 2) * 9f, centerY, 11f);
            }
        }

        private static void BuildShadowCasters(ref int createdGameObjectCount)
        {
            GameObject parent = CreateObject("ShadowCasters", ref createdGameObjectCount);

            // z=-4면 약 4.3m 그림자가 카메라 쪽 평지에 남고, z=11의 경사 열과 겹치지 않는다.
            for (int index = 0; index < 5; index++)
            {
                GameObject pillar = CreatePrimitive(PrimitiveType.Cube, $"Pillar_{index:00}", ref createdGameObjectCount);
                pillar.transform.SetParent(parent.transform, false);
                pillar.transform.localPosition = new Vector3((index - 2) * 4f, 1.5f, -4f);
                pillar.transform.localScale = new Vector3(0.6f, 3f, 0.6f);
            }

            // 작은 캐스터의 그림자를 놓쳐도 수신 여부를 한눈에 가를 수 있는 큰 기준 그림자다.
            GameObject slab = CreatePrimitive(PrimitiveType.Cube, "CasterSlab", ref createdGameObjectCount);
            slab.transform.SetParent(parent.transform, false);
            slab.transform.localPosition = new Vector3(6f, 6f, -10f);
            slab.transform.localScale = new Vector3(10f, 0.4f, 10f);
        }

        private static void BuildGround(ref int createdGameObjectCount)
        {
            GameObject ground = CreatePrimitive(PrimitiveType.Cube, "Ground", ref createdGameObjectCount);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(120f, 0.2f, 120f);
        }

        private static void BuildToneReference(Shader shader, ref int createdGameObjectCount)
        {
            GameObject parent = CreateObject("ToneReference", ref createdGameObjectCount);

            // 스크린샷을 픽셀 단위로 잴 때 알려진 선형 회색이 함께 찍혀야 캡처마다 계조를 자체 보정할 수 있다.
            for (int index = 0; index < _toneValues.Length; index++)
            {
                int percent = Mathf.RoundToInt(_toneValues[index] * 100f);
                string suffix = percent.ToString("000");
                GameObject tone = CreatePrimitive(PrimitiveType.Cube, $"Tone_{suffix}", ref createdGameObjectCount);
                tone.transform.SetParent(parent.transform, false);
                tone.transform.localPosition = new Vector3(-18.5f, 1f + index * 1.15f, -12f);
                tone.transform.localScale = new Vector3(1f, 1f, 0.02f);

                Collider collider = tone.GetComponent<Collider>();
                if (collider != null) Object.DestroyImmediate(collider);

                Material material = LoadOrCreateToneMaterial(shader, _toneValues[index], suffix);
                tone.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        private static Material LoadOrCreateToneMaterial(Shader shader, float linearValue, string suffix)
        {
            string materialPath = $"{_materialsFolderPath}/M_Tone_{suffix}.mat";
            Object existingAsset = AssetDatabase.LoadMainAssetAtPath(materialPath);
            Material material;
            if (existingAsset == null)
            {
                material = new Material(shader) { name = $"M_Tone_{suffix}" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material = existingAsset as Material;
                if (material == null)
                {
                    throw new System.InvalidOperationException($"톤 기준 에셋이 Material이 아니다: {materialPath}");
                }

                material.shader = shader;
            }

            Color linearGray = new Color(linearValue, linearValue, linearValue, 1f);
            material.SetColor(_baseColorId, linearGray);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildCamera(ref int createdGameObjectCount)
        {
            GameObject cameraObject = CreateObject("TestCamera", ref createdGameObjectCount);
            cameraObject.tag = "MainCamera";

            // 세션 간 픽셀 비교가 흔들리지 않도록 위치 (0, 9, -34), 회전 (14, 0, 0), FOV 60을 고정한다.
            cameraObject.transform.position = new Vector3(0f, 9f, -34f);
            cameraObject.transform.eulerAngles = new Vector3(14f, 0f, 0f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.enabled = false;

            AudioListener listener = cameraObject.AddComponent<AudioListener>();
            listener.enabled = false;

            // Play에서는 복사한 펭귄의 기본 카메라를 쓴다. 이 카메라는 고정 구도 비교가 필요할 때
            // 펭귄 CameraRig 대신 사람이 켜는 기준 포즈이며, 둘을 동시에 켜면 화면과 오디오가 겹친다.
        }

        private static bool TryCopyAndConfigureSourceRigs(Scene targetScene, Light sun, out GameObject snowRig,
            out GameObject penguin, out string error)
        {
            snowRig = null;
            penguin = null;
            error = null;
            Scene sourceScene = default;
            bool sourceSceneOpened = false;
            bool sourceSceneClosed = true;
            bool configured = false;

            try
            {
                sourceScene = EditorSceneManager.OpenScene(_sourceScenePath, OpenSceneMode.Additive);
                sourceSceneOpened = true;

                GameObject snowSourceRoot = null;
                GameObject penguinSourceRoot = null;
                foreach (GameObject root in sourceScene.GetRootGameObjects())
                {
                    if (root.name == _snowRootName)
                    {
                        if (snowSourceRoot != null)
                        {
                            error = $"원본 씬에 루트 {_snowRootName}가 둘 이상 있다: {_sourceScenePath}";
                            return false;
                        }

                        snowSourceRoot = root;
                    }

                    if (root.name != _penguinRootName) continue;
                    if (penguinSourceRoot != null)
                    {
                        error = $"원본 씬에 루트 {_penguinRootName}가 둘 이상 있다: {_sourceScenePath}";
                        return false;
                    }

                    penguinSourceRoot = root;
                }

                if (snowSourceRoot == null)
                {
                    error = $"원본 씬에 루트 {_snowRootName}가 없다: {_sourceScenePath}";
                    return false;
                }

                if (penguinSourceRoot == null)
                {
                    error = $"원본 씬에 루트 {_penguinRootName}가 없다: {_sourceScenePath}";
                    return false;
                }

                snowRig = Object.Instantiate(snowSourceRoot);
                snowRig.name = _snowRootName;
                if (snowRig.scene != targetScene) SceneManager.MoveGameObjectToScene(snowRig, targetScene);

                SnowCpuStage snowStage = snowRig.GetComponent<SnowCpuStage>();
                if (snowStage == null)
                {
                    error = $"{_snowRootName}에 {nameof(SnowCpuStage)} 컴포넌트가 없다.";
                    return false;
                }

                SnowSystem snowSystem = snowRig.GetComponent<SnowSystem>();
                if (snowSystem == null)
                {
                    error = $"{_snowRootName}에 {nameof(SnowSystem)} 컴포넌트가 없다.";
                    return false;
                }

                SnowCpuStageView cpuView = snowRig.GetComponent<SnowCpuStageView>();
                if (cpuView == null)
                {
                    error = $"{_snowRootName}에 {nameof(SnowCpuStageView)} 컴포넌트가 없다.";
                    return false;
                }

                SnowDisplaceView displaceView = snowRig.GetComponent<SnowDisplaceView>();
                if (displaceView == null)
                {
                    error = $"{_snowRootName}에 {nameof(SnowDisplaceView)} 컴포넌트가 없다.";
                    return false;
                }

                if (!TrySetEnumByName(snowSystem, "_look", "Displace", out error)) return false;
                if (!TrySetObjectReference(cpuView, "_sun", sun, out error)) return false;
                if (!TrySetObjectReference(displaceView, "_sun", sun, out error)) return false;

                penguin = Object.Instantiate(penguinSourceRoot);
                penguin.name = _penguinRootName;
                if (penguin.scene != targetScene) SceneManager.MoveGameObjectToScene(penguin, targetScene);
                if (!TryConfigurePlayablePenguin(penguin, snowStage, out error)) return false;
                configured = true;
            }
            catch (System.Exception exception)
            {
                error = $"원본 눈 리그를 복사하지 못했다: {exception.Message}";
            }
            finally
            {
                if (sourceSceneOpened && sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    sourceSceneClosed = EditorSceneManager.CloseScene(sourceScene, true);
                }

                if (!sourceSceneClosed)
                {
                    error = $"원본 씬을 저장하지 않고 닫지 못했다: {_sourceScenePath}";
                    configured = false;
                }
            }

            return configured;
        }

        private static bool TryConfigurePlayablePenguin(GameObject penguin, SnowCpuStage snowStage, out string error)
        {
            Transform cameraRig = penguin.transform.Find("CameraRig");
            if (cameraRig == null)
            {
                error = $"{_penguinRootName}에 CameraRig 자식이 없다.";
                return false;
            }

            cameraRig.gameObject.SetActive(true);

            if (penguin.GetComponent<Rigidbody>() == null)
            {
                error = $"{_penguinRootName}에 Rigidbody 컴포넌트가 없다.";
                return false;
            }

            PenguinLocomotion locomotion = penguin.GetComponent<PenguinLocomotion>();
            if (locomotion == null)
            {
                error = $"{_penguinRootName}에 {nameof(PenguinLocomotion)} 컴포넌트가 없다.";
                return false;
            }

            PenguinSnowball snowball = penguin.GetComponent<PenguinSnowball>();
            if (snowball == null)
            {
                error = $"{_penguinRootName}에 {nameof(PenguinSnowball)} 컴포넌트가 없다.";
                return false;
            }

            // 서로 다른 루트를 따로 Instantiate하면 원본 씬의 루트 간 참조는 복제 대상에 맞춰
            // 재매핑되지 않는다. 새 눈 리그를 명시해 슬라이딩과 눈덩이 조작이 원본과 같게 한다.
            if (!TrySetObjectReference(locomotion, "_snowCpuStage", snowStage, out error)) return false;
            if (!TrySetObjectReference(snowball, "_stage", snowStage, out error)) return false;

            // 원본의 배선·회전·입력·물리를 그대로 보존하고 시작점만 테스트 눈밭 중앙에 고정한다.
            // WASD·마우스·Space·Shift와 눈덩이 E/좌클릭/우클릭/Q가 원본 씬과 똑같이 살아 있어야 한다.
            penguin.transform.position = new Vector3(0f, 0.6f, -17f);

            error = null;
            return true;
        }

        private static bool TrySetEnumByName(Object target, string fieldName, string enumName, out string error)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                error = $"{target.GetType().Name}에서 직렬화 필드 {fieldName}을 찾을 수 없다.";
                return false;
            }

            if (property.propertyType != SerializedPropertyType.Enum)
            {
                error = $"{target.GetType().Name}.{fieldName}이 enum 필드가 아니다.";
                return false;
            }

            int enumIndex = System.Array.IndexOf(property.enumNames, enumName);
            if (enumIndex < 0)
            {
                error = $"{target.GetType().Name}.{fieldName}에 enum 멤버 {enumName}이 없다.";
                return false;
            }

            property.enumValueIndex = enumIndex;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            error = null;
            return true;
        }

        private static bool TrySetObjectReference(Object target, string fieldName, Object value, out string error)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(fieldName);
            if (property == null)
            {
                error = $"{target.GetType().Name}에서 직렬화 필드 {fieldName}을 찾을 수 없다.";
                return false;
            }

            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                error = $"{target.GetType().Name}.{fieldName}이 오브젝트 참조 필드가 아니다.";
                return false;
            }

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            error = null;
            return true;
        }

        private static bool EnsureMaterialsFolder()
        {
            if (AssetDatabase.IsValidFolder(_materialsFolderPath)) return true;

            AssetDatabase.CreateFolder("Assets/Game/InGame/Snow/Tests", "Materials");
            return AssetDatabase.IsValidFolder(_materialsFolderPath);
        }

        private static GameObject CreateObject(string name, ref int createdGameObjectCount)
        {
            createdGameObjectCount++;
            return new GameObject(name);
        }

        private static GameObject CreatePrimitive(PrimitiveType primitiveType, string name,
            ref int createdGameObjectCount)
        {
            createdGameObjectCount++;
            GameObject gameObject = GameObject.CreatePrimitive(primitiveType);
            gameObject.name = name;
            return gameObject;
        }
    }
}
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class VfxTestBuilder
    {
        private const string AssetPath = "Assets/Game/Sandbox/VfxTest/VFX/VFX_Firework.vfx";

        /// <summary>잉걸 스프라이트. `docs/images/generate_particle_sprites.py` 가 만든다.</summary>
        private const string EmberTexturePath = "Assets/Game/Sandbox/VfxTest/Textures/T_Spark_Ember.png";
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags AllStatic = All | BindingFlags.Static;

        private static Assembly _vfxAssembly;
        private static Type _modelType;
        private static Type _contextType;
        private static Dictionary<string, Type> _blockTypes;

        #region Reflection

        private static PropertyInfo Prop(object target, string name)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            Type type = target.GetType();
            while (type is not null)
            {
                foreach (PropertyInfo property in type.GetProperties(All | BindingFlags.DeclaredOnly))
                {
                    if (property.Name == name)
                        return property;
                }

                type = type.BaseType;
            }

            throw new MissingMemberException(target.GetType().FullName, name);
        }

        private static FieldInfo Field(object target, string name)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            Type type = target.GetType();
            while (type is not null)
            {
                FieldInfo field = type.GetField(name, All | BindingFlags.DeclaredOnly);
                if (field is not null)
                    return field;

                type = type.BaseType;
            }

            throw new MissingFieldException(target.GetType().FullName, name);
        }

        private static MethodInfo Method(Type type, string name, int parameterCount, bool isStatic)
        {
            Type current = type;
            while (current is not null)
            {
                foreach (MethodInfo method in current.GetMethods(AllStatic | BindingFlags.DeclaredOnly))
                {
                    if (method.Name == name && method.IsStatic == isStatic &&
                        method.GetParameters().Length == parameterCount)
                    {
                        return method;
                    }
                }

                current = current.BaseType;
            }

            string signature = $"{name}({parameterCount} parameters)";
            throw new MissingMethodException(type.FullName, signature);
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            MethodInfo method = Method(target.GetType(), name, arguments.Length, false);
            return InvokeMember(method, target, arguments);
        }

        private static object InvokeStatic(Type type, string name, params object[] arguments)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            MethodInfo method = Method(type, name, arguments.Length, true);
            return InvokeMember(method, null, arguments);
        }

        private static object InvokeMember(MethodInfo method, object target, object[] arguments)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }

        private static void InitializeReflection()
        {
            // VFX Graph 의 저작 API 는 **두 어셈블리에 흩어져 있다.** 이걸 하나로 보면 초기화가 통째로
            // 실패한다 (실측 2026-08-13):
            //
            //   UnityEditor.VFX.VisualEffectResource  ->  UnityEditor.VFXModule
            //   UnityEditor.VFX.VFXModel / VFXGraph   ->  Unity.VisualEffectGraph.Editor
            //   UnityEditor.VisualEffectAssetEditorUtility -> Unity.VisualEffectGraph.Editor
            //                                                (네임스페이스가 UnityEditor.VFX 가 아니다)
            //
            // 블록 열거의 기준이 되는 것은 모델 쪽이므로 그 어셈블리를 잡는다. 타입 하나하나는
            // RequireType 이 로드된 어셈블리 전체에서 찾는다.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType("UnityEditor.VFX.VFXModel") is null)
                    continue;

                _vfxAssembly = assembly;
                break;
            }

            if (_vfxAssembly is null)
                throw new TypeLoadException("UnityEditor.VFX.VFXModel");

            _modelType = RequireType("VFXModel", "UnityEditor.VFX");
            _contextType = RequireType("VFXContext", "UnityEditor.VFX");

            Type[] types;
            try
            {
                types = _vfxAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).ToArray();
            }

            // 블록은 **네임스페이스 두 곳에 나뉘어 있다** (실측 2026-08-13):
            //
            //   UnityEditor.VFX        7 개 — 스포너 계열 (VFXSpawnerBurst, VFXSpawnerConstantRate …)
            //   UnityEditor.VFX.Block  63 개 — 나머지 전부 (Gravity, Drag, GPUEventOnDie …)
            //
            // `.Block` 만 걸면 스포너가 통째로 빠져 Spawn 컨텍스트를 못 채운다. 네임스페이스로 거르지
            // 말고 VFXBlock 파생인지로 거른다.
            Type blockBaseType = RequireType("VFXBlock", "UnityEditor.VFX");
            _blockTypes = types
                .Where(type => !type.IsAbstract && blockBaseType.IsAssignableFrom(type))
                .GroupBy(type => type.Name)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        private static Type RequireType(string name, string typeNamespace)
        {
            // 한 어셈블리만 뒤지면 안 된다 — 위 InitializeReflection 의 주석 참조.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] candidates;
                try
                {
                    candidates = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    candidates = exception.Types.Where(candidate => candidate is not null).ToArray();
                }

                Type match = candidates
                    .FirstOrDefault(candidate => candidate.Name == name && candidate.Namespace == typeNamespace);
                if (match is not null)
                    return match;
            }

            throw new TypeLoadException($"{typeNamespace}.{name}");
        }

        private static object CreateContext(object graph, string typeName, string label, Vector2 position,
            IList<ContextRecord> contexts)
        {
            Type type = RequireType(typeName, "UnityEditor.VFX");
            ScriptableObject context = ScriptableObject.CreateInstance(type);
            if (context == null)
                throw new InvalidOperationException($"ScriptableObject.CreateInstance({type.FullName})");

            Invoke(graph, "AddChild", context, -1, true);
            SetProperty(context, "label", label);
            SetProperty(context, "position", position);
            contexts.Add(new ContextRecord(label, context));
            return context;
        }

        private static object AddBlock(object context, string typeName)
        {
            if (_blockTypes is null || !_blockTypes.TryGetValue(typeName, out Type type))
                throw new TypeLoadException($"UnityEditor.VFX.Block.{typeName}");

            ScriptableObject block = ScriptableObject.CreateInstance(type);
            if (block == null)
                throw new InvalidOperationException($"ScriptableObject.CreateInstance({type.FullName})");

            Invoke(context, "AddChild", block, -1, true);
            return block;
        }

        private static void LinkContexts(object from, object to)
        {
            if (!_contextType.IsInstanceOfType(from) || !_contextType.IsInstanceOfType(to))
                throw new ArgumentException("VFXContext.LinkTo requires VFXContext instances");

            Invoke(from, "LinkTo", to, 0, 0);
        }

        private static void LinkEvent(object triggerBlock, object gpuEventContext)
        {
            object outputSlot = FindSlot(triggerBlock, "outputSlots", "evt");
            object inputSlot = FindSlot(gpuEventContext, "inputSlots", "evt");
            object linked = Invoke(outputSlot, "Link", inputSlot, true);
            if (linked is not bool result || !result)
                throw new InvalidOperationException("VFXSlot.Link(evt, evt, true) returned false");
        }

        private static void SetSetting(object target, string name, object value)
        {
            if (_modelType is null || !_modelType.IsInstanceOfType(target))
                throw new ArgumentException($"VFXModel.SetSettingValue target: {target?.GetType().FullName ?? "null"}");

            Invoke(target, "SetSettingValue", name, value);
        }

        private static void SetEnumSetting(object target, string name, string enumValue)
        {
            FieldInfo field = Field(target, name);
            if (!field.FieldType.IsEnum)
                throw new InvalidOperationException($"{target.GetType().FullName}.{name} is not an enum");

            object value = Enum.Parse(field.FieldType, enumValue, false);
            SetSetting(target, name, value);
        }

        private static void SetProperty(object target, string name, object value)
        {
            PropertyInfo property = Prop(target, name);
            if (!property.CanWrite)
                throw new MissingMemberException(target.GetType().FullName, $"set_{name}");

            property.SetValue(target, ConvertValue(value, property.PropertyType));
        }

        private static void SetSlot(object target, string slotName, object value)
        {
            object slot = FindSlot(target, "inputSlots", slotName);
            PropertyInfo valueProperty = Prop(slot, "value");
            if (!valueProperty.CanWrite)
                throw new MissingMemberException(slot.GetType().FullName, "set_value");

            object currentValue = valueProperty.GetValue(slot);
            Type destinationType = currentValue?.GetType() ?? valueProperty.PropertyType;
            valueProperty.SetValue(slot, ConvertValue(value, destinationType));
        }

        private static object FindSlot(object target, string collectionName, string slotName)
        {
            PropertyInfo collectionProperty = Prop(target, collectionName);
            if (collectionProperty.GetValue(target) is not IEnumerable slots)
                throw new InvalidOperationException($"{target.GetType().FullName}.{collectionName} is not IEnumerable");

            List<string> names = new List<string>();
            object caseInsensitiveMatch = null;
            foreach (object slot in slots)
            {
                string name = Prop(slot, "name").GetValue(slot) as string;
                names.Add(name ?? "<null>");
                if (name == slotName)
                    return slot;

                if (string.Equals(name, slotName, StringComparison.OrdinalIgnoreCase))
                    caseInsensitiveMatch = slot;
            }

            if (caseInsensitiveMatch is not null)
                return caseInsensitiveMatch;

            throw new MissingMemberException(target.GetType().FullName,
                $"{collectionName}[{slotName}] (available: {string.Join(", ", names)})");
        }

        private static object ConvertValue(object value, Type destinationType)
        {
            if (value is null)
                return null;

            Type sourceType = value.GetType();
            if (destinationType.IsAssignableFrom(sourceType))
                return value;

            if (value is Vector3 vector)
            {
                object wrapper = Activator.CreateInstance(destinationType);
                FieldInfo vectorField = destinationType.GetFields(All)
                    .FirstOrDefault(field => field.FieldType == typeof(Vector3));
                if (vectorField is not null)
                {
                    vectorField.SetValue(wrapper, vector);
                    return wrapper;
                }
            }

            if (destinationType.IsEnum && value is string enumName)
                return Enum.Parse(destinationType, enumName, false);

            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(destinationType))
                return Convert.ChangeType(value, destinationType);

            throw new InvalidCastException($"Cannot convert {sourceType.FullName} to {destinationType.FullName}");
        }

        private static IEnumerable<object> Children(object model)
        {
            if (Prop(model, "children").GetValue(model) is not IEnumerable children)
                throw new InvalidOperationException($"{model.GetType().FullName}.children is not IEnumerable");

            foreach (object child in children)
                yield return child;
        }

        #endregion

        [MenuItem("Tools/VfxTest/Build Firework")]
        private static void BuildFirework()
        {
            object resource = null;
            object graph = null;
            Dictionary<string, Type> blockTypes = null;
            List<ContextRecord> contexts = new List<ContextRecord>();
            List<LinkRecord> links = new List<LinkRecord>();
            bool assetCreated = false;

            if (!RunStep("Reflection: initialize VFX Graph API", () =>
                {
                    InitializeReflection();
                    blockTypes = _blockTypes;
                }))
            {
                return;
            }

            if (!RunStep("Asset: overwrite VFX_Firework.vfx", () =>
                {
                    // 존재 판정을 GUID 로 하면 안 된다. `AssetPathToGUID` 는 방금 지운 경로에도 캐시된
                    // GUID 를 돌려주므로, 없는 파일을 지우려다 DeleteAsset 이 false 를 내고 여기서
                    // 멈춘다 (실측). 실제로 로드되는지로 판정한다.
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetPath) != null &&
                        !AssetDatabase.DeleteAsset(AssetPath))
                        throw new InvalidOperationException($"AssetDatabase.DeleteAsset({AssetPath}) returned false");

                    Type utilityType = RequireType("VisualEffectAssetEditorUtility", "UnityEditor");
                    assetCreated = true;
                    object asset = InvokeStatic(utilityType, "CreateNewAsset", AssetPath);
                    if (asset is null)
                        throw new InvalidOperationException("VisualEffectAssetEditorUtility.CreateNewAsset returned null");
                }))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            if (!RunStep("Asset: get VisualEffectResource.graph", () =>
                {
                    Type resourceType = RequireType("VisualEffectResource", "UnityEditor.VFX");
                    resource = InvokeStatic(resourceType, "GetResourceAtPath", AssetPath);
                    if (resource is null)
                        throw new InvalidOperationException("VisualEffectResource.GetResourceAtPath returned null");

                    graph = Prop(resource, "graph").GetValue(resource);
                    if (graph is null)
                        throw new InvalidOperationException("VisualEffectResource.graph returned null");
                }))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            if (blockTypes is null)
            {
                Debug.LogError("[VfxTest] Reflection: concrete VFX block enumeration returned null");
                CleanupFailedAsset(assetCreated);
                return;
            }

            SystemContexts stage1 = null;
            SystemContexts stage2 = null;
            SystemContexts burst = null;
            SystemContexts trail = null;
            object stage1OnDie = null;
            object stage1Rate = null;
            object stage2OnDie = null;
            object stage2Rate = null;

            if (!RunStep("System 1: create contexts", () =>
                    stage1 = CreateCpuSystem(graph, "System 1 — Stage 1 Rise", new Vector2(0, 0), contexts)) ||
                !RunStep("System 1: link Spawn → Init", () => AddContextLink(stage1.Spawn, stage1.Initialize,
                    "System 1 Spawn → Init", links)) ||
                !RunStep("System 1: link Init → Update", () => AddContextLink(stage1.Initialize, stage1.Update,
                    "System 1 Init → Update", links)) ||
                !RunStep("System 1: link Update → Output", () => AddContextLink(stage1.Update, stage1.Output,
                    "System 1 Update → Output", links)) ||
                !RunStep("System 1: configure Spawn", () =>
                {
                    // 주기 발사다. `VFXSpawnerBurst`(Single Burst) 로 두면 재생하자마자 한 발 쏘고
                    // 영원히 조용해서, Play 를 누른 사람이 2 초 안에 못 보면 놓친다. 룩을 눈으로
                    // 판정해야 하는 테스트 자산이므로 계속 다시 쏘는 편이 맞다.
                    // 단발이다. 반복 발사는 `VFXSpawnerPeriodicBurst` 로 바꿔봤으나 파티클이 하나도
                    // 안 나왔다 (컴파일 에러 없이 alive=0 이 8 초 내내 — 실측 2026-08-13). 슬롯 이름과
                    // 타입은 맞췄는데(`nb`/`period`, 둘 다 Vector2 범위) 그래도 안 났다. 원인을 더 파는
                    // 대신 **검증된 단발을 두고 반복은 씬 쪽 스크립트가 Reinit 으로 돌린다** —
                    // 그래프와 씨름하는 것보다 눈으로 볼 수 있게 만드는 게 먼저다.
                    object burstBlock = AddBlock(stage1.Spawn, "VFXSpawnerBurst");
                    SetSlot(burstBlock, "Count", 6.0f);
                }) ||
                !RunStep("System 1: configure Initialize", () =>
                {
                    SetSetting(stage1.Initialize, "capacity", 32u);

                    // 여러 발을 동시에 쏜다. 셋 다 흩어야 한 발처럼 안 보인다 —
                    // 발사 위치가 같으면 겹쳐 보이고, 수명이 같으면 전부 같은 순간에 터진다.
                    AddRandomAttribute(stage1.Initialize, "position",
                        new Vector3(-6.0f, 0.0f, -3.0f), new Vector3(6.0f, 0.0f, 3.0f));
                    AddRandomAttribute(stage1.Initialize, "lifetime", 0.45f, 0.75f);
                    AddRandomAttribute(stage1.Initialize, "velocity",
                        new Vector3(-1.6f, 15.0f, -1.6f), new Vector3(1.6f, 21.0f, 1.6f));
                    AddSetAttribute(stage1.Initialize, "size", 0.12f);
                    AddSetAttribute(stage1.Initialize, "color", new Vector3(1.0f, 0.82f, 0.55f));
                }) ||
                !RunStep("System 1: configure Update", () =>
                {
                    AddForce(stage1.Update, new Vector3(0.0f, -9.81f, 0.0f), 0.6f);
                    stage1OnDie = AddBlock(stage1.Update, "GPUEventOnDie");
                    SetSlot(stage1OnDie, "count", 1u);
                    stage1Rate = AddBlock(stage1.Update, "GPUEventRate");
                    SetSlot(stage1Rate, "Rate", 42.0f);
                }) ||
                !RunStep("System 1: configure Output", () => ConfigureOutput(stage1.Output)))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            if (!RunStep("System 2: create contexts", () =>
                    stage2 = CreateGpuSystem(graph, "System 2 — Stage 2 Rise", new Vector2(0, 450), contexts)) ||
                !RunStep("System 2: link GPU Event → Init", () => AddContextLink(stage2.Spawn, stage2.Initialize,
                    "System 2 GPU Event → Init", links)) ||
                !RunStep("System 2: link Init → Update", () => AddContextLink(stage2.Initialize, stage2.Update,
                    "System 2 Init → Update", links)) ||
                !RunStep("System 2: link Update → Output", () => AddContextLink(stage2.Update, stage2.Output,
                    "System 2 Update → Output", links)) ||
                !RunStep("System 2: link System 1 OnDie → GPU Event", () => AddEventLink(stage1OnDie, stage2.Spawn,
                    "System 1 OnDie.evt → System 2 GPU Event.evt", links)) ||
                !RunStep("System 2: configure Initialize", () =>
                {
                    SetSetting(stage2.Initialize, "capacity", 32u);
                    AddInheritedAttribute(stage2.Initialize, "position");
                    AddSetAttribute(stage2.Initialize, "lifetime", 0.5f);
                    AddSetAttribute(stage2.Initialize, "velocity", new Vector3(0.0f, 13.0f, 0.0f));
                    AddSetAttribute(stage2.Initialize, "size", 0.14f);
                    AddSetAttribute(stage2.Initialize, "color", new Vector3(1.0f, 0.42f, 0.08f));
                }) ||
                !RunStep("System 2: configure Update", () =>
                {
                    AddForce(stage2.Update, new Vector3(0.0f, -9.81f, 0.0f), 0.5f);
                    stage2OnDie = AddBlock(stage2.Update, "GPUEventOnDie");
                    SetSlot(stage2OnDie, "count", 260u);
                    stage2Rate = AddBlock(stage2.Update, "GPUEventRate");
                    SetSlot(stage2Rate, "Rate", 52.0f);
                }) ||
                !RunStep("System 2: configure Output", () => ConfigureOutput(stage2.Output)))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            if (!RunStep("System 3: create contexts", () =>
                    burst = CreateGpuSystem(graph, "System 3 — Burst", new Vector2(0, 900), contexts)) ||
                !RunStep("System 3: link GPU Event → Init", () => AddContextLink(burst.Spawn, burst.Initialize,
                    "System 3 GPU Event → Init", links)) ||
                !RunStep("System 3: link Init → Update", () => AddContextLink(burst.Initialize, burst.Update,
                    "System 3 Init → Update", links)) ||
                !RunStep("System 3: link Update → Output", () => AddContextLink(burst.Update, burst.Output,
                    "System 3 Update → Output", links)) ||
                !RunStep("System 3: link System 2 OnDie → GPU Event", () => AddEventLink(stage2OnDie, burst.Spawn,
                    "System 2 OnDie.evt → System 3 GPU Event.evt", links)) ||
                !RunStep("System 3: configure Initialize", () =>
                {
                    SetSetting(burst.Initialize, "capacity", 2048u);
                    AddInheritedAttribute(burst.Initialize, "position");
                    AddRandomAttribute(burst.Initialize, "lifetime", 1.1f, 2.0f);
                    AddRandomVelocity(burst.Initialize, 6.0f, 15.0f);
                    AddSetAttribute(burst.Initialize, "size", 0.08f);
                }) ||
                !RunStep("System 3: configure Update", () =>
                {
                    AddForce(burst.Update, new Vector3(0.0f, -6.0f, 0.0f), 1.2f);
                    AddAttributeCurve(burst.Update, "size", AnimationCurve.Linear(0.0f, 0.08f, 1.0f, 0.03f));
                    AddColorCurve(burst.Update, CreateBurstGradient());
                }) ||
                !RunStep("System 3: configure Output", () => ConfigureOutput(burst.Output)))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            if (!RunStep("System 4: create contexts", () =>
                    trail = CreateTrailSystem(graph, "System 4 — Spark Trail", new Vector2(0, 1350), contexts)) ||
                // 파티클 시스템 하나에 GPU 이벤트 입력을 **둘 이상 물릴 수 없다.** 두 개를 물리면
                // 자산은 만들어지지만 컴파일이 통째로 실패한다 (실측 2026-08-13):
                //   InvalidOperationException: Unexpected multiple input dependency for GPU event
                //     at UnityEditor.VFX.VFXDataParticle.FillDescs
                // 그래서 GPU 이벤트 컨텍스트는 하나만 두고, 1단·2단의 Rate 블록을 **같은 컨텍스트**에
                // 물린다. trail.SecondarySpawn 은 그래프에 남지만 아무것도 안 먹인다.
                !RunStep("System 4: link Stage 1 GPU Event → Init", () => AddContextLink(trail.Spawn, trail.Initialize,
                    "System 4 Stage 1 GPU Event → Init", links)) ||
                !RunStep("System 4: link Init → Update", () => AddContextLink(trail.Initialize, trail.Update,
                    "System 4 Init → Update", links)) ||
                !RunStep("System 4: link Update → Output", () => AddContextLink(trail.Update, trail.Output,
                    "System 4 Update → Output", links)) ||
                !RunStep("System 4: link System 1 Rate → GPU Event", () => AddEventLink(stage1Rate, trail.Spawn,
                    "System 1 Rate.evt → System 4 Stage 1 GPU Event.evt", links)) ||
                !RunStep("System 4: link System 2 Rate → GPU Event", () => AddEventLink(stage2Rate,
                    trail.Spawn, "System 2 Rate.evt → System 4 GPU Event.evt", links)) ||
                !RunStep("System 4: configure Initialize", () =>
                {
                    SetSetting(trail.Initialize, "capacity", 2048u);
                    AddInheritedAttribute(trail.Initialize, "position");
                    AddRandomAttribute(trail.Initialize, "lifetime", 0.25f, 0.5f);
                    AddRandomVelocity(trail.Initialize, 0.25f, 1.1f);
                    AddSetAttribute(trail.Initialize, "size", 0.05f);
                }) ||
                !RunStep("System 4: configure Update", () =>
                {
                    AddForce(trail.Update, new Vector3(0.0f, -2.0f, 0.0f), 2.0f);
                    AddColorCurve(trail.Update, CreateTrailGradient());
                }) ||
                !RunStep("System 4: configure Output", () => ConfigureOutput(trail.Output)))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            if (!RunStep("Asset: compile and save", () =>
                {
                    // `RecompileIfNeeded` 는 이 버전에 **없다**. 구버전 API 다 (실측 2026-08-13 —
                    // VFXGraph 가 실제로 노출하는 것은 Compile() 과 CompileAndUpdateAsset(asset) 이다).
                    Invoke(graph, "SetExpressionGraphDirty", true);
                    Invoke(graph, "Compile");
                    Invoke(resource, "WriteAsset");

                    UnityEngine.Object resourceObject = resource as UnityEngine.Object;
                    if (resourceObject == null)
                        throw new InvalidCastException("VisualEffectResource is not UnityEngine.Object");

                    EditorUtility.SetDirty(resourceObject);
                    AssetDatabase.SaveAssets();
                }))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            LogSummary(contexts, links);
        }

        private const string VortexAssetPath = "Assets/Game/Sandbox/VfxTest/VFX/VFX_Vortex.vfx";
        private const string VortexMeshPath = "Assets/Game/Sandbox/VfxTest/Meshes/SM_VortexRibbon.fbx";
        private const string WispTexturePath = "Assets/Game/Sandbox/VfxTest/Textures/T_Vortex_Wisp.png";

        /// <summary>
        /// 소용돌이. 두 계로 나뉜다 — Blender 로 만든 나선 리본을 <b>메시 출력</b>으로 돌리는 깔때기
        /// 본체와, 그 주위를 감아 도는 <b>위습 파티클</b>이다.
        ///
        /// 본체만 두면 도는 판때기로 보이고 파티클만 두면 형태가 안 잡힌다. 둘이 같이 있어야
        /// 빨려드는 깔때기로 읽힌다.
        /// </summary>
        [MenuItem("Tools/VfxTest/Build Vortex")]
        private static void BuildVortex()
        {
            object resource = null;
            object graph = null;
            List<ContextRecord> contexts = new List<ContextRecord>();
            List<LinkRecord> links = new List<LinkRecord>();
            bool assetCreated = false;

            if (!RunStep("Reflection: initialize VFX Graph API", InitializeReflection))
                return;

            if (!RunStep("Asset: overwrite VFX_Vortex.vfx", () =>
                {
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VortexAssetPath) != null &&
                        !AssetDatabase.DeleteAsset(VortexAssetPath))
                        throw new InvalidOperationException($"DeleteAsset({VortexAssetPath}) returned false");

                    Type utilityType = RequireType("VisualEffectAssetEditorUtility", "UnityEditor");
                    assetCreated = true;
                    if (InvokeStatic(utilityType, "CreateNewAsset", VortexAssetPath) is null)
                        throw new InvalidOperationException("CreateNewAsset returned null");
                }))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            if (!RunStep("Asset: get graph", () =>
                {
                    Type resourceType = RequireType("VisualEffectResource", "UnityEditor.VFX");
                    resource = InvokeStatic(resourceType, "GetResourceAtPath", VortexAssetPath);
                    graph = Prop(resource, "graph").GetValue(resource);
                    if (graph is null)
                        throw new InvalidOperationException("resource.graph is null");
                }))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            SystemContexts funnel = null;
            SystemContexts wisps = null;

            if (!RunStep("Funnel: create contexts", () =>
                    funnel = new SystemContexts(
                        CreateContext(graph, "VFXBasicSpawner", "Funnel / Spawn", new Vector2(0, 0), contexts),
                        CreateContext(graph, "VFXBasicInitialize", "Funnel / Initialize", new Vector2(230, 0), contexts),
                        CreateContext(graph, "VFXBasicUpdate", "Funnel / Update", new Vector2(460, 0), contexts),
                        CreateContext(graph, "VFXMeshOutput", "Funnel / Output", new Vector2(690, 0), contexts))) ||
                !RunStep("Funnel: link Spawn → Init", () =>
                    AddContextLink(funnel.Spawn, funnel.Initialize, "Funnel Spawn → Init", links)) ||
                !RunStep("Funnel: link Init → Update", () =>
                    AddContextLink(funnel.Initialize, funnel.Update, "Funnel Init → Update", links)) ||
                !RunStep("Funnel: link Update → Output", () =>
                    AddContextLink(funnel.Update, funnel.Output, "Funnel Update → Output", links)) ||
                !RunStep("Funnel: configure Spawn", () =>
                {
                    object burst = AddBlock(funnel.Spawn, "VFXSpawnerBurst");
                    SetSlot(burst, "Count", 1.0f);
                }) ||
                !RunStep("Funnel: configure Initialize", () =>
                {
                    SetSetting(funnel.Initialize, "capacity", 4u);
                    AddSetAttribute(funnel.Initialize, "position", Vector3.zero);

                    // 깔때기는 계속 서 있어야 한다. 짧게 주면 껌뻑인다.
                    AddSetAttribute(funnel.Initialize, "lifetime", 9999.0f);
                    AddSetAttribute(funnel.Initialize, "size", 1.0f);
                    AddSetAttribute(funnel.Initialize, "color", new Vector3(1.6f, 2.4f, 3.4f));

                    // 각속도만 넣으면 안 돈다 — Update 에 적분 블록이 있어야 실제로 회전한다.
                    AddSetAttribute(funnel.Initialize, "angularVelocityY", 70.0f);
                }) ||
                !RunStep("Funnel: configure Update", () =>
                {
                    AddBlock(funnel.Update, "AngularEulerIntegration");
                }) ||
                !RunStep("Funnel: configure Output", () =>
                {
                    SetEnumSetting(funnel.Output, "blendMode", "Additive");

                    Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(VortexMeshPath);
                    if (mesh == null)
                    {
                        GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(VortexMeshPath);
                        if (imported != null)
                            mesh = imported.GetComponentInChildren<MeshFilter>()?.sharedMesh;
                    }

                    if (mesh == null)
                        throw new InvalidOperationException($"메시를 못 찾았다: {VortexMeshPath}");

                    SetSlot(funnel.Output, "mesh", mesh);
                }))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            if (!RunStep("Wisps: create contexts", () =>
                    wisps = CreateCpuSystem(graph, "Wisps", new Vector2(0, 420), contexts)) ||
                !RunStep("Wisps: link Spawn → Init", () =>
                    AddContextLink(wisps.Spawn, wisps.Initialize, "Wisps Spawn → Init", links)) ||
                !RunStep("Wisps: link Init → Update", () =>
                    AddContextLink(wisps.Initialize, wisps.Update, "Wisps Init → Update", links)) ||
                !RunStep("Wisps: link Update → Output", () =>
                    AddContextLink(wisps.Update, wisps.Output, "Wisps Update → Output", links)) ||
                !RunStep("Wisps: configure Spawn", () =>
                {
                    object rate = AddBlock(wisps.Spawn, "VFXSpawnerConstantRate");
                    SetSlot(rate, "Rate", 220.0f);
                }) ||
                !RunStep("Wisps: configure Initialize", () =>
                {
                    SetSetting(wisps.Initialize, "capacity", 1024u);

                    // 바깥 링에서 태어나 안쪽으로 감겨 들어간다. 위치를 링으로 뿌리고 속도에 접선
                    // 성분을 크게, 위 성분을 작게 주면 나선이 나온다 — 힘 블록 없이도 읽힌다.
                    // 멀리서부터 빨려들어야 하므로 **넓게** 태운다. 좁게 뿌리면 이미 도착한
                    // 것들만 도는 것으로 보이고 "주변에서 끌려온다"가 안 읽힌다.
                    AddRandomAttribute(wisps.Initialize, "position",
                        new Vector3(-9.0f, 0.05f, -9.0f), new Vector3(9.0f, 1.6f, 9.0f));
                    AddRandomAttribute(wisps.Initialize, "lifetime", 1.6f, 3.0f);
                    AddRandomAttribute(wisps.Initialize, "size", 0.20f, 0.55f);
                    AddSetAttribute(wisps.Initialize, "color", new Vector3(0.82f, 0.82f, 0.82f));

                    // 회전 성분. 랜덤 속도로는 흩어지기만 하고 절대 감기지 않는다.
                    object tangent = AddBlock(wisps.Initialize, "VelocityTangent");
                    SetSlot(tangent, "axis", new Vector3(0.0f, 1.0f, 0.0f));
                    SetSlot(tangent, "Speed", 5.0f);

                    // **구심 성분.** 접선만 있으면 축을 *돌기만* 하지 *다가가지* 않는다. 빨려드는
                    // 인상은 안쪽으로 당기는 성분에서 나온다. VelocitySpherical 은 중심에서 바깥으로
                    // 미는 블록이므로 **속도를 음수로** 주면 그대로 구심력이 된다 — 위치에서 축까지의
                    // 벡터를 오퍼레이터로 계산할 필요가 없다.
                    object inward = AddBlock(wisps.Initialize, "VelocitySpherical");
                    SetEnumSetting(inward, "composition", "Add");   // 문자열은 SetEnumSetting 으로. SetSetting 은 열거형에 못 넣는다
                    SetSlot(inward, "Speed", -3.4f);
                }) ||
                !RunStep("Wisps: configure Update", () =>
                {
                    // 난류를 세게 주면 방금 만든 회전을 도로 흩어버린다. 낮게 깔아 결만 준다.
                    AddForce(wisps.Update, new Vector3(0.0f, 3.2f, 0.0f), 0.35f);
                    object turb = AddBlock(wisps.Update, "Turbulence");
                    SetSlot(turb, "Intensity", 0.7f);
                }) ||
                !RunStep("Wisps: configure Output", () =>
                {
                    SetEnumSetting(wisps.Output, "primitiveType", "Quad");
                    SetEnumSetting(wisps.Output, "blendMode", "Additive");

                    // 쉼표 모양 위습이 전부 같은 각도로 서 있으면 도장 찍은 것처럼 보인다. 속도 방향으로
                    // 눕히면 흐름을 따라 흘러 회전이 눈에 보인다 — 랜덤 각도보다 이쪽이 맞다.
                    //
                    // **블록만 넣으면 아무 일도 안 일어난다.** Orient 는 입력 슬롯이 없고 mode 가 설정이라,
                    // 기본값 FaceCameraPlane 으로 남으면 카메라만 보고 회전에 반응하지 않는다.
                    object orient = AddBlock(wisps.Output, "Orient");
                    SetEnumSetting(orient, "mode", "AlongVelocity");

                    Texture2D wisp = AssetDatabase.LoadAssetAtPath<Texture2D>(WispTexturePath);
                    if (wisp == null)
                    {
                        Debug.LogWarning($"[VfxTest] 위습 텍스처가 아직 없다 — 기본 스프라이트로 둔다: {WispTexturePath}");
                        return;
                    }

                    SetSlot(wisps.Output, "mainTexture", wisp);
                }))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            if (!RunStep("Asset: compile and save", () =>
                {
                    Invoke(graph, "SetExpressionGraphDirty", true);
                    Invoke(graph, "Compile");
                    Invoke(resource, "WriteAsset");
                    if (resource is not UnityEngine.Object resourceObject)
                        throw new InvalidCastException("VisualEffectResource is not UnityEngine.Object");

                    EditorUtility.SetDirty(resourceObject);
                    AssetDatabase.SaveAssets();
                }))
            {
                CleanupFailedAsset(assetCreated);
                return;
            }

            Debug.Log($"[VfxTest] Vortex graph built — contexts {contexts.Count}, links {links.Count}");
        }

        private static SystemContexts CreateCpuSystem(object graph, string name, Vector2 origin,
            IList<ContextRecord> contexts)
        {
            return new SystemContexts(
                CreateContext(graph, "VFXBasicSpawner", $"{name} / Spawn", origin, contexts),
                CreateContext(graph, "VFXBasicInitialize", $"{name} / Initialize", origin + new Vector2(230, 0), contexts),
                CreateContext(graph, "VFXBasicUpdate", $"{name} / Update", origin + new Vector2(460, 0), contexts),
                CreateContext(graph, "VFXPlanarPrimitiveOutput", $"{name} / Output", origin + new Vector2(690, 0), contexts));
        }

        private static SystemContexts CreateGpuSystem(object graph, string name, Vector2 origin,
            IList<ContextRecord> contexts)
        {
            return new SystemContexts(
                CreateContext(graph, "VFXBasicGPUEvent", $"{name} / GPU Event", origin, contexts),
                CreateContext(graph, "VFXBasicInitialize", $"{name} / Initialize", origin + new Vector2(230, 0), contexts),
                CreateContext(graph, "VFXBasicUpdate", $"{name} / Update", origin + new Vector2(460, 0), contexts),
                CreateContext(graph, "VFXPlanarPrimitiveOutput", $"{name} / Output", origin + new Vector2(690, 0), contexts));
        }

        private static SystemContexts CreateTrailSystem(object graph, string name, Vector2 origin,
            IList<ContextRecord> contexts)
        {
            // GPU 이벤트 입력 슬롯은 단일 링크만 받으므로 두 소스를 같은 파티클 데이터로 합친다.
            object primarySpawn = CreateContext(graph, "VFXBasicGPUEvent", $"{name} / Stage 1 GPU Event",
                origin, contexts);
            object secondarySpawn = CreateContext(graph, "VFXBasicGPUEvent", $"{name} / Stage 2 GPU Event",
                origin + new Vector2(0, 230), contexts);
            return new SystemContexts(
                primarySpawn,
                CreateContext(graph, "VFXBasicInitialize", $"{name} / Initialize", origin + new Vector2(230, 0), contexts),
                CreateContext(graph, "VFXBasicUpdate", $"{name} / Update", origin + new Vector2(460, 0), contexts),
                CreateContext(graph, "VFXPlanarPrimitiveOutput", $"{name} / Output", origin + new Vector2(690, 0), contexts),
                secondarySpawn);
        }

        private static void AddSetAttribute(object context, string attribute, object value)
        {
            object block = AddBlock(context, "SetAttribute");
            SetSetting(block, "attribute", attribute);
            SetEnumSetting(block, "Composition", "Overwrite");
            SetEnumSetting(block, "Source", "Slot");
            SetEnumSetting(block, "Random", "Off");
            SetSlot(block, $"_{attribute}", value);
        }

        private static void AddRandomAttribute(object context, string attribute, object minimum, object maximum)
        {
            object block = AddBlock(context, "SetAttribute");
            SetSetting(block, "attribute", attribute);
            SetEnumSetting(block, "Composition", "Overwrite");
            SetEnumSetting(block, "Source", "Slot");
            SetEnumSetting(block, "Random", "Uniform");
            SetSlot(block, "A", minimum);
            SetSlot(block, "B", maximum);
        }

        private static void AddInheritedAttribute(object context, string attribute)
        {
            object block = AddBlock(context, "SetAttribute");
            SetSetting(block, "attribute", attribute);
            SetEnumSetting(block, "Composition", "Overwrite");
            SetEnumSetting(block, "Source", "Source");
        }

        private static void AddRandomVelocity(object context, float minimumSpeed, float maximumSpeed)
        {
            object block = AddBlock(context, "VelocityRandomize");
            SetEnumSetting(block, "composition", "Overwrite");
            SetEnumSetting(block, "speedMode", "Random");
            SetSlot(block, "MinSpeed", minimumSpeed);
            SetSlot(block, "MaxSpeed", maximumSpeed);
        }

        private static void AddForce(object update, Vector3 gravity, float drag)
        {
            object gravityBlock = AddBlock(update, "Gravity");
            SetSlot(gravityBlock, "Force", gravity);

            object dragBlock = AddBlock(update, "Drag");
            SetSlot(dragBlock, "dragCoefficient", drag);
        }

        private static void AddAttributeCurve(object context, string attribute, AnimationCurve curve)
        {
            object block = AddBlock(context, "AttributeFromCurve");
            SetSetting(block, "attribute", attribute);
            SetEnumSetting(block, "Composition", "Overwrite");
            SetEnumSetting(block, "SampleMode", "OverLife");
            SetEnumSetting(block, "Mode", "Uniform");
            SetSlot(block, char.ToUpperInvariant(attribute[0]) + attribute.Substring(1), curve);
        }

        private static void AddColorCurve(object context, Gradient gradient)
        {
            object block = AddBlock(context, "AttributeFromCurve");
            SetSetting(block, "attribute", "color");
            SetEnumSetting(block, "Composition", "Overwrite");
            SetEnumSetting(block, "AlphaComposition", "Overwrite");
            SetEnumSetting(block, "SampleMode", "OverLife");
            SetEnumSetting(block, "Mode", "PerComponent");
            SetEnumSetting(block, "ColorMode", "ColorAndAlpha");
            SetSlot(block, "Color", gradient);
        }

        private static void ConfigureOutput(object output)
        {
            SetEnumSetting(output, "primitiveType", "Quad");
            SetEnumSetting(output, "blendMode", "Additive");

            // 기본 스프라이트는 딱딱한 사각형이라 잉걸로 안 읽힌다. 슬롯 이름은 `mainTexture` 다.
            // 텍스처가 없으면 조용히 건너뛴다 — 자산이 아직 임포트 안 된 상태에서도 그래프는 만들어져야
            // 원인을 가릴 수 있다.
            Texture2D ember = AssetDatabase.LoadAssetAtPath<Texture2D>(EmberTexturePath);
            if (ember == null)
            {
                Debug.LogWarning($"[VfxTest] 잉걸 텍스처가 없어 기본 스프라이트로 둔다: {EmberTexturePath}");
                return;
            }

            SetSlot(output, "mainTexture", ember);
        }

        private static Gradient CreateBurstGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.78f, 0.32f), 0.0f),
                    new GradientColorKey(new Color(1.0f, 0.2f, 0.03f), 0.55f),
                    new GradientColorKey(new Color(0.42f, 0.01f, 0.0f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.85f, 0.65f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            return gradient;
        }

        private static Gradient CreateTrailGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.9f, 0.6f), 0.0f),
                    new GradientColorKey(new Color(1.0f, 0.3f, 0.04f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0.0f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            return gradient;
        }

        private static void AddContextLink(object from, object to, string description, ICollection<LinkRecord> links)
        {
            LinkContexts(from, to);
            links.Add(new LinkRecord(from, to, description));
        }

        private static void AddEventLink(object from, object to, string description, ICollection<LinkRecord> links)
        {
            LinkEvent(from, to);
            object sourceContext = Invoke(from, "GetParent");
            if (sourceContext is null)
                throw new InvalidOperationException($"{from.GetType().FullName}.GetParent returned null");

            links.Add(new LinkRecord(sourceContext, to, description));
        }

        private static bool RunStep(string step, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[VfxTest] {step}: {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
                return false;
            }
        }

        private static void CleanupFailedAsset(bool assetCreated)
        {
            // 실패한 그래프가 다음 실행의 입력처럼 보이지 않도록 생성물만 제거한다.
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);
            if (assetCreated && !string.IsNullOrEmpty(guid) && !AssetDatabase.DeleteAsset(AssetPath))
                Debug.LogError($"[VfxTest] Cleanup: AssetDatabase.DeleteAsset({AssetPath}) returned false");
        }

        private static void LogSummary(IEnumerable<ContextRecord> contexts, IEnumerable<LinkRecord> links)
        {
            StringBuilder summary = new StringBuilder("[VfxTest] Firework graph built\nContexts:");
            foreach (ContextRecord context in contexts)
            {
                int blockCount = Children(context.Model).Count();
                summary.Append($"\n- {context.Label}: {blockCount} block(s)");
                string[] contextLinks = links
                    .Where(link => ReferenceEquals(link.From, context.Model) || ReferenceEquals(link.To, context.Model))
                    .Select(link => link.Description)
                    .ToArray();
                summary.Append(contextLinks.Length == 0
                    ? "; links: none"
                    : $"; links: {string.Join(" | ", contextLinks)}");
            }

            Debug.Log(summary.ToString());
        }

        private sealed class SystemContexts
        {
            internal SystemContexts(object spawn, object initialize, object update, object output,
                object secondarySpawn = null)
            {
                Spawn = spawn;
                Initialize = initialize;
                Update = update;
                Output = output;
                SecondarySpawn = secondarySpawn;
            }

            internal object Spawn { get; }
            internal object Initialize { get; }
            internal object Update { get; }
            internal object Output { get; }
            internal object SecondarySpawn { get; }
        }

        private sealed class ContextRecord
        {
            internal ContextRecord(string label, object model)
            {
                Label = label;
                Model = model;
            }

            internal string Label { get; }
            internal object Model { get; }
        }

        private sealed class LinkRecord
        {
            internal LinkRecord(object from, object to, string description)
            {
                From = from;
                To = to;
                Description = description;
            }

            internal object From { get; }
            internal object To { get; }
            internal string Description { get; }
        }
    }
}

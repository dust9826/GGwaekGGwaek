using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 현재 목표인 집의 선물 배치 범위를 눈 표면에 붙는 얇은 입체 장판과 펭귄 문양으로 표시한다.
    /// 판정은 <see cref="GiftDropZone"/>이 소유하고, 이 컴포넌트는 위치와 크기만 읽는 표시 레이어다.
    ///
    /// 이전의 공중 반투명 직육면체는 배송 지점보다 플랫폼처럼 보였다. 지금은 빈 중앙을 유지한
    /// 이중 링, 안쪽으로 흐르는 얇은 리본, 펭귄 엠블럼과 위로 말려 올라오는 빛줄기를 섞어
    /// 눈 위에서도 실루엣을 잃지 않으면서 "펭귄이 이 색의 선물을 이곳에 놓는다"는 의미를 전달한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftDropZoneHighlight : MonoBehaviour
    {
        private static readonly Color Cream = new Color32(255, 244, 196, 255);
        private static readonly Color PenguinInk = new Color32(21, 57, 80, 255);

        [SerializeField, Min(0f)] private float _surfaceOffset = 0.34f;
        [SerializeField, Range(24, 96)] private int _ringSegments = 64;
        [SerializeField, Range(0f, 0.12f)] private float _pulseScale = 0.025f;
        [SerializeField, Min(0f)] private float _pulseSpeed = 2.1f;
        [SerializeField] private Color _giftColor = new Color32(241, 91, 98, 255);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseAlphaId = Shader.PropertyToID("_BaseAlpha");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int UseBaseMapId = Shader.PropertyToID("_UseBaseMap");
        private static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
        private static readonly int UseAccentRemapId = Shader.PropertyToID("_UseAccentRemap");
        private static readonly int AmbientFloorId = Shader.PropertyToID("_AmbientFloor");
        private static readonly int SpecularIntensityId = Shader.PropertyToID("_SpecularIntensity");

        private static Material _sharedMarkerMaterial;
        private static Material _sharedParticleMaterial;
        private static Texture2D _sharedStarTexture;
        private static Texture2D _sharedPraiseStampTexture;

        private readonly List<Mesh> _runtimeMeshes = new List<Mesh>();
        private readonly List<MeshRenderer> _idleRenderers = new List<MeshRenderer>();

        private Transform _zone;
        private Transform _pulseRoot;
        private Transform _ribbonRoot;
        private Vector2 _sizeXZ;
        private ParticleSystem _idleMotes;
        private ParticleSystem _risingSteam;
        private ParticleSystem _completionBurst;
        private float _phase;
        private bool _completed;

        /// <summary>활성화 전에 호출해야 런타임 메시가 올바른 크기와 색으로 만들어진다.</summary>
        public void Configure(Transform zone, Vector2 sizeXZ) => Configure(zone, sizeXZ, _giftColor);

        /// <summary>활성화 전에 호출해야 런타임 메시가 올바른 크기와 색으로 만들어진다.</summary>
        public void Configure(Transform zone, Vector2 sizeXZ, Color giftColor)
        {
            _zone = zone;
            _sizeXZ = sizeXZ;
            _giftColor = new Color(giftColor.r, giftColor.g, giftColor.b, 1f);
        }

        public void PlayCompletion()
        {
            if (_completed) return;
            _completed = true;

            foreach (MeshRenderer renderer in _idleRenderers)
                if (renderer != null) renderer.enabled = false;
            StopAndClear(_idleMotes);
            StopAndClear(_risingSteam);
            if (_completionBurst != null)
            {
                _completionBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _completionBurst.Play(true);
            }
        }

        private void Awake()
        {
            _phase = Random.Range(0f, Mathf.PI * 2f);
            BuildVisual();
        }

        private void LateUpdate()
        {
            if (_zone == null) return;

            transform.SetPositionAndRotation(
                _zone.position + Vector3.up * _surfaceOffset,
                _zone.rotation);

            if (_completed) return;
            float pulse = 1f + Mathf.Sin(Time.time * _pulseSpeed + _phase) * _pulseScale;
            if (_pulseRoot != null) _pulseRoot.localScale = Vector3.one * pulse;
            if (_ribbonRoot != null)
                _ribbonRoot.Rotate(0f, 14f * Time.deltaTime, 0f, Space.Self);
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in _runtimeMeshes)
                if (mesh != null) Destroy(mesh);
            _runtimeMeshes.Clear();
        }

        private void BuildVisual()
        {
            float diameter = Mathf.Max(0.5f, Mathf.Min(_sizeXZ.x, _sizeXZ.y));
            float outerRadius = diameter * 0.44f;
            Color lightGiftColor = Color.Lerp(_giftColor, Cream, 0.42f);
            Color matColor = Color.Lerp(PenguinInk, _giftColor, 0.12f);

            _pulseRoot = Child(transform, "GroundMarker");
            // 눈과 비슷한 밝기의 평면 VFX만 두면 비스듬한 카메라에서 문양이 사라진다.
            // 윗면은 표면에 붙이고 옆면만 아래로 내린 얇은 메시를 받쳐, 플랫폼처럼 뜨지 않으면서
            // 어느 각도에서도 장판의 실루엣이 읽히게 한다.
            CreateMeshVisual(_pulseRoot, "PenguinMatMesh",
                BuildExtrudedDisc(outerRadius * 0.86f, 0.075f),
                0f, matColor, 0.90f);
            CreateMeshVisual(_pulseRoot, "SoftGiftColorWash", BuildDisc(outerRadius * 0.83f),
                0.008f, _giftColor, 0.22f);
            CreateMeshVisual(_pulseRoot, "CreamOuterRing", BuildRing(outerRadius, outerRadius - 0.070f),
                0.016f, Cream, 1f);
            CreateMeshVisual(_pulseRoot, "GiftColorInnerRing",
                BuildRing(outerRadius - 0.13f, outerRadius - 0.17f),
                0.024f, _giftColor, 0.92f);

            _ribbonRoot = Child(_pulseRoot, "InwardRibbonFlow");
            CreateMeshVisual(_ribbonRoot, "GiftColorRibbons", BuildRibbonSwirls(outerRadius, 0f),
                0.032f, _giftColor, 0.68f);
            CreateMeshVisual(_ribbonRoot, "LightGiftRibbons", BuildRibbonSwirls(outerRadius, 60f),
                0.034f, lightGiftColor, 0.58f);

            CreateTexturedMeshVisual(_pulseRoot, "PenguinPraiseStampDecal",
                BuildTexturedDisc(outerRadius * 0.96f), 0.044f, GetPraiseStampTexture(), _giftColor);

            _idleMotes = CreateIdleMotes(_pulseRoot, outerRadius, lightGiftColor);
            _risingSteam = CreateRisingSteam(_pulseRoot, outerRadius, lightGiftColor);
            _completionBurst = CreateCompletionBurst(transform, outerRadius, lightGiftColor);
        }

        private MeshRenderer CreateMeshVisual(
            Transform parent,
            string name,
            Mesh mesh,
            float localHeight,
            Color color,
            float alpha)
        {
            Transform visual = Child(parent, name);
            visual.localPosition = Vector3.up * localHeight;
            MeshFilter filter = visual.gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = visual.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = GetMarkerMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ApplyColor(renderer, color, alpha);
            _idleRenderers.Add(renderer);
            return renderer;
        }

        private MeshRenderer CreateTexturedMeshVisual(
            Transform parent,
            string name,
            Mesh mesh,
            float localHeight,
            Texture texture,
            Color accentColor)
        {
            MeshRenderer renderer = CreateMeshVisual(parent, name, mesh, localHeight, Color.white, 1f);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, texture);
            properties.SetFloat(UseBaseMapId, 1f);
            properties.SetColor(AccentColorId, accentColor);
            properties.SetFloat(UseAccentRemapId, 1f);
            renderer.SetPropertyBlock(properties);
            renderer.sortingOrder = 6;
            return renderer;
        }

        private Mesh BuildRing(float outerRadius, float innerRadius)
        {
            int segments = Mathf.Max(24, _ringSegments);
            var vertices = new Vector3[segments * 2];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * 6];
            for (int index = 0; index < segments; index++)
            {
                float angle = Mathf.PI * 2f * index / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[index * 2] = direction * outerRadius;
                vertices[index * 2 + 1] = direction * innerRadius;
                normals[index * 2] = normals[index * 2 + 1] = Vector3.up;

                int next = (index + 1) % segments;
                int triangle = index * 6;
                triangles[triangle] = index * 2;
                triangles[triangle + 1] = next * 2 + 1;
                triangles[triangle + 2] = next * 2;
                triangles[triangle + 3] = index * 2;
                triangles[triangle + 4] = index * 2 + 1;
                triangles[triangle + 5] = next * 2 + 1;
            }

            return CreateMesh("GiftDropZoneRing", vertices, normals, triangles);
        }

        private Mesh BuildDisc(float radius)
        {
            int segments = Mathf.Max(24, _ringSegments);
            var vertices = new Vector3[segments + 1];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * 3];
            normals[0] = Vector3.up;
            for (int index = 0; index < segments; index++)
            {
                float angle = Mathf.PI * 2f * index / segments;
                vertices[index + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                normals[index + 1] = Vector3.up;
                int next = (index + 1) % segments;
                triangles[index * 3] = 0;
                triangles[index * 3 + 1] = next + 1;
                triangles[index * 3 + 2] = index + 1;
            }

            return CreateMesh("GiftDropZoneWash", vertices, normals, triangles);
        }

        private Mesh BuildTexturedDisc(float radius)
        {
            int segments = Mathf.Max(24, _ringSegments);
            var vertices = new Vector3[segments + 1];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 3];
            normals[0] = Vector3.up;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int index = 0; index < segments; index++)
            {
                float angle = Mathf.PI * 2f * index / segments;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);
                vertices[index + 1] = new Vector3(x * radius, 0f, z * radius);
                normals[index + 1] = Vector3.up;
                uv[index + 1] = new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f);
                int next = (index + 1) % segments;
                triangles[index * 3] = 0;
                triangles[index * 3 + 1] = next + 1;
                triangles[index * 3 + 2] = index + 1;
            }

            return CreateMesh("GiftDropZonePraiseStamp", vertices, normals, triangles, uv);
        }

        private Mesh BuildExtrudedDisc(float radius, float depth)
        {
            int segments = Mathf.Max(24, _ringSegments);
            int topCenter = 0;
            int topRing = 1;
            int sideTopRing = topRing + segments;
            int sideBottomRing = sideTopRing + segments;
            var vertices = new Vector3[1 + segments * 3];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * 9];

            vertices[topCenter] = Vector3.zero;
            normals[topCenter] = Vector3.up;
            for (int index = 0; index < segments; index++)
            {
                float angle = Mathf.PI * 2f * index / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[topRing + index] = direction * radius;
                normals[topRing + index] = Vector3.up;
                vertices[sideTopRing + index] = direction * radius;
                normals[sideTopRing + index] = direction;
                vertices[sideBottomRing + index] = direction * radius + Vector3.down * depth;
                normals[sideBottomRing + index] = direction;

                int next = (index + 1) % segments;
                int triangle = index * 9;
                triangles[triangle] = topCenter;
                triangles[triangle + 1] = topRing + next;
                triangles[triangle + 2] = topRing + index;
                triangles[triangle + 3] = sideTopRing + index;
                triangles[triangle + 4] = sideTopRing + next;
                triangles[triangle + 5] = sideBottomRing + next;
                triangles[triangle + 6] = sideTopRing + index;
                triangles[triangle + 7] = sideBottomRing + next;
                triangles[triangle + 8] = sideBottomRing + index;
            }

            return CreateMesh("GiftDropZonePenguinMat", vertices, normals, triangles);
        }

        private Mesh BuildRibbonSwirls(float radius, float angleOffset)
        {
            const int ribbonCount = 3;
            const int samples = 9;
            var vertices = new List<Vector3>(ribbonCount * samples * 2);
            var normals = new List<Vector3>(vertices.Capacity);
            var triangles = new List<int>(ribbonCount * (samples - 1) * 6);
            for (int ribbon = 0; ribbon < ribbonCount; ribbon++)
            {
                float angle = (angleOffset + ribbon * 120f) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 curled = new Vector3(-direction.z, 0f, direction.x);
                Vector3 start = direction * radius * 0.82f;
                Vector3 control = direction * radius * 0.48f + curled * radius * 0.34f;
                Vector3 end = direction * radius * 0.20f;
                int startIndex = vertices.Count;

                for (int sample = 0; sample < samples; sample++)
                {
                    float t = sample / (samples - 1f);
                    Vector3 point = Quadratic(start, control, end, t);
                    Vector3 tangent = QuadraticTangent(start, control, end, t).normalized;
                    Vector3 side = new Vector3(-tangent.z, 0f, tangent.x);
                    float width = Mathf.Lerp(0.085f, 0.015f, t);
                    vertices.Add(point - side * width);
                    vertices.Add(point + side * width);
                    normals.Add(Vector3.up);
                    normals.Add(Vector3.up);
                }

                for (int sample = 0; sample < samples - 1; sample++)
                {
                    int a = startIndex + sample * 2;
                    int b = a + 1;
                    int c = a + 2;
                    int d = a + 3;
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                    triangles.Add(a); triangles.Add(b); triangles.Add(d);
                }
            }

            return CreateMesh("GiftDropZoneRibbons", vertices.ToArray(), normals.ToArray(), triangles.ToArray());
        }

        private ParticleSystem CreateIdleMotes(Transform parent, float radius, Color lightGiftColor)
        {
            ParticleSystem particles = CreateParticles(parent, "SnowflakeStarMotes");
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.12f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(_giftColor, lightGiftColor);
            main.maxParticles = 28;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 6f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.86f;
            shape.radiusThickness = 0.82f;

            AddFade(particles);
            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = ZeroRange();
            velocity.y = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            velocity.z = ZeroRange();
            velocity.orbitalX = ZeroRange();
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(-0.32f, 0.32f);
            velocity.orbitalZ = ZeroRange();
            AddRotation(particles, 1.6f);
            particles.Play(true);
            return particles;
        }

        private ParticleSystem CreateRisingSteam(Transform parent, float radius, Color lightGiftColor)
        {
            ParticleSystem particles = CreateParticles(parent, "RisingGiftColorSteam");
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.28f, 0.62f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            main.startColor = new ParticleSystem.MinMaxGradient(_giftColor, lightGiftColor);
            main.maxParticles = 36;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 8f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.70f;
            shape.radiusThickness = 0.65f;

            AddFade(particles);
            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = ZeroRange();
            velocity.y = new ParticleSystem.MinMaxCurve(0.28f, 0.58f);
            velocity.z = ZeroRange();
            velocity.orbitalX = ZeroRange();
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(-0.18f, 0.18f);
            velocity.orbitalZ = ZeroRange();

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = 0.09f;
            noise.strengthY = 0.025f;
            noise.strengthZ = 0.09f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.24f;
            particles.Play(true);
            return particles;
        }

        private ParticleSystem CreateCompletionBurst(Transform parent, float radius, Color lightGiftColor)
        {
            ParticleSystem particles = CreateParticles(parent, "GiftAcceptedBurst");
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.65f, 1.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.18f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(_giftColor, lightGiftColor);
            main.maxParticles = 32;
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.18f);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius * 0.28f;
            shape.radiusThickness = 1f;

            AddFade(particles);
            AddRotation(particles, 2.4f);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static ParticleSystem CreateParticles(Transform parent, string name)
        {
            GameObject objectWithParticles = new GameObject(name, typeof(ParticleSystem));
            objectWithParticles.transform.SetParent(parent, false);
            objectWithParticles.transform.localPosition = Vector3.up * 0.08f;
            ParticleSystem particles = objectWithParticles.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.duration = 3f;

            ParticleSystemRenderer renderer = objectWithParticles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = GetParticleMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        private static void AddFade(ParticleSystem particles)
        {
            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0f, 1f)
                }
            });
        }

        private static void AddRotation(ParticleSystem particles, float speed)
        {
            ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-speed, speed);
        }

        private static void StopAndClear(ParticleSystem particles)
        {
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static ParticleSystem.MinMaxCurve ZeroRange() =>
            new ParticleSystem.MinMaxCurve(0f, 0f);

        private static Vector3 Quadratic(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * a + 2f * inverse * t * b + t * t * c;
        }

        private static Vector3 QuadraticTangent(Vector3 a, Vector3 b, Vector3 c, float t) =>
            2f * (1f - t) * (b - a) + 2f * t * (c - b);

        private Mesh CreateMesh(
            string name,
            Vector3[] vertices,
            Vector3[] normals,
            int[] triangles,
            Vector2[] uv = null)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            if (uv != null) mesh.uv = uv;
            mesh.RecalculateBounds();
            _runtimeMeshes.Add(mesh);
            return mesh;
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void ApplyColor(MeshRenderer renderer, Color color, float alpha)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, color);
            properties.SetFloat(BaseAlphaId, alpha);
            properties.SetFloat(UseBaseMapId, 0f);
            properties.SetFloat(UseAccentRemapId, 0f);
            properties.SetFloat(AmbientFloorId, 0.78f);
            properties.SetFloat(SpecularIntensityId, 0.08f);
            renderer.SetPropertyBlock(properties);
        }

        private static Material GetMarkerMaterial()
        {
            if (_sharedMarkerMaterial != null) return _sharedMarkerMaterial;
            Shader shader = Shader.Find("PPack/DeliveryBeaconUnlit");
            _sharedMarkerMaterial = new Material(shader)
            {
                name = "GiftDropZoneMarker_Runtime",
                hideFlags = HideFlags.HideAndDontSave
            };
            return _sharedMarkerMaterial;
        }

        private static Texture2D GetPraiseStampTexture()
        {
            if (_sharedPraiseStampTexture != null) return _sharedPraiseStampTexture;
            _sharedPraiseStampTexture = Resources.Load<Texture2D>("Textures/PenguinPraiseStamp");
            if (_sharedPraiseStampTexture == null)
                Debug.LogError("PenguinPraiseStamp 리소스를 찾을 수 없습니다.");
            return _sharedPraiseStampTexture;
        }

        private static Material GetParticleMaterial()
        {
            if (_sharedParticleMaterial != null) return _sharedParticleMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                            Shader.Find("Particles/Standard Unlit");
            _sharedParticleMaterial = new Material(shader)
            {
                name = "GiftDropZoneStars_Runtime",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.Transparent
            };
            Texture2D texture = GetStarTexture();
            if (_sharedParticleMaterial.HasProperty("_BaseMap"))
                _sharedParticleMaterial.SetTexture("_BaseMap", texture);
            if (_sharedParticleMaterial.HasProperty("_MainTex"))
                _sharedParticleMaterial.SetTexture("_MainTex", texture);
            if (_sharedParticleMaterial.HasProperty("_Surface")) _sharedParticleMaterial.SetFloat("_Surface", 1f);
            if (_sharedParticleMaterial.HasProperty("_ZWrite")) _sharedParticleMaterial.SetFloat("_ZWrite", 0f);
            if (_sharedParticleMaterial.HasProperty("_SrcBlend"))
                _sharedParticleMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (_sharedParticleMaterial.HasProperty("_DstBlend"))
                _sharedParticleMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            _sharedParticleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return _sharedParticleMaterial;
        }

        private static Texture2D GetStarTexture()
        {
            if (_sharedStarTexture != null) return _sharedStarTexture;
            const int size = 32;
            _sharedStarTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GiftDropZoneStar_Runtime",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                float py = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
                float diamond = Mathf.Clamp01(1f - (px + py) * 1.15f);
                float vertical = Mathf.Clamp01(1f - px * 9f) * Mathf.Clamp01(1f - py * 0.95f);
                float horizontal = Mathf.Clamp01(1f - py * 9f) * Mathf.Clamp01(1f - px * 0.95f);
                float alpha = Mathf.Pow(Mathf.Max(diamond, Mathf.Max(vertical, horizontal)), 1.7f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
            _sharedStarTexture.SetPixels32(pixels);
            _sharedStarTexture.Apply(false, true);
            return _sharedStarTexture;
        }
    }
}

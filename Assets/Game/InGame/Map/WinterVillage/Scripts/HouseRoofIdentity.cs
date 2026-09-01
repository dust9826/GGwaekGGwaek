using UnityEngine;

namespace PPack
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HouseRoofIdentity : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private string _houseId = "House";
        [SerializeField] private bool _useRoofColor;
        [SerializeField] private Color _roofColor = Color.white;
        [SerializeField] private bool _isQuestSender;
        [SerializeField] private Color _questHighlightColor = new(1f, 0.78f, 0.18f, 1f);
        [SerializeField, Range(0f, 1f)] private float _questHighlightBlend = 0.38f;
        [SerializeField] private Renderer[] _roofRenderers;
        [SerializeField] private int[] _roofMaterialIndices;
        [SerializeField] private Bounds _roofLocalBounds;
        [SerializeField] private bool _hasRoofLocalBounds;

        private MaterialPropertyBlock _propertyBlock;

        public string HouseId => _houseId;
        public Color RoofColor => _roofColor;
        public bool IsQuestSender => _isQuestSender;
        public Color DisplayColor => _isQuestSender ? ResolveQuestDisplayColor() : _roofColor;

        /// <summary>
        /// 등록된 지붕 렌더러 전체를 감싸는 월드 바운드의 상단 중앙을 반환한다.
        /// 렌더러가 현재 꺼져 있어도 bounds는 유효하므로 기본색 집에도 HELP를 배치할 수 있다.
        /// </summary>
        public bool TryGetRoofTopCenter(out Vector3 worldPosition)
        {
            if (_hasRoofLocalBounds && _roofRenderers != null && _roofRenderers.Length > 0 &&
                _roofRenderers[0] != null)
            {
                Bounds worldBounds = TransformBounds(
                    _roofRenderers[0].transform.localToWorldMatrix,
                    _roofLocalBounds);
                worldPosition = new Vector3(
                    worldBounds.center.x,
                    worldBounds.max.y,
                    worldBounds.center.z);
                return true;
            }

            Bounds roofBounds = default;
            bool hasBounds = false;

            if (_roofRenderers != null)
            {
                for (int index = 0; index < _roofRenderers.Length; index++)
                {
                    Renderer roofRenderer = _roofRenderers[index];
                    if (roofRenderer == null) continue;

                    if (!hasBounds)
                    {
                        roofBounds = roofRenderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        roofBounds.Encapsulate(roofRenderer.bounds);
                    }
                }
            }

            if (!hasBounds)
            {
                worldPosition = transform.position;
                return false;
            }

            worldPosition = new Vector3(roofBounds.center.x, roofBounds.max.y, roofBounds.center.z);
            return true;
        }

        /// <summary>
        /// 지붕 메시에서 가장 높은 작은 꼭짓점 군집을 찾아 굴뚝 연기 방출 위치를 반환한다.
        /// 현재 WinterVillage 집에서는 기존 ChimneySmoke 배치점과 약 0.02m 이내로 일치한다.
        /// </summary>
        public bool TryGetChimneyTopCenter(out Vector3 worldPosition)
        {
            const float topClusterDepth = 0.12f;
            const float smokeClearance = 0.09f;

            MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
            var localVertices = new System.Collections.Generic.List<Vector3>();
            float highest = float.NegativeInfinity;

            for (int filterIndex = 0; filterIndex < meshFilters.Length; filterIndex++)
            {
                MeshFilter meshFilter = meshFilters[filterIndex];
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;

                Vector3[] vertices = mesh.vertices;
                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    Vector3 worldVertex = meshFilter.transform.TransformPoint(vertices[vertexIndex]);
                    Vector3 localVertex = transform.InverseTransformPoint(worldVertex);
                    localVertices.Add(localVertex);
                    if (localVertex.y > highest) highest = localVertex.y;
                }
            }

            if (localVertices.Count == 0)
            {
                worldPosition = transform.position;
                return false;
            }

            Vector3 clusterCenter = Vector3.zero;
            int clusterCount = 0;
            for (int index = 0; index < localVertices.Count; index++)
            {
                Vector3 vertex = localVertices[index];
                if (vertex.y < highest - topClusterDepth) continue;
                clusterCenter += vertex;
                clusterCount++;
            }

            if (clusterCount == 0)
            {
                worldPosition = transform.position;
                return false;
            }

            clusterCenter /= clusterCount;
            clusterCenter.y = highest + smokeClearance;
            worldPosition = transform.TransformPoint(clusterCenter);
            return true;
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds localBounds)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            Vector3 worldExtents = new(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, worldExtents * 2f);
        }

        private void OnEnable()
        {
            ApplyRoofAppearance();
        }

        private void OnValidate()
        {
            ApplyRoofAppearance();
        }

        private void OnDisable()
        {
            ClearRendererOverrides();
        }

        public void SetRoofColor(Color color)
        {
            _roofColor = color;
            _roofColor.a = 1f;
            _useRoofColor = true;
            ApplyRoofAppearance();
        }

        public void ClearRoofColor()
        {
            _useRoofColor = false;
            _isQuestSender = false;
            ApplyRoofAppearance();
        }

        public void SetQuestSender(bool active)
        {
            _isQuestSender = active;
            ApplyRoofAppearance();
        }

        public void SetQuestSender(bool active, Color highlightColor)
        {
            _questHighlightColor = highlightColor;
            _questHighlightColor.a = 1f;
            SetQuestSender(active);
        }

        public void ConfigureRoofMaterials(
            Renderer[] renderers,
            int[] materialIndices,
            Bounds roofLocalBounds)
        {
            _roofRenderers = renderers;
            _roofMaterialIndices = materialIndices;
            _roofLocalBounds = roofLocalBounds;
            _hasRoofLocalBounds = true;
            ApplyRoofAppearance();
        }

        public void ConfigureIdentity(string houseId, Color roofColor, bool useRoofColor)
        {
            _houseId = string.IsNullOrWhiteSpace(houseId) ? name : houseId;
            _roofColor = roofColor;
            _roofColor.a = 1f;
            _useRoofColor = useRoofColor;
            ApplyRoofAppearance();
        }

        public void ApplyRoofAppearance()
        {
            if (_roofRenderers == null || _roofRenderers.Length == 0) return;

            _propertyBlock ??= new MaterialPropertyBlock();
            bool useColor = _useRoofColor || _isQuestSender;
            Color displayColor = DisplayColor;
            displayColor.a = 1f;

            for (int i = 0; i < _roofRenderers.Length; i++)
            {
                Renderer roofRenderer = _roofRenderers[i];
                if (roofRenderer == null) continue;

                if (_roofMaterialIndices == null || _roofMaterialIndices.Length == 0)
                {
                    // 이전 오버레이 프리팹은 새 셋업 메뉴가 실행되기 전에도 화면에 나타나지 않게 한다.
                    if (roofRenderer.name == "QuestRoofOverlay") roofRenderer.enabled = false;
                    continue;
                }

                for (int material = 0; material < _roofMaterialIndices.Length; material++)
                {
                    int materialIndex = _roofMaterialIndices[material];
                    if (materialIndex < 0 || materialIndex >= roofRenderer.sharedMaterials.Length) continue;

                    if (!useColor)
                    {
                        roofRenderer.SetPropertyBlock(null, materialIndex);
                        continue;
                    }

                    _propertyBlock.Clear();
                    roofRenderer.GetPropertyBlock(_propertyBlock, materialIndex);
                    _propertyBlock.SetColor(BaseColorId, displayColor);
                    _propertyBlock.SetColor(ColorId, displayColor);
                    // 원래 텍스처와 씬 조명만 남기고, 퀘스트 색 자체는 발광시키지 않는다.
                    _propertyBlock.SetColor(EmissionColorId, Color.black);
                    roofRenderer.SetPropertyBlock(_propertyBlock, materialIndex);
                }
            }
        }

        private Color ResolveQuestDisplayColor()
        {
            // Keep enough of the original roof albedo to retain its texture and lighting.
            float questTint = Mathf.Lerp(0.32f, 0.58f, _questHighlightBlend);
            Color displayColor = Color.Lerp(Color.white, _questHighlightColor, questTint);
            displayColor.a = 1f;
            return displayColor;
        }

        private void ClearRendererOverrides()
        {
            if (_roofRenderers == null) return;
            for (int i = 0; i < _roofRenderers.Length; i++)
            {
                Renderer roofRenderer = _roofRenderers[i];
                if (roofRenderer == null) continue;

                if (_roofMaterialIndices == null || _roofMaterialIndices.Length == 0)
                {
                    if (roofRenderer.name == "QuestRoofOverlay")
                    {
                        roofRenderer.SetPropertyBlock(null);
                        roofRenderer.enabled = false;
                    }
                    continue;
                }

                for (int material = 0; material < _roofMaterialIndices.Length; material++)
                {
                    int materialIndex = _roofMaterialIndices[material];
                    if (materialIndex < 0 || materialIndex >= roofRenderer.sharedMaterials.Length) continue;
                    roofRenderer.SetPropertyBlock(null, materialIndex);
                }
            }
        }
    }
}

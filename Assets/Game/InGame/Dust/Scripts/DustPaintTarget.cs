using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 이 렌더러의 오염 마스크를 런타임에 소유한다. 마스크는 RenderTexture 로 바뀌고,
    /// 붓질은 메시를 UV 공간에 렌더해서 지운다.
    ///
    /// 마스크 규약(<c>Dust/AGENTS.md</c>): UV0 · R 1채널 · 1 = 더러움 · 지우기 전용.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public sealed class DustPaintTarget : MonoBehaviour
    {
        [SerializeField] private int _resolution = 1024;
        [Tooltip("비워두면 머티리얼의 _DirtMask 로 시작한다. 지정하면 이 텍스처로 시작한다.\n" +
                 "시작 오염 패턴은 머티리얼이 아니라 표면 하나하나의 성질이다 — 같은 바닥 재질에 " +
                 "깨끗한 자리와 더러운 자리가 섞이는 것이 정상이고, 패턴마다 머티리얼을 늘리면 " +
                 "머티리얼 수가 패턴 수만큼 불어난다.")]
        [SerializeField] private Texture _startMask;
        [SerializeField] private Shader _paintShader;
        [SerializeField] private Shader _erasedShader;

        private static readonly int DirtMaskId = Shader.PropertyToID("_DirtMask");
        private static readonly int BrushWorldToPadId = Shader.PropertyToID("_BrushWorldToPad");
        private static readonly int BrushHalfExtentsId = Shader.PropertyToID("_BrushHalfExtents");
        private static readonly int BrushThicknessId = Shader.PropertyToID("_BrushThickness");
        private static readonly int BrushFeatherId = Shader.PropertyToID("_BrushFeather");
        private static readonly int BrushStrengthId = Shader.PropertyToID("_BrushStrength");
        private static readonly int BrushNoiseAmountId = Shader.PropertyToID("_BrushNoiseAmount");
        private static readonly int BrushNoiseScaleId = Shader.PropertyToID("_BrushNoiseScale");

        private RenderTexture _mask;
        private Material _paintMaterial;
        private Material _erasedMaterial;
        private Material _surfaceMaterial;
        private Texture _sourceMask;
        private Mesh _mesh;
        private CommandBuffer _command;

        private void Awake()
        {
            _mesh = GetComponent<MeshFilter>().sharedMesh;

            // .material 은 인스턴스를 만든다 — 공유 에셋을 런타임에 더럽히지 않기 위해 일부러 이걸 쓴다.
            _surfaceMaterial = GetComponent<MeshRenderer>().material;
            _sourceMask = _startMask != null ? _startMask : _surfaceMaterial.GetTexture(DirtMaskId);

            if (_paintShader == null) _paintShader = Shader.Find("PPack/DustPaint");
            _paintMaterial = new Material(_paintShader) { hideFlags = HideFlags.HideAndDontSave };

            if (_erasedShader == null) _erasedShader = Shader.Find("PPack/DustErased");
            _erasedMaterial = new Material(_erasedShader) { hideFlags = HideFlags.HideAndDontSave };

            _mask = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                name = name + "_DirtMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _mask.Create();

            _command = new CommandBuffer { name = "DustPaint" };

            ResetMask();
            _surfaceMaterial.SetTexture(DirtMaskId, _mask);
        }

        /// <summary>마스크를 시작 상태로 되돌린다. 원본 텍스처가 없으면 전면 더러움으로 채운다.</summary>
        public void ResetMask()
        {
            if (_sourceMask != null) Graphics.Blit(_sourceMask, _mask);
            else
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _mask;
                GL.Clear(false, true, Color.white);
                RenderTexture.active = previous;
            }
        }

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

        /// <summary>
        /// 이 붓질로 <b>실제로</b> 지워질 양과 그 월드 좌표를 <paramref name="target"/> 에 기록한다.
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

        /// <summary>붓 파라미터를 머티리얼에 싣는다. 지우는 패스와 기록하는 패스가 같은 값을 써야 한다.</summary>
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

        private void OnDestroy()
        {
            if (_mask != null) { _mask.Release(); Destroy(_mask); }
            if (_paintMaterial != null) Destroy(_paintMaterial);
            if (_erasedMaterial != null) Destroy(_erasedMaterial);
            _command?.Dispose();
        }
    }
}

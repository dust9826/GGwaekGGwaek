using UnityEngine;
using UnityEngine.VFX;

namespace PPack
{
    /// <summary>
    /// 청소 VFX. 이번 프레임에 실제로 지워진 양과 그 월드 좌표를 담은 렌더 텍스처를 소유하고,
    /// VFX Graph 가 그것을 포지션 맵으로 읽어 스스로 파티클을 뿌린다. <b>CPU 는 개입하지 않는다.</b>
    ///
    /// 텍스처는 이펙트 전체에 하나뿐인 프로퍼티지만 그 안에 텍셀이 수천 개 있다. 파티클이 각자
    /// 다른 텍셀을 읽으면 균일한 입력 하나로 파티클별 위치가 나온다 — 지연도 Readback 도 없이
    /// 붓의 너덜너덜한 모양을 그대로 따라가는 이유다.
    ///
    /// 렌더 텍스처는 <b>표면당이 아니라 도구당</b> 하나다. 패드 공간에 굽기 때문에 패드 아래
    /// 표면이 여럿이어도 모두 같은 것에 그려 넣는다.
    ///
    /// 붓질을 미는 쪽(지금은 <see cref="DustMousePainter"/>, 나중에 <c>InGame/Vacuum</c>)이
    /// 소유한다. 그래서 Fusion 이 오면 원격 플레이어의 도구도 자기 것을 갖는다.
    /// </summary>
    public sealed class DustCleanVfx : MonoBehaviour
    {
        [SerializeField] private VisualEffect _puff;
        [SerializeField] private VisualEffect _sparkle;

        [Tooltip("지운양 맵의 해상도. 퍼프가 성기게 보이면 올린다.")]
        [SerializeField, Min(8)] private int _resolution = 64;

        [Tooltip("이 값보다 적게 지워진 텍셀에서는 파티클이 뜨지 않는다.")]
        [SerializeField, Range(0f, 1f)] private float _threshold = 0.02f;

        [Tooltip("흡입구가 표면에서 떠 있는 높이. 0 이면 빨려드는 방향이 바닥 속을 향한다.")]
        [SerializeField, Min(0f)] private float _nozzleHeight = 0.3f;

        // VFX Graph 의 노출 프로퍼티 이름. **셰이더 프로퍼티가 아니므로 밑줄을 붙이지 않는다** —
        // 그래프 블랙보드에 적은 이름과 글자 그대로 같아야 하고, 다르면 조용히 안 붙는다.
        private const string ErasedMapName = "ErasedMap";
        private const string ErasedThresholdName = "ErasedThreshold";
        private const string PadCenterName = "PadCenter";
        private const string TravelName = "Travel";

        private static readonly int ErasedMapId = Shader.PropertyToID(ErasedMapName);
        private static readonly int ErasedThresholdId = Shader.PropertyToID(ErasedThresholdName);
        private static readonly int PadCenterId = Shader.PropertyToID(PadCenterName);
        private static readonly int TravelId = Shader.PropertyToID(TravelName);

        private RenderTexture _erasedMap;

        /// <summary>이번 프레임에 지워진 것. rgb = 월드 좌표, a = 지운 양.</summary>
        public RenderTexture ErasedMap => _erasedMap;

        private void Awake()
        {
            _erasedMap = new RenderTexture(_resolution, _resolution, 0,
                                           RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = name + "_ErasedMap",
                wrapMode = TextureWrapMode.Clamp,
                // 텍셀 사이를 섞으면 지워지지 않은 자리의 좌표가 딸려와 파티클이 엉뚱한 데 뜬다.
                filterMode = FilterMode.Point,
            };
            _erasedMap.Create();

            Bind(_puff);
            Bind(_sparkle);
        }

        /// <summary>
        /// 그래프가 기대하는 프로퍼티를 갖고 있는지 <b>시작할 때 한 번</b> 확인한다.
        ///
        /// 이름이 어긋나면 <c>SetVector3</c> 는 아무 말 없이 무시되고, 증상은 "파티클이 안
        /// 죽는다"거나 "바닥 아래로 빨려든다" 같은 엉뚱한 모습으로 나타난다. 실제로 그렇게
        /// 한참 헤맸으므로 조용히 실패하게 두지 않는다.
        ///
        /// 다만 <b>모든 그래프가 넷을 다 쓰지는 않는다.</b> 퍼프는 진행 방향이 필요 없고
        /// 반짝은 흡입구도 필요 없다. 그래서 맵과 임계값만 필수로 보고, 나머지 둘은 없으면
        /// 경고만 남긴 뒤 보내지 않는다 — 오타와 "안 쓴다"를 구별할 수 있게.
        /// </summary>
        private void Bind(VisualEffect effect)
        {
            if (effect == null) return;

            var missing = new System.Collections.Generic.List<string>();
            if (!effect.HasTexture(ErasedMapId)) missing.Add(ErasedMapName + " (Texture2D)");
            if (!effect.HasFloat(ErasedThresholdId)) missing.Add(ErasedThresholdName + " (float)");

            if (missing.Count > 0)
            {
                Debug.LogError(
                    $"{effect.name}: 그래프에 없는 필수 노출 프로퍼티 — {string.Join(", ", missing)}. " +
                    "블랙보드의 이름이 글자 그대로 같아야 한다 (밑줄 없음).", effect);
            }
            else
            {
                effect.SetTexture(ErasedMapId, _erasedMap);
                effect.SetFloat(ErasedThresholdId, _threshold);
            }

            var unused = new System.Collections.Generic.List<string>();
            if (!effect.HasVector3(PadCenterId)) unused.Add(PadCenterName);
            if (!effect.HasVector3(TravelId)) unused.Add(TravelName);

            if (unused.Count > 0)
            {
                Debug.LogWarning(
                    $"{effect.name}: 안 쓰는 선택 프로퍼티 — {string.Join(", ", unused)}. " +
                    "의도한 것이면 무시해도 된다. 쓸 생각이었다면 블랙보드 이름을 확인할 것.", effect);
            }
        }

        /// <summary>
        /// 이번 프레임의 기록을 비운다. 붓질을 그리기 전에 부른다.
        ///
        /// <b>붓질하는 프레임에만이 아니라 매 프레임 불러야 한다.</b> 안 부르면 맵이 지난 프레임의
        /// 내용을 그대로 들고 있고, 그래프는 그것을 계속 읽어 파티클을 영원히 뿌린다. 도구가 멈춰
        /// 조기 return 하는 순간 그렇게 되므로, 호출은 <b>모든 조기 return 보다 위에</b> 있어야 한다.
        /// </summary>
        public void BeginFrame()
        {
            if (_erasedMap == null) return;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _erasedMap;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        /// <summary>
        /// 흡입구의 위치와 진행 방향을 그래프에 넘긴다. 파티클 <b>위치</b>는 맵이 주고, 이 둘은
        /// 파티클이 <b>어디로 갈지</b>를 정한다.
        ///
        /// 흡입구가 필요한 이유: 흡입은 파티클이 한 점으로 수렴해야 읽힌다. 진행 방향만으로는
        /// 흩날리는 먼지와 구별되지 않는다.
        ///
        /// <b>표면 위로 띄운 점을 넘긴다.</b> 패드 중심을 그대로 넘기면 그것이 표면 위의 점이라
        /// 빨려드는 방향이 바닥 속을 향하고, 파티클이 불투명한 바닥 아래로 사라진다. 패드의 up
        /// 축을 따라 띄우므로 벽에서도 옳다.
        /// </summary>
        public void Play(in BrushPad pad, Vector3 travelDirection)
        {
            Matrix4x4 padToWorld = pad.WorldToPad.inverse;
            Vector3 center = padToWorld.GetColumn(3);
            Vector3 up = ((Vector3)padToWorld.GetColumn(1)).normalized;
            Vector3 nozzle = center + up * _nozzleHeight;

            Send(_puff, nozzle, travelDirection);
            Send(_sparkle, nozzle, travelDirection);
        }


        private static void Send(VisualEffect effect, Vector3 nozzle, Vector3 travelDirection)
        {
            if (effect == null) return;
            // 없는 프로퍼티는 Bind 가 이미 경고했다. 여기서는 조용히 건너뛴다.
            if (effect.HasVector3(PadCenterId)) effect.SetVector3(PadCenterId, nozzle);
            if (effect.HasVector3(TravelId)) effect.SetVector3(TravelId, travelDirection);
        }

        private void OnDestroy()
        {
            if (_erasedMap != null) { _erasedMap.Release(); Destroy(_erasedMap); }
        }
    }
}

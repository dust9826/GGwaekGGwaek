using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// <see cref="SnowCpuStage"/> 의 격자를 화면에 그린다 — AnyTest 에서 가져온 레이마칭 경로다.
    ///
    /// <para>순서가 설계다: 로브를 먼저 굽고(<see cref="SnowSurfaceBakeCpu"/>), 그 로브의 들림을 담는
    /// 상한을 굽고(<see cref="SnowCoarseMaxCpu"/>), 마처가 그 둘을 읽는다. 상한이 로브를 읽으므로
    /// 로브가 먼저다.</para>
    ///
    /// <para>매 프레임 <b>바뀐 청크만</b> 다시 굽는다. 전부 굽는 것은 84만 셀 전수 스캔이라 dirty 청크
    /// 설계를 통째로 무효화한다 — 스파이크에서 그 실수가 34 ms 였다.</para>
    ///
    /// <para>그래픽 장치가 없으면 스스로 꺼진다. 데디케이티드 서버는 이 컴포넌트를 돌릴 수 없고, 돌릴
    /// 필요도 없다 — 권위는 <see cref="SnowCpuStage"/> 의 CPU 격자에 있고 이것은 표시일 뿐이다.</para>
    /// </summary>
    [RequireComponent(typeof(SnowCpuStage))]
    public sealed class SnowCpuStageView : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float _lumpRadiusM = 0.30f;

        [SerializeField, Min(0.05f)] private float _lumpSpacingM = 0.22f;

        [SerializeField, Range(0f, 1f)] private float _lumpAmount = 1f;

        [SerializeField, Range(0f, 1f)] private float _filletAmount = 1f;

        // 룩(팔레트)은 SnowSystem 이 소유한다 - 렌더 경로가 둘이고 둘 다 같은 값을 읽어야 한다.

        /// <summary>해가 어디서 오는가. 마처는 자기 상수 ambient 로도 밝지만 기복은 방향광이 만든다.</summary>
        [SerializeField] private Light _sun;

        private SnowCpuStage _stage;
        private SnowSurfaceBakeCpu _lump;
        private SnowCoarseMaxCpu _coarse;
        private SnowRaymarchRendererCpu _march;
        private readonly List<int> _dirty = new List<int>(256);

        /// <summary>지금 그리는 대상 격자. 세션이 바뀌면 이것이 달라지므로 다시 짓는 신호가 된다.</summary>
        private SnowHeightFieldCpu _builtFor;

        private void Awake()
        {
            _stage = GetComponent<SnowCpuStage>();

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) enabled = false;
        }

        private void LateUpdate()
        {
            SnowHeightFieldCpu field = _stage.Field;
            if (field == null) return;

            if (_march == null)
            {
                Build(field);
                return;
            }

            // 필드가 새로 만들어졌으면(세션 교체) 다시 짓는다 - 옛 격자를 읽으면 화면이 굳는다.
            if (!ReferenceEquals(_builtFor, field))
            {
                Dispose();
                Build(field);
                return;
            }

            _dirty.Clear();
            _dirty.AddRange(field.ChangedChunks);
            if (_dirty.Count > 0)
            {
                _lump.RebuildChunks(_dirty);
                _coarse.RebuildChunks(_dirty);
            }

            if (_sun != null) _march.SetSun(-_sun.transform.forward);
            _march.UploadAll();
        }

        private void Build(SnowHeightFieldCpu field)
        {
            _builtFor = field;

            _lump = new SnowSurfaceBakeCpu(field)
            {
                RadiusM = _lumpRadiusM,
                SpacingM = _lumpSpacingM,
            };
            _lump.RebuildAll();

            _coarse = new SnowCoarseMaxCpu(field, _lump);
            _march = new SnowRaymarchRendererCpu(field, _coarse, _lump, transform)
            {
                LumpAmount = _lumpAmount,
                FilletAmount = _filletAmount,
            };

            // 아무 라이트나 집으면 안 된다 — 근거는 SnowSunLight 주석.
            if (_sun == null) _sun = SnowSunLight.Resolve();
            if (_sun != null) _march.SetSun(-_sun.transform.forward);

            _march.UploadAll();
        }

        private void OnDisable() => Dispose();

        private void Dispose()
        {
            _march?.Dispose();
            _march = null;
            _coarse = null;
            _lump = null;
            _builtFor = null;
        }
    }
}

// CARRIED OVER FROM v6 UNCHANGED IN SUBSTANCE. Only identifiers were renamed V6 -> V7 (and the
// _Cs6 shader-global prefix -> _Cs7). Prose below that says "v6", "v5", "v4" or "v3" is the
// LINEAGE talking, not a claim about v7: v7 is v6 with the ball replaced by an accumulating pile
// written into the height field, and this file is one of the parts that did not have to change.
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// THE STEPS-PER-PIXEL INSTRUMENT for the raymarcher, and the deliverable for v6's field-size risk.
    ///
    /// THE RISK IT MEASURES. Storage and the simulation both scale comfortably to 120 x 110 m. The one
    /// thing that genuinely might break is the MARCH: the proxy box goes from 8 x 8 m to 120 x 110 m,
    /// so a grazing ray traverses fifteen times the chord while the screen pixel count is unchanged.
    /// Steps per pixel is not conserved, and a flat dilated coarse-max skip advances a fixed radius per
    /// step, so crossing empty space costs time proportional to distance rather than log of it.
    ///
    /// HOW IT MEASURES, AND WHY THIS IS THE SECOND DESIGN. The first version drew an extra pass of the
    /// raymarch shader into an offscreen target with a CommandBuffer. It compiled, it ran, and it
    /// produced nothing at all - `probe=waiting` for an entire course - because the target came back
    /// empty every frame. Rather than keep guessing which of SetRenderTarget / SetViewProjectionMatrices
    /// / DrawRenderer / ExecuteCommandBuffer silently did nothing, the march moved into
    /// <c>SnowMarchCoreV7.hlsl</c> and the probe became a plain compute dispatch, which is the machinery
    /// the twenty simulation kernels already use without trouble.
    ///
    /// It is still not a reimplementation: the core header is included by the fragment shader and by the
    /// probe kernel, and the uniforms are forwarded from the cached values the renderer pushed to its
    /// material in the same frame. Same code, same inputs.
    ///
    /// AND IT NOW SAYS WHY IT HAS NOTHING. <see cref="StatusText"/> distinguishes "no dispatch",
    /// "dispatched but every ray missed the box", "read-back errored" and "waiting for the first
    /// read-back". The first design could not tell any of those apart, which is most of the reason it
    /// went a whole session unnoticed.
    ///
    /// WHAT THE NUMBER IS NOT: the frame's total march cost. The DepthOnly pass marches the volume a
    /// second time whenever URP depth priming or the depth texture is enabled, and ShadowCaster a third
    /// time when the shadow march is on. Multiply accordingly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowStepsProbeV7 : MonoBehaviour
    {
        private const int kBuckets = 512;
        private const int kSlotMarched   = kBuckets + 0;
        private const int kSlotExhausted = kBuckets + 1;
        private const int kSlotSum       = kBuckets + 2;
        private const int kSlotMax       = kBuckets + 3;
        private const int kSlotTested    = kBuckets + 4;
        private const int kSlotOffBox    = kBuckets + 5;
        private const int kSlots         = kBuckets + 6;

        [Header("Steps-per-pixel probe")]
        [Tooltip("Run the probe at all. Off costs nothing and the console line prints 'off'.")]
        [SerializeField] private bool _enabled = true;

        [Tooltip("Probe height in rays; the width follows the camera's aspect. 216 over a 16:9 view is " +
                 "384 x 216 = 83k rays, a 4% sample of a 1080p frame and enough for a stable p95. The " +
                 "probe is a REAL march, so this is real cost - it is amortised by Probe Interval below " +
                 "rather than by being cheap.")]
        [Range(32, 720)]
        [SerializeField] private int _probeHeight = 216;

        [Tooltip("Frames between probes. 6 puts the instrument at roughly a sixth of one low-resolution " +
                 "march per frame. Set it to 1 while hunting a specific camera angle.")]
        [Range(1, 120)]
        [SerializeField] private int _probeInterval = 6;

        [Tooltip("Draw the FORWARD pass as a march-cost heat map instead of as snow: blue cheap, red at " +
                 "the step budget. This is the picture that says WHERE the steps go, which a mean and a " +
                 "p95 cannot. It does not affect the probe's numbers - the probe is its own dispatch.")]
        [SerializeField] private bool _heatView = false;

        private static readonly int kHisto      = Shader.PropertyToID("_Histo");
        private static readonly int kCamPos     = Shader.PropertyToID("_ProbeCamPos");
        private static readonly int kCamFwd     = Shader.PropertyToID("_ProbeCamFwd");
        private static readonly int kCamRight   = Shader.PropertyToID("_ProbeCamRight");
        private static readonly int kCamUp      = Shader.PropertyToID("_ProbeCamUp");
        private static readonly int kProbeNear  = Shader.PropertyToID("_ProbeNear");
        private static readonly int kProbeW     = Shader.PropertyToID("_ProbeW");
        private static readonly int kProbeH     = Shader.PropertyToID("_ProbeH");
        private static readonly int kStepsHeatMode = Shader.PropertyToID("_StepsHeatModeV7");

        private ComputeShader _cs;
        private int _kClear = -1, _kMarch = -1;

        private GraphicsBuffer _histo;
        private int[] _histoCpu;
        private bool _requested;
        private bool _ready;
        private string _initError;

        private Camera _camera;
        private SnowRaymarchRendererV7 _raymarch;
        private Renderer _volumeRenderer;

        private int _frame;
        private int _probeW, _probeH;

        // ---- lifetime counters, so a dead probe is diagnosable from the console line ----
        private int _dispatches;
        private int _readbacks;
        private int _readbackErrors;

        // ---- last completed measurement ----
        private float _meanSteps;
        private int _p95Steps;
        private int _maxSteps;
        private float _exhaustedPct;
        private int _marchedRays;
        private int _testedRays;
        private int _offBoxRays;
        private bool _valid;

        public bool Enabled => _enabled;
        public bool HeatView => _heatView;
        public bool HasMeasurement => _valid;

        public float MeanSteps => _meanSteps;
        public int P95Steps => _p95Steps;
        public int MaxSteps => _maxSteps;
        public float ExhaustedPercent => _exhaustedPct;
        public int MarchedRays => _marchedRays;
        public int ProbeWidth => _probeW;
        public int ProbeHeight => _probeH;
        public int ProbeInterval => _probeInterval;

        /// <summary>
        /// Why there is no measurement, in one token for the console line. A probe that can only say
        /// "waiting" is a probe that can die unnoticed, which is exactly what happened to the first one.
        /// </summary>
        public string StatusText
        {
            get
            {
                if (!_enabled) return "off";
                if (!_ready) return "INIT-FAILED:" + (_initError ?? "?");
                if (_valid) return "ok";
                if (_dispatches == 0) return "no-dispatch";
                if (_readbackErrors > 0) return "readback-error";
                if (_readbacks == 0) return "waiting-readback";
                if (_testedRays == 0) return "kernel-did-not-run";
                if (_marchedRays == 0) return "all-rays-missed-box";
                return "waiting";
            }
        }

        /// <summary>Dispatches issued / read-backs completed / read-back errors, for the console line.</summary>
        public int Dispatches => _dispatches;
        public int Readbacks => _readbacks;
        public int ReadbackErrors => _readbackErrors;
        public int TestedRays => _testedRays;
        public int OffBoxRays => _offBoxRays;

        /// <summary>
        /// Forwards the probe knobs from the bootstrap, because this component is created at runtime and
        /// therefore has no inspector while the editor is stopped. -1 leaves a value alone; enable and
        /// heat take -1 for "leave alone", 0 for off and 1 for on, because 0 is meaningful.
        /// </summary>
        public void ApplyOverrides(int enabled, int probeHeight, int probeInterval, int heatView)
        {
            if (enabled >= 0) _enabled = enabled != 0;
            if (probeHeight >= 32) _probeHeight = Mathf.Clamp(probeHeight, 32, 720);
            if (probeInterval >= 1) _probeInterval = Mathf.Clamp(probeInterval, 1, 120);
            if (heatView >= 0) _heatView = heatView != 0;
        }

        public void Initialize(Camera camera, SnowRaymarchRendererV7 raymarch)
        {
            _camera = camera;
            _raymarch = raymarch;
            _volumeRenderer = (raymarch != null) ? raymarch.VolumeRenderer : null;

            if (_camera == null || _raymarch == null || _volumeRenderer == null)
            {
                _initError = "no-camera-or-renderer";
                Debug.LogError("[SnowSpike.PileV7] Steps probe: " + _initError);
                return;
            }

            _cs = Resources.Load<ComputeShader>("SnowStepsProbeV7");
            if (_cs == null)
            {
                _initError = "compute-not-found";
                Debug.LogError("[SnowSpike.PileV7] Resources/SnowStepsProbeV7.compute not found; the " +
                               "steps probe is disabled.");
                return;
            }

            // FindKernel throws when the kernel is absent, which on a shader that failed to compile is
            // the normal case. Caught rather than allowed to abort Awake, because a broken instrument
            // must not take the whole variant down with it - but it MUST say so, which is what
            // StatusText is for.
            try
            {
                _kClear = _cs.FindKernel("ClearHisto");
                _kMarch = _cs.FindKernel("ProbeMarch");
            }
            catch (System.Exception e)
            {
                _initError = "kernel-missing";
                Debug.LogError("[SnowSpike.PileV7] Steps probe kernels not found (did " +
                               "SnowStepsProbeV7.compute fail to compile?): " + e.Message);
                return;
            }

            _histo = new GraphicsBuffer(GraphicsBuffer.Target.Structured, kSlots, sizeof(int));
            _histoCpu = new int[kSlots];

            _ready = true;
        }

        private void OnDestroy()
        {
            _histo?.Dispose();
            _histo = null;
        }

        /// <summary>
        /// Pushes the heat-view uniform. Separate from <see cref="Tick"/> because the heat view is a
        /// property of the FORWARD pass, which draws whether or not the probe runs this frame.
        /// </summary>
        public void PushHeatMode()
        {
            Shader.SetGlobalFloat(kStepsHeatMode, _heatView ? 1f : 0f);
        }

        /// <summary>
        /// Runs the probe if this is a probe frame. Call AFTER the camera has been positioned and AFTER
        /// the raymarcher has pushed its uniforms for this frame: the probe uses the camera's current
        /// pose and the renderer's cached uniforms, and a probe taken with last frame's camera measures
        /// last frame's geometry against this frame's field.
        /// </summary>
        public void Tick()
        {
            if (!_ready || !_enabled || _camera == null) return;

            // Only meaningful while the raymarch mode is the one drawing.
            if (_volumeRenderer == null || !_volumeRenderer.enabled) return;

            _frame++;
            if (_frame % Mathf.Max(1, _probeInterval) != 0) return;

            int h = Mathf.Clamp(_probeHeight, 32, 720);
            float aspect = (_camera.aspect > 0.01f) ? _camera.aspect : (16f / 9f);
            _probeH = h;
            _probeW = Mathf.Clamp(Mathf.RoundToInt(h * aspect), 32, 4096);

            if (!_raymarch.PushMarchUniforms(_cs, _kMarch)) return;
            SnowCasualStyleV7.ApplyToCompute(_cs);

            // The camera basis, pre-scaled so the kernel's ray generation is three multiply-adds and a
            // normalize. No projection matrix and no inverse, so there is no row/column or clip-space
            // convention left to get backwards - which is the class of mistake that killed the first
            // probe design.
            Transform t = _camera.transform;
            float tanHalfV = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            _cs.SetVector(kCamPos, t.position);
            _cs.SetVector(kCamFwd, t.forward);
            _cs.SetVector(kCamRight, t.right * (tanHalfV * aspect));
            _cs.SetVector(kCamUp, t.up * tanHalfV);
            _cs.SetFloat(kProbeNear, _camera.nearClipPlane);
            _cs.SetInt(kProbeW, _probeW);
            _cs.SetInt(kProbeH, _probeH);

            _cs.SetBuffer(_kClear, kHisto, _histo);
            _cs.Dispatch(_kClear, (kSlots + 63) / 64, 1, 1);

            _cs.SetBuffer(_kMarch, kHisto, _histo);
            _cs.Dispatch(_kMarch, (_probeW + 7) / 8, (_probeH + 7) / 8, 1);

            _dispatches++;

            if (!_requested)
            {
                _requested = true;
                AsyncGPUReadback.Request(_histo, OnHistoRead);
            }
        }

        private void OnHistoRead(AsyncGPUReadbackRequest request)
        {
            _requested = false;

            if (request.hasError)
            {
                _readbackErrors++;
                return;
            }
            if (_histoCpu == null) return;

            NativeArray<int> data = request.GetData<int>();
            if (data.Length < kSlots) { _readbackErrors++; return; }

            NativeArray<int>.Copy(data, 0, _histoCpu, 0, kSlots);
            _readbacks++;

            _testedRays = _histoCpu[kSlotTested];
            _offBoxRays = _histoCpu[kSlotOffBox];

            int marched = _histoCpu[kSlotMarched];
            _marchedRays = marched;

            if (marched <= 0)
            {
                _valid = false;
                return;
            }

            _meanSteps = _histoCpu[kSlotSum] / (float)marched;
            _maxSteps = _histoCpu[kSlotMax];
            _exhaustedPct = 100f * _histoCpu[kSlotExhausted] / marched;

            // Exact p95: the smallest step count at or below which 95% of the marching rays fall. One
            // bucket per step count, so there is no interpolation and no bucket-width error - which
            // matters because the whole question is the shape of the tail.
            int target = Mathf.CeilToInt(marched * 0.95f);
            int running = 0;
            _p95Steps = _maxSteps;
            for (int i = 0; i < kBuckets; ++i)
            {
                running += _histoCpu[i];
                if (running >= target) { _p95Steps = i; break; }
            }

            _valid = true;
        }
    }
}

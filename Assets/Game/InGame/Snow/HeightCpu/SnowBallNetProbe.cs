using System.Globalization;
using System.IO;
using System.Text;
using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <b>임시 계측 컴포넌트다. 원인을 가른 뒤 지운다 (2026-09-01).</b>
    ///
    /// <para>멀티에서 클라이언트의 눈덩이가 <b>크기가 떨리고 밀리지 않는다</b>는 보고를 가르기 위한
    /// 것이다. 정적으로는 <c>localScale</c> 을 쓰는 주체가 <c>SnowBallCarrier.ApplySize</c>
    /// 하나뿐이고 성장은 <c>SnowCpuStage.OwnsBallState</c> 로 막혀 있어, 남은 갈림길이 둘이다 —
    /// 복제되는 질량이 흔들리는가, 아니면 질량은 멀쩡한데 스케일이 흔들리는가.</para>
    ///
    /// <para>피어마다 다른 파일에 쓴다. 콘솔은 MPPM 가상 플레이어별로 흩어지고 프레임 단위 값이
    /// 스크롤에 묻히므로, 프레임당 한 줄 CSV 로 남겨 나중에 비교한다.</para>
    /// </summary>
    [RequireComponent(typeof(SnowBallCarrier))]
    public sealed class SnowBallNetProbe : MonoBehaviour
    {
        /// <summary>이 초 동안만 기록한다. 무한히 쌓으면 파일이 커지고 프레임을 먹는다.</summary>
        private const float RecordSeconds = 90f;

        private const string OutputDirectory =
            "/private/tmp/claude-501/-Users-dust9826-Documents-UnityProjects-branchB-PPackPPack-v2-branchB/" +
            "1d059f19-bb9b-48a5-8556-25d2aab7ac02/scratchpad";

        private SnowBallCarrier _ball;
        private Rigidbody _body;
        private NetworkObject _object;
        private StreamWriter _writer;
        private float _elapsed;
        private readonly StringBuilder _line = new StringBuilder(256);

        private void Awake()
        {
            _ball = GetComponent<SnowBallCarrier>();
            _body = GetComponent<Rigidbody>();
            _object = GetComponent<NetworkObject>();
        }

        private void LateUpdate()
        {
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed > RecordSeconds) return;
            if (_writer == null && !TryOpen()) return;

            Vector3 p = transform.position;
            Vector3 s = transform.localScale;
            Vector3 v = _body != null ? _body.linearVelocity : Vector3.zero;
            Vector3 w = _body != null ? _body.angularVelocity : Vector3.zero;

            _line.Clear();
            Append(Time.frameCount);
            Append(_elapsed);
            Append(_ball.MassMm);
            Append(_ball.RadiusM);
            Append(s.x);
            Append(p.x); Append(p.y); Append(p.z);
            Append(v.magnitude);
            Append(w.magnitude);
            Append(_body != null && _body.isKinematic ? 1 : 0);
            Append(_body != null ? _body.mass : 0f);
            _line.Length -= 1;   // 마지막 쉼표
            _writer.WriteLine(_line.ToString());
            _writer.Flush();     // 크래시·강제종료로 버퍼를 잃지 않게 한다
        }

        private void Append(float value)
        {
            _line.Append(value.ToString("0.#####", CultureInfo.InvariantCulture)).Append(',');
        }

        private void Append(int value)
        {
            _line.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
        }

        /// <summary>
        /// 스폰 직후에는 <c>Object</c> 가 아직 안 서 있어 역할을 못 정한다. 그래서 첫 유효 프레임에
        /// 연다 — 파일 이름에 역할이 들어가야 두 피어의 로그를 섞지 않는다.
        /// </summary>
        private bool TryOpen()
        {
            if (_object == null || !_object.IsValid) return false;

            string role = _object.HasStateAuthority ? "authority" : "proxy";
            string path = Path.Combine(OutputDirectory,
                $"ballprobe_{role}_{_object.Id.Raw}_{System.Diagnostics.Process.GetCurrentProcess().Id}.csv");

            Directory.CreateDirectory(OutputDirectory);
            _writer = new StreamWriter(path, false);
            _writer.WriteLine("frame,t,massMm,radiusM,scaleX,px,py,pz,speed,angSpeed,kinematic,rbMass");
            _writer.Flush();
            return true;
        }

        private void OnDestroy()
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

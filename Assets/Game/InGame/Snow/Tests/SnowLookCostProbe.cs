using System.Collections;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 렌더 경로별 프레임 비용을 잰다. <b>같은 씬·같은 카메라·같은 눈 상태</b>에서 룩만 바꿔야
    /// 비교가 성립하므로, 사람이 두 번 재는 대신 이 컴포넌트가 연속으로 잰다.
    ///
    /// <para>프레임 시간을 쓰는 이유: 이 경로들의 차이는 GPU(마칭 vs 정점)와 CPU(굽기 유무)에 동시에
    /// 걸려 있어서 한쪽만 재면 결론이 뒤집힌다 - 이 폴더에 이미 그 사례가 있다(디테일 옥타브를
    /// 줄이자 스텝이 늘고 프레임은 줄었다).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowLookCostProbe : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float _secondsPerLook = 4f;

        [SerializeField, Min(0.2f)] private float _warmupSeconds = 1.5f;

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            SnowSystem sys = FindAnyObjectByType<SnowSystem>();
            if (sys == null)
            {
                Debug.LogError("[LookCost] SnowSystem 이 없다");
                yield break;
            }

            var looks = new[] { ESnowLook.Raymarch, ESnowLook.Displace, ESnowLook.Hidden };

            foreach (ESnowLook look in looks)
            {
                sys.Look = look;

                // 워밍업 - 셰이더 컴파일과 첫 업로드가 첫 프레임들에 몰린다.
                float until = Time.realtimeSinceStartup + _warmupSeconds;
                while (Time.realtimeSinceStartup < until) yield return null;

                int frames = 0;
                double sum = 0;
                double worst = 0;
                until = Time.realtimeSinceStartup + _secondsPerLook;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                    double ms = Time.unscaledDeltaTime * 1000.0;
                    sum += ms;
                    if (ms > worst) worst = ms;
                    frames++;
                }

                double avg = frames > 0 ? sum / frames : 0;
                Debug.Log($"[LookCost] {look} 실제={sys.EffectiveLook} 프레임 {avg:F2} ms " +
                          $"(최악 {worst:F2}) 표본 {frames}");
            }

            Debug.Log("[LookCost] 끝");
        }
    }
}

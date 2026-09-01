using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// MPPM(Play Mode Scenarios)을 메뉴에서 켜고 끄는 도구.
    ///
    /// <para>왜 필요한가: 시나리오 <b>구성</b>(추가 에디터 인스턴스를 몇 개 둘지, 태그를 뭘 달지)은 공개
    /// API 가 없어 창에서 한 번 해야 한다. 그러나 <b>시작·정지·상태</b>는
    /// <c>Unity.PlayMode.Editor.PlayModeScenarioManager</c> 의 공개 API 라, 한 번 구성해 두면 그 뒤의
    /// 반복은 메뉴(그리고 CLI 의 <c>menu</c> 명령)로 돌릴 수 있다. 검증 루프를 사람 클릭에 묶어 두지
    /// 않으려고 이 도구를 둔다.</para>
    ///
    /// <para>타입을 리플렉션으로 잡는 이유는 어셈블리 참조 때문이다. <c>UnityEditor.PlayModeModule</c> 은
    /// 엔진 모듈이라 우리 asmdef 에서 직접 참조하면 Unity 버전에 따라 컴파일이 깨진다. 없으면 없다고
    /// 말하고 끝나는 편이 안전하다.</para>
    /// </summary>
    public static class MultiplayScenarioTools
    {
        private const string ManagerType = "Unity.PlayMode.Editor.PlayModeScenarioManager, UnityEditor.PlayModeModule";
        private const string ScenarioType = "Unity.PlayMode.Editor.PlayModeScenario, UnityEditor.PlayModeModule";

        /// <summary>
        /// 스크립트가 다시 컴파일됐는가. <b>도메인 리로드가 아니라 컴파일만 센다</b> — Play 진입·종료도
        /// 도메인을 리로드하지만 그때는 클론이 멀쩡하기 때문이다.
        ///
        /// <para><see cref="SessionState"/> 는 도메인 리로드를 넘어 살아남고 에디터를 닫으면 사라진다.
        /// 기본값을 <c>true</c> 로 두는 이유는 에디터를 새로 연 직후에도 한 번은 클론을 새로 만드는 편이
        /// 안전하기 때문이다.</para>
        /// </summary>
        private const string StaleKey = "PPack.Multiplay.ClonesStale";

        [InitializeOnLoadMethod]
        private static void HookCompilation()
        {
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationFinished(object _) => SessionState.SetBool(StaleKey, true);

        [MenuItem("PPack/Multiplay/시나리오 창 열기", priority = 100)]
        private static void OpenWindow()
        {
            // 메뉴 경로는 버전에 따라 바뀐다. 6000.6 에서 확인한 두 곳을 순서대로 시도한다.
            if (EditorApplication.ExecuteMenuItem("Window/Play Mode/Scenarios")) return;
            EditorApplication.ExecuteMenuItem("Window/Multiplayer/Multiplayer Play Mode");
        }

        [MenuItem("PPack/Multiplay/2인 자동 시작 (서버+클라2)", priority = 101)]
        private static void Start2PlayerServer() => StartNamed("2PlayerServer");

        [MenuItem("PPack/Multiplay/2인 로비 흐름 시작 (수동)", priority = 102)]
        private static void Start2PlayerLobbyFlow() => StartNamed("2PlayerLobbyFlow");

        [MenuItem("PPack/Multiplay/시나리오 시작 (현재 활성)", priority = 103)]
        private static void StartScenario() => Invoke("Start");

        [MenuItem("PPack/Multiplay/시나리오 정지", priority = 104)]
        private static void StopScenario() => Invoke("Stop");

        /// <summary>
        /// 추가 에디터(클론)를 닫는다. 다음 시작에서 새로 만들어진다.
        ///
        /// <para><b>왜 필요한가 (2026-08-21 실측).</b> 스크립트를 다시 컴파일한 뒤 <b>기존 클론을
        /// 재사용하면 시나리오가 시작되지 않는다</b> — 클론이 Play 에 진입조차 하지 않아 세션이
        /// <c>Lobby</c> 에서 멈추고, 클론 콘솔에는 <c>[MPPM]</c> 로그가 한 줄도 안 남는다. 정지 후
        /// 다시 시작해도 같다. 클론을 닫고 새로 만들면 매번 정상이다.</para>
        ///
        /// <para>재컴파일이 없으면 재사용이 잘 되므로(실측: 20초 만에 <c>Playing</c>) 무조건 닫지 않고
        /// <see cref="StaleKey"/> 로 판단한다 — 클론을 새로 만드는 데 20~30초가 더 든다.</para>
        /// </summary>
        [MenuItem("PPack/Multiplay/클론 닫기", priority = 106)]
        private static void CloseClonesMenu() => CloseClones();

        /// <summary>
        /// ⚠ <b><c>VirtualProject.Close()</c> 를 쓰지 않는다 — 그 길은 막다른 길이다 (2026-08-21 실측).</b>
        /// 그것은 클론 프로세스를 닫지만 <b>가상 프로젝트 등록은 남긴다</b>. 그 뒤 <c>Start()</c> 를 부르면
        /// 매니저는 <c>Running</c> 이 되는데 클론은 하나도 뜨지 않는다 — 이미 있다고 판단하는 것으로 보인다.
        /// 등록 2개 · 디스크 폴더 2개 · 실제 프로세스 0개인 상태로 굳는다.
        ///
        /// <para>프로세스를 직접 죽이면 <c>Start()</c> 가 매번 새로 띄운다. 그래서 이 투박한 방법을 쓴다.
        /// 팀 환경이 macOS 고정이라(<c>AGENTS.md</c>) <c>pkill</c> 에 의존해도 된다.</para>
        /// </summary>
        private static int CloseClones()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/pkill", "-f scenarioClone")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(5000);

                // pkill 은 죽인 대상이 없으면 1 을 준다 - 그건 실패가 아니다.
                int code = p?.ExitCode ?? -1;
                Debug.Log($"[MPPM] 클론 종료 요청(pkill -f scenarioClone) exit={code}"
                          + (code == 1 ? " - 닫을 클론이 없었다." : string.Empty));
                return code == 0 ? 1 : 0;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MPPM] 클론을 닫지 못했다: {e.GetType().Name} {e.Message}");
                return 0;
            }
        }

        [MenuItem("PPack/Multiplay/시나리오 상태 보기", priority = 105)]
        private static void LogState()
        {
            System.Type type = System.Type.GetType(ManagerType);
            if (type == null)
            {
                Debug.LogWarning("[MPPM] PlayModeScenarioManager 가 없다 - 이 에디터 버전에는 Play Mode Scenarios 가 없다.");
                return;
            }

            object scenario = type.GetProperty("ActiveScenario", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object state = type.GetProperty("State", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            Debug.Log($"[MPPM] activeScenario={scenario ?? (object)"null"} state={state ?? (object)"?"}");
        }

        /// <summary>
        /// 시나리오를 <b>이름으로 지정해서</b> 시작한다.
        ///
        /// <para>왜 이름으로 지정하는가: <b>일반 Play 는 오케스트레이션을 하지 않는다.</b> 실측 —
        /// 활성 시나리오가 <c>2PlayerServer</c> 인 상태에서 에디터 ▶(또는 CLI <c>editor_play</c>)로
        /// 실행하면 Unity 프로세스가 6개에서 그대로 6개, 즉 메인 에디터만 돈다. 시나리오 API 로 시작하면
        /// 8개가 되어 추가 에디터 2개가 실제로 뜬다. 그래서 멀티를 볼 때는 반드시 이 경로로 시작해야 하고,
        /// 그때 무엇이 선택돼 있든 의존하지 않게 이름으로 지정한다.</para>
        /// <para>(<c>ActiveScenario</c> 자체는 재컴파일 후에도 유지된다 — 처음에 리셋되는 것으로 의심했으나
        /// 실측으로 아니었다.)</para>
        /// </summary>
        private static void StartNamed(string scenarioName)
        {
            System.Type mgr = System.Type.GetType(ManagerType);
            System.Type scenarioType = System.Type.GetType(ScenarioType);
            if (mgr == null || scenarioType == null)
            {
                Debug.LogWarning("[MPPM] Play Mode Scenario API 가 없다.");
                return;
            }

            string path = $"Assets/Settings/PlayMode/{scenarioName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath(path, scenarioType);
            if (asset == null)
            {
                Debug.LogWarning($"[MPPM] 시나리오를 찾지 못했다: {path}");
                return;
            }

            // 돌고 있으면 먼저 세운다. <b>클론은 여기서 닫지 않는다</b> — 정지 중인 매니저는 클론들의
            // 종료 보고를 기다리는데, 그 클론을 밑에서 닫아 버리면 보고할 주체가 사라져 매니저가
            // <c>Stopping</c> 에 영구히 갇힌다(실측: 20초 타임아웃에 걸렸다).
            if (!IsIdle(mgr)) Invoke("Stop");

            // ⚠ <b>`ActiveScenario` 는 `Idle` 일 때만 쓸 수 있다.</b> 정지가 끝나기 전에 대입하면
            // <c>InvalidOperationException: Cannot set config while in a running state (Stopping)</c>
            // 가 나고, 그 줄에서 죽으므로 시작도 되지 않는다(실측). 에디터 코드는 블록할 수 없으니
            // <see cref="EditorApplication.update"/> 로 `Idle` 이 될 때까지 기다렸다가 대입한다.
            WhenIdle(mgr, () =>
            {
                void Go()
                {
                    mgr.GetProperty("ActiveScenario", BindingFlags.Public | BindingFlags.Static)
                       ?.SetValue(null, asset);
                    Debug.Log($"[MPPM] 활성 시나리오 = {scenarioName}. 시작한다.");
                    Invoke("Start");
                }

                // <b>Idle 이 된 뒤에 닫는다.</b> 이 시점에는 매니저가 아무도 기다리지 않는다.
                if (!SessionState.GetBool(StaleKey, true))
                {
                    Go();
                    return;
                }

                Debug.Log("[MPPM] 스크립트가 다시 컴파일됐다 - 낡은 클론을 닫고 새로 만든다.");
                CloseClones();
                SessionState.SetBool(StaleKey, false);

                // 프로세스가 실제로 사라질 시간을 준다. 바로 Start 하면 매니저가 아직 살아 있는
                // 클론을 보고 새로 띄우지 않는다.
                Delay(6d, Go);
            });
        }

        /// <summary>에디터는 블록할 수 없다. <paramref name="seconds"/> 뒤에 한 번 실행한다.</summary>
        private static void Delay(double seconds, System.Action action)
        {
            double at = EditorApplication.timeSinceStartup + seconds;
            void Pump()
            {
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Pump;
                action();
            }

            EditorApplication.update += Pump;
        }

        private static bool IsIdle(System.Type mgr)
        {
            object state = mgr.GetProperty("State", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return state == null || state.ToString() == "Idle";
        }

        /// <summary>
        /// 시나리오 매니저가 <c>Idle</c> 이 되면 <paramref name="action"/> 을 한 번 실행한다.
        /// 20 초 안에 안 되면 포기하고 이유를 남긴다 — 조용히 아무 일도 안 일어나는 것이 제일 나쁘다.
        /// </summary>
        private static void WhenIdle(System.Type mgr, System.Action action)
        {
            double deadline = EditorApplication.timeSinceStartup + 20d;
            void Pump()
            {
                if (!IsIdle(mgr))
                {
                    if (EditorApplication.timeSinceStartup < deadline) return;
                    EditorApplication.update -= Pump;
                    object state = mgr.GetProperty("State", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    Debug.LogWarning($"[MPPM] 20초 안에 Idle 이 되지 않았다(state={state}) - 시작을 포기한다.");
                    return;
                }

                EditorApplication.update -= Pump;
                action();
            }

            EditorApplication.update += Pump;
        }

        private static void Invoke(string methodName)
        {
            System.Type type = System.Type.GetType(ManagerType);
            MethodInfo method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                Debug.LogWarning($"[MPPM] PlayModeScenarioManager.{methodName} 를 찾지 못했다.");
                return;
            }

            method.Invoke(null, null);
            Debug.Log($"[MPPM] PlayModeScenarioManager.{methodName} 호출.");
        }
    }
}

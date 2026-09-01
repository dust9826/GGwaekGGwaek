using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace PPack
{
    public static class PenguinMomentumTestRunner
    {
        public const string ResultPath = "Library/PenguinMomentumTestResults.txt";
        public const string AccelerationResultPath =
            "Library/PenguinMomentumAccelerationResults.txt";
        private const string RunningKey = "PPack.Penguin.MomentumHandling.TestsRunning";

        private static TestRunnerApi _runner;
        private static Callbacks _callbacks;

        [InitializeOnLoadMethod]
        private static void RestoreCallbacksAfterDomainReload()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            EditorApplication.delayCall += RegisterCallbacks;
        }

        [MenuItem("PPack/Penguin/Run Momentum Handling Tests")]
        public static void Run()
            => RunWithFilters(new Filter
            {
                testMode = TestMode.PlayMode,
                assemblyNames = new[] { "PPack.Penguin.MomentumHandling.PlayModeTests" },
                groupNames = new[] { "^PPack.PenguinMomentumHandlingTests$" }
            });

        [MenuItem("PPack/Penguin/Measure Momentum Acceleration")]
        public static void MeasureAcceleration()
        {
            if (File.Exists(AccelerationResultPath)) File.Delete(AccelerationResultPath);
            RunWithFilters(new Filter
            {
                testMode = TestMode.PlayMode,
                assemblyNames = new[] { "PPack.Penguin.MomentumHandling.PlayModeTests" },
                groupNames = new[] { "^PPack.PenguinMomentumAccelerationMeasurementTests$" }
            });
        }

        [MenuItem("PPack/Penguin/Run Momentum Handling Regression")]
        public static void RunRegression()
            => RunWithFilters(
                new Filter
                {
                    testMode = TestMode.PlayMode,
                    assemblyNames = new[] { "PPack.Penguin.MomentumHandling.PlayModeTests" },
                    groupNames = new[] { "^PPack.PenguinMomentumHandlingTests$" }
                },
                new Filter
                {
                    testMode = TestMode.PlayMode,
                    assemblyNames = new[] { "PPack.Penguin.PlayModeTests" },
                    groupNames = new[]
                    {
                        "^PPack.PenguinSlideHandlingTests",
                        "^PPack.PenguinCarryTests",
                        "^PPack.PenguinSnowballPushTests"
                    }
                });

        private static void RunWithFilters(params Filter[] filters)
        {
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            SessionState.SetBool(RunningKey, true);
            RegisterCallbacks();
            _runner.Execute(new ExecutionSettings(filters));
            Debug.Log("Momentum handling tests started.");
        }

        private static void RegisterCallbacks()
        {
            if (_callbacks != null) TestRunnerApi.UnregisterTestCallback(_callbacks);
            _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            _callbacks = new Callbacks();
            _runner.RegisterCallbacks(_callbacks);
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var summary = new StringBuilder();
                summary.AppendLine($"status={result.TestStatus}");
                summary.AppendLine($"passed={result.PassCount}");
                summary.AppendLine($"failed={result.FailCount}");
                summary.AppendLine($"skipped={result.SkipCount}");
                summary.AppendLine($"duration={result.Duration:0.000}");
                if (File.Exists(ResultPath)) summary.Append(File.ReadAllText(ResultPath));
                File.WriteAllText(ResultPath, summary.ToString());
                Debug.Log($"Momentum handling tests finished: {result.TestStatus} · " +
                          $"{result.PassCount} passed · {result.FailCount} failed");
                SessionState.SetBool(RunningKey, false);
                _runner = null;
                _callbacks = null;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus != TestStatus.Failed) return;
                File.AppendAllText(ResultPath,
                    $"FAIL {result.FullName}: {result.Message}\n{result.StackTrace}\n");
            }
        }
    }
}

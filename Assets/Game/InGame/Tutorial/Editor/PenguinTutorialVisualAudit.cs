using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// Captures every training room from the same player-readable oblique angle so layout changes
    /// can be compared without relying on a manually positioned Scene view camera.
    /// </summary>
    public static class PenguinTutorialVisualAudit
    {
        private const string OutputFolder =
            "Assets/Game/InGame/Tutorial/Docs/VisualAudit/Current";

        private readonly struct AuditShot
        {
            public readonly string FileName;
            public readonly Vector3 Position;
            public readonly Vector3 Target;
            public readonly float FieldOfView;

            public AuditShot(string fileName, Vector3 position, Vector3 target, float fieldOfView)
            {
                FileName = fileName;
                Position = position;
                Target = target;
                FieldOfView = fieldOfView;
            }
        }

        private static readonly AuditShot[] Shots =
        {
            new AuditShot("01_Walk.png", new Vector3(-22f, 6.8f, -17f), new Vector3(-16f, 0.65f, -11f), 58f),
            new AuditShot("02_Run.png", new Vector3(-6f, 6.8f, -17f), new Vector3(0f, 0.65f, -11f), 58f),
            new AuditShot("03_Slide.png", new Vector3(22f, 7.2f, -15f), new Vector3(16f, 0.65f, -4f), 63f),
            new AuditShot("04_Snowball.png", new Vector3(22f, 6.8f, 5f), new Vector3(16f, 0.65f, 11f), 58f),
            new AuditShot("05_Machine.png", new Vector3(8f, 7.0f, 5f), new Vector3(2f, 1.0f, 10.5f), 58f),
            new AuditShot("06_GiftDelivery.png", new Vector3(-4f, 5.5f, 16f), new Vector3(-10.5f, 0.8f, 6.5f), 60f),
            new AuditShot("07_WarehousePickup.png", new Vector3(-14f, 5.8f, 7f), new Vector3(-19f, 1.0f, 13.5f), 57f),
            new AuditShot("08_NeighborDelivery.png", new Vector3(-7f, 5.4f, 8f), new Vector3(-12.3f, 0.8f, 14f), 55f)
        };

        [MenuItem("PPack/Tutorial/Capture Penguin Tutorial Room Audit")]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("구역 촬영은 Play Mode를 종료한 뒤 실행한다.");

            Scene original = SceneManager.GetActiveScene();
            bool useCurrent = original.IsValid() && original.isLoaded &&
                              original.path == PenguinTutorialSceneBuilder.ScenePath;
            Scene scene = useCurrent
                ? original
                : EditorSceneManager.OpenScene(PenguinTutorialSceneBuilder.ScenePath, OpenSceneMode.Additive);

            try
            {
                Directory.CreateDirectory(Path.GetFullPath(OutputFolder));
                foreach (AuditShot shot in Shots) CaptureShot(scene, shot);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"Penguin tutorial room audit captured: {OutputFolder}");
            }
            finally
            {
                if (!useCurrent)
                {
                    if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void CaptureShot(Scene scene, AuditShot shot)
        {
            GameObject cameraObject = new GameObject("PenguinTutorialAuditCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = shot.Position;
            camera.transform.LookAt(shot.Target);
            camera.fieldOfView = shot.FieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.055f, 0.10f);
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 160f;
            camera.allowHDR = true;

            RenderTexture renderTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                texture.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                texture.Apply(false, false);
                string assetPath = $"{OutputFolder}/{shot.FileName}";
                File.WriteAllBytes(Path.GetFullPath(assetPath), texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(texture);
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}

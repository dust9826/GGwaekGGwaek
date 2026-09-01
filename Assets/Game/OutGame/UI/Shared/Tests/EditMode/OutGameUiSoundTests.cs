using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    public sealed class OutGameUiSoundTests
    {
        private const string MainMenuScenePath =
            "Assets/Game/OutGame/UI/MainMenu/Scenes/MainMenu.unity";

        [Test]
        public void MainMenu_UI_사운드는_역할별_클립과_낮은_볼륨을_사용한다()
        {
            Scene originalActiveScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Additive);
            try
            {
                OutGameScreenController controller = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    controller = root.GetComponentInChildren<OutGameScreenController>(true);
                    if (controller != null)
                        break;
                }
                Assert.That(controller, Is.Not.Null);

                var serialized = new SerializedObject(controller);
                AssertClip(serialized, "_hoverClip", "UI_Hover_Dustyroom");
                AssertClip(serialized, "_clickClip", "UI_Click_Dustyroom");
                AssertClip(serialized, "_navigationClip", "UI_Click_Casual");
                AssertClip(serialized, "_confirmClip", "UI_Click_Confirm");
                Assert.That(serialized.FindProperty("_hoverVolume").floatValue, Is.EqualTo(0.18f).Within(0.001f));
                Assert.That(serialized.FindProperty("_clickVolume").floatValue, Is.EqualTo(0.30f).Within(0.001f));
                Assert.That(serialized.FindProperty("_navigationVolume").floatValue, Is.EqualTo(0.24f).Within(0.001f));
                Assert.That(serialized.FindProperty("_confirmVolume").floatValue, Is.EqualTo(0.34f).Within(0.001f));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                    SceneManager.SetActiveScene(originalActiveScene);
            }
        }

        private static void AssertClip(SerializedObject serialized, string propertyName, string expectedName)
        {
            AudioClip clip = serialized.FindProperty(propertyName).objectReferenceValue as AudioClip;
            Assert.That(clip, Is.Not.Null, propertyName + " is not assigned");
            Assert.That(clip.name, Is.EqualTo(expectedName));
        }
    }
}

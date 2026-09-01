using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class PenguinSnowInteractionTests
    {
        private GameObject _stageObject;
        private GameObject _groundObject;
        private GameObject _penguinObject;

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_penguinObject != null) Object.DestroyImmediate(_penguinObject);
            if (_stageObject != null) Object.DestroyImmediate(_stageObject);
            if (_groundObject != null) Object.DestroyImmediate(_groundObject);
        }

        [Test]
        public void PenguinPrefab_UsesOneSharedSnowHeightPivot()
        {
            GameObject prefab = LoadPenguinPrefab();
            Assert.IsNotNull(prefab);

            Transform pivot = prefab.transform.Find("SnowHeightPivot");
            Assert.IsNotNull(pivot);
            Assert.AreSame(pivot, prefab.transform.Find("SnowHeightPivot/BodyPivot").parent);
            Assert.AreSame(pivot, prefab.transform.Find("SnowHeightPivot/CameraRig").parent);
            Assert.IsNotNull(prefab.GetComponent<PenguinSnowInteraction>());
        }

        [UnityTest]
        public IEnumerator ThreeHundredMillimetresOfSnow_RaisesPresentationByHalfWithoutMovingPhysicsRoot()
        {
            BuildStageAndGround();
            GameObject prefab = LoadPenguinPrefab();
            _penguinObject = Object.Instantiate(prefab, new Vector3(0f, 0.01f, 0f), Quaternion.identity);
            _penguinObject.name = "__TEST__SnowHeightPenguin";
            _penguinObject.GetComponent<PenguinInputReader>().enabled = false;

            float initialRootY = _penguinObject.transform.position.y;
            for (int frame = 0; frame < 60; frame++) yield return null;

            SnowCpuStage stage = _stageObject.GetComponent<SnowCpuStage>();
            PenguinSnowInteraction interaction = _penguinObject.GetComponent<PenguinSnowInteraction>();
            Transform pivot = _penguinObject.transform.Find("SnowHeightPivot");

            Assert.IsNotNull(stage.Field);
            Assert.IsTrue(_penguinObject.GetComponent<PenguinLocomotion>().IsGrounded);
            Assert.AreEqual(0.3f, stage.DepthAt(interaction.ContactWorldPosition), 0.001f);
            Assert.AreEqual(0.15f, interaction.VisualOffsetM, 0.01f);
            Assert.AreEqual(0.15f, pivot.localPosition.y, 0.01f);
            Assert.AreEqual(initialRootY, _penguinObject.transform.position.y, 0.03f,
                "눈 높이 표현 때문에 물리 루트가 떠서는 안 된다");
        }

        [UnityTest]
        public IEnumerator SnowBelowBareElevatedGround_DoesNotRaisePresentation()
        {
            BuildStageAndGround(2f);
            GameObject prefab = LoadPenguinPrefab();
            _penguinObject = Object.Instantiate(prefab, new Vector3(0f, 2.01f, 0f), Quaternion.identity);
            _penguinObject.name = "__TEST__BareSlopeHeightPenguin";
            _penguinObject.GetComponent<PenguinInputReader>().enabled = false;

            for (int frame = 0; frame < 60; frame++) yield return null;

            PenguinLocomotion locomotion = _penguinObject.GetComponent<PenguinLocomotion>();
            PenguinSnowInteraction interaction = _penguinObject.GetComponent<PenguinSnowInteraction>();
            Transform pivot = _penguinObject.transform.Find("SnowHeightPivot");

            Assert.IsTrue(locomotion.IsGrounded);
            Assert.AreEqual(0.3f, _stageObject.GetComponent<SnowCpuStage>()
                .DepthAt(interaction.ContactWorldPosition), 0.001f,
                "구형 XZ 깊이 질의는 아래 지면 시트의 눈을 읽는 재현 조건이다");
            Assert.AreEqual(0f, interaction.VisualOffsetM, 0.01f);
            Assert.AreEqual(0f, pivot.localPosition.y, 0.01f,
                "현재 지지면이 눈 바닥과 다른 높이면 아래 눈 때문에 몸과 카메라가 떠서는 안 된다");
        }

        private void BuildStageAndGround(float groundTopY = 0f)
        {
            _stageObject = new GameObject("__TEST__SnowCpuStage");
            _stageObject.AddComponent<SnowCpuStage>();

            _groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundObject.name = "__TEST__Ground";
            _groundObject.transform.position = new Vector3(0f, groundTopY - 0.5f, 0f);
            _groundObject.transform.localScale = new Vector3(20f, 1f, 20f);
        }

        private static GameObject LoadPenguinPrefab()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab");
#else
            return null;
#endif
        }
    }
}

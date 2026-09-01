#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <see cref="SnowZone"/> 의 씬 뷰 핸들. <b>콜라이더를 쓰지 않는 이유</b>가 이 파일의 존재 이유다 —
    /// <c>BoxCollider</c> 로 크기를 주면 드래그 핸들은 공짜로 얻지만 <b>물리 장애물이 하나 생긴다</b>.
    /// 지붕 위에 눈 상자를 놓는 순간 플레이어가 그 상자에 부딪히게 되므로, 크기는 컴포넌트가 갖고
    /// 핸들만 여기서 그린다.
    ///
    /// <para>핸들은 상자 로컬에서 그린다. 기울어진 램프에 붙은 상자를 월드 축 핸들로 끌면 크기가
    /// 회전과 섞여 저작자가 예상하지 못한 값이 된다.</para>
    /// </summary>
    [CustomEditor(typeof(SnowZone))]
    public sealed class SnowZoneEditor : Editor
    {
        private readonly BoxBoundsHandle _handle = new BoxBoundsHandle();

        private void OnSceneGUI()
        {
            var zone = (SnowZone)target;

            SerializedProperty sizeProp = serializedObject.FindProperty("_sizeXZ");
            SerializedProperty heightProp = serializedObject.FindProperty("_heightM");
            SerializedProperty slackProp = serializedObject.FindProperty("_baseSlackM");

            Vector2 sizeXZ = sizeProp.vector2Value;
            float heightM = heightProp.floatValue;
            float slackM = slackProp.floatValue;

            using (new Handles.DrawingScope(zone.transform.localToWorldMatrix))
            {
                _handle.center = new Vector3(0f, (heightM - slackM) * 0.5f, 0f);
                _handle.size = new Vector3(sizeXZ.x, heightM + slackM, sizeXZ.y);

                EditorGUI.BeginChangeCheck();
                _handle.DrawHandle();
                if (!EditorGUI.EndChangeCheck()) return;

                // 바닥면은 상자의 로컬 y = 0 에 고정이다 — 눈이 앉는 평면이 핸들을 끌 때마다
                // 움직이면 저작자가 표면과 상자를 다시 맞춰야 한다. 그래서 XZ 와 높이만 받는다.
                serializedObject.Update();
                sizeProp.vector2Value = new Vector2(Mathf.Max(0.25f, _handle.size.x),
                                                    Mathf.Max(0.25f, _handle.size.z));
                heightProp.floatValue = Mathf.Max(0.1f, _handle.size.y - slackM);
                serializedObject.ApplyModifiedProperties();
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var zone = (SnowZone)target;
            Vector2 size = zone.SizeXZ;
            int cells = Mathf.CeilToInt(size.x / SnowFieldGeometry.CellSizeM)
                      * Mathf.CeilToInt(size.y / SnowFieldGeometry.CellSizeM);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"셀 약 {cells:N0} 개 (상한 {SnowZone.MaxCells:N0})\n" +
                "· 크기는 스케일이 아니라 Size XZ 로 준다. 스케일은 1 이어야 한다.\n" +
                "· 눈은 상자의 로컬 y = 0 평면에 앉는다. 그 평면을 표면에 맞춰 놓을 것.\n" +
                "· 상자를 더하거나 옮긴 뒤에는 Play 를 다시 눌러야 반영된다.",
                cells > SnowZone.MaxCells ? MessageType.Error : MessageType.Info);

            Vector3 scale = zone.transform.lossyScale;
            if (Mathf.Abs(scale.x - 1f) > 1e-3f || Mathf.Abs(scale.y - 1f) > 1e-3f
                                                || Mathf.Abs(scale.z - 1f) > 1e-3f)
            {
                EditorGUILayout.HelpBox($"스케일이 {scale} 다 — 격자와 그림이 그만큼 늘어난다.",
                                        MessageType.Warning);
            }
        }
    }
}
#endif

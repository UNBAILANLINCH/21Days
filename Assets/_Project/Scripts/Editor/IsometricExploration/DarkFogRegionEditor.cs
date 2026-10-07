// 职责：减雾区域可视化；现有 Inspector 不含空间雾，使用 Unity 原生手柄而不引入编辑器框架。
using Game.IsometricExploration;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.IsometricExploration
{
    [CustomEditor(typeof(DarkFogRegion))]
    public sealed class DarkFogRegionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var region = (DarkFogRegion)target;
            if (region.Config != null)
            {
                var config = new SerializedObject(region.Config);
                config.Update();
                EditorGUILayout.PropertyField(config.FindProperty("color"), new GUIContent("外部雾颜色"));
                EditorGUILayout.PropertyField(config.FindProperty("density"), new GUIContent("外部雾浓度", "0 无雾，1 完整雾效果；中间值线性调整。"));
                if (config.ApplyModifiedProperties())
                {
                    SceneView.RepaintAll();
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                EditorGUILayout.HelpBox("颜色和浓度修改会保存到引用的雾配置；共用这份配置的区域会一起改变。", MessageType.None);
            }
            EditorGUILayout.HelpBox("位置是减雾中心；缩放 XYZ 是三个方向的外半径。Scene 中青色为清晰内圈，蓝色为过渡外圈。挂在主角子节点即可跟随。", MessageType.Info);
        }

        private void OnSceneGUI()
        {
            var region = (DarkFogRegion)target;
            Transform t = region.transform;
            float inner = region.Config == null ? 0.7f : 1f - region.Config.Feather;
            using (new Handles.DrawingScope(t.localToWorldMatrix))
            {
                Handles.color = Color.cyan;
                Handles.DrawWireDisc(Vector3.zero, Vector3.up, inner);
                Handles.DrawWireDisc(Vector3.zero, Vector3.right, inner);
                Handles.DrawWireDisc(Vector3.zero, Vector3.forward, inner);
                Handles.color = new Color(0.3f, 0.6f, 1f);
                EditorGUI.BeginChangeCheck();
                float size = Handles.RadiusHandle(Quaternion.identity, Vector3.zero, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(t, "调整减雾范围");
                    t.localScale *= Mathf.Max(0.01f, size);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                }
            }
        }
    }
}

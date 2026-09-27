// 职责：PerformanceStage 的自定义 Inspector——默认字段之外加「打开时间轴」「校验」两个按钮，并把校验结果画成 HelpBox。
// 为什么新建（复用 → 扩展 → 新建）：CustomEditor 必须按组件类型注册；PerformanceStage 在 Runtime 程序集里不能带编辑器代码，
//   工程里也没有可承载它的通用 Inspector，只能新建。
using System.Collections.Generic;
using System.IO;
using Game.Performance;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Performance
{
    [CustomEditor(typeof(PerformanceStage))]
    public sealed class PerformanceStageEditor : UnityEditor.Editor
    {
        private List<PerformanceIssue> issues;

        private void OnEnable()
        {
            issues = null;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var stage = (PerformanceStage)target;
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开时间轴")) OpenTimeline(stage.gameObject);
            if (GUILayout.Button("校验")) issues = Validate(stage.gameObject);
            EditorGUILayout.EndHorizontal();

            PerformanceEditorWindow.DrawIssues(issues);
        }

        private static List<PerformanceIssue> Validate(GameObject root)
        {
            // 能认出预制体资产路径时，按「文件名 = 演出 id = 地址」一并查 Addressables 登记。
            string path = PerformanceValidator.ResolvePrefabAssetPath(root);
            string address = string.IsNullOrEmpty(path) ? null : Path.GetFileNameWithoutExtension(path);
            return PerformanceValidator.Validate(root, address);
        }

        private static void OpenTimeline(GameObject root)
        {
            // 预制体资产（Project 窗口里选中）要先进预制体模式；场景实例或已在预制体模式里的内容直接开。
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(root)))
                PerformanceEditorWindow.OpenTimeline(root);
            else
                PerformanceEditorWindow.ShowTimelineFor(root);
        }
    }
}

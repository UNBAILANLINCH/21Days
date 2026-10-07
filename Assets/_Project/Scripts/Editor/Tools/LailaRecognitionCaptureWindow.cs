using Game.LailaFaceRecognition;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Tools
{
    // 职责：采集前显式选择近邻组与数据划分，保留未标注状态。
    // 新建原因：一键生成每条独立group会使近邻样本有跨集泄漏风险。
    public sealed class LailaRecognitionCaptureWindow : EditorWindow
    {
        private LailaExpressionRecognizer recognizer;
        private string group = "";
        private int splitIndex;
        private string status = "样本无标签；不要将模型预测作为人工标注";
        private static readonly string[] Splits = { "unassigned", "train", "dev", "test" };

        public static void Open()
        {
            var window = GetWindow<LailaRecognitionCaptureWindow>("莱拉样本采集");
            var selected = Selection.activeGameObject;
            if (selected != null) window.recognizer = selected.GetComponent<LailaExpressionRecognizer>();
            window.Show();
        }
        private void OnGUI()
        {
            recognizer = (LailaExpressionRecognizer)EditorGUILayout.ObjectField("识别对象", recognizer, typeof(LailaExpressionRecognizer), true);
            group = EditorGUILayout.TextField("近邻组（同配方共用）", group);
            splitIndex = EditorGUILayout.Popup("数据划分", splitIndex, Splits);
            EditorGUILayout.HelpBox("同配方强度变化、镜像和近邻脸必须共用group；锁定test后不用于修映射/阈值。截图不包含预测UI，文件始终未标注。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || recognizer == null || string.IsNullOrWhiteSpace(group)))
                if (GUILayout.Button("采集当前脸与17轴快照"))
                {
                    try { status = "已采集未标注样本：" + LailaRecognitionTools.Capture(recognizer, Splits[splitIndex], group); }
                    catch (System.Exception e) { status = e.Message; }
                }
            EditorGUILayout.HelpBox(status, MessageType.None);
        }
    }
}

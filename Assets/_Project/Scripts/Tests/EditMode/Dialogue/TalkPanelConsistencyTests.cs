// 职责：守住「NPC 交互对白（DialogueView.prefab）与时间轴演出对白（PerformanceView.prefab）是同一套视觉」——
//   按节点名逐项比对两份预制体的布局（RectTransform）、底图（Image）与文字样式（TMP，含字体与材质引用），任何一边被单独改样式就红；
//   控件同位：两份预制体放进同一个 1920×1080 画布、强制布局后比 LOG / 自动 / 跳过三个控件的屏幕矩形（对白的跳过按钮 ↔ 演出的 SkipRoot），
//   以及 LOG / 自动两对按钮下 Label / LabelShadow / Hint 的屏幕矩形与样式；
//   记录面板（TranscriptView.prefab，对白与演出共用的 LOG 面板）的「关闭」不得压在两份面板的控件上（同一张画布比屏幕矩形）。
// 为什么新建：复用——既有 Dialogue 测试都守规则 / 策略，没有预制体样式的守卫；扩展——塞进某个规则测试类职责说不通，
//   这是跨两个模块预制体的视觉契约，所以单独成类。刻意读真实资产路径：它守的就是这两份预制体本身。
// Image / TMP 字段经 SerializedObject 按序列化名读（m_Color、m_fontSize……），比的正是落盘的值；
//   只有强制布局用到 uGUI 的 LayoutGroup / LayoutRebuilder（对白的自动 / 跳过按钮由布局组排位，不跑布局量不出真实位置）。
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class TalkPanelConsistencyTests
    {
        private const string DialoguePath = "Assets/_Project/Prefabs/UI/DialogueView.prefab";
        private const string PerformancePath = "Assets/_Project/Prefabs/UI/PerformanceView.prefab";
        private const string TranscriptPath = "Assets/_Project/Prefabs/UI/TranscriptView.prefab";
        private const string ImageType = "Image";
        private const string TextType = "TextMeshProUGUI";
        private const float Tolerance = 0.0001f;

        /// <summary>控件屏幕矩形的比对容差（参考分辨率像素）。</summary>
        private const float PixelTolerance = 0.5f;

        /// <summary>参考分辨率（与 UIConfig 一致：1920×1080、按高度匹配）。</summary>
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        // 同位控件：对白面板节点 ↔ 演出面板节点（演出不做倍速，倍速位空着；跳过在演出里是 SkipRoot）。
        private static readonly (string Dialogue, string Performance)[] ControlPairs =
            { ("HistoryButton", "HistoryButton"), ("AutoButton", "AutoButton"), ("SkipButton", "SkipRoot") };

        // 对白面板的四个控件位（演出没有倍速，按对白算；演出的三个控件与对白同位，由 Controls_SitAtSameScreenRect 守住）。
        private static readonly string[] DialogueControls = { "HistoryButton", "AutoButton", "SpeedButton", "SkipButton" };

        // 两边都从同一份按钮结构来的控件：子节点的位置与样式也要一致。
        private static readonly string[] ClonedControls = { "HistoryButton", "AutoButton" };
        private static readonly string[] ControlChildren = { "Label", "LabelShadow", "Hint" };

        // 两边同名、样式必须完全一致的对白面板节点。
        private static readonly string[] SharedNodes =
            { "SubtitleRoot", "PanelBackground", "AvatarFrame", "AvatarFrameRight", "Speaker", "Body", "HoldPrompt", "HoldHint" };

        // 固定文案也要一致的节点（其余节点的文字由运行时填）。
        private static readonly HashSet<string> FixedTextNodes = new HashSet<string> { "HoldPrompt", "HoldHint" };

        private GameObject dialogue;
        private GameObject performance;

        [SetUp]
        public void SetUp()
        {
            dialogue = AssetDatabase.LoadAssetAtPath<GameObject>(DialoguePath);
            performance = AssetDatabase.LoadAssetAtPath<GameObject>(PerformancePath);
            Assert.That(dialogue, Is.Not.Null, "找不到预制体 " + DialoguePath);
            Assert.That(performance, Is.Not.Null, "找不到预制体 " + PerformancePath);
        }

        [Test]
        public void SharedPanelNodes_MatchPerformanceView()
        {
            var diffs = new List<string>();
            foreach (string node in SharedNodes)
            {
                Transform d = Require(dialogue, "DialogueView", node, diffs);
                Transform p = Require(performance, "PerformanceView", node, diffs);
                if (d == null || p == null) continue;
                string label = $"DialogueView {node} ↔ PerformanceView {node}";
                CompareRect(label, d, p, diffs);
                CompareImage(label, d, p, diffs);
                CompareText(label, d, p, FixedTextNodes.Contains(node), diffs);
            }
            AssertNoDiffs(diffs);
        }

        // 左右头像位分别对齐：对白左槽 ↔ 演出左头像、对白右槽 ↔ 演出右头像。
        [Test]
        public void Portraits_SitAtPerformanceAvatarRect()
        {
            var diffs = new List<string>();
            var pairs = new[] { ("PortraitLeft", "Avatar"), ("PortraitRight", "AvatarRight") };
            foreach ((string slot, string reference) in pairs)
            {
                Transform d = Require(dialogue, "DialogueView", slot, diffs);
                Transform p = Require(performance, "PerformanceView", reference, diffs);
                if (d != null && p != null) CompareRect($"DialogueView {slot} ↔ PerformanceView {reference}", d, p, diffs);
            }
            AssertNoDiffs(diffs);
        }

        [Test]
        public void ControlLabels_MatchPerformanceSkipStyle()
        {
            var diffs = new List<string>();
            Transform skip = Require(dialogue, "DialogueView", "SkipButton", diffs);
            var pairs = new[] { ("Label", "SkipLabel"), ("LabelShadow", "SkipLabelShadow"), ("Hint", "SkipHint") };
            foreach ((string child, string reference) in pairs)
            {
                Transform d = skip == null ? null : skip.Find(child);
                if (skip != null && d == null) diffs.Add($"DialogueView：缺节点 SkipButton/{child}");
                Transform p = Require(performance, "PerformanceView", reference, diffs);
                if (d == null || p == null) continue;
                string label = $"DialogueView SkipButton/{child} ↔ PerformanceView {reference}";
                SerializedObject dt = Serialized(d, TextType);
                SerializedObject pt = Serialized(p, TextType);
                if (dt == null || pt == null)
                {
                    diffs.Add($"{label}：一边缺 TMP 文本");
                    continue;
                }
                CheckFont(diffs, label, dt, pt);
                CheckFloat(diffs, label, dt, pt, "m_fontSize");
                CheckColor(diffs, label, dt, pt, "m_fontColor");
            }
            AssertNoDiffs(diffs);
        }

        [Test]
        public void Controls_SitAtSameScreenRect()
        {
            var diffs = new List<string>();
            GameObject canvas = CreateReferenceCanvas();
            try
            {
                GameObject d = Object.Instantiate(dialogue, canvas.transform, false);
                GameObject p = Object.Instantiate(performance, canvas.transform, false);
                ForceLayout(d);
                ForceLayout(p);
                var canvasRect = (RectTransform)canvas.transform;
                foreach ((string dialogueNode, string performanceNode) in ControlPairs)
                {
                    Transform dc = Require(d, "DialogueView", dialogueNode, diffs);
                    Transform pc = Require(p, "PerformanceView", performanceNode, diffs);
                    if (dc == null || pc == null) continue;
                    CompareScreenRect($"DialogueView {dialogueNode} ↔ PerformanceView {performanceNode}", canvasRect, dc, pc, diffs);
                }
                foreach (string control in ClonedControls)
                {
                    Transform dc = FindDeep(d.transform, control);
                    Transform pc = FindDeep(p.transform, control);
                    if (dc == null || pc == null) continue; // 缺节点上面已点名
                    foreach (string child in ControlChildren)
                    {
                        Transform dChild = RequireChild(dc, "DialogueView", control, child, diffs);
                        Transform pChild = RequireChild(pc, "PerformanceView", control, child, diffs);
                        if (dChild == null || pChild == null) continue;
                        CompareScreenRect($"{control}/{child}", canvasRect, dChild, pChild, diffs);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
            AssertNoDiffs(diffs);
        }

        // 记录面板开着时压在对白 / 演出面板之上（Top 层）：「关闭」若与下层控件重叠，玩家会分不清点的是哪个，视觉上也像盖住了「跳过」。
        [Test]
        public void TranscriptClose_DoesNotOverlapPanelControls()
        {
            var diffs = new List<string>();
            var transcript = AssetDatabase.LoadAssetAtPath<GameObject>(TranscriptPath);
            Assert.That(transcript, Is.Not.Null, "找不到预制体 " + TranscriptPath);
            GameObject canvas = CreateReferenceCanvas();
            try
            {
                GameObject d = Object.Instantiate(dialogue, canvas.transform, false);
                GameObject p = Object.Instantiate(performance, canvas.transform, false);
                GameObject t = Object.Instantiate(transcript, canvas.transform, false);
                ForceLayout(d);
                ForceLayout(p);
                ForceLayout(t);
                var canvasRect = (RectTransform)canvas.transform;
                Transform close = Require(t, "TranscriptView", "CloseButton", diffs);
                if (close != null)
                {
                    Rect closeRect = ScreenRect(canvasRect, close);
                    foreach (string node in DialogueControls)
                        CheckApart(closeRect, canvasRect, Require(d, "DialogueView", node, diffs), "DialogueView " + node, diffs);
                    foreach ((string _, string node) in ControlPairs)
                        CheckApart(closeRect, canvasRect, Require(p, "PerformanceView", node, diffs), "PerformanceView " + node, diffs);
                }
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
            Assert.That(diffs, Is.Empty, "记录面板「关闭」与对白 / 演出面板的控件重叠：\n" + string.Join("\n", diffs));
        }

        [Test]
        public void ControlLabels_HistoryAndAuto_MatchDialogueStyle()
        {
            var diffs = new List<string>();
            foreach (string control in ClonedControls)
            {
                Transform dc = Require(dialogue, "DialogueView", control, diffs);
                Transform pc = Require(performance, "PerformanceView", control, diffs);
                if (dc == null || pc == null) continue;
                foreach (string child in ControlChildren)
                {
                    Transform d = RequireChild(dc, "DialogueView", control, child, diffs);
                    Transform p = RequireChild(pc, "PerformanceView", control, child, diffs);
                    if (d == null || p == null) continue;
                    string label = $"DialogueView {control}/{child} ↔ PerformanceView {control}/{child}";
                    SerializedObject dt = Serialized(d, TextType);
                    SerializedObject pt = Serialized(p, TextType);
                    if (dt == null || pt == null)
                    {
                        diffs.Add($"{label}：一边缺 TMP 文本");
                        continue;
                    }
                    CheckFont(diffs, label, dt, pt);
                    CheckFloat(diffs, label, dt, pt, "m_fontSize");
                    CheckColor(diffs, label, dt, pt, "m_fontColor");
                    CheckInt(diffs, label, dt, pt, "m_HorizontalAlignment");
                    CheckInt(diffs, label, dt, pt, "m_VerticalAlignment");
                }
            }
            AssertNoDiffs(diffs);
        }

        // 世界空间画布、尺寸固定为参考分辨率：不受 Game 视图分辨率影响，两份面板根节点都铺满它。
        private static GameObject CreateReferenceCanvas()
        {
            var canvas = new GameObject("TalkPanelConsistencyTests_Canvas", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvas.transform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = ReferenceResolution;
            rect.position = Vector3.zero;
            rect.localScale = Vector3.one;
            return canvas;
        }

        // 布局组（对白的 Controls）只在布局重建时才给子节点排位：逐个强制重建，量到的才是运行时的真实位置。
        private static void ForceLayout(GameObject root)
        {
            Canvas.ForceUpdateCanvases();
            foreach (LayoutGroup group in root.GetComponentsInChildren<LayoutGroup>(true))
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
        }

        // 屏幕矩形：画布本地坐标平移到左下角为原点（参考分辨率像素）。
        private static Rect ScreenRect(RectTransform canvas, Transform node)
        {
            var corners = new Vector3[4];
            ((RectTransform)node).GetWorldCorners(corners);
            Vector3 min = canvas.InverseTransformPoint(corners[0]);
            Vector3 max = canvas.InverseTransformPoint(corners[2]);
            Vector2 offset = ReferenceResolution * 0.5f;
            return Rect.MinMaxRect(min.x + offset.x, min.y + offset.y, max.x + offset.x, max.y + offset.y);
        }

        private static void CompareScreenRect(string label, RectTransform canvas, Transform d, Transform p, List<string> diffs)
        {
            Rect dr = ScreenRect(canvas, d);
            Rect pr = ScreenRect(canvas, p);
            if (Mathf.Abs(dr.xMin - pr.xMin) > PixelTolerance || Mathf.Abs(dr.yMin - pr.yMin) > PixelTolerance ||
                Mathf.Abs(dr.xMax - pr.xMax) > PixelTolerance || Mathf.Abs(dr.yMax - pr.yMax) > PixelTolerance)
                diffs.Add($"{label} 屏幕矩形不同位（1920×1080，左下为原点）：DialogueView={Describe(dr)}，PerformanceView={Describe(pr)}");
        }

        // 两个屏幕矩形相交（贴边不算）就记一条。
        private static void CheckApart(Rect closeRect, RectTransform canvas, Transform control, string label, List<string> diffs)
        {
            if (control == null) return; // 缺节点 Require 已点名
            Rect controlRect = ScreenRect(canvas, control);
            if (closeRect.Overlaps(controlRect))
                diffs.Add($"TranscriptView CloseButton {Describe(closeRect)} 与 {label} {Describe(controlRect)} 相交（1920×1080，左下为原点）");
        }

        private static string Describe(Rect r) => $"x[{r.xMin:0.#},{r.xMax:0.#}] y[{r.yMin:0.#},{r.yMax:0.#}]";

        private static Transform RequireChild(Transform control, string prefabName, string controlName, string child, List<string> diffs)
        {
            Transform found = control.Find(child);
            if (found == null) diffs.Add($"{prefabName}：缺节点 {controlName}/{child}");
            return found;
        }

        private static Transform Require(GameObject root, string prefabName, string node, List<string> diffs)
        {
            Transform found = FindDeep(root.transform, node);
            if (found == null) diffs.Add($"{prefabName}：缺节点 {node}");
            return found;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform hit = FindDeep(parent.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static SerializedObject Serialized(Transform node, string typeName)
        {
            foreach (Component component in node.GetComponents<Component>())
                if (component != null && component.GetType().Name == typeName) return new SerializedObject(component);
            return null;
        }

        private static void CompareRect(string label, Transform d, Transform p, List<string> diffs)
        {
            var dr = (RectTransform)d;
            var pr = (RectTransform)p;
            label += " 的 RectTransform";
            CheckVector(diffs, label, "anchorMin", dr.anchorMin, pr.anchorMin);
            CheckVector(diffs, label, "anchorMax", dr.anchorMax, pr.anchorMax);
            CheckVector(diffs, label, "pivot", dr.pivot, pr.pivot);
            CheckVector(diffs, label, "anchoredPosition", dr.anchoredPosition, pr.anchoredPosition);
            CheckVector(diffs, label, "sizeDelta", dr.sizeDelta, pr.sizeDelta);
        }

        private static void CompareImage(string label, Transform d, Transform p, List<string> diffs)
        {
            SerializedObject di = Serialized(d, ImageType);
            SerializedObject pi = Serialized(p, ImageType);
            label += " 的 Image";
            if (di == null && pi == null) return;
            if (di == null || pi == null)
            {
                diffs.Add($"{label}：DialogueView {(di == null ? "没有" : "有")}、PerformanceView {(pi == null ? "没有" : "有")}");
                return;
            }
            Object ds = di.FindProperty("m_Sprite").objectReferenceValue;
            Object ps = pi.FindProperty("m_Sprite").objectReferenceValue;
            if (ds != ps) diffs.Add($"{label}.sprite：DialogueView={Name(ds)}，PerformanceView={Name(ps)}");
            CheckColor(diffs, label, di, pi, "m_Color");
            CheckInt(diffs, label, di, pi, "m_Type");
            CheckBool(diffs, label, di, pi, "m_PreserveAspect");
            CheckBool(diffs, label, di, pi, "m_Enabled");
        }

        private static void CompareText(string label, Transform d, Transform p, bool compareText, List<string> diffs)
        {
            SerializedObject dt = Serialized(d, TextType);
            SerializedObject pt = Serialized(p, TextType);
            label += " 的 TMP";
            if (dt == null && pt == null) return;
            if (dt == null || pt == null)
            {
                diffs.Add($"{label}：一边缺 TMP 文本");
                return;
            }
            CheckFont(diffs, label, dt, pt);
            CheckFloat(diffs, label, dt, pt, "m_fontSize");
            CheckColor(diffs, label, dt, pt, "m_fontColor");
            CheckInt(diffs, label, dt, pt, "m_fontStyle");
            CheckInt(diffs, label, dt, pt, "m_HorizontalAlignment");
            CheckInt(diffs, label, dt, pt, "m_VerticalAlignment");
            CheckFloat(diffs, label, dt, pt, "m_lineSpacing");
            CheckBool(diffs, label, dt, pt, "m_enableWordWrapping");
            if (!compareText) return;
            string ds = dt.FindProperty("m_text").stringValue;
            string ps = pt.FindProperty("m_text").stringValue;
            if (ds != ps) diffs.Add($"{label}.text：DialogueView=「{ds}」，PerformanceView=「{ps}」");
        }

        // 字体与材质按引用比：同一份字体资产、同一个材质子资产。
        private static void CheckFont(List<string> diffs, string label, SerializedObject d, SerializedObject p)
        {
            CheckReference(diffs, label, d, p, "m_fontAsset");
            CheckReference(diffs, label, d, p, "m_sharedMaterial");
        }

        private static void CheckReference(List<string> diffs, string label, SerializedObject d, SerializedObject p, string property)
        {
            Object dv = d.FindProperty(property).objectReferenceValue;
            Object pv = p.FindProperty(property).objectReferenceValue;
            if (dv != pv) diffs.Add($"{label}.{property}：DialogueView={Name(dv)}，PerformanceView={Name(pv)}");
        }

        private static void CheckFloat(List<string> diffs, string label, SerializedObject d, SerializedObject p, string property)
        {
            float dv = d.FindProperty(property).floatValue;
            float pv = p.FindProperty(property).floatValue;
            if (Mathf.Abs(dv - pv) > Tolerance) diffs.Add($"{label}.{property}：DialogueView={dv}，PerformanceView={pv}");
        }

        private static void CheckInt(List<string> diffs, string label, SerializedObject d, SerializedObject p, string property)
        {
            int dv = d.FindProperty(property).intValue;
            int pv = p.FindProperty(property).intValue;
            if (dv != pv) diffs.Add($"{label}.{property}：DialogueView={dv}，PerformanceView={pv}");
        }

        private static void CheckBool(List<string> diffs, string label, SerializedObject d, SerializedObject p, string property)
        {
            bool dv = d.FindProperty(property).boolValue;
            bool pv = p.FindProperty(property).boolValue;
            if (dv != pv) diffs.Add($"{label}.{property}：DialogueView={dv}，PerformanceView={pv}");
        }

        private static void CheckColor(List<string> diffs, string label, SerializedObject d, SerializedObject p, string property)
        {
            Color dv = d.FindProperty(property).colorValue;
            Color pv = p.FindProperty(property).colorValue;
            if (Mathf.Abs(dv.r - pv.r) > Tolerance || Mathf.Abs(dv.g - pv.g) > Tolerance ||
                Mathf.Abs(dv.b - pv.b) > Tolerance || Mathf.Abs(dv.a - pv.a) > Tolerance)
                diffs.Add($"{label}.{property}：DialogueView={dv}，PerformanceView={pv}");
        }

        private static void CheckVector(List<string> diffs, string label, string property, Vector2 d, Vector2 p)
        {
            if ((d - p).sqrMagnitude > Tolerance * Tolerance)
                diffs.Add($"{label}.{property}：DialogueView={d}，PerformanceView={p}");
        }

        private static string Name(Object asset) => asset == null ? "（空）" : asset.name;

        private static void AssertNoDiffs(List<string> diffs)
        {
            Assert.That(diffs, Is.Empty, "对白面板与演出面板样式不一致：\n" + string.Join("\n", diffs));
        }
    }
}

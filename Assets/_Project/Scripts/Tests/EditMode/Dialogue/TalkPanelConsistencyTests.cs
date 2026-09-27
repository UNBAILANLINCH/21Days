// 职责：守住「NPC 交互对白（DialogueView.prefab）与时间轴演出对白（PerformanceView.prefab）是同一套视觉」——
//   按节点名逐项比对两份预制体的布局（RectTransform）、底图（Image）与文字样式（TMP，含字体与材质引用），任何一边被单独改样式就红。
// 为什么新建：复用——既有 Dialogue 测试都守规则 / 策略，没有预制体样式的守卫；扩展——塞进某个规则测试类职责说不通，
//   这是跨两个模块预制体的视觉契约，所以单独成类。刻意读真实资产路径：它守的就是这两份预制体本身。
// Image / TMP 字段经 SerializedObject 按序列化名读（m_Color、m_fontSize……）：本文件不直接引用 TMP / uGUI 类型（程序集已引用 Unity.TextMeshPro，供别的测试用），
//   且这样比的正是落盘的值。
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class TalkPanelConsistencyTests
    {
        private const string DialoguePath = "Assets/_Project/Prefabs/UI/DialogueView.prefab";
        private const string PerformancePath = "Assets/_Project/Prefabs/UI/PerformanceView.prefab";
        private const string ImageType = "Image";
        private const string TextType = "TextMeshProUGUI";
        private const float Tolerance = 0.0001f;

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

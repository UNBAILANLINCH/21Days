// ShowcaseOverlay —— 回放时贴在 Game 视图顶部的信息条：模块名、第几步在做什么、最近一次检查点的绿/红结果、节奏倍率。
//
// 做什么：让开发者盯着 Game 视图就能同时看到「画面」和「现在演到哪一步、这一步判没判过」，
//         不用一边看画面一边翻 Console。字号 ≥ 20，录屏发给别人也看得清。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— UGUI / TextMeshPro 要预制体和 Canvas 资产，而回放要能在「代码搭场景」的空场景里也立刻可用，
//           多带一份预制体依赖等于给每个模块的验证场景加一道必须接线的前置。IMGUI 零资产、零接线。
//   扩展 —— 没有任何已有的运行时显示组件可扩展（Scripts/Runtime/ 目前是空的）。

using UnityEngine;

namespace Game.Tests.Showcase
{
    /// <summary>
    /// 回放信息条。字段全私有，由 ShowcaseScenario 通过 Show / Result 更新；
    /// 文案在这两个方法里一次拼好缓存，OnGUI 只负责画，不做字符串拼接、不查场景。
    /// </summary>
    public sealed class ShowcaseOverlay : MonoBehaviour
    {
        private const float BarHeight = 78f;
        private const float LineHeight = 34f;
        private const float SidePadding = 16f;
        private const float ScaleWidth = 190f;
        private const int FontSize = 22;

        private string module = string.Empty;
        private string stepLine = string.Empty;
        private string resultLine = string.Empty;
        private string scaleLine = string.Empty;
        private bool resultOk;

        private GUIStyle stepStyle;
        private GUIStyle passStyle;
        private GUIStyle failStyle;
        private GUIStyle scaleStyle;
        private Texture2D barTexture;

        /// <summary>
        /// 建一个跨场景存活的信息条。DontDestroyOnLoad 是必须的：回放中途可能 Additive / Single 换场景，
        /// 挂在场景里的对象会跟着被卸掉。
        /// </summary>
        public static ShowcaseOverlay Create(string module)
        {
            GameObject host = new GameObject("ShowcaseOverlay");
            Object.DontDestroyOnLoad(host);

            ShowcaseOverlay overlay = host.AddComponent<ShowcaseOverlay>();
            overlay.module = string.IsNullOrEmpty(module) ? "?" : module;
            overlay.stepLine = $"[{overlay.module}] 回放准备中";
            overlay.RefreshScaleLine();
            return overlay;
        }

        /// <summary>切到第 N 步：拼好整行缓存起来，同时刷新节奏倍率（开发者可能刚用菜单调过）。</summary>
        public void Show(int step, string title)
        {
            stepLine = $"[{module}] 第 {step} 步 · {title}";
            RefreshScaleLine();
        }

        /// <summary>更新最近一次检查点的结果：通过画绿色 ✓，失败画红色 ✗。</summary>
        public void Result(bool ok, string text)
        {
            resultOk = ok;
            resultLine = (ok ? "✓ " : "✗ ") + text;
        }

        private void OnGUI()
        {
            EnsureStyles();

            float width = Screen.width;
            GUI.DrawTexture(new Rect(0f, 0f, width, BarHeight), barTexture);

            float textWidth = Mathf.Max(120f, width - ScaleWidth - SidePadding * 2f);
            GUI.Label(new Rect(SidePadding, 6f, textWidth, LineHeight), stepLine, stepStyle);

            if (!string.IsNullOrEmpty(resultLine))
            {
                GUI.Label(
                    new Rect(SidePadding, 6f + LineHeight, textWidth, LineHeight),
                    resultLine,
                    resultOk ? passStyle : failStyle);
            }

            GUI.Label(new Rect(width - ScaleWidth - SidePadding, 6f, ScaleWidth, LineHeight), scaleLine, scaleStyle);
        }

        private void OnDestroy()
        {
            if (barTexture != null)
            {
                Object.Destroy(barTexture);
                barTexture = null;
            }
        }

        /// <summary>
        /// 样式和底条贴图只在第一次 OnGUI 时建一次。不能放 Awake：GUIStyle 依赖 GUI.skin，
        /// 而 GUI.skin 只在 GUI 事件里有效，Awake 里读到的是空的。
        /// </summary>
        private void EnsureStyles()
        {
            if (barTexture != null)
            {
                return;
            }

            barTexture = new Texture2D(1, 1);
            barTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            barTexture.Apply();

            stepStyle = MakeStyle(new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleLeft);
            passStyle = MakeStyle(new Color(0.35f, 0.95f, 0.45f), TextAnchor.MiddleLeft);
            failStyle = MakeStyle(new Color(1f, 0.38f, 0.35f), TextAnchor.MiddleLeft);
            scaleStyle = MakeStyle(new Color(0.75f, 0.85f, 1f), TextAnchor.MiddleRight);
        }

        private static GUIStyle MakeStyle(Color color, TextAnchor alignment)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = FontSize;
            style.fontStyle = FontStyle.Bold;
            style.alignment = alignment;
            style.clipping = TextClipping.Clip;
            style.normal.textColor = color;
            return style;
        }

        private void RefreshScaleLine()
        {
            scaleLine = $"回放 x{ShowcaseOptions.HoldScale:0.##}";
        }
    }
}

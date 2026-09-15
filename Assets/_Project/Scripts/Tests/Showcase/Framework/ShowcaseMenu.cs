// ShowcaseMenu —— 编辑器菜单 21Days/验证/…：调回放节奏、打开最近的报告目录。
//
// 做什么：把「回放太慢看得烦 / 太快看不清」变成两下点击的事，不用改代码也不用重进 Play；
//         节奏值写进 EditorPrefs，ShowcaseOptions.HoldScale 每次读都现取，所以改完立刻生效。
//         Claude 走 MCP execute_menu_item 调这些菜单项，比 execute_code 安全（不注入任意代码）。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— 没有现成的设置界面可用；为四个数值做 SettingsProvider / EditorWindow 属于杀鸡用牛刀。
//   扩展 —— 已有的编辑器脚本只有 BuildScript.cs（21Days/打包/…），职责是打包，
//           把验证菜单塞进去名字和职责都说不通；菜单风格与它保持一致即可。

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.Showcase
{
    /// <summary>
    /// 回放相关的编辑器菜单。菜单路径与 BuildScript 的 21Days/打包/… 同风格，全中文。
    /// </summary>
    public static class ShowcaseMenu
    {
        private const string MenuRoot = "21Days/验证/";
        private const string SpeedRoot = MenuRoot + "回放节奏/";

        [MenuItem(SpeedRoot + "慢速 (x2)")]
        public static void SetSlow()
        {
            SetHoldScale(2f);
        }

        [MenuItem(SpeedRoot + "标准 (x1)")]
        public static void SetNormal()
        {
            SetHoldScale(1f);
        }

        [MenuItem(SpeedRoot + "快速 (x0.25)")]
        public static void SetFast()
        {
            SetHoldScale(0.25f);
        }

        [MenuItem(SpeedRoot + "不停顿 (x0)")]
        public static void SetNoHold()
        {
            SetHoldScale(0f);
        }

        /// <summary>
        /// 打开报告根目录。目录不存在（还没跑过任何回放）就先建出来，
        /// 免得 RevealInFinder 拿到一个不存在的路径、什么都不弹。
        /// </summary>
        [MenuItem(MenuRoot + "打开最近报告目录")]
        public static void OpenReportFolder()
        {
            string root = ShowcaseOptions.ReportRoot;
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
                Debug.Log($"{ShowcaseOptions.Prefix} 报告目录还是空的（{ShowcaseOptions.ReportRootRelative}），"
                          + "跑一次 /verify-module 之后这里就会有 report.md 和截图。");
            }

            EditorUtility.RevealInFinder(root);
        }

        private static void SetHoldScale(float scale)
        {
            EditorPrefs.SetFloat(ShowcaseOptions.HoldScaleKey, scale);
            Debug.Log($"{ShowcaseOptions.Prefix} 回放节奏已设为 x{scale}"
                      + (scale <= 0f ? "（不停顿，适合当回归跑，肉眼看不清表现）" : "，下一步回放立刻生效。"));
        }
    }
}
#endif

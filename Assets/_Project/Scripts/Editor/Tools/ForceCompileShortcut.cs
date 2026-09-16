// 职责：主动触发一次脚本编译——工具栏左区的「编译」按钮、菜单与 Alt+Shift+C。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：BuildScript 是出包（走 BuildPipeline，跟编译不是一回事），别的工具都不碰编译。
//   2. 扩展不行：塞进 MainSceneShortcut 名实不符，那是切场景的。
//
// 解决的问题：在外部编辑器改完 .cs 切回 Unity，靠窗口聚焦触发的自动刷新偶尔不生效
//（资产数据库没注意到磁盘变化），结果跑的还是旧代码。这里显式走一遍
// AssetDatabase.Refresh() 加 CompilationPipeline.RequestScriptCompilation()，不靠聚焦那一下。

using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Editor
{
    /// <summary>
    /// 主动编译。三个入口效果一样：工具栏「编译」按钮、菜单 21Days/工程/立即编译、快捷键 Alt+Shift+C。
    /// </summary>
    public static class ForceCompileShortcut
    {
        [MenuItem("21Days/工程/立即编译 &#c", false, 220)]
        public static void RequestCompile()
        {
            if (EditorApplication.isCompiling)
            {
                Debug.Log("已经在编译了，不用重复点。");
                return;
            }

            if (EditorApplication.isUpdating)
            {
                Debug.Log("资产还在导入，等它跑完再点。");
                return;
            }

            if (EditorApplication.isPlaying)
            {
                // 播放模式下 Unity 不编译脚本，请求了也是白请求，说清楚而不是假装成功。
                Debug.LogWarning("播放模式下 Unity 不编译脚本。先退出 Play 再点编译。");
                return;
            }

            // 先 Refresh 再请求编译：漏编译通常是资产数据库还没看到磁盘上改过的文件，
            // 那种情况下光 RequestScriptCompilation 编的还是旧内容。
            AssetDatabase.Refresh();
            CompilationPipeline.RequestScriptCompilation();
            Debug.Log("已请求重新编译。看右下角的进度圈，或等控制台报错。");
        }

        /// <summary>编译中、导入中、播放中都点不动。按钮据此灰显。</summary>
        public static bool CanCompile
        {
            get
            {
                return !EditorApplication.isCompiling
                       && !EditorApplication.isUpdating
                       && !EditorApplication.isPlaying;
            }
        }
    }

    /// <summary>
    /// 工具栏左区的「编译」按钮。注入机制见 <see cref="MainToolbarInjector"/>。
    /// <para>
    /// 灰显状态靠 <see cref="EditorApplication.update"/> 轮询：编译开始与结束没有一个公开事件能盖全
    /// （CompilationPipeline 那两个事件覆盖不到资产导入和播放模式）。每帧就是几个 bool 比较，
    /// 只有值变了才动 UI。
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class ForceCompileToolbarButton
    {
        private static EditorToolbarButton button;
        private static bool lastEnabled = true;

        static ForceCompileToolbarButton()
        {
            MainToolbarInjector.Register(MainToolbarInjector.Zone.Left, Create);
            EditorApplication.update += UpdateEnabled;
        }

        private static VisualElement Create()
        {
            button = MainToolbarInjector.CreateTextButton(
                "编译",
                "立即重新编译脚本（Alt+Shift+C）。"
                + "在外部编辑器改完切回来、靠聚焦没触发编译时用它。播放模式下不可用。",
                ForceCompileShortcut.RequestCompile);
            lastEnabled = ForceCompileShortcut.CanCompile;
            button.SetEnabled(lastEnabled);
            return button;
        }

        private static void UpdateEnabled()
        {
            if (button == null)
            {
                return;
            }

            bool canCompile = ForceCompileShortcut.CanCompile;
            if (canCompile == lastEnabled)
            {
                return;
            }

            lastEnabled = canCompile;
            button.SetEnabled(canCompile);
        }
    }
}

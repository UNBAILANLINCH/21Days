// 职责：一键切到主场景——主工具栏左区的一个按钮，外加菜单项与快捷键 Alt+B。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：现有编辑器工具中 BuildScript 是打包、GenerateTablesMenu 是跑配置表生成、
//      ProjectStructureMenu 是建模块目录、AssetAuditWindow 是扫资产，没有一个管「切场景」。
//   2. 扩展不行：塞进上面任何一个都名实不符——这是编辑器导航，不是它们的职责。
//
// 只做「切过去」这一件事，不改 Unity 的播放行为（不碰 playModeStartScene）：
// 切过去之后按不按 Play、什么时候按，都还是你自己说了算。
// 切到别的场景在 Project 窗口里双击就行，不在这里做列表。

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Editor
{
    /// <summary>
    /// 回到主场景的快捷入口。解决的问题：玩法场景是由流程 Additive 加载的，单独打开它按 Play
    /// 什么都起不来——没有启动入口就没有服务、DI 和状态机，所以每次验证都得先切回主场景。
    /// <para>三个入口效果一样：工具栏「主场景」按钮、菜单 21Days/场景/打开主场景、快捷键 Alt+B。</para>
    /// <para>
    /// <b>主场景是哪个，取自 Build Settings 第一个启用的场景</b>——那就是 Unity 定义的启动场景，
    /// 打出包来也是从它起。所以主场景改名、换文件、换成别的场景，只要在 Build Settings 里调，
    /// 这里不用改任何代码。
    /// </para>
    /// </summary>
    public static class MainSceneShortcut
    {
        [MenuItem("21Days/场景/打开主场景 &b", false, 300)]
        public static void OpenMainScene()
        {
            string path = MainScenePath;
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("Build Settings 里没有启用的场景，不知道该切到哪。"
                                 + "去 File > Build Settings 把入口场景加进去并放在第一位。");
                return;
            }

            OpenScene(path);
        }

        /// <summary>
        /// 切到指定场景。已经在那个场景就什么都不做——重新 Open 一次只会白白丢掉未保存的改动。
        /// </summary>
        public static void OpenScene(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (EditorSceneManager.GetActiveScene().path == path)
            {
                Debug.Log("当前已经是「" + Path.GetFileNameWithoutExtension(path) + "」，不用切。");
                return;
            }

            // 当前场景有未保存改动时先问存不存；用户点取消就中止，绝不闷头丢改动。
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        /// <summary>
        /// 主场景的资产路径：Build Settings 里第一个**启用**的场景。
        /// 跳过没打勾的，因为打包时它们也不参与。一个都没有时返回空串。
        /// </summary>
        public static string MainScenePath
        {
            get
            {
                EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
                for (int i = 0; i < scenes.Length; i++)
                {
                    if (scenes[i].enabled)
                    {
                        return scenes[i].path;
                    }
                }

                return string.Empty;
            }
        }

        /// <summary>当前打开的是不是主场景。按路径判，不按场景名——同名场景可以有多个。</summary>
        public static bool IsMainSceneOpen
        {
            get
            {
                string path = MainScenePath;
                return !string.IsNullOrEmpty(path) && EditorSceneManager.GetActiveScene().path == path;
            }
        }
    }

    /// <summary>工具栏左区的「主场景」按钮。注入机制见 <see cref="MainToolbarInjector"/>。</summary>
    [InitializeOnLoad]
    internal static class MainSceneToolbarButton
    {
        static MainSceneToolbarButton()
        {
            MainToolbarInjector.Register(MainToolbarInjector.Zone.Left, Create);
        }

        private static VisualElement Create()
        {
            return MainToolbarInjector.CreateTextButton(
                "主场景",
                "打开主场景（Alt+B）——Build Settings 第一个启用的场景。"
                + "玩法场景单独 Play 起不来，验证都要回主场景播放。",
                MainSceneShortcut.OpenMainScene);
        }
    }
}

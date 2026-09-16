// 职责：不退出播放模式，把游戏流程重跑一遍——工具栏 Play 按钮旁的「刷新」按钮、菜单与 Alt+R。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：没有任何现成工具碰过运行时的容器与状态机。
//   2. 扩展不行：塞进 MainSceneShortcut 名实不符——那个管编辑期切场景，这个管运行期重跑流程，
//      一个在没播放时用、一个只在播放时用。
//
// 能做到什么、做不到什么（实测结论，别指望更多）：
//   能：当前状态 ExitAsync（玩法场景被卸载、UI 关闭）→ 回到 TitleState，等于「回标题重开」。
//   不能：重新初始化框架服务。IGameService.InitializeAsync 的契约没要求幂等，也没有任何服务做了
//     重复初始化保护——UIService 会再建一套 Canvas 和第二个 EventSystem（UI 会直接失灵）、
//     TelemetryService 会再写一次 session_start、AudioService 会再建一套声部。所以不碰它们。
//   不能：重新读配置表。IConfigService 只暴露只读的 Tables，没有 Reload 入口。
//   不能：重新编译代码。改了 .cs 必须退出播放模式让 Unity 重编译，这是 Unity 的硬限制。

using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Flow;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Game.Editor
{
    /// <summary>
    /// 运行期重跑游戏流程。三个入口效果一样：工具栏 Play 旁的「刷新」按钮、
    /// 菜单 21Days/运行/刷新游戏流程、快捷键 Alt+R。
    /// </summary>
    public static class GameFlowRestartShortcut
    {
        [MenuItem("21Days/运行/刷新游戏流程 &r", false, 320)]
        public static void RestartFlow()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("没在播放模式，没有游戏流程可刷新。先按 Play。");
                return;
            }

            // 容器挂在 GameBootstrap 物体上，那个物体是 DontDestroyOnLoad 的，播放期间一直找得到。
            GameLifetimeScope scope = Object.FindObjectOfType<GameLifetimeScope>();
            if (scope == null || scope.Container == null)
            {
                Debug.LogWarning("找不到已构建的 GameLifetimeScope——启动可能还没跑完，或者当前场景不是主场景。");
                return;
            }

            IGameFlow flow;
            if (!scope.Container.TryResolve(out flow))
            {
                Debug.LogWarning("容器里没有 IGameFlow，无法刷新流程。");
                return;
            }

            // 不 await：调用方是编辑器按钮的同步回调，等在这里没意义。
            // 切换过程中的异常由 GameFlow 自己记日志。
            flow.GoToAsync<TitleState>().Forget();
            Debug.Log("已请求回到 TitleState：当前状态会先 ExitAsync（玩法场景随之卸载），再重开标题。"
                      + "框架服务不重启，配置表不重读。");
        }

        /// <summary>播放中才有流程可刷。按钮据此灰显。</summary>
        public static bool CanRestart
        {
            get { return EditorApplication.isPlaying; }
        }
    }

    /// <summary>
    /// 工具栏 PlayMode 区（Play / Pause / Step 那一组）里的「刷新」按钮。
    /// 注入机制见 <see cref="MainToolbarInjector"/>；不在播放模式时按钮灰显。
    /// </summary>
    [InitializeOnLoad]
    internal static class GameFlowRestartToolbarButton
    {
        /// <summary>紧跟在 Play / Pause / Step 那一组后面。</summary>
        private const int PlayModeZoneIndex = 1;

        private static EditorToolbarButton button;

        static GameFlowRestartToolbarButton()
        {
            // 钉死在 PlayModeButtons（Play/Pause/Step）后面第一个位置。
            // 不能和速度条一样用「末尾」：两个都用末尾的话，先后完全取决于 [InitializeOnLoad]
            // 静态构造的执行顺序，而那个顺序 Unity 不保证——实测出现过速度条插到刷新前面。
            MainToolbarInjector.Register(MainToolbarInjector.Zone.PlayMode, Create, PlayModeZoneIndex);

            // 进出播放模式时刷新灰显。延一帧再读：状态切换的那一刻 isPlaying 还没稳定。
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            EditorApplication.delayCall += UpdateEnabled;
        }

        private static VisualElement Create()
        {
            button = MainToolbarInjector.CreateIconButton(
                "Refresh",
                "刷新",
                "不退出播放模式，把游戏流程重跑一遍（Alt+R）："
                + "当前状态退出、玩法场景卸载，回到标题。框架服务不重启，配置表不重读，代码不重编译。",
                GameFlowRestartShortcut.RestartFlow);
            UpdateEnabled();
            return button;
        }

        private static void UpdateEnabled()
        {
            if (button == null)
            {
                return;
            }

            button.SetEnabled(GameFlowRestartShortcut.CanRestart);
        }
    }
}

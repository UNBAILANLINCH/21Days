// 职责：退出游戏的唯一实现——记一条日志，先跑完退出前钩子（总超时兜底），再在编辑器里退出 Play 模式、出包后 Application.Quit()。
// 为什么新建（按「复用 → 扩展 → 新增」的顺序）：
//   1. 复用不行：原实现是 PauseMenuController.HandleQuit，私有方法，外部调不到；
//   2. 扩展不行：把它改成 public 让 TitleState 调，等于让「标题」这个状态依赖「暂停菜单控制器」，
//      依赖方向不对（标题在暂停菜单之前，也不该认识暂停会话）；退出和暂停菜单本来就是两件事。
//   所以抽成 Boot/ 下一个静态小类，暂停菜单与标题界面共用，将来别处的「退出」也调它。
// 存档会话（PRP save-session D4）要在退出前做最后一次保存，所以加了退出前钩子：执行器是纯类 GameQuitHooks（可测），
//   这里只持有它的一个静态实例并转发；调用方的 Quit(reason) 写法不变。

using System;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using UnityEngine;

namespace Game.Core.Boot
{
    /// <summary>
    /// 退出游戏。调用方只管说「为什么退」，平台差异（编辑器 / 出包）在这里处理。
    /// <para>触屏为主的平台（手机）一般不给退出按钮，是否显示由界面按 <c>IPlatformService.IsTouchPrimary</c> 决定，不在这里判断。</para>
    /// <para>
    /// 退出前钩子：模块用 <see cref="RegisterBeforeQuit"/> 登记异步收尾（如最后一次存档），<see cref="Quit"/> 会先按登记顺序
    /// await 全部钩子（总共最多等 <see cref="BeforeQuitTimeoutSeconds"/> 秒）再真正退出。
    /// 只覆盖走 <see cref="Quit"/> 的退出；直接关窗口 / 系统杀进程不经过这里。
    /// </para>
    /// </summary>
    public static class GameQuit
    {
        /// <summary>退出前钩子的总等待上限（秒）。超了就不再等，直接退出——卡住的钩子不能让玩家退不出游戏。</summary>
        public const float BeforeQuitTimeoutSeconds = 2f;

        private static GameQuitHooks hooks = new GameQuitHooks();
        private static bool quitting;

        /// <summary>
        /// 登记一个退出前钩子，返回的句柄 Dispose 即注销（登记方在自己 Dispose 时成对调用）。
        /// 钩子抛异常只记 Error，不影响别的钩子，也不影响退出。
        /// </summary>
        public static IDisposable RegisterBeforeQuit(Func<UniTask> hook) => hooks.Register(hook);

        /// <summary>退出游戏。先跑退出前钩子再退；重复调用（连点退出）只执行第一次。</summary>
        /// <param name="reason">谁发起的退出（如「暂停菜单」「标题界面」），只用于日志。</param>
        public static void Quit(string reason)
        {
            if (quitting)
            {
                Log.Info($"退出已在进行中，忽略重复请求（来源：{reason}）");
                return;
            }

            quitting = true;
            QuitAsync(reason).Forget();
        }

        private static async UniTaskVoid QuitAsync(string reason)
        {
            Log.Info($"退出游戏（来源：{reason}）");
            try
            {
                await hooks.RunAllAsync(TimeSpan.FromSeconds(BeforeQuitTimeoutSeconds));
            }
            catch (Exception e)
            {
                // RunAllAsync 自己不抛；这里兜底是为了「无论如何都要退出去」。
                Log.Error($"退出前钩子执行异常，照常退出：{e}");
            }

#if UNITY_EDITOR
            // 仅调试用途：编辑器里 Application.Quit 不生效，改为退出 Play 模式。
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 进 Play / 域重载时清空静态状态：关了域重载（Enter Play Mode Options）时静态字段会跨次保留，
        /// 上一次 Play 登记的钩子（指向已销毁的服务）和「正在退出」标志都得丢掉。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            hooks = new GameQuitHooks();
            quitting = false;
        }
    }
}

// 职责：两个平台实现的共用部分——存档根目录的拼法与启动时建目录。
// 为什么新建：Standalone 与 Android 两个实现里 SaveRoot 与 InitializeAsync 完全一样，
// 各写一遍就是两份会走样的真相；抽成基类比复制粘贴更经得起后面加平台。

using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using UnityEngine;

namespace Game.Core.Platform
{
    /// <summary>
    /// 平台服务基类。子类只需回答「我是什么平台、是不是触屏为主、怎么震动」。
    /// 同时实现 IGameService：启动串行初始化时确保存档目录存在（波 2 的 ISaveService 直接用）。
    /// </summary>
    public abstract class PlatformServiceBase : IPlatformService, IGameService
    {
        private const string SaveFolderName = "saves";

        private string saveRoot;

        /// <summary>
        /// 存档根目录覆盖，只给编辑器内的测试 / 回放（Showcase）用，正式包不设。
        /// 非空时 <see cref="SaveRoot"/> 每次都会现取这个值而不是拼 persistentDataPath——
        /// 从「开始」进场景的回放会往真实存档目录写 slot1..N.json，几条用例跑下来就把玩家的
        /// 真实存档槽写满（ai-docs/pitfalls.md「从『开始』进场景的回放把玩家真实存档写满了」）。
        /// Showcase 在加载 Boot 场景之前（<see cref="Tests.Showcase.ShowcaseScenario.ShowcaseSetUp"/>）
        /// 就把它设到一个临时目录，此时 PlatformServiceFactory.Create() 还没跑，容器建出的实例
        /// 第一次读 SaveRoot 时覆盖已经生效，不存在「构造时缓存了旧值」的问题。
        /// </summary>
        public static string SaveRootOverride { get; set; }

        public abstract PlatformKind Kind { get; }

        public abstract bool IsTouchPrimary { get; }

        public string SaveRoot => SaveRootOverride ?? (saveRoot ??= Path.Combine(Application.persistentDataPath, SaveFolderName));

        public abstract void Vibrate(VibrationKind kind);

        public UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(SaveRoot))
            {
                Directory.CreateDirectory(SaveRoot);
            }

            return UniTask.CompletedTask;
        }
    }
}

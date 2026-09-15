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

        public abstract PlatformKind Kind { get; }

        public abstract bool IsTouchPrimary { get; }

        public string SaveRoot => saveRoot ??= Path.Combine(Application.persistentDataPath, SaveFolderName);

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

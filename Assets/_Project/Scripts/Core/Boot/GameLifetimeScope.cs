// 职责：根作用域——框架层全部服务、事件与内置状态的唯一注册处。
// 为什么新建：architecture.md 第 6 节「规避」第 1 条明确要替掉「单例 + 多处手写调用清单」，
// 集中注册必须有一个入口类；VContainer 的 LifetimeScope 只提供基类，注册内容得自己写。
//
// 注意：VContainer 的编辑器脚本模板处理器会在 Unity 首次为「文件名以 LifetimeScope.cs 结尾」的
// 新脚本生成 .meta 时，用空模板覆盖文件内容。本文件已经存在，之后再改不会再被覆盖；
// 但如果把它删掉重建，记得重建后再写一次内容（见 developer-guide.md 第 15 章）。

using System;
using Game.Core.Assets;
using Game.Core.Audio;
using Game.Core.Config;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Platform;
using Game.Core.Save;
using Game.Core.Timing;
using Game.Core.UI;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Core.Boot
{
    /// <summary>
    /// 根作用域，挂在 Boot 场景的 GameBootstrap 物体上。
    /// **注册顺序即启动初始化顺序**（GameBootstrap 按 IReadOnlyList&lt;IGameService&gt; 的顺序串行 await），
    /// 顺序按 architecture.md 5.1：Platform → Log → Assets → Config → Save → Input → Audio → UI。
    /// 玩法模块不改这个文件：继承 <see cref="GameplayInstaller"/> 把组件挂到同一个物体上，
    /// 由 <c>InstallGameplay</c> 统一接进来（不要建子作用域，理由见那个方法的注释）。
    /// </summary>
    public sealed class GameLifetimeScope : LifetimeScope
    {
        [Header("框架配置资产")]
        [Tooltip("UI 配置：参考分辨率、缩放匹配、过渡时长。资产在 Assets/_Project/Data/UI/UIConfig.asset。")]
        [SerializeField] private UIConfig uiConfig;

        [Tooltip("音频配置：Mixer（可空）、SFX 声部数、BGM 默认淡入淡出。资产在 Assets/_Project/Data/Audio/AudioConfig.asset。")]
        [SerializeField] private AudioConfig audioConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            // --- 事件总线：全局事件在根作用域注册 ---
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<BootCompletedEvent>(options);
            builder.RegisterMessageBroker<GameStateChangedEvent>(options);
            builder.RegisterMessageBroker<TitleStartClickedEvent>(options);

            // --- 服务（注册顺序 = 初始化顺序）---
            // Platform 第一个：存档目录、触屏判定这些后面都要用
            builder.RegisterInstance(PlatformServiceFactory.Create())
                .As<IPlatformService, IGameService>();

            // Log 是静态门面，不进容器
            RegisterConfigs(builder);

            // Assets 排在 Config 前面：ConfigService 的 InitializeAsync 要靠 IAssetService 按标签取表数据，
            // 而 Addressables 必须先 InitializeAsync 过才能加载。
            builder.Register<AddressablesAssetService>(Lifetime.Singleton)
                .As<IAssetService, IGameService>();

            builder.Register<ConfigService>(Lifetime.Singleton)
                .As<IConfigService, IGameService>();

            builder.Register<JsonSaveService>(Lifetime.Singleton)
                .As<ISaveService, IGameService>();

            builder.Register<LocalClock>(Lifetime.Singleton).As<IClock>();

            // ITickable 要靠 EntryPoint 才会被每帧驱动，所以用 RegisterEntryPoint 而不是 Register
            builder.RegisterEntryPoint<TimerService>(Lifetime.Singleton).AsSelf();

            builder.Register<InputService>(Lifetime.Singleton)
                .As<IInputService, IGameService>();

            // Audio 排在 UI 前面：按 5.1 的顺序，而且 UI 面板一打开就可能要播音效。
            builder.Register<AudioService>(Lifetime.Singleton)
                .As<IAudioService, IGameService>();

            // UI 最后：它的 InitializeAsync 要拿 IInputService.Actions 去接 EventSystem，
            // Input 必须已经初始化完。
            builder.Register<UIService>(Lifetime.Singleton)
                .As<IUIService, IGameService>();

            // --- 状态流与内置状态 ---
            builder.Register<GameFlow>(Lifetime.Singleton).As<IGameFlow>();
            builder.Register<BootState>(Lifetime.Singleton);
            builder.Register<TitleState>(Lifetime.Singleton);

            // --- 玩法层（Core 不认识玩法，玩法自己挂组件上来）---
            InstallGameplay(builder);
        }

        /// <summary>
        /// 调用挂在同一个物体上的全部 <see cref="GameplayInstaller"/>，让玩法模块把自己的状态、
        /// 规则类与入口点注册进**根作用域**。
        /// <para>
        /// 为什么必须进根作用域而不是玩法场景的子作用域：<see cref="Flow.GameFlow"/> 是从根
        /// <c>IObjectResolver</c> 解析状态类型的，而且玩家还在标题界面时玩法场景根本没加载，
        /// 子作用域还不存在——<c>GoToAsync&lt;玩法状态&gt;()</c> 会解析失败。
        /// </para>
        /// <para>没挂任何注册器是合法状态（纯框架也要能跑起来），只记一条日志。</para>
        /// </summary>
        private void InstallGameplay(IContainerBuilder builder)
        {
            GameplayInstaller[] installers = GetComponents<GameplayInstaller>();
            if (installers.Length == 0)
            {
                Log.Info("没有玩法注册器：只有框架层在跑。玩法模块要接入就继承 GameplayInstaller，"
                         + "把组件挂到 Boot 场景的 GameBootstrap 物体上。");
                return;
            }

            for (int i = 0; i < installers.Length; i++)
            {
                // 一个注册器抛异常会让整个容器建不成——连 Platform / Assets / Config 这些已经注册过的
                // 框架服务一起作废，最后只在 GameBootstrap 里报一句笼统的「启动失败」，看不出是谁的锅。
                // 所以这里点名记错，再把异常抛回去：容器确实没法半残着用，但至少知道该去改哪个文件。
                try
                {
                    installers[i].Install(builder);
                }
                catch (Exception e)
                {
                    Log.Error($"玩法注册器 {installers[i].GetType().Name} 注册失败，容器无法构建：{e}", this);
                    throw;
                }

                Log.Info($"玩法注册器 {installers[i].GetType().Name} 已装载");
            }
        }

        /// <summary>
        /// 注册两个配置资产。Inspector 上忘了拖时**不让启动直接崩**：
        /// 记一条 Error 指明该拖哪个字段，再塞一份代码建的默认配置顶上——
        /// 崩在容器构建阶段的话，报错只会说「解析 UIConfig 失败」，看不出是资产没拖。
        /// </summary>
        private void RegisterConfigs(IContainerBuilder builder)
        {
            UIConfig ui = uiConfig;
            if (ui == null)
            {
                Log.Error("GameLifetimeScope 的 UI Config 字段没赋值，已用默认值顶上。"
                          + "把 Assets/_Project/Data/UI/UIConfig.asset 拖到 Boot 场景的 GameBootstrap 物体上。", this);
                ui = ScriptableObject.CreateInstance<UIConfig>();
            }

            AudioConfig audio = audioConfig;
            if (audio == null)
            {
                Log.Error("GameLifetimeScope 的 Audio Config 字段没赋值，已用默认值顶上。"
                          + "把 Assets/_Project/Data/Audio/AudioConfig.asset 拖到 Boot 场景的 GameBootstrap 物体上。", this);
                audio = ScriptableObject.CreateInstance<AudioConfig>();
            }

            builder.RegisterInstance(ui);
            builder.RegisterInstance(audio);
        }
    }
}

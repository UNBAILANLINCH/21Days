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
using Game.Core.Telemetry;
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
    /// 顺序按 architecture.md 5.1，埋点插在最前面：Platform → Telemetry → Assets → Config → Save → Input → Audio → UI。
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

        [Tooltip("埋点配置：总开关、最低级别、模块过滤、采样与限流。资产在 Assets/_Project/Data/Telemetry/TelemetryConfig.asset。")]
        [SerializeField] private TelemetryConfig telemetryConfig;

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

            // Telemetry 紧跟在 Platform 之后、Assets 之前：注册顺序就是初始化顺序，
            // 而它后面的每一个服务（Assets / Config / Save / Input / Audio / UI）初始化时都要埋点。
            // 它要是排在后面，最该被记录的那一段——启动期——反而一条都留不下。
            RegisterTelemetry(builder);

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

        /// <summary>
        /// 注册埋点层：取值快照 → 时钟 → 输出终点 → 服务 → 性能采样器。
        /// <para>
        /// 配置资产没拖时的处理同 <see cref="RegisterConfigs"/>：记一条 Error 指明该拖哪个字段，
        /// 再用代码建的默认资产顶上。默认值只写在 TelemetryConfig 的字段初始值里一处，这里不再抄一遍。
        /// </para>
        /// <para>
        /// 服务注册的是具体类而不是现成实例：这样容器才会在作用域销毁时替我们调 <c>Dispose</c>，
        /// 把日志桥和 <c>Application.quitting</c> 的订阅摘干净（RegisterInstance 进来的对象容器不负责销毁）。
        /// </para>
        /// </summary>
        private void RegisterTelemetry(IContainerBuilder builder)
        {
            TelemetryConfig config = telemetryConfig;
            if (config == null)
            {
                Log.Error("GameLifetimeScope 的 Telemetry Config 字段没赋值，已用默认值顶上。"
                          + "把 Assets/_Project/Data/Telemetry/TelemetryConfig.asset 拖到 Boot 场景的 GameBootstrap 物体上。", this);
                config = ScriptableObject.CreateInstance<TelemetryConfig>();
            }

            builder.RegisterInstance(config.ToOptions());
            builder.RegisterInstance(new UnityTelemetryClock()).As<ITelemetryClock>();

            // 注册成数组而不是单个 ITelemetrySink：服务把**同一次格式化的结果**分发给列表里的每一个终点。
            // 正式路径这里只有 Unity 日志一个；编辑器镜像由服务在 InitializeAsync 里自己追加——
            // 那份文件名要用会话 sid，而 sid 是服务构造出来的，组合根这会儿还拿不到。
            builder.RegisterInstance(new ITelemetrySink[] { new UnityDebugTelemetrySink() });

            builder.Register<TelemetryService>(Lifetime.Singleton)
                .As<ITelemetryService, IGameService>();

            // 采样器要每帧跑，同 TimerService 用 RegisterEntryPoint（只 Register 的话没人驱动 ITickable）
            builder.RegisterEntryPoint<PerformanceSampler>(Lifetime.Singleton);
        }
    }
}

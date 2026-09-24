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
using Game.Core.Replay;
using Game.Core.Save;
using Game.Core.Simulation;
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

        [Tooltip("确定性内核配置：逻辑步长 tickRate、单帧追帧上限、主随机种子。"
                 + "资产在 Assets/_Project/Data/Simulation/SimulationConfig.asset；留空则用代码里的默认值跑。")]
        [SerializeField] private SimulationConfig simulationConfig;

        [Tooltip("回放配置：录制总开关、常驻缓冲秒数、状态哈希与快照间隔、自动保存与调试热键、重放静音。"
                 + "资产在 Assets/_Project/Data/Replay/ReplayConfig.asset；留空则用代码里的默认值跑。")]
        [SerializeField] private ReplayConfig replayConfig;

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

            // 确定性内核第一段：配置、随机源、逻辑时钟。这三项不依赖任何其他服务，紧挨着 IClock 注册，
            // 是为了让「渲染帧时间」和「逻辑 tick」两种时间在这个文件里也挨在一起，一眼看得出是两回事。
            RegisterSimulationCore(builder);

            // ITickable 要靠 EntryPoint 才会被每帧驱动，所以用 RegisterEntryPoint 而不是 Register
            builder.RegisterEntryPoint<TimerService>(Lifetime.Singleton).AsSelf();

            builder.Register<InputService>(Lifetime.Singleton)
                .As<IInputService, IGameService>();

            // 确定性内核第二段：输入源与推进器。位置是硬约束，必须在 InputService 之后，理由见方法注释。
            RegisterSimulationDriver(builder);

            // 世界暂停：timeScale + 逻辑 tick 的唯一持有者，要拿推进器所以排在第二段之后。
            // 实现了 IDisposable，VContainer 在作用域销毁时会调 Dispose，释放全部持有者并恢复 timeScale。
            builder.Register<WorldPauseService>(Lifetime.Singleton).As<IWorldPauseService>();

            // 回放系统：录制器、录制接线件、播放器。必须排在第二段之后——它们都要拿推进器与输入切换壳。
            RegisterReplay(builder);

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

        /// <summary>
        /// 确定性内核第一段：配置资产、随机源、逻辑时钟。这三项都不依赖别的服务。
        /// <para>
        /// 配置资产没拖时**不报 Error**（这一点和 <see cref="RegisterConfigs"/> / <see cref="RegisterTelemetry"/>
        /// 不同）：SimulationConfig.asset 是可选的，纯框架、纯测试作用域本来就该能在默认值下跑起来，
        /// 拿 Error 吓人反而让真正的错淹没在噪声里。默认值只写在 <see cref="SimulationConfig"/> 的字段
        /// 初始值里一处（tickRate 60 / 单帧最多补 5 tick / 种子 0），这里不抄第二遍。
        /// </para>
        /// </summary>
        private void RegisterSimulationCore(IContainerBuilder builder)
        {
            SimulationConfig config = simulationConfig;

            // SimulationConfig 是 ScriptableObject（UnityEngine.Object），判空只能用 ==：
            // Unity 重载了它来识别「已销毁但引用还在」的伪空对象，?. / is null 会绕过这个重载。
            if (config == null)
            {
                Log.Info("GameLifetimeScope 的 Simulation Config 字段没赋值，本次运行用默认确定性内核配置"
                         + "（tickRate 60 / 单帧最多补 5 tick / 种子 0 即每局随机）。"
                         + "要固定这几个数就建 Assets/_Project/Data/Simulation/SimulationConfig.asset 拖到本物体上。");
                config = ScriptableObject.CreateInstance<SimulationConfig>();
            }

            builder.RegisterInstance(config);

            // 种子按约定 0 表示「这局随机来」：用 RandomService 自带的 Guid 哈希取一次性种子。
            // 不用 UnityEngine.Random——那正是确定性内核明令禁掉的东西，而且它带全局状态，
            // 取一次种子就把别处用它的表现随机一起搅了。
            ulong masterSeed = config.MasterSeed != 0UL ? config.MasterSeed : RandomService.CreateSeedFromGuid();

            // 顺带注册成具体类型（RegisterInstance 的默认行为）：录制头要写 RandomService.MasterSeed，
            // 而 IRandomService 上没有这个属性。
            builder.RegisterInstance(new RandomService(masterSeed)).As<IRandomService>();

            // ILogicClock 绑到 SimulationRunner 内部那一个时钟上，不另 new 一个 LogicClock：
            // 推进权（LogicClock.Advance）只在推进器手里，另建一份谁也不会去推，注入方读到的 Tick
            // 会永远停在 0——不报错、不崩，只表现为「逻辑时间不走」，是这套系统里最难查的一类错。
            // 这里引用的 SimulationRunner 要到第二段才注册，但 VContainer 的解析是惰性的：
            // 注册顺序管的是 IGameService 的初始化次序与 EntryPoint 的驱动次序，不是依赖可见性。
            builder.Register<ILogicClock>(resolver => resolver.Resolve<SimulationRunner>().Clock, Lifetime.Singleton);
        }

        /// <summary>
        /// 确定性内核第二段：实时输入源、输入切换壳、推进器。
        /// <para>
        /// <b>必须注册在 <see cref="InputService"/> 之后</b>（注册顺序 = 初始化顺序）：
        /// <see cref="LiveInputSource"/> 初始化时要拿 <c>IInputService.Actions</c> 去 <c>FindAction</c>
        /// 缓存动作引用，而 <c>Actions</c> 在 <c>InputService.InitializeAsync</c> 跑完之前是 null。
        /// 排在前面只会缓存到一片空，整局采不到输入，而且不抛异常——画面照常跑，只是按键全不响应。
        /// </para>
        /// </summary>
        private static void RegisterSimulationDriver(IContainerBuilder builder)
        {
            // 注册成 IGameService 参与启动串行（顺序的理由见上）；同时 AsSelf，
            // 好让下面的切换壳按具体类型取到它——IInputSource 这个键要留给切换壳。
            builder.Register<LiveInputSource>(Lifetime.Singleton)
                .AsSelf()
                .As<IGameService>();

            // 玩法注入到的 IInputSource 永远是这个切换壳：回放开关只换它内部的指向，玩法手里的引用不变。
            // 用工厂显式把实时源传进去，不靠自动注入——切换壳自己就是 IInputSource，
            // 自动注入会把构造参数解析成它自己，成环。
            // 同时 AsSelf（同 LiveInputSource 那条）：回放播放器要调 SwitchToReplay / SwitchToLive，
            // 拿的必须是具体类型。**必须写在这一条上**，不能另起一条把具体类型映射过来——
            // VContainer 在建注册表时会为带 As<>() 的注册在「实现类型」这个键上占一个空位，
            // 再来一条同实现类型的注册会直接撞键，整个容器建不起来（实测过）。
            builder.Register<InputSourceSwitch>(
                    resolver => new InputSourceSwitch(resolver.Resolve<LiveInputSource>()),
                    Lifetime.Singleton)
                .AsSelf()
                .As<IInputSource>();

            // 推进器是 ITickable，只 Register 的话没人驱动它，同 TimerService 用 RegisterEntryPoint。
            builder.RegisterEntryPoint<SimulationRunner>(Lifetime.Singleton).AsSelf();
        }

        /// <summary>
        /// 回放系统：配置资产 → 录制器 → 录制接线件 → 播放器 → 世界状态注册表（并接到录制器与播放器上）。
        /// <para>
        /// 配置资产没拖时**不报 Error**（同 <see cref="RegisterSimulationCore"/>）：
        /// ReplayConfig.asset 是可选的，纯框架、纯测试作用域本来就该在默认值下跑得起来。
        /// 默认值只写在 <see cref="ReplayConfig"/> 的字段初始值里一处，这里不抄第二遍。
        /// </para>
        /// <para>
        /// 录制器与播放器都实现了 <c>ITickable</c>（一个轮询调试热键、一个按播放状态驱动推进），
        /// 所以用 <c>RegisterEntryPoint</c>——只 <c>Register</c> 的话没人每帧驱动它们。
        /// 录制器同时是 <c>IGameService</c>（启动时接上日志回调做崩溃自动保存），
        /// <c>RegisterEntryPoint</c> 会按已实现接口一并注册，所以它也会进启动串行队列。
        /// </para>
        /// </summary>
        private void RegisterReplay(IContainerBuilder builder)
        {
            ReplayConfig config = replayConfig;

            // ReplayConfig 是 ScriptableObject（UnityEngine.Object），判空只能用 ==。
            if (config == null)
            {
                Log.Info("GameLifetimeScope 的 Replay Config 字段没赋值，本次运行用默认回放配置"
                         + "（编辑器与 Development 包开录、常驻 5 分钟、每秒一次状态哈希、每 10 秒一个快照）。"
                         + "要调这几个数就建 Assets/_Project/Data/Replay/ReplayConfig.asset 拖到本物体上。");
                config = ScriptableObject.CreateInstance<ReplayConfig>();
            }

            builder.RegisterInstance(config);

            // 播放器要调 SwitchToReplay / SwitchToLive，拿的是 InputSourceSwitch 的具体类型——
            // 那个键由第二段的注册用 .AsSelf() 一并开出来（理由写在那一行上）。
            builder.RegisterEntryPoint<ReplayRecorder>(Lifetime.Singleton).AsSelf();

            // 接线件：录制器不订阅推进器（依赖方向只许 Replay → Simulation），改由它以一个步骤的身份
            // 每 tick 喂一条记录。它只在启动时做一次事，不用每帧驱动，所以不是 EntryPoint。
            builder.Register<ReplayRecordDriver>(Lifetime.Singleton)
                .AsSelf()
                .As<IGameService>();

            builder.RegisterEntryPoint<ReplayPlayer>(Lifetime.Singleton).AsSelf();

            // 世界状态注册表：玩法模块注入 IReplayStateProvider 把自己的 IReplayState 挂上来。
            // 玩法层要的是接口（.As<IReplayStateProvider>()），下面的接线要的是具体类型（.AsSelf()）。
            builder.Register<ReplayStateRegistry>(Lifetime.Singleton)
                .AsSelf()
                .As<IReplayStateProvider>();

            // 把注册表接到录制器与播放器上。**不接这一步，整套回放就只剩输入流**——
            // 状态哈希、完整快照、漂移检测、快照续跑全部静默空转，而且一条错都不会报。
            // 为什么用 RegisterBuildCallback 而不是构造注入：两者现有的入口就是 AttachStateProvider
            // （EditMode 测试与 Showcase 直接 new 出来再挂，构造参数里没有这一项），
            // 顺着现有形状接，测试与 Showcase 不用改一行。回调在容器建好之后跑，Resolve 是安全的。
            builder.RegisterBuildCallback(resolver =>
            {
                ReplayStateRegistry states = resolver.Resolve<ReplayStateRegistry>();
                resolver.Resolve<ReplayRecorder>().AttachStateProvider(states);
                resolver.Resolve<ReplayPlayer>().AttachStateProvider(states);
            });
        }
    }
}

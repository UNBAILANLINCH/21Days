// 职责：把对白模块的配置、规则、内容目录、控制器、条件来源、服务、场景绑定与交互焦点入口点注册进根作用域。挂在 Boot 场景的 GameBootstrap 物体上。
// 为什么新建：Game.Core 不许引用 Game.Runtime，玩法类型只能由玩法自己经 GameplayInstaller 缝注册；
//   其他模块的 Installer 只服务各自模块，把对白塞进去会让模块互相耦合。
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Performance;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Dialogue
{
    /// <summary>
    /// 对白模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，把 DialogueConfig 资产拖到 Config 字段。
    /// <para>
    /// 只 Register 不 Resolve。<see cref="DialogueController"/> 不在注册时取角色表：<see cref="DialogueCatalog"/>
    /// 惰性依赖 IConfigService 初始化完成，而 Controller 会随入口点 DialogueSceneBinder 在容器构建期被解析，
    /// 用「首次解析时取」的工厂也躲不开这个时序，所以 Controller 持有 Catalog 引用、首次展示时才建角色索引。
    /// </para>
    /// </summary>
    public sealed class DialogueInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "dialogue";

        [Tooltip("对白表现参数（打字速度、倍速挡位、三连点、自动播放、历史上限）。")]
        [SerializeField] private DialogueConfig config;

        public override void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(ResolveConfig());
            // 已读记录单例：启动时由 DialogueReadStore 从独立档案 "dialogue-read" 原地填充（同一实例，不换对象），
            // 对白结束后由它合并写出；不进槽位存档，读旧槽位不会让已读倒退。
            builder.Register<DialogueReadData>(Lifetime.Singleton);
            builder.Register<DialogueRules>(resolver => new DialogueRules(
                    resolver.Resolve<DialogueReadData>(),
                    resolver.Resolve<DialogueConfig>().HistoryLimit,
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            builder.Register<DialogueCatalog>(Lifetime.Singleton);
            builder.Register<DialogueController>(resolver => new DialogueController(
                    resolver.Resolve<DialogueRules>(),
                    resolver.Resolve<DialogueCatalog>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IAssetService>(),
                    resolver.Resolve<IClock>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule),
                    // 演出服务可缺席（Boot 没挂 PerformanceInstaller）：取不到传 null，节点插播记 Warn 后跳过。
                    resolver.TryResolve(out IPerformanceService performance) ? performance : null),
                Lifetime.Singleton);
            builder.Register<IDialogueConditionSource, DefaultDialogueConditionSource>(Lifetime.Singleton);
            builder.Register<DialogueService>(resolver => new DialogueService(
                    resolver.Resolve<DialogueCatalog>(),
                    resolver.Resolve<DialogueRules>(),
                    resolver.Resolve<DialogueController>(),
                    resolver.Resolve<DialogueConfig>(),
                    resolver.Resolve<IDialogueConditionSource>(),
                    resolver.Resolve<IWorldPauseService>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            // 已读档案存取：排在 DialogueService 之后。AsSelf + As<IGameService> 同一条注册：参与启动串行读档，
            // 又能按具体类型解析（验收要从真实容器取到它）；工厂注册是因为它有一条测试用的第二构造。实现 IDisposable，容器托管释放。
            builder.Register(resolver => new DialogueReadStore(
                    resolver.Resolve<DialogueReadData>(),
                    resolver.Resolve<ISaveService>(),
                    resolver.Resolve<DialogueService>()), Lifetime.Singleton)
                .AsSelf()
                .As<IGameService>();
            // AsSelf：焦点系统要按具体类型注入 Binder（RegisterEntryPoint 默认只注册接口）。
            builder.RegisterEntryPoint<DialogueSceneBinder>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint(resolver => new DialogueInteractionFocus(
                    resolver.Resolve<DialogueSceneBinder>(),
                    resolver.Resolve<DialogueService>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IHudVisibility>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton).AsSelf();
            // 对白键盘 / 手柄路径：每帧读 Dialogue 动作图，翻成与点击同一套处理（Dialogue 图由 DialogueService 开关）。
            builder.RegisterEntryPoint(resolver => new DialogueKeyboardInput(
                    resolver.Resolve<DialogueController>(),
                    resolver.Resolve<IInputService>()), Lifetime.Singleton);
        }

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 SampleInstaller）。
        private DialogueConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Log.Error("DialogueInstaller 的 Config 字段没赋值，已用默认值顶上。"
                      + "把 DialogueConfig 资产拖到 Boot 场景 GameBootstrap 物体的 DialogueInstaller 上。", this);
            return ScriptableObject.CreateInstance<DialogueConfig>();
        }
    }
}

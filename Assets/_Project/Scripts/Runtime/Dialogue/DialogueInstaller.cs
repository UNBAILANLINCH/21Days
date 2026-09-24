// 职责：把对白模块的配置、规则、内容目录、控制器、条件来源、服务、场景绑定与交互焦点入口点注册进根作用域。挂在 Boot 场景的 GameBootstrap 物体上。
// 为什么新建：Game.Core 不许引用 Game.Runtime，玩法类型只能由玩法自己经 GameplayInstaller 缝注册；
//   其他模块的 Installer 只服务各自模块，把对白塞进去会让模块互相耦合。
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
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
            // 本期已读记录只在内存里，重启清空；接存档时换成从存档读出的实例。
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
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            builder.Register<IDialogueConditionSource, DefaultDialogueConditionSource>(Lifetime.Singleton);
            builder.Register<DialogueService>(resolver => new DialogueService(
                    resolver.Resolve<DialogueCatalog>(),
                    resolver.Resolve<DialogueRules>(),
                    resolver.Resolve<DialogueController>(),
                    resolver.Resolve<DialogueConfig>(),
                    resolver.Resolve<IDialogueConditionSource>(),
                    resolver.Resolve<IWorldPauseService>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            // AsSelf：焦点系统要按具体类型注入 Binder（RegisterEntryPoint 默认只注册接口）。
            builder.RegisterEntryPoint<DialogueSceneBinder>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint(resolver => new DialogueInteractionFocus(
                    resolver.Resolve<DialogueSceneBinder>(),
                    resolver.Resolve<DialogueService>(),
                    resolver.Resolve<IUIService>(),
                    resolver.Resolve<IInputService>(),
                    resolver.Resolve<ISubscriber<BootCompletedEvent>>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton).AsSelf();
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

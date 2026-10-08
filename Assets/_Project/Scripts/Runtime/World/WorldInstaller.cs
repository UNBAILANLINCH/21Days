// 职责：把世界模块（A4 多场景流转 / A6 相机边界）接进根作用域——世界表只读查询（WorldCatalog）、
//   待处理转场（IWorldTransition）、数据驱动的场景状态（WorldSceneState）、出生点放置策略与相机约束策略。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Core 不许引用 Game.Runtime（asmdef 依赖方向），玩法类型只能经 GameplayInstaller 这道缝注册；
//      此前 Runtime/World/ 的 14 个文件一个 Installer 都没有，WorldCatalog / WorldRules 全是死代码（PRP §1.2 第 3 条）。
//   2. 扩展不行：塞进 MonsterInstaller 会让怪物注册器变成场景流转的注册处（它自己还要接身份 / 潜行消费方）；
//      本波还**不能**改 Boot.unity，所以也不能靠改 GameLifetimeScope 来注册。
//   3. 所以照 MonsterInstaller / IdentityInstaller 的形状新建一个注册器。
//
// ⚠️ **待挂载（本波刻意不挂）**：本组件**还没有**加到 Boot 场景的 GameBootstrap 物体上——
//    Boot.unity 目前被别的一波独占（并行会话正在改它，见 PRP world-scenes §1.3 与 S 组落地总规划第 2 节）。
//    挂载步骤（精确到字段，接线波照做即可）：
//      ① 打开 Assets/_Project/Scenes/Boot.unity，选中挂着 GameLifetimeScope 的 GameBootstrap 物体；
//      ② Add Component → 搜「WorldInstaller」（本类没有 AddComponentMenu，按类型名搜）；
//      ③ 该组件**没有任何必填字段**（世界表走 IConfigService，转场与策略都是纯 C# 对象，不需要拖资产）；
//      ④ 建议把它排在组件列表末尾（与 IdentityInstaller / StealthInstaller 一致）——本类不注册 IGameService，
//         位置不影响启动串行；排末尾只是让「谁注册了什么」在 Inspector 上的顺序和新增历史一致。
//
// 注册顺序（GameLifetimeScope 的类注释：**注册顺序 = IGameService 的初始化顺序**）：
//   本类注册的五个类型**没有一个是 IGameService**（都是纯查询 / 纯状态 / 纯策略，没有 InitializeAsync），
//   所以插在组件列表哪个位置都不改变启动串行，对既有 14 个注册器的相对次序零影响。
//   本类的 Install **不做任何 Resolve**：它跑在容器构建期，那时容器还不存在（同 GameplayInstaller 的类注释）。
//
// 谁用什么（本波的实际接线状态）：
//   - WorldCatalog：供 WorldTransition（校验目标场景）、WorldSceneState（选出生点）、编辑器工具读。
//   - IWorldTransition：写方是接线后的传送点触发方（本波还没有调用方），读方是 WorldSceneState。
//   - WorldSceneState：由 GameFlow.GoToAsync<WorldSceneState>() 解析（本波还没有调用方，PRP §2.1 的流程图那一步）。
//   - ISpawnPlacement：默认实现是 UnwiredSpawnPlacement（**一定返回 false 并说清缺什么**）——
//     真正的摆人要等场景波把锚点摆进场景后替换这一行注册（换实现即可，状态与测试一行不改）。
//   - CameraConstraintPolicy：WorldSceneState 在场景就绪时把它推进场景相机（SmoothCameraFollow.BindConstraintPolicy）。
//   - SceneStateScope 刻意**不注册**：它按场景键构造、随场景进出换绑，注册成单例等于把「当前在哪张图」钉死；
//     消费方从 WorldSceneState.Scope 取（对应 PRP §1.2 第 3 条「SceneStateScope 没注册进容器」的缺口）。
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.IsometricExploration;
using Game.Player;
using VContainer;
using VContainer.Unity;

namespace Game.World
{
    /// <summary>
    /// 世界模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上（本波刻意没挂，见文件头注释）。
    /// 无必填字段，只 Register 不 Resolve。
    /// </summary>
    public sealed class WorldInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "world";

        public override void Install(IContainerBuilder builder)
        {
            // 世界表只读查询（惰性读表）：构造只要 IConfigService，容器里有，按类型自动注入即可。
            builder.Register<WorldCatalog>(Lifetime.Singleton);

            // 世界场景登记器：扫已加载场景里的出生点锚点 / 传送点 / 相机。
            // **必须用 RegisterEntryPoint**：扫描发生在 IStartable.Start（容器启动、IGameService 都初始化完之后），
            // 用普通 Register 的话 Start 根本不会被调用——表现为「锚点全在场景里，却一个都登记不到」，
            // 而且不报任何错（WorldSceneBinder 会一直认为没有世界场景）。
            builder.RegisterEntryPoint<WorldSceneBinder>(Lifetime.Singleton).AsSelf();

            // 待处理转场：工厂注册（构造要 ITelemetryScope，容器里只有 ITelemetryService，按类型解析不到）。
            // AsSelf 让调试与将来的接线能按具体类型拿到它（OverwrittenCount 这类现场只在具体类型上）。
            builder.Register(resolver => new WorldTransition(
                    resolver.Resolve<WorldCatalog>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton)
                .As<IWorldTransition>()
                .AsSelf();

            // 出生点放置策略：**接线波已换成真实现**（机制波那行 UnwiredSpawnPlacement 是「未接线」的占位，
            // 它一定返回 false 并说清缺什么）。换的只有这一行注册，WorldSceneState 与它的测试一行没改。
            // 构造期只收一个 PlayerRules 工厂、不当场解析：装配 WorldInstaller 不该依赖 Player 模块先注册好
            // （WorldInstallerTests 只装框架服务），而真正摆放时玩家模块必然已经在容器里。
            builder.Register(resolver => new WorldSpawnPlacement(
                    resolver.Resolve<WorldSceneBinder>(),
                    () => resolver.Resolve<PlayerRules>()), Lifetime.Singleton)
                .As<ISpawnPlacement>();

            // 相机约束策略：纯 C# 对象（不是 MonoBehaviour、不是 ScriptableObject），单例共享给
            // WorldSceneState（推向场景相机）与将来的构图切换（A6 的另一半）。
            builder.Register<CameraConstraintPolicy>(Lifetime.Singleton);

            // 场景状态：工厂注册（构造要 ITelemetryScope；且基类要 IAssetService）。
            // 它由 GameFlow 按类型解析，注册成本身即可（Register<T>(factory) 默认按实现类型可用）。
            builder.Register(resolver => new WorldSceneState(
                    resolver.Resolve<IAssetService>(),
                    resolver.Resolve<IWorldTransition>(),
                    resolver.Resolve<WorldCatalog>(),
                    resolver.Resolve<ISpawnPlacement>(),
                    resolver.Resolve<ISaveService>(),
                    resolver.Resolve<CameraConstraintPolicy>(),
                    resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);

            // 世界场景驱动（接线波新增）：ITickable 跑进入范围型传送点 / 按键型传送点的统一交互登记 / 投影，ISimulationStep 按输入推玩家，
            // 按键型传送点的交互键归 Game.Interaction.InteractionFocus（PRP/interaction D9），本驱动要 IInteractionRegistry（InteractionInstaller 注册）；
            // IStartable 把自己挂进确定性内核的步骤表（见 WorldSceneDriver.Start 的注释）。
            // 它注册在最后：本类前面几项都是纯查询 / 纯状态，谁先谁后不影响启动串行（它不注册任何 IGameService）。
            builder.RegisterEntryPoint<WorldSceneDriver>(Lifetime.Singleton).AsSelf();
        }
    }
}

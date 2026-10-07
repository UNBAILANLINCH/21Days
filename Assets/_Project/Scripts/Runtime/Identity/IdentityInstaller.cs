// 职责：把身份内核（S1「换皮与附身」/ S2「身份暴露与怀疑」）接进根作用域——
//   数值块（IdentitySettings）、身份定义表（IdentityCatalog）、状态（IdentityState）、
//   账簿（IdentityLedger）、怀疑度（SuspicionState）与规则（IdentityRules）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Core 不许引用 Game.Runtime（asmdef 依赖方向），玩法类型只能经
//      GameplayInstaller 这道缝自己注册。
//   2. 扩展不行：MonsterInstaller / NarrativeInstaller 只服务各自模块，把身份的注册塞进去
//      会让它们变成别人的注册处；而这两个文件本波还要各自接消费方，混在一起改冲突面更大。
// 注册顺序（GameLifetimeScope 的类注释：**注册顺序 = IGameService 的初始化顺序**）：
//   本类注册的六个类型**没有一个是 IGameService**（纯状态与纯规则，没有 InitializeAsync），
//   所以插在组件列表哪个位置都不改变启动串行；挂在 GameBootstrap 的组件列表末尾即可，
//   对既有 11 个注册器的相对次序零影响（Identity 不注册任何 IGameService）。
//   本类的 Install **不做任何 Resolve**：它跑在容器构建期，那时容器还不存在（同 GameplayInstaller 的类注释）。
// 谁绑谁（本波的关键决定，依据 ai-docs/docs/modules/identity/identity-module-guide.md 第 41 行
//   「本模块不依赖 Monster / Player / Narrative / Dialogue，反向由那些模块调本模块的公开接口」）：
//   **消费方自己拉**——MonsterInstaller 在它的构建回调里把 IdentityState 绑给 EncounterStep，
//   NarrativeInstaller 在它的构建回调里把四个服务绑给 NarrativeConditionSource。
//   本模块**不反向引用 Monster / Narrative**，因此不可能形成「Identity → Monster → Identity」这类环。
//   两侧都用 TryResolve：没有 IdentityInstaller 的独立原型场景 / 独立测试作用域照旧能建容器，
//   只是那两层不生效——这正是接线前「不接线时行为逐字不变」的边界。
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Telemetry;
using UnityEngine;
using VContainer;

namespace Game.Identity
{
    /// <summary>
    /// 身份模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，
    /// 把 <c>Data/Identity/IdentityConfig.asset</c> 拖到 Config 字段。只 Register 不 Resolve。
    /// </summary>
    public sealed class IdentityInstaller : GameplayInstaller
    {
        private const string TelemetryModule = "identity";

        [Tooltip("身份数值与定义表（时限、冷却、账簿上限与口径、怀疑度上限与回落、露馅惩罚、档位阈值）。"
                 + "拖 Data/Identity/IdentityConfig.asset。")]
        [SerializeField] private IdentityConfig config;

        public override void Install(IContainerBuilder builder)
        {
            IdentityConfig value = ResolveConfig();
            IdentitySettings settings = value.Settings;
            IdentityCatalog catalog = value.CreateCatalog();

            builder.RegisterInstance(settings);
            builder.RegisterInstance(catalog);
            builder.Register<IdentityState>(Lifetime.Singleton);
            builder.Register<IdentityLedger>(Lifetime.Singleton);
            // 怀疑度与规则都要 ITelemetryScope（跨档、借还、露馅各记一条，见各自的类注释），
            // 容器里只有 ITelemetryService，所以用工厂注册（同 LootInstaller 的写法）。
            builder.Register(resolver => new SuspicionState(
                    settings, resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)), Lifetime.Singleton);
            // 露馅后果策略本波**不注入**：死亡 vs 追逐是 `00_功能总览.md` §5 C1 的原文矛盾（§8.1 #3），
            // 不注入时 ResolveExposure 恒给 ExposureOutcome.None——判定照跑、后果不硬编，
            // 这正是 IdentityRules 的契约（见它的类注释「不硬编的两处」）。策略接线归 S2 后果那一波。
            builder.Register(resolver => new IdentityRules(
                    settings, catalog, null, resolver.Resolve<ITelemetryService>().Scope(TelemetryModule)),
                Lifetime.Singleton);
        }

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 MonsterInstaller）。
        // 默认资产的定义表为空是**合法状态**（IdentityCatalog 的空表：任何身份都借不到，TryEnter 返回
        // UnknownIdentity），所以这里只点名、不假装成功：空表时日志里看得到「借不到身份」的原因。
        private IdentityConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Log.Error("IdentityInstaller 的 Config 字段没赋值，已用默认值顶上（身份定义表为空：任何身份都借不到）。"
                      + "把 Data/Identity/IdentityConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 IdentityInstaller 上。", this);
            return ScriptableObject.CreateInstance<IdentityConfig>();
        }
    }
}

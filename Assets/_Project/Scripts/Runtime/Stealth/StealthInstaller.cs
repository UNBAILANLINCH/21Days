// 职责：把感知与潜行内核（S3「潜行与暗杀」/ S4「追逐」）接进根作用域——配置资产与装配好的 StealthKernel。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Core 不许引用 Game.Runtime（asmdef 依赖方向）；而本内核此前**一个调用方都没有**
//      （EncounterStep.UseStealth 没有调用点），策划改 StealthConfig.asset 根本不生效——本类就是那个调用点的来源。
//   2. 扩展不行：StealthKernel 的文件头建议「由 MonsterInstaller 注册一个单例」，但配置资产字段挂在注册器上，
//      塞进 MonsterInstaller 等于让怪物模块持有潜行配置；两条事（配置 + 内核装配）都是潜行自己的，所以单独一个注册器。
//      **不动 StealthKernel 的构造签名**（StealthKernel(StealthConfig)，见 StealthKernel.cs:31）。
// 注册顺序（GameLifetimeScope 的类注释：**注册顺序 = IGameService 的初始化顺序**）：
//   本类只注册两个**非 IGameService** 的类型（配置实例与内核），不参与启动串行，因此挂在组件列表末尾
//   不影响任何既有初始化次序。真正的装配发生在消费方的构建回调里（MonsterInstaller 用
//   TryResolve<StealthKernel> 取内核再调 step.UseStealth(...)），而构建回调在**全部注册器都跑完之后**
//   才执行，所以两个组件谁在前谁在后都不影响结果。
//   本类**不做任何 Resolve、不碰 EncounterStep**：那样会让 Stealth 反向依赖 Monster，方向反了
//   （ai-docs/docs/modules/stealth/stealth-module-guide.md 第 45 行：本模块不依赖 Monster，反向由那些模块调本模块）。
using Game.Core.Boot;
using Game.Core.Logging;
using UnityEngine;
using VContainer;

namespace Game.Stealth
{
    /// <summary>
    /// 潜行模块注册器。**接线要求**：挂到 Boot 场景 <c>GameBootstrap</c> 物体上，
    /// 把 <c>Data/Stealth/StealthConfig.asset</c> 拖到 Config 字段。只 Register 不 Resolve。
    /// </summary>
    public sealed class StealthInstaller : GameplayInstaller
    {
        [Tooltip("潜行 / 暗杀 / 击倒 / 追逐 / 召唤的全部数值（28 个字段）。拖 Data/Stealth/StealthConfig.asset。")]
        [SerializeField] private StealthConfig config;

        public override void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(ResolveConfig());
            builder.Register(resolver =>
            {
                var kernel = new StealthKernel(resolver.Resolve<StealthConfig>());
                // 配置非法就在装配处炸（StealthKernel 的契约：配置错误不许静默跑）。典型是 chaseSpeed
                // 不高于玩家步行——那正是 `04_追逐.md:165` 记的现存缺陷，不许它静默地再跑一版；
                // 在这里炸比在 EncounterStep.UseStealth 里炸更早、且不依赖 Monster 是否挂上。
                string issue = kernel.Validate();
                if (issue != null)
                {
                    throw new System.InvalidOperationException(
                        "潜行配置非法（Data/Stealth/StealthConfig.asset）：" + issue);
                }

                return kernel;
            }, Lifetime.Singleton);
        }

        // 忘了拖配置时不让启动直接崩：记 Error 指明该拖哪个字段，再用代码建的默认值顶上（同 MonsterInstaller）。
        private StealthConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null。
            if (config != null) return config;
            Log.Error("StealthInstaller 的 Config 字段没赋值，已用代码默认值顶上（追兵速度 3.6 等占位值）。"
                      + "把 Data/Stealth/StealthConfig.asset 拖到 Boot 场景 GameBootstrap 物体的 StealthInstaller 上。", this);
            return ScriptableObject.CreateInstance<StealthConfig>();
        }
    }
}

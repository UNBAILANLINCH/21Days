// 职责：把示例模块的配置、规则类、状态与路由注册进根作用域。挂在 Boot 场景的 GameBootstrap 物体上。
// 为什么新建：Game.Core 不许引用 Game.Runtime，所以玩法类型只能由玩法自己注册进容器。
//   1. 复用不行：GameLifetimeScope 是 Core 的类，往里写 builder.Register<SampleState>() 会成环。
//   2. 扩展不行：放进玩法场景的子作用域（原计划的 SampleLifetimeScope）跑不通——
//      GameFlow 从**根** IObjectResolver 解析状态类型，而且玩家还在标题界面时 Sample 场景根本没加载，
//      子作用域还不存在，GoToAsync<SampleState>() 必然解析失败。
//      所以走 Core 提供的 GameplayInstaller 缝，注册进根作用域。

using Game.Core.Boot;
using Game.Core.Logging;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Sample
{
    /// <summary>
    /// 示例模块的注册器。**接线要求**：把这个组件挂到 <c>Assets/_Project/Scenes/Boot.unity</c> 里
    /// 的 <c>GameBootstrap</c> 物体上（和 <c>GameLifetimeScope</c> 同一个物体），
    /// 并把 <c>Assets/_Project/Data/Sample/SampleConfig.asset</c> 拖到 Config 字段。
    /// <para>
    /// 现状（2026-09-26）：Boot 未挂本组件、场景地址未登记，本模块只作样板；见 sample-module-guide「当前接线状态」。
    /// </para>
    /// <para>
    /// 新玩法模块照抄这个文件即可：换个类名、换成自己的配置资产、注册自己的类型。
    /// 一个模块一个 Installer，互不干扰。
    /// </para>
    /// <para>
    /// <see cref="Install"/> 在容器**构建期间**被调用，里面只能 Register，不能 Resolve、
    /// 不能碰其它服务。要在启动时做事就注册入口点（本模块的 <see cref="SampleTitleRouter"/>）。
    /// </para>
    /// </summary>
    public sealed class SampleInstaller : GameplayInstaller
    {
        [Tooltip("示例模块的数值配置。资产在 Assets/_Project/Data/Sample/SampleConfig.asset。")]
        [SerializeField] private SampleConfig config;

        public override void Install(IContainerBuilder builder)
        {
            builder.RegisterInstance(ResolveConfig());
            builder.Register<SampleRules>(Lifetime.Singleton);

            // 状态用 AsSelf 注册（GoToAsync<SampleState>() 按具体类型解析，不是按接口）。
            builder.Register<SampleState>(Lifetime.Singleton);

            // 入口点：容器建好后 VContainer 会调它的 Start()，作用域销毁时调 Dispose()。
            builder.RegisterEntryPoint<SampleTitleRouter>(Lifetime.Singleton);
        }

        /// <summary>
        /// Inspector 上忘了拖配置时**不让启动直接崩**：记一条 Error 指明该拖哪个字段，
        /// 再用一份代码建的默认配置顶上。崩在容器构建阶段的话，报错只会说「解析 SampleConfig 失败」，
        /// 看不出是资产没拖（和 GameLifetimeScope 处理 UIConfig / AudioConfig 的做法一致）。
        /// </summary>
        private SampleConfig ResolveConfig()
        {
            // ScriptableObject 是 UnityEngine.Object，判空只用 == null（Unity 重载了 ==）。
            if (config != null)
            {
                return config;
            }

            Log.Error("SampleInstaller 的 Config 字段没赋值，已用默认值顶上。"
                      + "把 Assets/_Project/Data/Sample/SampleConfig.asset 拖到 Boot 场景 GameBootstrap 物体的"
                      + " SampleInstaller 上。", this);
            return ScriptableObject.CreateInstance<SampleConfig>();
        }
    }
}

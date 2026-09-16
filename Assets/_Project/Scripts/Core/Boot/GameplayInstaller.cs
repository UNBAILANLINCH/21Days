// 职责：玩法层往**根作用域**注册自己类型（状态、规则类、入口点）的唯一缝。
//   Core 不认识任何玩法，玩法自己写一个子类挂到 Boot 场景的 GameBootstrap 物体上，
//   GameLifetimeScope 在 Configure 末尾把它们逐个调一遍。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：没有任何现成类型承担「反转依赖、让上层把自己注册进下层容器」这个职责。
//   2. 扩展不行：直接往 GameLifetimeScope.Configure 里写 builder.Register<SampleState>()
//      等于让 Game.Core 引用 Game.Runtime——asmdef 依赖方向禁止，而且会成环。
//      改成子作用域也不行：GameFlow 是从**根** IObjectResolver 解析状态类型的
//      （见 Core/Flow/GameFlow.cs 的 TransitionAsync），子作用域的注册对父作用域不可见，
//      而且玩法场景的作用域在标题界面那会儿还没加载出来。
//   所以只能新建一个抽象基类，由玩法层继承——Core 只认识这个基类，不认识任何玩法类型。

using UnityEngine;
using VContainer;

namespace Game.Core.Boot
{
    /// <summary>
    /// 玩法注册器。写法：在 <c>Game.Runtime</c> 里继承它，重写 <see cref="Install"/>，
    /// 把本模块的状态 / 规则类 / 入口点注册进去，再把这个组件挂到 Boot 场景的 <c>GameBootstrap</c> 物体上。
    /// <para>
    /// 同一个物体上可以挂多个（一个玩法模块一个），<see cref="GameLifetimeScope"/> 按 Inspector 上的
    /// 组件顺序依次调用。注册进的是**根作用域**，所以这些类型能注入任何框架服务，
    /// 也能被 <c>IGameFlow.GoToAsync&lt;T&gt;()</c> 解析到。
    /// </para>
    /// <para>
    /// 只注册、不做别的：<see cref="Install"/> 在容器构建期间被调用，这时候容器还没建好，
    /// 里面不要 Resolve、不要碰别的服务，要在启动时做事就注册一个 VContainer 的入口点
    /// （<c>builder.RegisterEntryPoint&lt;T&gt;()</c>，实现 <c>IStartable</c>）。
    /// </para>
    /// </summary>
    public abstract class GameplayInstaller : MonoBehaviour
    {
        /// <summary>把本模块的类型注册进根作用域。由 <see cref="GameLifetimeScope.Configure"/> 调用。</summary>
        public abstract void Install(IContainerBuilder builder);
    }
}

// 职责：待处理转场的契约——一次只挂一个目标，「取用即清空」。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有任何「下一步要去哪张图」的落点。PortalAnchor 是场景侧组件（发一次事件就结束），
//      SceneGameState 只有「加载哪个地址」，两者之间缺一个「这次要去哪」的交接点。
//   2. 扩展不行：把它塞进 GameFlow 会让流程层认识场景表；塞进 WorldCatalog（表适配层）会让只读查询变成可写状态。
//   3. 所以单独一个窄接口：写方一个（触发转场的那一方），读方一个（WorldSceneState）。
namespace Game.World
{
    /// <summary>
    /// 待处理转场服务（根作用域单例）。**一次只挂一个**：玩家连点传送点时后到覆盖先到并 Warn，
    /// 不抛异常（PRP §2.1：连点不该崩）。
    /// <para>
    /// <b>为什么不是队列</b>：转场是「玩家现在要去哪」而不是「依次去若干张图」；排队会让中间那些目标的
    /// 出生点与到达方式在真正到达时已经过期，反而更难查。真要连续转场，触发方在到达后再写一条即可。
    /// </para>
    /// </summary>
    public interface IWorldTransition
    {
        /// <summary>有没有待处理转场。</summary>
        bool HasPending { get; }

        /// <summary>待处理的转场请求；没有时为 null。**只读用**，取用请走 <see cref="TryConsume"/>（它才清空）。</summary>
        WorldTransitionRequest Current { get; }

        /// <summary>
        /// 挂起一次转场。已经挂着一条时**后到覆盖先到并 Warn**（不抛：玩家连点传送点不该崩）。
        /// </summary>
        /// <exception cref="System.ArgumentNullException"><paramref name="request"/> 为 null。</exception>
        void Request(WorldTransitionRequest request);

        /// <summary>
        /// 校验待处理转场但**不消费**。给「进场景之前就要地址」的调用方用：
        /// <see cref="WorldSceneState.SceneKey"/> 必须在 <c>OnSceneReadyAsync</c> 之前（加载场景那一刻）就给出地址，
        /// 而那时还不能消费掉——真正消费发生在场景就绪之后。
        /// </summary>
        WorldTransitionResolution Peek();

        /// <summary>
        /// 取用并**立即清空**待处理转场，取用前先校验目标场景在 <c>TbScene</c> 里存在且可加载
        /// （<c>implemented=true</c> 且地址非空）。
        /// <para>
        /// 清空与校验成败无关：**失败的请求也不会留在待处理位上**——否则下一次进入会拿一个已经作废的目标，
        /// 那正是「重进这张图站到了上一张图的落点」这类错法的来源。
        /// </para>
        /// </summary>
        WorldTransitionResolution TryConsume();

        /// <summary>丢弃待处理转场（放弃这次转场）。没有待处理时是空操作。</summary>
        void Clear();
    }
}

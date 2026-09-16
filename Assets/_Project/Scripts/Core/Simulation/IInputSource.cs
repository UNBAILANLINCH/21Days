// 职责：逻辑推进取输入的唯一入口——「当前 tick 的那条输入命令」从哪来。
//   实时玩是设备采样（LiveInputSource），重放是录像回放（后续的 ReplayInputSource），
//   推进器两边都只认这个接口，于是「录」和「放」在逻辑眼里是同一件事。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：工程内只有 Core/Input/IInputService，它给出的是 GameInput 动作集本身——
//      读它拿到的永远是「此刻的设备状态」，没有哪个实现能把「一条录好的命令」伪装成设备，
//      这条路录不到也放不回。
//   2. 扩展不行：不能往 IInputService 上加一个 Current。那个接口只管动作图的启停，
//      而且它要继续服务 UI 导航、调试快捷键这些**不需要确定性**的场合；两种口径混在一个接口里，
//      调用方分不清自己读到的是「可重放的一帧」还是「实时设备」，重放分叉就是这么来的。

namespace Game.Core.Simulation
{
    /// <summary>
    /// 输入源。给出**当前 tick** 的输入命令，值在这一 tick 内定格不变。
    /// <para>
    /// 玩法不直接注入具体实现，注入的是 <see cref="InputSourceSwitch"/>——
    /// 它也实现本接口，回放开始 / 结束时只换内部指向，玩法手里的引用永远不变。
    /// </para>
    /// <para>实现要求：<see cref="Current"/> 是纯读属性，零分配，不在里面做采样或 IO。</para>
    /// </summary>
    public interface IInputSource
    {
        /// <summary>
        /// 为指定 tick 准备好 <see cref="Current"/>。<b>推进器在每次推进前调一次</b>，
        /// 调完这一 tick 的 <see cref="Current"/> 才算有效。
        /// <para>
        /// <b>为什么采样要进接口：</b>不能让推进器去认 <see cref="LiveInputSource"/> 这个具体实现。
        /// <see cref="InputSourceSwitch"/> 存在的全部目的，就是让上游不知道当前是实时输入还是回放输入；
        /// 推进器一旦为了调采样而向下转型成实时源，这层隔离当场就破了。
        /// 采样进了接口，推进器只写 <c>inputSource.Sample(tick); var cmd = inputSource.Current;</c>——
        /// 实时与回放走**完全相同**的两行代码，这是重放能对得上的前提。
        /// </para>
        /// <para>
        /// <b>为什么带 tick 参数：</b>留给将来的 <c>ReplayInputSource</c> 做对齐断言——
        /// 它可以校验「我取到的这条命令的 tick 号，和推进器正在推的 tick 对得上」，
        /// 一道几乎零成本的保险。输入错位一格是回放类 bug 里最典型、也最难用肉眼看出来的一种：
        /// 画面照样在动，只是每个操作都晚一格生效，越滚越偏。
        /// 实时源用不上这个参数（它读的是当前设备状态），但接口要照顾的是所有实现。
        /// </para>
        /// </summary>
        /// <param name="tick">推进器正要推进的那个 tick 号。</param>
        void Sample(long tick);

        /// <summary>当前 tick 的输入命令。没有输入（源未就绪 / 录像已放完）时返回 <see cref="InputCommand.Empty"/>。</summary>
        InputCommand Current { get; }
    }
}

// 职责：效果执行者的契约——认领一个效果标识，并把它执行掉。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：ItemUseRules.TryUse 只判「能不能用、效果标识是什么」（它的文件头第 7-11 行写明
//      「本文件只把标识交出去，标识怎么解释、白名单有哪些由执行者定」），工程里没有任何「执行者」的形状。
//   2. 扩展不行：往 ItemUseRules 里塞 switch(effectId) 会让纯规则依赖玩家、场景与 Unity 对象，
//      EditMode 就再也测不了它了（那 12 条用例的前提就是「不依赖执行环境」）。
// 一个实现管一个效果标识（或一组同族标识）：皮 / 面具类效果的实现归 S1「换皮与附身」，
//   本波次只落占位治疗（见 HealPlayerEffect）。
namespace Game.Inventory
{
    /// <summary>
    /// 一个道具效果的执行者。注册进 <see cref="ItemEffectRegistry"/> 后按 <see cref="EffectId"/> 命中。
    /// </summary>
    public interface IItemEffect
    {
        /// <summary>认领的效果标识，与 <see cref="ItemUseSpec.EffectId"/> 同一套字面量（约定写成「域.动作」）。</summary>
        string EffectId { get; }

        /// <summary>
        /// 执行效果。实现**不得抛异常**：参数不成立时返回
        /// <see cref="ItemEffectOutcome.BadTarget"/> / <see cref="ItemEffectOutcome.Rejected"/> 并写明中文原因。
        /// </summary>
        ItemEffectOutcome Apply(ItemEffectContext context);
    }
}

// 职责：按效果标识分发的执行者注册表——`effectId → IItemEffect`，查不到就明确回「没有执行者」。
// 为什么是这个形状（命名与结构的选择理由）：
//   1. 叫 Registry 不叫 Service：它不持状态、不认识存档与 UI，只做「标识 → 执行者」的一次查表；
//      叫 Service 会让人以为它能替调用方落盘（落盘归 Loot 公开口，inventory-module-guide.md 禁止事项）。
//   2. 用接口 + 字典而不是 switch：效果会一个一个接上来（S1 换皮与附身、各阶段专属机制），
//      switch 长在纯规则里会让 ItemUseRules 反过来依赖玩家与场景；接口注册让每个效果留在自己的文件里。
//   3. 查不到返回 ItemEffectOutcome.NoExecutor()（中文原因）而不是 null / false：
//      本工程反复强调防静默失败——没接执行者时玩家必须看到「这个道具的效果还没有接上」，
//      而不是「点了没反应」，日志与埋点也才有东西可查。
//   4. 重复注册同一标识时抛异常：那是装配期写错（两个效果抢同一个标识），在启动时就炸比在玩家点下去时才炸好。
using System;
using System.Collections.Generic;

namespace Game.Inventory
{
    /// <summary>效果执行者注册表。装配期注册，运行期只读查表。</summary>
    public sealed class ItemEffectRegistry
    {
        private readonly Dictionary<string, IItemEffect> effects = new Dictionary<string, IItemEffect>(StringComparer.Ordinal);

        /// <summary>已注册的执行者数量。</summary>
        public int Count => effects.Count;

        /// <summary>
        /// 注册一个执行者。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> 为 null。</exception>
        /// <exception cref="ArgumentException">执行者的 <see cref="IItemEffect.EffectId"/> 为空，或该标识已经被注册过。</exception>
        public void Register(IItemEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            string id = effect.EffectId;
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("效果执行者的 EffectId 不能为空：" + effect.GetType().Name, nameof(effect));
            }

            if (effects.ContainsKey(id))
            {
                throw new ArgumentException("效果标识重复注册：" + id + "（已有 " + effects[id].GetType().Name + "）", nameof(effect));
            }

            effects.Add(id, effect);
        }

        /// <summary>这个效果标识有没有执行者。</summary>
        public bool Has(string effectId) => !string.IsNullOrEmpty(effectId) && effects.ContainsKey(effectId);

        /// <summary>
        /// 执行一个效果。<paramref name="effectId"/> 为空或查不到时返回 <see cref="ItemEffectStatus.NoExecutor"/>；
        /// <paramref name="context"/> 为 null 时返回 <see cref="ItemEffectStatus.BadTarget"/>。两者都带中文原因，不抛。
        /// </summary>
        public ItemEffectOutcome Execute(string effectId, ItemEffectContext context)
        {
            if (context == null)
            {
                return ItemEffectOutcome.BadTarget("没有可用的效果目标");
            }

            if (string.IsNullOrEmpty(effectId) || !effects.TryGetValue(effectId, out IItemEffect effect))
            {
                return ItemEffectOutcome.NoExecutor();
            }

            return effect.Apply(context);
        }
    }
}

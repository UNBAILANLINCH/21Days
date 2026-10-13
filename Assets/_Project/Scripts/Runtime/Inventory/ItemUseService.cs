// 职责：把「使用一件道具」这件事串起来——查使用表（ItemUseRules）→ 找效果执行者（ItemEffectRegistry）→ 扣件数（LootService.TryConsume）。
//   规则、执行、落盘三段各归各家，本类只负责顺序与失败短路。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：ItemUseRules 只判定（它的文件头写明「不执行效果、不扣件数、不写背包」）；
//      LootService 是背包写入口，不认识效果标识；InventoryPanelController 是 UI 会话（它不该知道规则细节，
//      否则 Showcase 之外没有任何地方能跑「用掉一件」这条路）。
//   2. 扩展不行：把这三段塞进控制器会让「使用」逻辑绑死 UI（没法在 EditMode 里测，也没法被快捷栏复用）。
// 扣件数与落盘的顺序（本类最重要的一条判断，理由写全）：
//   先执行效果、**执行成功之后才扣件数**。这样「执行者拒绝」（满血 / 已倒下 / 治疗口没接）对背包是零影响，
//   不存在「扣了再退回去」的窗口。反过来（先扣后执行）必须靠补回来兜底，而补回本身也可能失败
//   （背包分区在读档时被整体替换过），会留下「道具没了、效果没有」的最坏状态。
//   代价是：执行成功但扣减失败时效果已经生效、退不掉——那时必须留一条 Error（不一致不能静默），
//   本类在 TryConsume 失败那一支里就是这么做的。判定（ItemUseRules.TryUse）与实际扣减之间背包可能变化，
//   所以这个窗口无法靠「判定过了就一定扣得动」消除。
// 写入路径：只走 Loot 的公开口（TryConsume / GrantItems），**本模块不新增第二条写入路径**
//   （inventory-module-guide.md「禁止事项」：写入只走 Loot 公开方法）。
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Loot;

namespace Game.Inventory
{
    /// <summary>
    /// 使用道具的门面。构造注入即可（根作用域单例）。
    /// <para>
    /// 使用表来自 <see cref="InventoryConfig"/>（占位配置资产）；效果执行者来自 <see cref="ItemEffectRegistry"/>。
    /// 没有执行者的效果标识回 <see cref="ItemUseStatus.NoExecutor"/>，**不静默失败**。
    /// </para>
    /// </summary>
    public sealed class ItemUseService
    {
        /// <summary>埋点里「背包在判定与扣减之间变了」的原因码（E 级事件 use_consume_failed 用）。</summary>
        private const string ConsumeFailed = "consume_failed";

        private readonly IReadOnlyList<ItemUseSpec> specs;
        private readonly ItemEffectRegistry effects;
        private readonly LootService loot;
        private readonly ITelemetryScope telemetry;

        public ItemUseService(IReadOnlyList<ItemUseSpec> specs, ItemEffectRegistry effects, LootService loot,
            ITelemetryScope telemetry)
        {
            this.specs = specs ?? Array.Empty<ItemUseSpec>();
            this.effects = effects ?? throw new ArgumentNullException(nameof(effects));
            this.loot = loot ?? throw new ArgumentNullException(nameof(loot));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>使用表（只读）。</summary>
        public IReadOnlyList<ItemUseSpec> Specs => specs;

        /// <summary>这件道具在不在使用表里（界面用它决定「使用」按钮的状态与提示）。</summary>
        public bool CanUse(int itemId) => !string.IsNullOrEmpty(EffectIdOf(itemId));

        /// <summary>这件道具的效果标识；不在使用表里时为空串。</summary>
        public string EffectIdOf(int itemId)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].ItemId == itemId) return specs[i].EffectId;
            }

            return string.Empty;
        }

        /// <summary>
        /// 这件道具的效果有没有执行者。false 时界面可以提前告诉玩家「这个效果还没接上」，而不是让他点了才发现。
        /// </summary>
        public bool HasExecutor(int itemId)
        {
            string effectId = EffectIdOf(itemId);
            return !string.IsNullOrEmpty(effectId) && effects.Has(effectId);
        }

        /// <summary>
        /// 使用一件道具：判定 → 执行效果 → **成功之后**才扣件数。
        /// <paramref name="itemId"/> 非正、没在使用表里、件数不够、效果没执行者、执行者拒绝时都返回带结果码的失败，
        /// 且**不改背包**（见文件头的顺序说明）。
        /// </summary>
        /// <param name="itemId">要用的道具 id。</param>
        /// <param name="target">效果目标口（玩家生命）；调用方没接时为 null，需要它的效果会明确报「治疗口还没接上」。</param>
        public ItemUseOutcome TryUse(int itemId, IItemEffectTarget target)
        {
            ItemUseResult use = ItemUseRules.TryUse(loot.Items, itemId, specs);
            if (!use.IsOk)
            {
                telemetry.TrackWarn("use_rejected", TelemetryProps.Of(("item", itemId), ("reason", use.Status.ToString())));
                return ItemUseOutcome.Failed(use.Status, use);
            }

            var context = new ItemEffectContext(itemId, use.EffectId, target);
            ItemEffectOutcome effect = effects.Execute(use.EffectId, context);
            if (effect.Status == ItemEffectStatus.NoExecutor)
            {
                // 关键分支：没有执行者时**绝不扣件数**（皮 / 面具类效果归 S1，现在点了要看到明确原因）。
                telemetry.TrackWarn("use_rejected", TelemetryProps.Of(("item", itemId), ("reason", "no_executor")));
                return ItemUseOutcome.Failed(ItemUseStatus.NoExecutor, use);
            }

            if (!effect.IsApplied)
            {
                telemetry.TrackWarn("use_rejected",
                    TelemetryProps.Of(("item", itemId), ("reason", effect.Status.ToString())));
                return ItemUseOutcome.Rejected(use, effect);
            }

            int consume = use.ConsumeCount;
            if (!loot.TryConsume(itemId, consume))
            {
                // 判定过的件数扣不动说明背包在两步之间变了（同一帧别的东西扣过）：把已经生效的效果退不掉，
                // 但必须留一条 Error —— 这是真正的不一致，不能静默。
                Log.Error($"ItemUseService：道具 {itemId} 的效果已生效，但扣 {consume} 件失败（背包在判定与扣减之间变了）。");
                telemetry.TrackError("use_consume_failed", ConsumeFailed,
                    TelemetryProps.Of(("item", itemId), ("consume", consume)));
                return ItemUseOutcome.Failed(ItemUseStatus.NotEnough, use);
            }

            telemetry.Track("item_used", ("item", itemId), ("effect", use.EffectId), ("heal", effect.Amount));
            return ItemUseOutcome.Succeeded(use, effect, consume);
        }
    }
}

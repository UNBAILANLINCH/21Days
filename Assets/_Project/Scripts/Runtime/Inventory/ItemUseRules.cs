// 职责：道具使用的纯规则——在「使用表」（道具 id → 效果标识 + 消耗件数）里查这件道具，判定背包件数够不够，
//   够了就回「效果标识 + 这次扣几件」；本文件不执行效果、不扣件数、不写背包。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：InventoryRules 只做「表项 + 件数」的展示合并与筛选，没有任何「使用」语义；LootRules 只管开箱写入。
//   2. 扩展不行：塞进 InventoryRules 会把「展示口径」和「使用口径」两种职责绑在一个类上（职责尺子说不通）；
//      塞进 Loot 会让依赖方向反过来（依赖方向是 Inventory → Loot）。
// 谁来回调这个效果（本波次不做，接口先立）：
//   · 效果标识的执行者 = S1「换皮与附身」后续波次（docs/roadmap.md:401 步 4：使用皮 / 面具即借身份，
//     落点是 Game.Disguise 的「身份」状态）+ 各阶段专属机制（量尺调水位等，features-spotlight/07）。
//     本文件只把标识交出去，标识怎么解释、白名单有哪些由执行者定（05_皮面具与道具.md:219 还写着
//     「使用要不要按键、从背包还是快捷栏用」未定，所以这里不预设触发方式）。
//   · 扣件数与落盘：调用方拿到 Ok 之后自己扣，写入走 Loot 的公开方法 / 将来的背包服务
//     （roadmap 第 2 节 E1 落盘存档）；Inventory 不新增第二条写入路径（inventory-module-guide.md「禁止事项」）。
//   · 使用表从哪来：本波次没有使用表，由调用方以 ItemUseSpec 列表传入（测试里手搓）。
//     要搬进 Luban 表时改 Tables/Data/__beans__.xlsx + __tables__.xlsx 加一张表，本文件签名不用动。
using System.Collections.Generic;

namespace Game.Inventory
{
    /// <summary>道具使用规则。全部静态、只读输入，不抛异常（非法参数落成结果码）。</summary>
    public static class ItemUseRules
    {
        /// <summary>
        /// 查这件道具能不能用。判定顺序：itemId 非正或 <paramref name="specs"/> 为 null / 空 → <see cref="ItemUseStatus.UnknownItem"/>；
        /// 查不到 → <see cref="ItemUseStatus.UnknownItem"/>；查到但写坏 → <see cref="ItemUseStatus.InvalidSpec"/>；
        /// 件数不够 → <see cref="ItemUseStatus.NotEnough"/>；否则 Ok。
        /// <para><paramref name="items"/> 为 null 视作空背包（结果同样是件数不够）。</para>
        /// </summary>
        public static ItemUseResult TryUse(IReadOnlyDictionary<int, int> items, int itemId, IReadOnlyList<ItemUseSpec> specs)
        {
            if (itemId <= 0 || specs == null || specs.Count == 0)
                return ItemUseResult.Failed(ItemUseStatus.UnknownItem, default, 0);

            for (int i = 0; i < specs.Count; i++)
            {
                ItemUseSpec spec = specs[i];
                if (spec.ItemId != itemId) continue;

                int owned = Owned(items, itemId);
                if (!spec.IsValid) return ItemUseResult.Failed(ItemUseStatus.InvalidSpec, spec, owned);
                if (owned < spec.ConsumeCount) return ItemUseResult.Failed(ItemUseStatus.NotEnough, spec, owned);
                return ItemUseResult.Succeeded(spec, owned);
            }

            return ItemUseResult.Failed(ItemUseStatus.UnknownItem, default, 0);
        }

        // 背包里有多少件；items 为 null 或查不到都算 0。
        private static int Owned(IReadOnlyDictionary<int, int> items, int itemId)
        {
            if (items == null) return 0;
            return items.TryGetValue(itemId, out int owned) ? owned : 0;
        }
    }
}

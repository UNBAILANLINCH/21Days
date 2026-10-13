// 职责：把「一次击杀掉哪几件」转交给 Loot 的掉落结算门面——实现 Game.Monster 声明的结算口 IMonsterDropSink。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LootService 自己不能实现这个接口——它是 IGameService（门面 / 数据层），
//      而接口声明在调用方 Game.Monster 那一侧且写明了「Loot 不该让 Monster 认识存档与通知」；
//      让门面去实现调用方接口会把「谁依赖谁」拧成一团。LootSceneBinder 也不行：它管的是场景物资箱。
//   2. 扩展不行：塞进 LootService 会同时踩上面两条；塞进 Monster 侧则违反另一路对该目录的文件所有权。
// 契约方向（同 Game.Monster 实现 Game.Stealth 的 IStealthFactSink，IMonsterDropSink.cs:8-11 写明了这条既有接法）：
//   接口声明在**调用方**那一侧（Game.Monster），实现方（Game.Loot）反向认识它。
//   全工程是一个 Game.Runtime 程序集、多命名空间，所以这里 using Game.Monster 能编译；
//   但依赖方向是约定：**Game.Monster 不认识 Game.Loot，Game.Loot 认识 Game.Monster 的结算口**。
//   反向（Loot 里引用 Monster 的具体类型 EncounterStep / MonsterRules）仍然禁止。
// 空操作安全（接口注释 IMonsterDropSink.cs:27-30 的硬要求）：
//   yaoId 非正 / 列表 null 或空时不得抛——本文件不自己判，直接交给 LootService.SettleMonsterDrop，
//   它的第一段（LootService.cs:150）就是这三种退化并返回 false；判定只留一处，避免两处口径走岔。
using System.Collections.Generic;
using Game.Monster;

namespace Game.Loot
{
    /// <summary>
    /// 击杀掉落结算口 <see cref="IMonsterDropSink"/> 的 Loot 侧实现：一行转发给
    /// <see cref="LootService.SettleMonsterDrop"/>（写背包分区 → 通知 → 发布 <see cref="MonsterDroppedEvent"/> → 埋点）。
    /// <para>
    /// **幂等由调用方保证**（一只怪只调一次），本类不做去重——理由见 <see cref="LootService.SettleMonsterDrop"/> 的注释。
    /// 不接这一层时（<c>EncounterStep</c> 没拿到 sink）怪物什么都不掉，行为与接线前逐字一致。
    /// </para>
    /// </summary>
    public sealed class LootMonsterDropSink : IMonsterDropSink
    {
        private readonly LootService loot;

        public LootMonsterDropSink(LootService loot)
        {
            // LootService 是普通 C# 类（非 UnityEngine.Object），判空用 ?? 即可。
            this.loot = loot ?? throw new System.ArgumentNullException(nameof(loot));
        }

        /// <summary>
        /// 结算一次击杀掉落。参数非法（yaoId 非正、列表 null / 空）时空操作、不抛；
        /// 返回值语义与 <see cref="LootService.SettleMonsterDrop"/> 一致（真加了至少一件物品才是 true）。
        /// </summary>
        public void SettleKillDrop(int yaoId, IReadOnlyList<int> dropItemIds)
        {
            loot.SettleMonsterDrop(yaoId, dropItemIds);
        }
    }
}

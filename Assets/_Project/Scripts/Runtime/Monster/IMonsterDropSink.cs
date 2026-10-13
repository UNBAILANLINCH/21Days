// 职责：一次击杀的掉落结算口——调用方是遭遇结算（EncounterStep 的死亡分支），实现方在 Game.Loot
//   （它才知道物品表、背包分区与通知）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Stealth 的 IStealthFactSink 只写事实键，没有「一次击杀掉哪几件」的语义；
//      LootService 是 IGameService，Monster 直接依赖它会让 Monster 认识存档与通知。
//   2. 扩展不行：往 MonsterIntent 或 PlayerSnapshot 里挂掉落，会把「结算时机」藏进每 tick 的数据结构里，
//      而掉落必须**一只怪只结一次**，时机由遭遇结算的死亡分支唯一决定。
// 契约方向（同 StealthKernel / NarrativeConditionSource 的既有接法）：
//   接口声明在**调用方**这一侧（Game.Monster），实现方（Game.Loot）反向认识它；
//   EncounterStep 只持一个可空引用，装配走 MonsterInstaller 的构建回调 TryResolve——
//   **不接时什么都不掉**，行为与接线前逐字一致（独立原型场景与纯 Monster 测试作用域没有 Loot）。
// 装配方式：与 EncounterStep 的 BindIdentity / UseStealth 同一条纪律——本类构造参数不能变
//   （TypeAnalyzer 取参数最多的构造函数），所以走 EncounterStep.BindDropSink 后置装配。
using System.Collections.Generic;

namespace Game.Monster
{
    /// <summary>
    /// 击杀掉落的结算口。实现方负责把物品累加进背包分区并发通知；调用方只负责「谁死了、掉什么」。
    /// <para>
    /// <b>幂等由调用方保证</b>：同一只怪的同一个死亡时刻只调一次（同 <c>MonsterLootRules.Grant</c>
    /// 文件头写的分工）。
    /// </para>
    /// </summary>
    public interface IMonsterDropSink
    {
        /// <summary>
        /// 结算一次击杀掉落。实现方应当是空操作安全的：
        /// <paramref name="yaoId"/> 非正、<paramref name="dropItemIds"/> 为 null / 空时不得抛异常。
        /// </summary>
        /// <param name="yaoId">这只怪在妖物表（tbyao）里的 id；掉落内容按它记录，用于埋点与通知文案。</param>
        /// <param name="dropItemIds">这次掉的 tbitem id 列表；只交 id，类别判定归道具侧。</param>
        void SettleKillDrop(int yaoId, IReadOnlyList<int> dropItemIds);
    }
}

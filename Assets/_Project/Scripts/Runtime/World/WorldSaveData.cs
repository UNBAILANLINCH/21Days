// 职责：跨场景场景状态分区——按「场景键 + 实体标识」记住已开箱与已死怪，供场景卸载后再进来时还原。
// 为什么新增分区（复用 → 扩展 → 新建）：
//   1. 复用不行：LootSaveData.CollectedCrates 是「已开箱」的扁平列表、EncounterSaveData 是**单个**遭遇快照
//      且 Validate() 写死只认地址 IsometricEncounter（Assets/_Project/Scripts/Runtime/Monster/EncounterSaveData.cs:23），
//      两者都表达不了「同一实体 id 在不同场景里各记一份」。
//   2. 扩展不行：LootSaveData / EncounterSaveData / QuestSaveData / NarrativeSaveData 的所有权分别属于
//      Loot / Monster / Quest / Narrative 模块，本模块不改它们的既有文件（文件所有权规则）。
//   3. 所以按 ISaveData 的既有模式新建一个分区，版本从 1 起，Migrate 逐级往上迁（Core/Save/ISaveData.cs:18）。
//   为什么不用「给现有键加前缀」的写法代替新分区：那样会让 LootSaveData.CollectedCrates 同时装箱子与怪物，
//   语义污染比多一个分区更贵；而且现有分区没有「场景」这一维，前缀约定散在每个调用点上，改不动也测不全。
using System;
using System.Collections.Generic;
using Game.Core.Save;

namespace Game.World
{
    /// <summary>
    /// 跨场景场景状态分区。两个集合的键都是 <see cref="SceneStateKey.Scoped"/> 拼出来的
    /// 「场景键 + 实体标识」，所以同名实体在人间与妖界各记一份，不会互相顶掉。
    /// <para>
    /// 只存「这一场里已经发生过的事」，不存位置 / 血量这类连续量：连续量是遭遇快照的职责。
    /// </para>
    /// </summary>
    public sealed class WorldSaveData : ISaveData
    {
        /// <summary>当前分区版本。改语义（字段改名、键口径变了）时加一并补 <see cref="Migrate"/>。</summary>
        public int Version => 1;

        /// <summary>已开过的箱子（场景作用域键），按打开顺序。</summary>
        public List<string> CollectedCrates { get; set; } = new List<string>();

        /// <summary>已死的怪（场景作用域键）。</summary>
        public HashSet<string> DefeatedMonsters { get; set; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 第 1 版没有历史版本要迁；本方法存在是契约要求（<see cref="ISaveData.Migrate"/>），
        /// 将来加版本时按 <c>if (fromVersion &lt; 2) { ... }</c> 逐级往上写，不要只处理相邻版本。
        /// </summary>
        public void Migrate(int fromVersion)
        {
        }
    }
}

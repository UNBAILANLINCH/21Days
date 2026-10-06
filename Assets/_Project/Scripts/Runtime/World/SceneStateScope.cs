// 职责：一个场景的跨场景状态视图——按「场景键 + 实体标识」读写已开箱 / 已死怪，
//   并在同一进程内缓存一份，让「同一个箱子开第二次」不必回查存档分区。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LootService / MonsterEncounterState 都是整局单例、只认自己那一套现场；
//      场景卸载后它们的运行期状态就没了，而「这张图里哪些箱子开过」必须活过场景卸载。
//   2. 扩展不行：WorldSaveData 是纯 DTO，不能认识 ISaveService（分区实例在读档 Commit 时会被整体替换，
//      缓存分区实例的类会把进度写进一份没人读的对象，见 LootService 的类注释）。
//   3. 所以本类只做「分区 + 场景键」这一层适配：每次操作都重新 saves.Get<WorldSaveData>()，
//      进程内另存一份已记录集合，避免重复写盘。
using System;
using System.Collections.Generic;
using Game.Core.Save;

namespace Game.World
{
    /// <summary>
    /// 一个场景的跨场景状态。构造时绑定场景键，之后所有读写都落在该场景的作用域键上。
    /// <para>
    /// 与现有分区的关系：这是**约定**，不是新一套存档系统。箱子与怪物死亡记在本视图对应的
    /// <see cref="WorldSaveData"/>；任务点与 NPC 状态不在这里——任务点是全局进度，归
    /// <c>Game.Quest.QuestSaveData</c>；NPC 状态的落点见交付报告「跨场景状态怎么存的」一节。
    /// </para>
    /// </summary>
    public sealed class SceneStateScope
    {
        private readonly ISaveService saves;
        private readonly string sceneKey;

        // 进程内缓存：**只用来省掉对分区的重复扫描，不是真相**。真相在分区里
        // （读档后由 RestoreFromPartition 重新灌一遍）。所以下面几个查询都是「缓存 ∪ 分区」，
        // 分区被换掉（读档 Commit / 新游戏 ResetAll）时缓存即便还留着旧记录，查询结果也仍然是对的。
        private readonly List<string> recordedCrates = new List<string>();
        private readonly HashSet<string> recordedCratesSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> defeatedMonsters = new HashSet<string>(StringComparer.Ordinal);

        public SceneStateScope(ISaveService saves, string sceneKey)
        {
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));

            // 空场景键拼出来的作用域键会退化成「::实体」，两份地图会互相顶掉，所以这里就拦掉。
            if (string.IsNullOrEmpty(sceneKey))
            {
                throw new ArgumentException("场景键不能为空（见 TbScene.scene_key）。", nameof(sceneKey));
            }

            this.sceneKey = sceneKey;
            RestoreFromPartition();
        }

        /// <summary>本视图绑定的场景键。</summary>
        public string SceneKey => sceneKey;

        /// <summary>本次开机以来本场景开过的箱子（场景内实体标识，按开箱顺序）。</summary>
        public IReadOnlyList<string> RecordedCrates => recordedCrates;

        /// <summary>
        /// 该箱子是否已开过：先看本次开机的记录，再查存档分区。分区里存的是场景作用域键，
        /// 所以「人间开过的箱子」不会被「妖界同名箱子的键」误判。
        /// </summary>
        public bool IsCrateOpened(string crateId)
        {
            if (recordedCratesSet.Contains(Require(crateId, nameof(crateId))))
            {
                return true;
            }

            List<string> stored = Partition.CollectedCrates;
            return stored != null && stored.Contains(SceneStateKey.Scoped(sceneKey, crateId));
        }

        /// <summary>
        /// 记一次开箱。幂等：已记过返回 false（调用方据此决定要不要再弹奖励），没记过才写分区并返回 true。
        /// </summary>
        /// <exception cref="ArgumentException">箱子 id 为空或带分隔符。</exception>
        public bool MarkCrateOpened(string crateId)
        {
            string id = Require(crateId, nameof(crateId));
            if (recordedCratesSet.Contains(id) || IsCrateOpened(id))
            {
                return false;
            }

            WorldSaveData data = Partition;
            if (data.CollectedCrates == null)
            {
                data.CollectedCrates = new List<string>();
            }

            data.CollectedCrates.Add(SceneStateKey.Scoped(sceneKey, id));
            recordedCrates.Add(id);
            recordedCratesSet.Add(id);
            return true;
        }

        /// <summary>该怪是否已经死过（死了就不该再刷出来）。</summary>
        public bool IsMonsterDefeated(string monsterId)
        {
            string id = Require(monsterId, nameof(monsterId));
            HashSet<string> stored = Partition.DefeatedMonsters;
            return defeatedMonsters.Contains(id)
                || (stored != null && stored.Contains(SceneStateKey.Scoped(sceneKey, id)));
        }

        /// <summary>记一只怪死了。幂等：已记过返回 false。</summary>
        /// <exception cref="ArgumentException">怪 id 为空或带分隔符。</exception>
        public bool MarkMonsterDefeated(string monsterId)
        {
            string id = Require(monsterId, nameof(monsterId));
            if (IsMonsterDefeated(id))
            {
                return false;
            }

            WorldSaveData data = Partition;
            if (data.DefeatedMonsters == null)
            {
                data.DefeatedMonsters = new HashSet<string>(StringComparer.Ordinal);
            }

            data.DefeatedMonsters.Add(SceneStateKey.Scoped(sceneKey, id));
            defeatedMonsters.Add(id);
            return true;
        }

        /// <summary>
        /// 清掉内存缓存，重新从存档分区灌一遍。读档（Commit 换掉分区实例）之后要调一次，
        /// 否则进程内缓存还留着上一份存档的记录。
        /// </summary>
        public void RestoreFromPartition()
        {
            recordedCrates.Clear();
            recordedCratesSet.Clear();
            defeatedMonsters.Clear();

            string prefix = SceneStateKey.PrefixOf(sceneKey);

            List<string> crates = Partition.CollectedCrates;
            if (crates != null)
            {
                for (int i = 0; i < crates.Count; i++)
                {
                    if (!TryUnscope(crates[i], prefix, out string id) || !recordedCratesSet.Add(id))
                    {
                        continue;
                    }

                    recordedCrates.Add(id);
                }
            }

            HashSet<string> monsters = Partition.DefeatedMonsters;
            if (monsters == null)
            {
                return;
            }

            foreach (string key in monsters)
            {
                if (TryUnscope(key, prefix, out string id))
                {
                    defeatedMonsters.Add(id);
                }
            }
        }

        // 每次操作都重新取分区：读档 Commit 会整体替换分区实例，缓存旧实例会把进度写进一份没人读的对象。
        private WorldSaveData Partition => saves.Get<WorldSaveData>();

        private static string Require(string id, string parameterName)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("标识不能为空。", parameterName);
            }

            return id;
        }

        // 只认本场景前缀的键。别的场景的键直接跳过——同一个分区里装着所有场景的记录。
        private static bool TryUnscope(string scopedKey, string prefix, out string entryId)
        {
            if (scopedKey != null && scopedKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                entryId = scopedKey.Substring(prefix.Length);
                return entryId.Length > 0;
            }

            entryId = null;
            return false;
        }
    }
}

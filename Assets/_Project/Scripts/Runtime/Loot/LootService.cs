// 职责：物资箱模块对外门面（根作用域单例）——开箱时写存档分区、推进任务计数、弹奖励通知、发布事件与埋点；重置时清空分区。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：LootRules 是纯规则，不认识存档服务、任务、通知与 MessagePipe。
//   2. 扩展不行：塞进 QuestService 会让任务认识物品表与箱子，依赖方向是 Loot → Quest。
//   同 QuestService / QuestRules 的分法：接线放门面，规则保持可单测。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Quest;
using MessagePipe;

namespace Game.Loot
{
    /// <summary>
    /// 物资箱服务。存档分区每次操作都重新 <c>saves.Get&lt;LootSaveData&gt;()</c>——读档 Commit 会整体替换分区实例，
    /// 缓存旧实例会把进度写进一份没人读的对象（同 QuestService.Flush 的注释）。
    /// </summary>
    public sealed class LootService : IGameService, IDisposable
    {
        private static readonly IReadOnlyDictionary<int, int> EmptyItems = new Dictionary<int, int>();

        private readonly LootConfig config;
        private readonly ISaveService saves;
        private readonly IConfigService configService;
        private readonly QuestService quest;
        private readonly INotificationService notifications;
        private readonly IPublisher<CrateCollectedEvent> collectedPublisher;
        private readonly IPublisher<MonsterDroppedEvent> monsterDroppedPublisher;
        private readonly IPublisher<LootResetEvent> resetPublisher;
        private readonly ITelemetryScope telemetry;

        public LootService(LootConfig config, ISaveService saves, IConfigService configService, QuestService quest,
            INotificationService notifications, IPublisher<CrateCollectedEvent> collected,
            IPublisher<MonsterDroppedEvent> monsterDropped, IPublisher<LootResetEvent> reset,
            ITelemetryScope telemetry)
        {
            // LootConfig 是 ScriptableObject，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
            this.quest = quest ?? throw new ArgumentNullException(nameof(quest));
            this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            collectedPublisher = collected ?? throw new ArgumentNullException(nameof(collected));
            monsterDroppedPublisher = monsterDropped ?? throw new ArgumentNullException(nameof(monsterDropped));
            resetPublisher = reset ?? throw new ArgumentNullException(nameof(reset));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>最小背包：tbitem id → 数量。只读视图，随分区变化。</summary>
        public IReadOnlyDictionary<int, int> Items => (IReadOnlyDictionary<int, int>)Data.Items ?? EmptyItems;

        private LootSaveData Data => saves.Get<LootSaveData>();

        /// <summary>只确保分区存在（首次 Get 会建默认分区），不做别的。</summary>
        public UniTask InitializeAsync(CancellationToken ct)
        {
            LootSaveData data = Data;
            telemetry.Track("initialized", ("crates", data.CollectedCrates == null ? 0 : data.CollectedCrates.Count));
            return UniTask.CompletedTask;
        }

        /// <summary>该箱子键是否已开过。</summary>
        public bool IsCollected(string key) => LootRules.IsCollected(Data, key);

        /// <summary>
        /// 打开箱子：按 <see cref="SupplyCrate.Key"/> 幂等，已开 / 参数非法 / crate 为空返回 false。
        /// 成功顺序：写分区 → 箱子切开 → 上报任务 Counter（任务未就绪则跳过并记 Warn）→ 通知 → 发布事件 → 埋点。
        /// </summary>
        public bool TryCollect(SupplyCrate crate)
        {
            // SupplyCrate 是 UnityEngine.Object，判空只用 == null。
            if (crate == null) return false;

            string key = crate.Key;
            int itemId = crate.ItemId;
            int count = crate.Count;
            if (!LootRules.Collect(Data, key, itemId, count))
            {
                telemetry.Track("collect_rejected", ("key", key ?? string.Empty), ("count", count));
                return false;
            }

            crate.SetOpened(true);

            if (quest.IsReady)
            {
                quest.Report(QuestObjectiveKind.Counter, config.CrateQuestKey);
            }
            else
            {
                Log.Warn($"LootService：任务系统未就绪，开箱 {key} 不上报任务计数。", crate);
                telemetry.TrackWarn("quest_report_skipped", TelemetryProps.Of(("key", key), ("reason", "quest_not_ready")));
            }

            notifications.Show(config.RewardTitle, ComposeBody(config.RewardBodyFormat, ItemName(itemId), count));
            collectedPublisher.Publish(new CrateCollectedEvent(key, itemId, count));
            telemetry.Track("crate_collected", ("key", key), ("item", itemId), ("count", count));
            return true;
        }

        /// <summary>清空已开记录与背包并发布 <see cref="LootResetEvent"/>；场景里的箱子由 <see cref="LootSceneBinder"/> 合上。</summary>
        public void Reset()
        {
            LootRules.Reset(Data);
            resetPublisher.Publish(new LootResetEvent());
            telemetry.Track("loot_reset");
        }

        /// <summary>
        /// 从背包扣掉 <paramref name="count"/> 件物品（战斗里用掉一件道具，PRP/turnbased-battle D10）。
        /// 数量不够或参数非法返回 false 且不改分区。**不发事件**：调用方是战斗流程，那时背包面板关着；
        /// 落盘由战斗结束后剧情回写触发的保存请求带上（分区每次现取，见本类注释）。
        /// </summary>
        public bool TryConsume(int itemId, int count = 1)
        {
            if (!LootRules.Consume(Data, itemId, count))
            {
                telemetry.TrackWarn("consume_rejected", TelemetryProps.Of(("item", itemId), ("count", count)));
                return false;
            }

            telemetry.Track("item_consumed", ("item", itemId), ("count", count));
            return true;
        }

        /// <summary>
        /// 按怪物种类结算掉落：把 <paramref name="dropItemIds"/> 累加进背包、弹一次奖励通知、发布
        /// <see cref="MonsterDroppedEvent"/> 并埋点。成功顺序与开箱一致（写分区 → 通知 → 事件 → 埋点）。
        /// <para>
        /// <b>调用方负责「一只怪只调一次」</b>：怪物掉落没有箱子键那种天然幂等键（同一只种类会死很多次，每次都要掉），
        /// 所以本方法不做去重。死亡结算点（<c>EncounterStep</c> 的胜利结果）一场遭遇只会结算一次，
        /// 在那之后调它就是安全的（后续波次接死亡掉落时按这个约束调）。
        /// </para>
        /// <para>
        /// <paramref name="dropItemIds"/> 由调用方从妖物表的 <c>drop_items</c> 取（<c>Game.Monster</c> 侧经
        /// <c>YaoCatalog.DropItemsOf</c>）：本服务不认识妖物表，也不认识道具类别，只认一批 item id。
        /// </para>
        /// </summary>
        /// <returns>真的加了至少一件物品返回 true；没有掉落、参数非法或物品表里查不到这些 id 时返回 false。</returns>
        public bool SettleMonsterDrop(int yaoId, IReadOnlyList<int> dropItemIds)
        {
            if (yaoId <= 0 || dropItemIds == null || dropItemIds.Count == 0)
            {
                telemetry.Track("monster_drop_empty", ("yao", yaoId));
                return false;
            }

            if (!MonsterLootRules.Grant(Data, yaoId, dropItemIds))
            {
                telemetry.Track("monster_drop_rejected", ("yao", yaoId));
                return false;
            }

            // 逐件检查 id 能不能在 tbitem 里查到，查不到只 Warn 不拦：怪物掉落是生成物的引用，
            // 指错了（例如掉了 item 表里没有的 id）必须留下线索，但它不该让整条掉落路径崩掉——
            // 拦下来的话玩家会看到「怪死了什么都没掉」，比「日志里一条 Warn + 背包里一个 #id」更难查。
            WarnUnknownItems(dropItemIds);
            notifications.Show(config.RewardTitle, ComposeMonsterDropBody(dropItemIds));
            monsterDroppedPublisher.Publish(new MonsterDroppedEvent(yaoId, dropItemIds));
            telemetry.Track("monster_dropped", ("yao", yaoId), ("kinds", dropItemIds.Count));
            return true;
        }

        /// <summary>
        /// 拼怪物掉落的通知正文：逐件「物品名 ×1」用顿号连起来（每种种类的 drop_items 是「掉哪些件」，
        /// 每件一次，没有数量列）。名字查 tbitem，查不到用「#id」——同 <see cref="ComposeBody"/> 的容错口径。
        /// </summary>
        public string ComposeMonsterDropBody(IReadOnlyList<int> dropItemIds)
        {
            if (dropItemIds == null || dropItemIds.Count == 0) return string.Empty;
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < dropItemIds.Count; i++)
            {
                if (dropItemIds[i] <= 0) continue;
                if (builder.Length > 0) builder.Append('、');
                builder.Append(ItemName(dropItemIds[i])).Append(" ×1");
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            // 无订阅、无非托管资源；实现 IDisposable 是为与其它门面服务一致，容器托管释放。
        }

        /// <summary>
        /// 按格式拼通知正文：{0} = 物品名，{1} = 数量。格式为空时退回「名 ×数」；格式写坏时退回「格式原文 + 名 ×数」而不抛
        /// （文案是策划在 Inspector 里改的，写错不该让开箱路径跟着崩，同 QuestNotificationPresenter.Compose）。
        /// </summary>
        public static string ComposeBody(string format, string itemName, int count)
        {
            string name = itemName ?? string.Empty;
            if (string.IsNullOrEmpty(format)) return name + " ×" + count;
            try
            {
                return string.Format(format, name, count);
            }
            catch (FormatException)
            {
                return format + name + " ×" + count;
            }
        }

        // 查 tbitem 名字；表未就绪（抛 InvalidOperationException）或查不到时用 "#id"。
        private string ItemName(int itemId)
        {
            try
            {
                global::cfg.Item item = configService.Tables.TbItem.GetOrDefault(itemId);
                if (item != null && !string.IsNullOrEmpty(item.Name)) return item.Name;
            }
            catch (InvalidOperationException e)
            {
                Log.Warn($"LootService：配置表未就绪，物品 {itemId} 用 id 代替名字：{e.Message}");
            }

            return "#" + itemId;
        }

        // 只报不改：见 SettleMonsterDrop 里调用点的注释。
        private void WarnUnknownItems(IReadOnlyList<int> dropItemIds)
        {
            try
            {
                for (int i = 0; i < dropItemIds.Count; i++)
                {
                    int itemId = dropItemIds[i];
                    if (itemId > 0 && configService.Tables.TbItem.GetOrDefault(itemId) == null)
                    {
                        Log.Warn($"LootService：怪物掉落的物品 {itemId} 不在 tbitem 里，背包里会记成一个查不到名字的 id。"
                                 + "检查 tbyao 的 drop_items 与 Tables/Data/item.xlsx。");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // 表没就绪时 ItemName 已经 Warn 过一次，这里不重复刷屏。
            }
        }
    }
}

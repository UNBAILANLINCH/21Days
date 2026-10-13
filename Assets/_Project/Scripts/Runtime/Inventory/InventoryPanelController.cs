// 职责：背包面板的会话控制——开关面板、开着期间持世界暂停令牌并关 Gameplay 输入图、把背包数据（LootService.Items + 物品表）
//   经 InventoryRules 绑到面板、处理筛选与选中；开着时订阅开箱 / 重置 / 背包新增事件刷新；
//   另承接「使用」与「合成」两个动作（调用落在 ItemUseService / ItemCraftService，结果经 INotificationService 告诉玩家）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：QuestPanelController 绑的是任务服务与任务事件。
//   2. 扩展不行：InventoryPanelView 只显示不注入服务；LootService 是数据门面，不该依赖 UI / 暂停 / 输入。
//   会话收尾（返回、Esc 外部关闭、作用域销毁）照 QuestPanelController 的写法。
// 使用 / 合成为什么挂在控制器而不是各写一个入口点：
//   这两个动作的触发点就是本面板上的按钮（05_皮面具与道具.md:219 还写着「使用要不要按键、从背包还是快捷栏用」未定），
//   做成入口点会先替策划定下快捷键；将来加快捷栏时由那个入口调同一个 ItemUseService 即可，本类不用动。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Config;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Loot;
using MessagePipe;

namespace Game.Inventory
{
    /// <summary>
    /// 背包面板控制器（根作用域单例）。面板开着时世界暂停、Gameplay 输入图关闭；关闭时只恢复进来之前的状态。
    /// </summary>
    public sealed class InventoryPanelController : IDisposable
    {
        /// <summary>「使用」结果通知的标题。</summary>
        private const string UseTitle = "使用道具";

        /// <summary>「合成」结果通知的标题。</summary>
        private const string CraftTitle = "合成";

        private readonly LootService loot;
        private readonly IConfigService config;
        private readonly IUIService ui;
        private readonly IWorldPauseService pause;
        private readonly IInputService input;
        private readonly ItemUseService useService;
        private readonly ItemCraftService craftService;
        private readonly INotificationService notifications;
        private readonly ISubscriber<CrateCollectedEvent> collected;
        private readonly ISubscriber<LootResetEvent> reset;
        private readonly ISubscriber<BackpackChangedEvent> backpackChanged;
        private readonly ITelemetryScope telemetry;
        private readonly IItemEffectTarget effectTarget;
        private readonly List<InventoryEntry> buffer = new List<InventoryEntry>();

        private InventoryPanelView view;
        private IDisposable pauseToken;
        private IDisposable eventSubscription;
        private bool gameplayWasEnabled;
        private bool opening;
        private bool closing;
        private bool disposed;
        private bool tableWarned;
        private int selectedId;

        // 「合成」按钮这一次要合哪件产物：通常就是选中项本身；**选中原料**时是那条配方的产物
        // （见 ItemCraftService.FindRecipeUsingIngredient 的注释——列表只列已持有的东西，
        //   而 AlreadyOwned 又拒绝重复合成，不给这条路闭环就不成立）。
        private int craftProductId;
        private InventoryFilter filter = InventoryFilter.All;

        public InventoryPanelController(LootService loot, IConfigService config, IUIService ui, IWorldPauseService pause,
            IInputService input, ItemUseService useService, ItemCraftService craftService,
            INotificationService notifications, ISubscriber<CrateCollectedEvent> collected,
            ISubscriber<LootResetEvent> reset, ISubscriber<BackpackChangedEvent> backpackChanged,
            IItemEffectTarget effectTarget, ITelemetryScope telemetry)
        {
            this.loot = loot ?? throw new ArgumentNullException(nameof(loot));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.pause = pause ?? throw new ArgumentNullException(nameof(pause));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.useService = useService ?? throw new ArgumentNullException(nameof(useService));
            this.craftService = craftService ?? throw new ArgumentNullException(nameof(craftService));
            this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            this.collected = collected ?? throw new ArgumentNullException(nameof(collected));
            this.reset = reset ?? throw new ArgumentNullException(nameof(reset));
            this.backpackChanged = backpackChanged ?? throw new ArgumentNullException(nameof(backpackChanged));
            // 玩家生命口由 InventoryInstaller 传进来（PlayerItemEffectTarget）。允许 null 是为了测试与
            // 将来「没有玩家的场景」：需要它的效果会明确报「治疗口还没接上」而不是静默失败，面板其余部分照常可用。
            this.effectTarget = effectTarget;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>面板是否开着（打开流程走完、关闭流程未开始收尾）。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>正在打开或已开着——给热键防连按用。</summary>
        public bool IsBusy => IsOpen || opening || closing;

        /// <summary>打开背包面板。已开或正在开时直接返回。每次打开回到「全部」档、选中第一条。</summary>
        public async UniTask OpenAsync(CancellationToken ct = default)
        {
            if (disposed || IsBusy) return;
            opening = true;
            try
            {
                InventoryPanelView opened = await ui.OpenAsync<InventoryPanelView>(ct: ct);
                // await 期间 Dispose 可能已执行：此时不收尾就会留下没人关的面板。
                if (disposed)
                {
                    await ui.CloseAsync(opened);
                    return;
                }

                view = opened;
                pauseToken = pause.Acquire(this);
                // 只恢复进来之前的状态：Gameplay 图本来就关着时，关面板后不擅自打开。
                bool hasInput = input.Actions != null; // lint-ok: 只判动作集是否已创建，不读设备输入、不影响回放
                gameplayWasEnabled = hasInput && input.Actions.Gameplay.enabled; // lint-ok: 只读动作图启用状态用于收尾恢复，不读设备输入、不影响回放
                if (hasInput) input.DisableMap(InputService.GameplayMap);

                view.OnFilterChanged += HandleFilterChanged;
                view.OnEntrySelected += HandleEntrySelected;
                view.OnClose += HandleCloseRequested;
                view.OnUseRequested += HandleUseRequested;
                view.OnCraftRequested += HandleCraftRequested;
                view.OnClosed += HandleViewClosed;

                IsOpen = true;
                filter = InventoryFilter.All;
                selectedId = 0; // Refresh 会落到列表第一项
                Refresh();

                // 订阅句柄必须托管（EventConventions.cs 第 5 条）；面板开着时列表跟着开箱 / 重置 / 入包变。
                DisposableBagBuilder bag = DisposableBag.CreateBuilder();
                collected.Subscribe(_ => Refresh()).AddTo(bag);
                reset.Subscribe(_ => Refresh()).AddTo(bag);
                backpackChanged.Subscribe(_ => Refresh()).AddTo(bag);
                eventSubscription = bag.Build();

                telemetry.Track("inventory_opened", ("count", buffer.Count));
            }
            finally
            {
                opening = false;
            }
        }

        /// <summary>关闭背包面板：关 UI → 恢复输入图 → 释放暂停令牌 → 退订。没开时空操作。</summary>
        public async UniTask CloseAsync()
        {
            if (!IsOpen || closing || view == null) return;
            closing = true;
            InventoryPanelView target = view;
            Detach(target);
            try
            {
                await ui.CloseAsync(target);
            }
            finally
            {
                ReleaseSession("button");
                closing = false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            InventoryPanelView target = view;
            bool wasOpen = IsOpen;
            ReleaseSession("dispose");
            if (target != null)
            {
                Detach(target);
                if (wasOpen) CloseViewAsync(target).Forget();
            }
        }

        // 刷新筛选标记、列表与详情；只在打开、筛选 / 选中变化、开箱 / 重置事件时调用，不每帧。
        private void Refresh()
        {
            if (view == null) return;
            InventoryRules.Build(loot.Items, TryGetTable(), filter, buffer);

            int index = -1;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (buffer[i].Id == selectedId)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0 && buffer.Count > 0) index = 0;
            selectedId = index < 0 ? 0 : buffer[index].Id;

            view.SetFilter(filter);
            view.SetList(buffer, selectedId);
            view.SetDetail(index >= 0, index >= 0 ? buffer[index] : default);
            RefreshActions(index >= 0 ? buffer[index].Id : 0);
        }

        // 两个动作按钮与说明：可点性按「有没有执行者 / 原料够不够」算，说明写清为什么不能点。
        // 说明文案全中文短句，与失败通知同一套口径（不许只留结果码）。
        private void RefreshActions(int itemId)
        {
            if (view == null) return;
            if (itemId <= 0)
            {
                craftProductId = 0;
                view.SetActions(false, false, string.Empty, string.Empty);
                return;
            }

            string useHint = string.Empty;
            bool canUse = false;
            if (useService.CanUse(itemId))
            {
                canUse = useService.HasExecutor(itemId);
                useHint = canUse ? string.Empty : "这件道具的效果还没有接上（皮 / 面具类效果归「换皮与附身」后续波次）";
            }

            string craftHint = string.Empty;
            bool canCraft = false;
            ItemRecipe recipe = craftService.FindRecipe(itemId);
            if (recipe == null)
            {
                // 选中的不是产物、而是**某条配方的原料**：合成那条配方的产物。
                // 没有这一步，「选中产物才合成、而列表只列已持有的东西」会与 AlreadyOwned 组成死循环。
                // 说明文案里的「原料 → 产物」那根箭头本来就把去向写清了，不必额外标注。
                recipe = craftService.FindRecipeUsingIngredient(itemId);
            }

            craftProductId = recipe != null ? recipe.ProductId : 0;
            if (recipe != null)
            {
                canCraft = true;
                craftHint = ComposeRecipeHint(recipe);
            }
            else
            {
                // 「背包里没有可合成项」必须说清楚，不能只留一个灰按钮（空白面板 / 点了没反应都算静默失败）。
                craftHint = "这件东西没有配方";
            }

            view.SetActions(canUse, canCraft, useHint, craftHint);
        }

        // 配方说明：原料清单 + 每件现有 / 需要几件。缺料不是「禁用」，而是提示差哪件（玩家要看得懂差什么）。
        private string ComposeRecipeHint(ItemRecipe recipe)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append("合成：");
            IReadOnlyList<ItemStack> rows = recipe.Ingredients;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) builder.Append(" + ");
                builder.Append(ItemName(rows[i].ItemId)).Append(" ×").Append(rows[i].Count);
            }

            builder.Append(" → ").Append(ItemName(recipe.ProductId));
            for (int i = 0; i < rows.Count; i++)
            {
                int itemId = rows[i].ItemId;
                if (SeenEarlier(rows, i, itemId)) continue;
                int owned = Owned(itemId);
                if (owned < recipe.RequiredCount(itemId)) builder.Append("（缺 ").Append(ItemName(itemId)).Append('）');
            }

            if (craftService.ConsumesIngredients) builder.Append("（扣原料）");
            return builder.ToString();
        }

        private int Owned(int itemId)
        {
            IReadOnlyDictionary<int, int> items = loot.Items;
            return items.TryGetValue(itemId, out int owned) ? owned : 0;
        }

        private static bool SeenEarlier(IReadOnlyList<ItemStack> rows, int index, int itemId)
        {
            for (int i = 0; i < index; i++)
            {
                if (rows[i].ItemId == itemId) return true;
            }

            return false;
        }

        // 物品名查 tbitem；表未就绪（TryGetTable 已经 Warn 过一次）或查不到时用「#id」，与列表口径一致。
        private string ItemName(int itemId)
        {
            global::cfg.TbItem table = TryGetTable();
            global::cfg.Item item = table == null ? null : table.GetOrDefault(itemId);
            return item == null || string.IsNullOrEmpty(item.Name) ? "#" + itemId : item.Name;
        }

        // 表未就绪（直接 Play 玩法场景、或启动失败）时不崩：全部按「#id」显示，只记一次 Warn。
        private global::cfg.TbItem TryGetTable()
        {
            try
            {
                return config.Tables.TbItem;
            }
            catch (InvalidOperationException e)
            {
                if (!tableWarned)
                {
                    tableWarned = true;
                    Log.Warn($"InventoryPanelController：配置表未就绪，背包条目用 id 代替名字：{e.Message}");
                    telemetry.TrackWarn("table_not_ready", TelemetryProps.Of(("reason", e.GetType().Name)));
                }

                return null;
            }
        }

        private void HandleFilterChanged(InventoryFilter value)
        {
            if (value == filter) return;
            filter = value;
            telemetry.Track("filter_changed", ("filter", value.ToString()));
            Refresh();
        }

        private void HandleEntrySelected(int id)
        {
            if (id == selectedId) return;
            selectedId = id;
            Refresh();
        }

        private void HandleCloseRequested() => CloseRequestedAsync().Forget();

        // 点「使用」：用当前选中的那件。判定 / 执行 / 扣件数都在 ItemUseService 里，本方法只负责提示与刷新。
        private void HandleUseRequested()
        {
            if (selectedId <= 0)
            {
                notifications.Show(UseTitle, "先在左边选一件道具");
                return;
            }

            ItemUseOutcome outcome = useService.TryUse(selectedId, effectTarget);
            if (outcome.IsOk)
            {
                notifications.Show(UseTitle, ComposeUseBody(selectedId, outcome));
            }
            else
            {
                notifications.Show(UseTitle, DescribeUseFailure(selectedId, outcome));
            }

            Refresh();
        }

        // 点「合成」：合 RefreshActions 算出来的那件产物（通常是选中项；选中原料时是那条配方的产物）。
        // 原料不够 / 没有配方 / 已经有一件 都在这里落成中文短句。
        private void HandleCraftRequested()
        {
            if (craftProductId <= 0)
            {
                notifications.Show(CraftTitle,
                    selectedId <= 0 ? "先在左边选一件要合成的东西" : ItemName(selectedId) + " 没有配方，也没有用到它的配方");
                return;
            }

            ItemCraftResult result = craftService.TryCraft(craftProductId);
            if (result.IsOk)
            {
                notifications.Show(CraftTitle,
                    "合成了 " + ItemName(result.Product.ItemId) + " ×" + result.Product.Count
                    + (craftService.ConsumesIngredients ? "，原料已消耗" : "，原料留着"));
            }
            else
            {
                notifications.Show(CraftTitle, DescribeCraftFailure(craftProductId, result));
            }

            Refresh();
        }

        private string ComposeUseBody(int itemId, ItemUseOutcome outcome)
        {
            string name = ItemName(itemId);
            if (outcome.Effect.Status == ItemEffectStatus.Applied && outcome.Effect.Amount > 0)
            {
                return "用了 " + name + "，回复了 " + outcome.Effect.Amount + " 点生命";
            }

            return "用了 " + name;
        }

        // 失败一律给中文短句；结果码只进埋点（埋点在服务里），不给玩家看。
        private string DescribeUseFailure(int itemId, ItemUseOutcome outcome)
        {
            string name = ItemName(itemId);
            switch (outcome.Status)
            {
                case ItemUseStatus.UnknownItem:
                    return name + " 用不了（这件道具还没有配置效果）";
                case ItemUseStatus.InvalidSpec:
                    return name + " 的使用配置写坏了（效果标识为空或件数非正）";
                case ItemUseStatus.NotEnough:
                    return "背包里没有 " + name + "（或件数不够）";
                case ItemUseStatus.NoExecutor:
                    return name + " 的效果还没有接上";
                case ItemUseStatus.EffectRejected:
                    return string.IsNullOrEmpty(outcome.Effect.Reason) ? "现在用不了 " + name : outcome.Effect.Reason;
                default:
                    return name + " 用不了";
            }
        }

        private string DescribeCraftFailure(int productId, ItemCraftResult result)
        {
            string name = ItemName(productId);
            switch (result.Status)
            {
                case ItemCraftStatus.NoRecipe:
                    return name + " 没有配方";
                case ItemCraftStatus.AlreadyOwned:
                    return "背包里已经有一件 " + name + " 了";
                case ItemCraftStatus.MissingIngredient:
                    return "原料不够：还缺 " + ItemName(result.MissingItemId);
                case ItemCraftStatus.EmptyIngredients:
                case ItemCraftStatus.InvalidRecipe:
                    return name + " 的配方写坏了";
                default:
                    return "合不出 " + name;
            }
        }

        // 面板被 UIService 从外部关掉（没经过 CloseAsync，如 Esc）：只收尾会话资源，不再调 ui.CloseAsync。
        private void HandleViewClosed()
        {
            if (closing || view == null) return;
            Detach(view);
            ReleaseSession("external");
        }

        private async UniTaskVoid CloseRequestedAsync()
        {
            try
            {
                await CloseAsync();
            }
            catch (Exception e)
            {
                telemetry.TrackError("panel_close_failed", e);
                Log.Error($"InventoryPanelController：关闭背包面板失败：{e}");
            }
        }

        private void Detach(InventoryPanelView target)
        {
            target.OnFilterChanged -= HandleFilterChanged;
            target.OnEntrySelected -= HandleEntrySelected;
            target.OnClose -= HandleCloseRequested;
            target.OnUseRequested -= HandleUseRequested;
            target.OnCraftRequested -= HandleCraftRequested;
            target.OnClosed -= HandleViewClosed;
        }

        // 关闭的同步部分：恢复输入图（仅当进来前是开的）、释放暂停令牌、退订事件。幂等。
        private void ReleaseSession(string reason)
        {
            bool wasOpen = IsOpen;
            if (wasOpen && gameplayWasEnabled) input.EnableMap(InputService.GameplayMap);
            gameplayWasEnabled = false;
            if (pauseToken != null)
            {
                pauseToken.Dispose();
                pauseToken = null;
            }

            if (eventSubscription != null)
            {
                eventSubscription.Dispose();
                eventSubscription = null;
            }

            view = null;
            IsOpen = false;
            if (wasOpen) telemetry.Track("inventory_closed", ("reason", reason), ("filter", filter.ToString()));
        }

        // 作用域销毁时 UIService 往往已先释放，关面板会抛 ObjectDisposedException——那时面板已随 UIRoot 一并销毁，静默即可。
        private async UniTaskVoid CloseViewAsync(InventoryPanelView target)
        {
            try
            {
                await ui.CloseAsync(target);
            }
            catch (ObjectDisposedException)
            {
                // 见方法注释。
            }
            catch (Exception e)
            {
                Log.Warn($"InventoryPanelController：关闭背包面板失败：{e.Message}");
            }
        }
    }
}

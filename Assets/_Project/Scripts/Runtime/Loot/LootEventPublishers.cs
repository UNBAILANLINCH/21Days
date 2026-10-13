// 职责：Loot 模块四个对外事件发布器的**组合参数**——把 LootService 构造里连着四个 IPublisher<T> 的参数收成一件东西。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有任何「一组发布器」的形状；每个门面各自列参数。
//   2. 扩展不行：继续往 LootService 构造上直接加第 5 个 IPublisher<T> 参数时，**每一处构造点都要跟着改**
//      （本工程现有 4 处测试构造点：LootServiceTests / LootBattleBackpackTests / MirrorServiceTests /
//      MirrorInputPresenterTests）——2026-10-07 加 BackpackChangedEvent 时正是踩了这一点。
//      收成一个组合参数后，再加事件只动本文件与安装器，测试构造点不动。
// 不用「多个构造函数取参数最多的那个」那种兼容手法：VContainer 取参数最多的构造函数、且不看默认值，
//   留一个旧签名的重载会让容器与调用方各挑一个，出问题时很难看出走的是哪条（同 PlayerRules.UseKnockdown 的注释口径）。
using MessagePipe;

namespace Game.Loot
{
    /// <summary>
    /// <see cref="LootService"/> 要发布的四个事件。全部只读，四个都必填（缺发布器没有合理默认值）。
    /// </summary>
    public sealed class LootEventPublishers
    {
        /// <param name="crateCollected">开箱成功。</param>
        /// <param name="monsterDropped">怪物掉落已入背包。</param>
        /// <param name="lootReset">进度整体清空。</param>
        /// <param name="backpackChanged">背包新增物品（合成产物等）。</param>
        public LootEventPublishers(
            IPublisher<CrateCollectedEvent> crateCollected,
            IPublisher<MonsterDroppedEvent> monsterDropped,
            IPublisher<LootResetEvent> lootReset,
            IPublisher<BackpackChangedEvent> backpackChanged)
        {
            CrateCollected = crateCollected ?? throw new System.ArgumentNullException(nameof(crateCollected));
            MonsterDropped = monsterDropped ?? throw new System.ArgumentNullException(nameof(monsterDropped));
            LootReset = lootReset ?? throw new System.ArgumentNullException(nameof(lootReset));
            BackpackChanged = backpackChanged ?? throw new System.ArgumentNullException(nameof(backpackChanged));
        }

        /// <summary>开箱成功（<see cref="CrateCollectedEvent"/>）。</summary>
        public IPublisher<CrateCollectedEvent> CrateCollected { get; }

        /// <summary>怪物掉落已入背包（<see cref="MonsterDroppedEvent"/>）。</summary>
        public IPublisher<MonsterDroppedEvent> MonsterDropped { get; }

        /// <summary>进度整体清空（<see cref="LootResetEvent"/>）。</summary>
        public IPublisher<LootResetEvent> LootReset { get; }

        /// <summary>背包新增物品（<see cref="BackpackChangedEvent"/>）。</summary>
        public IPublisher<BackpackChangedEvent> BackpackChanged { get; }
    }
}

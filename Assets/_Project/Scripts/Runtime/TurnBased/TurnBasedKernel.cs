// 职责：回合制作战内核的统一入口——把配置与随机源装配起来，按「进入战斗判定」开一场仗。
//
// 为什么新建（复用 → 扩展 → 新建）：
// - 复用：工程里没有回合制相关的既有类型（`09_BOSS战.md:278`「工程侧一行代码都没有，要做等于新开一个战斗模块」）。
// - 扩展：`Runtime/Monster/EncounterStep`（遭遇流程）与 `Runtime/Player/PlayerRules` 都不该长出
//   「怒气 / 招式 / 醉酒四档」这条职责；本次任务也明令不动这两个模块。
// - 新建：以上两条都不成立，故新建本文件，作为接线侧唯一的入口。
//
// 接线建议（本次不改任何既有文件，只给建议；细节见交付报告「建议补丁」）：
// - 进战斗前：由遭遇 / 关卡侧把 `BattleEntryRequest` 的五个快照量填好（潜行、偷袭是否成功、
//   是否在警戒 / 敌对区域、怪物警觉档位、谁先动手），BOSS 的 `BossBattleSnapshot` 用怪物当前生命 +
//   战斗外醉酒值（07:66）。
// - 战斗中：界面按 `BattleSession.Phase` 收输入；每次动作后读 `BattleSession.Events` 做表现
//   （跳过回合的中央闪现文案在 `BattleEvent.HintText`）。
// - 战斗结束后：把 `BattleSession.ExitKey` 交给 `NarrativeRules.CompleteBattle`
//   （唯一写入方，见 `PRP/battle-to-narrative/prp.md` §2.1）；本模块**不写任何 `combat.*` 键**
//   （`ai-docs/docs/story-facts.md` §6.1「一个键一个写入方」，那三个键的写入方是 Monster / Inventory）。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md` 全篇；
// `docs/design/features-spotlight/09_BOSS战.md` §3.9；
// `docs/design/features-spotlight/00_功能总览.md:414` §8.1 #7。

using Game.Core.Simulation;

namespace Game.TurnBased
{
    /// <summary>回合制作战内核。装配好之后是只读的（配置在运行时不该被改）。</summary>
    public sealed class TurnBasedKernel
    {
        /// <summary>
        /// 用纯值设置装配内核。
        /// </summary>
        /// <exception cref="System.ArgumentNullException">随机源为 null（概率必须可注入、可回放）。</exception>
        public TurnBasedKernel(in BattleSettings settings, IRandomStream random, IBattleItemInventory inventory = null)
        {
            if (random == null)
            {
                throw new System.ArgumentNullException(nameof(random), "IRandomStream 不能为空：概率必须可注入、可回放");
            }

            Settings = settings;
            Random = random;
            Inventory = inventory;
        }

        /// <summary>
        /// 用一份配置资产装配内核。
        /// </summary>
        /// <exception cref="System.ArgumentNullException">配置为 null（静默用默认值会让策划改的资产不生效）。</exception>
        public TurnBasedKernel(TurnBasedConfig config, IRandomStream random, IBattleItemInventory inventory = null)
            : this(RequireSettings(config), random, inventory)
        {
            Config = config;
        }

        /// <summary>装配用的配置资产；用纯值设置构造时为 null。</summary>
        public TurnBasedConfig Config { get; }

        /// <summary>本次装配使用的全部数值。</summary>
        public BattleSettings Settings { get; }

        /// <summary>确定性随机源（跳过回合与选招都从它取数）。</summary>
        public IRandomStream Random { get; }

        /// <summary>背包只读口；为 null 时视作「什么道具都没有」（07:42 未拥有则不显示）。</summary>
        public IBattleItemInventory Inventory { get; }

        /// <summary>
        /// 配置自检：返回 null 表示没问题，否则是第一条问题的中文描述。
        /// 建议在接线启动时调一次（配置错误不许静默跑，见 <see cref="TurnBasedConfig.Validate"/>）。
        /// </summary>
        public string Validate() => TurnBasedConfigValidation.Validate(Settings);

        /// <summary>
        /// 按进入战斗的判定开一场仗。
        /// </summary>
        /// <param name="request">局势快照（五个量）。</param>
        /// <param name="player">玩家血量快照。</param>
        /// <param name="boss">BOSS 生命与战斗外醉酒值快照。</param>
        /// <param name="session">开出来的战斗；被拒时为 null。</param>
        /// <param name="reject">被拒原因；成功时为 <see cref="BattleEntryReject.None"/>。</param>
        /// <returns>是否真的进了战斗。</returns>
        public bool TryStartBattle(
            in BattleEntryRequest request,
            in PlayerBattleSnapshot player,
            in BossBattleSnapshot boss,
            out BattleSession session,
            out BattleEntryReject reject)
        {
            session = null;
            reject = BattleEntryReject.None;

            if (boss.Health <= 0)
            {
                // 已经没有生命的 BOSS 不该开战；静默开一场「一进去就赢」的仗是更坏的结果。
                reject = BattleEntryReject.InvalidSnapshot;
                return false;
            }

            BattleEntryDecision decision = BattleEntryRules.Decide(request, Settings.Entry);
            if (!decision.Accepted)
            {
                reject = decision.Reject;
                return false;
            }

            session = new BattleSession(decision, Settings, player, boss, Random, Inventory);
            return true;
        }

        private static BattleSettings RequireSettings(TurnBasedConfig config)
        {
            if (config == null)
            {
                throw new System.ArgumentNullException(nameof(config), "TurnBasedConfig 不能为空");
            }

            return config.Settings;
        }
    }
}

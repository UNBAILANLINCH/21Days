// 职责：开一场仗——按 BOSS 定义做玩家快照（本场玩家生命，满血）与 BOSS 快照（满血 + 战斗外醉酒值），
//   配好随机源与背包口，按**现场局势快照**（有就给、没有就按「正面攻击」）进 TurnBasedKernel。
// 为什么新建：这是 turnbased-module-guide「接线清单」里血量快照 / 背包口两行的落点；内核不认识 Player / Loot（依赖方向），
//   BattleFlow 管时序，把「怎么拼快照」拆出来，流程测试能直接换数值。
//
// 进战斗方式（2026-10-08，S 组「偷袭 / 被打两种开战入口」）：
//   `TryCreate(boss, context, ...)` 收一个 `Game.Monster.IBattleEntryContext`（实现方是 `EncounterStep`），
//   把它的五个量翻成 `BattleEntryRequest`，于是 07 的偷袭（`BattleEntryKind.SneakAttack`，BOSS −20% 生命）
//   与被打（`Ambushed`，BOSS 先手）两条分支在生产侧第一次有了入口。
//   **context 为 null 时退回接线前那句硬编码请求**（正面攻击、玩家先手），独立原型场景与纯 Battle 测试作用域
//   走的正是那一支——这条缺省行为有负对照测试钉住（`BattleSetupTests`）。
//
// 玩家血量（W2a 主窗口定，改掉 W1 的「读 PlayerModel」）：战斗血量与探索血量分开。探索里玩家只有 3 血，BOSS 重击 3、
//   药水 30% 取整为 0，演示不出东西；07 没写玩家血量，也没说与探索血量挂钩。所以改读 BossDefinition.PlayerHealth
//   （占位 10，等 C91），开战满血、战后不回写（PRP D10 的「不回写」照旧）。
//
// 随机源（PRP D8）：**不用 logic.* 流**。IRandomService 的约定只有两类前缀——logic.*（进快照、参与状态哈希）和
//   view.*（纯表现、多抽少抽都无所谓）。回合制战斗在确定性内核之外推进（不在 tick 里、会话不进快照），
//   用 logic.* 会让回放在战斗之后哈希对不上；用 view.* 又违背「表现流不影响逻辑」的语义。两个前缀都不合适，
//   所以按 D8 的退路**从会话主种子派生一条本地流**：每场 new XorShiftRandomStream(主种子 ^ 盐 ^ 场次)，
//   不登记进 IRandomService、不进任何快照。代价：回放不覆盖回合制战斗（模块 guide 要写明）。
//
// 临时换数值（W2b）：OverrideSettings 给回放 / 调试换一份 BattleSettings（BattleSettings 文件头「接线侧也能在运行时按阶段
//   换一份设置」的落点），不改 TurnBasedConfig 资产；Battle 回放用它把 BOSS 招式定死、伤害调高 / 调低，让胜负可控。
//   本地随机流没有测试口（种子取会话主种子），所以「可控」靠数值而不是靠种子。表现层（BattleScenePresenter）仍拿装配时的
//   那份数值画招式说明，只换 BOSS 招式时两边不矛盾。
//   防线（审查 WARN 1）：它是回放专用的测试口，却必须 public（见方法注释），所以靠两道闸防误用——正式包（非 Debug.isDebugBuild）
//   一调就抛；编辑器 / 开发版里每场开打时若处于替换状态，BattleFlow 埋一条 W 级 battle/settings_overridden，日志里一眼能看出
//   「这一仗用的不是配置资产的数值」。
using System;
using Game.Core.Simulation;
using Game.TurnBased;

namespace Game.Battle
{
    /// <summary>开仗装配（根作用域单例）。</summary>
    public sealed class BattleSetup
    {
        /// <summary>本地战斗流的盐：只为让它与主种子派生的其它流错开，取值本身无含义（XorShift 构造会再雪崩一遍）。</summary>
        private const ulong BattleStreamSalt = 0x7B1D_5E2A_C4F0_9B63UL;

        private readonly BattleSettings settings;
        private readonly Func<ulong> seedSource;
        private readonly IBattleItemInventory inventory;
        private ulong battleOrdinal;
        private SettingsOverride activeOverride;
        private Func<string, int?> drunkSource;

        /// <summary>场外醉酒快照来源；卸载调用方时传 null 恢复配置默认值，不修改配置资产。</summary>
        public void BindDrunkSource(Func<string, int?> source) => drunkSource = source;

        /// <param name="settings">回合制数值（TurnBasedConfig.Settings）。</param>
        /// <param name="seedSource">会话主种子（RandomService.MasterSeed）；每场开打时现读。</param>
        /// <param name="inventory">背包口（BattleItemInventory）。</param>
        public BattleSetup(in BattleSettings settings, Func<ulong> seedSource, IBattleItemInventory inventory)
        {
            this.settings = settings;
            this.seedSource = seedSource ?? throw new ArgumentNullException(nameof(seedSource));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        /// <summary>开仗实际用的回合制数值：有临时换数值时是换上的那份，否则是装配时的那份。</summary>
        public BattleSettings Settings => activeOverride != null ? activeOverride.Settings : settings;

        /// <summary>当前是否处于 <see cref="OverrideSettings"/> 换上的临时数值下（BattleFlow 开打时据此埋警告）。</summary>
        public bool IsOverridden => activeOverride != null;

        /// <summary>
        /// **回放专用的测试口**：临时换一份回合制数值（Battle 回放用它把 BOSS 伤害调高演「被击倒」），**不改任何配置资产**；
        /// 只影响之后开打的仗，已经开打的那一场不变。返回的句柄 Dispose 即恢复装配时的数值（幂等）；
        /// 同一时刻只容一份，上一份没撤又换会抛。数值不自洽时开仗照常被 <see cref="TryCreate"/> 拒绝。
        /// <para>
        /// 为什么是 public：调用方是 Showcase 程序集（Game.Tests.Showcase 从容器取 BattleSetup），而 Game.Runtime 不对测试程序集开
        /// InternalsVisibleTo（工程约定，先例 QuestHudPresenter.SwitchIconOverride），internal 就调不到。可见性守不住，就用两道闸：
        /// 正式包（<c>Debug.isDebugBuild</c> 为 false）调用直接抛 <see cref="InvalidOperationException"/>；编辑器 / 开发版里
        /// 每场开打时若处于替换状态，BattleFlow 埋 W 级 <c>battle/settings_overridden</c>。玩法代码不得调用。
        /// </para>
        /// </summary>
        public IDisposable OverrideSettings(in BattleSettings replacement)
        {
            if (!UnityEngine.Debug.isDebugBuild)
                throw new InvalidOperationException("BattleSetup.OverrideSettings 是回放专用的测试口，正式包里不许换战斗数值");
            if (activeOverride != null) throw new InvalidOperationException("BattleSetup：已经换了一份临时数值，先 Dispose 上一份");
            activeOverride = new SettingsOverride(this, replacement);
            return activeOverride;
        }

        /// <summary>
        /// 按 BOSS 定义开一场「正面攻击、玩家先手」的仗（PRP U2：走近按 E = 07「正面攻击」）。
        /// 失败（配置不自洽 / 本场玩家生命非正 / 进战斗判定被拒）返回 false 并给出中文原因。
        /// 玩家生命取 <see cref="BossDefinition.PlayerHealth"/>，满血开打，不读也不写探索血量。
        /// <para>
        /// 这是**没有局势快照时**的入口（独立原型场景、纯 Battle 测试作用域，以及接线前的调用方）：
        /// 请求写死成「正面攻击、玩家先手」，与接线前逐字一致。正式流程走
        /// <see cref="TryCreate(BossDefinition, Game.Monster.IBattleEntryContext, out BattleSession, out string)"/>。
        /// </para>
        /// </summary>
        public bool TryCreate(BossDefinition boss, out BattleSession session, out string error) =>
            TryCreate(boss, null, out session, out error);

        /// <summary>
        /// 按**现场局势快照**开一场仗——07 的三种进入方式（偷袭 / 正面攻击 / 被打）从这里进去。
        /// <para>
        /// <paramref name="context"/> 为 null 时退回「正面攻击、玩家先手」的硬编码请求，
        /// 行为与 <see cref="TryCreate(BossDefinition, out BattleSession, out string)"/> 逐字相同
        /// （**这条缺省行为有负对照测试钉住**：没有遭遇侧的快照时，开战方式不许变）。
        /// </para>
        /// <para>
        /// 翻译职责在这里：`Game.Monster` 只交出五个量（<see cref="Game.Monster.IBattleEntryContext"/>），        /// `Game.TurnBased` 只回答「进不进 / 怎么进 / 谁先手 / 扣多少血」（<see cref="BattleEntryRequest"/>），
        /// 本类负责把前者拼成后者——两个模块都不认识对方。
        /// </para>
        /// <para>
        /// ⚠️ **它不解决剧情身份**：开战之后回写走的是 `BattleStageEnteredEvent` 的
        /// generation / activationId / targetId，与 `EncounterStep.EncounterId` 不是同一套 id
        /// （见 `Game.Monster.IBattleEntryContext` 文件头）。本方法只影响「怎么进这场仗」。
        /// </para>
        /// </summary>
        public bool TryCreate(BossDefinition boss, Game.Monster.IBattleEntryContext context,
            out BattleSession session, out string error)
        {
            session = null;
            if (boss == null) throw new ArgumentNullException(nameof(boss));

            BattleSettings current = Settings;
            string issue = TurnBasedConfigValidation.Validate(current);
            if (issue != null)
            {
                error = "回合制配置不自洽（TurnBasedConfig）：" + issue;
                return false;
            }

            int playerHealth = boss.PlayerHealth;
            if (playerHealth <= 0)
            {
                error = "本场玩家生命必须大于 0（BossRosterConfig 里「" + boss.Id + "」的 playerHealth），当前 " + playerHealth;
                return false;
            }

            int drunk = drunkSource?.Invoke(boss.Id) ?? boss.OutOfBattleDrunk;
            if (drunk < 0) { error = "场外醉酒值不可为负"; return false; }
            var kernel = new TurnBasedKernel(current, NextStream(), inventory);
            BattleEntryRequest request = BuildEntryRequest(context);
            if (!kernel.TryStartBattle(request, new PlayerBattleSnapshot(playerHealth, playerHealth),
                    new BossBattleSnapshot(boss.MaxHealth, boss.MaxHealth, drunk), out session, out BattleEntryReject reject))
            {
                error = "进战斗判定被拒：" + BattleEntryRules.Describe(reject);
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 局势快照 → 进战斗请求。**没有快照时**用 07 的「正面攻击」那一支
        /// （在警戒区域内、玩家先动手：`07_回合制作战文档.md:20-23`），与接线前的硬编码请求逐字相同。
        /// </summary>
        private static BattleEntryRequest BuildEntryRequest(Game.Monster.IBattleEntryContext context)
        {
            if (context == null)
            {
                return new BattleEntryRequest(false, false, true, false, MonsterAlertState.Alert, BattleInitiator.PlayerAttacked);
            }

            return new BattleEntryRequest(
                context.PlayerSneaking,
                context.SneakAttackLanded,
                context.PlayerInAlertZone,
                context.PlayerInHostileZone,
                context.MonsterState,
                context.Initiator);
        }

        /// <summary>
        /// 遭遇侧此刻**有没有发言权**——它报的局势够不够构成一次开战理由。
        /// <para>
        /// 为什么需要这一道（2026-10-09 实测踩到，`BattleShowcase` 两条回放全红）：正式流程里唯一的开战触发是
        /// **剧情 Battle 阶段**（demo 是 `Npc_SampleBoss` 的「挑战」，PRP U2 定「走近按 E = 正面攻击」），
        /// 它与巡逻遭遇是两回事。把十几米外巡逻怪的**平静**快照交出去，<see cref="BattleEntryRules"/> 会判
        /// 「不在警戒 / 敌对区域、怪物也未察觉」（07:22、:27）而**把这一仗拒掉**——boss 战直接开不了。
        /// 遭遇平静时它没有发言权，退回硬编码缺省（与 <c>context == null</c> 同一口径）。
        /// </para>
        /// <para>
        /// <b>刻意不含「正在潜行」</b>：潜行着但没偷袭命中，按 07:22/:27 确实是「拒绝开战」——
        /// 那说的是「潜行去袭击这只怪」这件事（<see cref="BattleEntryRules"/> 的规则层不动，那条负对照测试仍在）。
        /// 但剧情 NPC 的「挑战」跟玩家恰好在潜行无关，不该被它拒掉。
        /// </para>
        /// <para>
        /// 够得上发言权的四种：偷袭已命中（07:15-18）、玩家在警戒区域、玩家在敌对区域、怪物处于警戒 / 敌对态
        /// （<see cref="Game.Monster.IBattleEntryContext.MonsterAware"/>，两条等价写法都认）。
        /// </para>
        /// </summary>
        public static bool IsEncounterEngaged(Game.Monster.IBattleEntryContext context)
        {
            if (context == null) return false;
            // ⚠️ 偷袭那一支必须**同时要求此刻正在潜行**（2026-10-09 实测踩到）：
            // `SneakAttackLanded` 读的是持久事实 `stealth.assassinated`——这一局只要处决过谁它就一直是真，
            // 不会衰减。只认它的话，「暗杀过一个目标之后、任何一次不潜行的挑战」都会被判成「遭遇有发言权」，
            // 而请求按现场翻出来是「没潜行 + 不在警戒/敌对」→ 规则落进最后一条分支、用 `NoTrigger` 把整场仗拒掉
            // （现象与 07:22/:27 那条旧回归的报错逐字相同，但成因不同：那次是闸没做，这次是闸太松）。
            // 潜行是**当下状态**，事实是**历史**；两者同时成立才算「此刻正在偷袭」。
            return (context.SneakAttackLanded && context.PlayerSneaking)
                   || context.PlayerInAlertZone
                   || context.PlayerInHostileZone
                   || context.MonsterAware;
        }

        private IRandomStream NextStream()
        {
            battleOrdinal++;
            return new XorShiftRandomStream(seedSource() ^ BattleStreamSalt ^ battleOrdinal);
        }

        /// <summary>一份临时数值的句柄：Dispose 撤掉（只撤自己，幂等）。</summary>
        private sealed class SettingsOverride : IDisposable
        {
            private readonly BattleSetup owner;

            public SettingsOverride(BattleSetup owner, in BattleSettings settings)
            {
                this.owner = owner;
                Settings = settings;
            }

            public BattleSettings Settings { get; }

            public void Dispose()
            {
                if (owner.activeOverride == this) owner.activeOverride = null;
            }
        }
    }
}

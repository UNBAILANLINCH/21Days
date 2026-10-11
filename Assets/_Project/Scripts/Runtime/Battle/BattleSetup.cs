// 职责：开一场仗——按 BOSS 定义做玩家快照（本场玩家生命，满血）与 BOSS 快照（满血 + 战斗外醉酒值），
//   配好随机源与背包口，以「正面攻击」进 TurnBasedKernel。
// 为什么新建：这是 turnbased-module-guide「接线清单」里血量快照 / 背包口两行的落点；内核不认识 Player / Loot（依赖方向），
//   BattleFlow 管时序，把「怎么拼快照」拆出来，流程测试能直接换数值。
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
        /// </summary>
        public bool TryCreate(BossDefinition boss, out BattleSession session, out string error)
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

            var kernel = new TurnBasedKernel(current, NextStream(), inventory);
            // 07 正面攻击：在警戒区域内、玩家先动手 → 玩家先手（BattleEntryRules.cs 的 FrontalAttack 分支）。
            var request = new BattleEntryRequest(false, false, true, false, MonsterAlertState.Alert, BattleInitiator.PlayerAttacked);
            if (!kernel.TryStartBattle(request, new PlayerBattleSnapshot(playerHealth, playerHealth),
                    new BossBattleSnapshot(boss.MaxHealth, boss.MaxHealth, boss.OutOfBattleDrunk), out session, out BattleEntryReject reject))
            {
                error = "进战斗判定被拒：" + BattleEntryRules.Describe(reject);
                return false;
            }

            error = null;
            return true;
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

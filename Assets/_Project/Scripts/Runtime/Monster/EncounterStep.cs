// 职责：在同一固定 tick 中先应用玩家意图，再处理怪物感知与战斗，最后把潜行 / 追逐判定合成事实键。
// 为什么新建：SimulationRunner 只负责调度步骤，Player 与 Monster 的先后属于遭遇玩法。
// 本波（S 组接线，2026-10-07）在原有职责之外接了三件事，全部按「不接线时行为逐字不变」的边界做：
//   1. 身份 → 敌人攻击许可（S1）：身份状态经 BindIdentity 进来，逐 tick 填进 MonsterIntent；
//   2. 潜行 → 事实与追逐（S3/S4）：双方规则推进之后调一次 StealthDecisionGate，结果写进内存事实集
//      （stealth.*），追逐四键由本类（Monster 侧）写，见 SettleChase；
//   3. 战斗结果（C5）：Result 枚举仍然只管流程收尾，新增 BattleSettlement 承载 BattleResult；
//      **胜负两侧都结算**（2026-10-09 用户拍板）：打赢写 Victory，玩家生命归零写 Downed——
//      「一场实时遭遇打赢或被击倒，等于这一关结束」。`SettleBattle` 的两道闸门不变（本场只结一次、default 不收）；
//   4. 掩体贯通（2026-10-08）：BindOcclusion / UseStealth 把 StealthSight 交给每一只巡逻怪，
//      视锥两档（红区 / 橙区）从此吃遮挡，见 MonsterRules.Sense 的文件头；
//   5. 进战斗快照（2026-10-08）：本类实现 IBattleEntryContext，把「潜行 / 偷袭是否命中 / 是否在警戒敌对区域 /
//      怪物警觉档位 / 谁先动手」五个量交给战斗侧，见 IBattleEntryContext.cs；
//   6. 击杀掉落（2026-10-08）：死亡分支调一次 BindDropSink 接进来的 IMonsterDropSink，见 SettleKillDropOnce。
// 装配方式：本类由 VContainer 构造，**构造参数不能变**（TypeAnalyzer 取参数最多的构造函数且不看默认值），
//   所以潜行内核、身份状态、埋点、遮挡、掉落口都走后置装配方法
//   （UseStealth / BindIdentity / UseTelemetry / BindOcclusion / BindDropSink），不装配时用各内核的占位默认值。
using System;
using System.Collections.Generic;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Narrative;
using Game.Player;
using Game.Taming;
using Game.Stealth;
using UnityEngine;

namespace Game.Monster
{
    /// <summary>
    /// 一次遭遇的内存事实集：`stealth.*` / `chase.*` 这些**瞬时态**（字典 §6.2 不入档）的落点。
    /// 写方是遭遇结算（<see cref="EncounterStep"/>），读方是剧情条件（<c>NarrativeConditionSource</c>）。
    /// <para>
    /// 为什么不进存档 / 回放：字典 §6 第 2 条——`stealth.hidden` / `stealth.behind` / `stealth.knockdown`
    /// 只活在内存里，写进 <c>NarrativeSaveData.StoryFlags</c> 会让读档把玩家恢复成「正被击倒」。
    /// </para>
    /// <para>
    /// 额外实现 <see cref="IStealthFactSink"/>（2026-10-07，背后处决波）：`stealth.assassinated` 是
    /// **命中即写的持久事实**，写入方是潜行侧的处决交互（<c>ExecutionInteractor</c> → <c>ExecutionResolver</c>），
    /// 它只认一个「写一个键」的小接口，不必认识 <see cref="EncounterStep"/> 或本类。
    /// 读侧契约 <c>IEncounterFacts</c> 在 Narrative（消费方），写侧契约在 Stealth（生产方），
    /// 两侧各归其主，本类只是两个契约的实现。
    /// </para>
    /// </summary>
    public sealed class EncounterFactLog : IEncounterFacts, IStealthFactSink
    {
        private readonly HashSet<string> facts = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>当前为真的键数量（调试与测试用）。</summary>
        public int Count => facts.Count;

        /// <summary>写一个键。空键忽略（档位键为 <c>KnockdownTier.None</c> 时就是空串，字典 §3.2 不接受空键）。</summary>
        public void Set(string key, bool value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (value)
            {
                facts.Add(key);
            }
            else
            {
                facts.Remove(key);
            }
        }

        /// <summary>这个键此刻是否成立。</summary>
        public bool IsTrue(string key) => !string.IsNullOrEmpty(key) && facts.Contains(key);

        /// <summary>清空（进入 / 离开遭遇时调，避免上一场的事实漏到下一场）。</summary>
        public void Clear() => facts.Clear();

        /// <summary>把当前为真的键追加进 <paramref name="into"/>（不清空它，调用方自己决定）。</summary>
        public void CollectFacts(List<string> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            foreach (string key in facts)
            {
                into.Add(key);
            }
        }
    }

    public sealed class EncounterStep : ISimulationStep, IReplayState, IBattleEntryContext
    {
        public enum Result : byte { None, Victory, Defeat, Aborted }

        /// <summary>
        /// 没有接 <c>StealthConfig</c> 时的追兵速度占位值：必须高于玩家步行 3、不高于奔跑 5，
        /// 否则起追会以配置错误拒绝（`ChaseRules.VerifyChaseSpeed`）；与 `StealthConfig.chaseSpeed` 的默认值一致。
        /// </summary>
        private const float PlaceholderChaseSpeed = 3.6f;

        private readonly PlayerRules player;
        private readonly MonsterRules monster;
        private Vector2[][] authoredRoutes;

        // —— 潜行关口的接线位（S3/S4）。全部给占位默认值：不装配时遮挡体为空、判定与接线前一致。
        private StealthSight sight = new StealthSight();
        private SightSettings sightSettings = SightSettings.PlaceholderDefault;
        private AssassinationRules assassination = new AssassinationRules(AssassinationSettings.PlaceholderDefault);
        private KnockdownRules knockdown = new KnockdownRules(KnockdownSettings.PlaceholderDefault);
        private ChaseRules chase = new ChaseRules(ChaseSettings.PlaceholderDefault, ChaseBaseline.ProjectCurrent);
        private int knockdownLowMax = 1;
        private int knockdownMidMax = 3;
        private ChasePhase chasePhase;
        private IdentityState identity;
        private ITelemetryScope telemetry = NullTelemetryScope.Instance;
        private readonly EncounterFactLog facts = new EncounterFactLog();

        // 击杀掉落口（Game.Loot 实现，本模块只持一个可空引用）：不接时什么都不掉（独立原型场景 / 纯 Monster 测试）。
        private IMonsterDropSink dropSink;

        // 本场遭遇的主怪掉落有没有结过。**一只怪只结一次**由它把关（重复结算会让掉落翻倍）。
        // 不进快照、不进存档：它管的是「这一次死亡有没有结」，Begin 时清零（下一场要能再掉）。
        private bool killDropSettled;

        public EncounterStep(PlayerRules player, MonsterRules monster)
        {
            this.player = player;
            this.monster = monster;
            Taming = player != null && monster != null ? new TamingRules(player, monster, NullTelemetryScope.Instance) : null;
            chase.ConfigureChaseSpeed(PlaceholderChaseSpeed);
            if (player != null) player.UseKnockdown(knockdown);
        }

        public TamingRules Taming { get; }
        public string CurrentControlId => Taming.CurrentControlId;

        public void ConfigureTaming(string playerId, string playerName, string[] ids, string[] names, Vector2[][] routes)
        {
            if (ids == null || names == null || routes == null || ids.Length == 0 || ids.Length > 128
                || ids.Length != names.Length || ids.Length != routes.Length)
                throw new ArgumentException("巡逻者身份和路线数量不匹配");
            var unique = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { playerId };
            for (int i = 0; i < ids.Length; i++)
                if (string.IsNullOrWhiteSpace(ids[i]) || ids[i].Length > 128 || !unique.Add(ids[i]) || string.IsNullOrWhiteSpace(names[i])
                    || routes[i] == null || routes[i].Length == 0 || routes[i].Length > 1024)
                    throw new ArgumentException("场景巡逻者稳定标识缺失或重复：" + ids[i]);
            Taming.Configure(playerId, playerName);
            authoredRoutes = new Vector2[routes.Length][];
            for (int i = 0; i < ids.Length; i++)
            {
                MonsterRules rules = i == 0 ? monster : monster.CreateForActor(ids[i]);
                authoredRoutes[i] = (Vector2[])routes[i].Clone();
                Taming.RegisterTarget(ids[i], names[i], rules);
            }

            // 巡逻者是在这里才造出来的，所以遮挡要在这里补发一次（UseStealth 可能跑在它前面，见 BindOcclusion 注释）。
            ApplyMonsterOcclusion();
        }

        public bool IsActive { get; private set; }
        public long EncounterId { get; private set; }
        public long ActivationId { get; private set; }
        public Result PendingResult { get; private set; }
        public bool ResultConsumed { get; private set; }

        /// <summary>
        /// 战斗结算结果（玩法语义：击倒 / 暴露 / 换形态 / 胜利）；<c>null</c> = 未结算或非战斗收尾。
        /// <para>
        /// 与 <see cref="PendingResult"/> 的分工（PRP/battle-to-narrative §2.2）：后者管「这场遭遇结束了没有」，
        /// 前者管「怎么结束的」。枚举保持不变是为了不动回放快照的字节布局——本属性**不进 Serialize**，
        /// 因此不触发 <c>ReplayFormat</c> 升版；战斗阶段本身也不在可存档边界内（NarrativeService.IsStable）。
        /// </para>
        /// <para>
        /// ⚠️ <b>它是「取走型」字段</b>：<see cref="Begin"/>（下一场遭遇）与换了身份的 <see cref="StartBattle"/>
        /// 都会把它清成 null。**<see cref="End"/> 不清它**（只置 <c>IsActive = false</c> 并清瞬时事实）——
        /// 但 <see cref="End"/> 之后本类整体不再推进，而消费方若把「遭遇还活着」当成读结果的前提，
        /// 就会在镜碎那条路上永远读不到：镜碎在**同一帧**结算之后调 <see cref="End"/>，
        /// 而下一场遭遇的 <see cref="Begin"/> 会把这份结果连痕迹一起抹掉。
        /// 所以消费方（<c>EncounterBattleBridge</c>）在结算那一刻就把它抄进自己的字段，不等下一渲染帧。
        /// </para>
        /// </summary>
        public BattleResult? BattleSettlement { get; private set; }

        /// <summary>当前遭遇的瞬时事实集（`stealth.*` / `chase.*`）；读方是剧情条件的事实源。</summary>
        public IEncounterFacts Facts => facts;

        /// <summary>
        /// 同一个事实集的**写侧契约**（<see cref="IStealthFactSink"/>）：潜行侧的处决交互靠它写
        /// `stealth.assassinated`（背后处决接线那一波接上的）。
        /// <para>
        /// 与 <see cref="Facts"/> 是**同一个对象**（<see cref="EncounterFactLog"/> 两侧接口都实现）；
        /// 分成两个属性是因为两个契约分属两个模块：读侧在 Narrative（消费方），写侧在 Stealth（生产方）。
        /// 接线方（<c>MonsterEncounterState</c> / <c>StandaloneEncounterController</c>）拿这一个喂给处决交互。
        /// </para>
        /// </summary>
        public IStealthFactSink FactSink => facts;

        /// <summary>
        /// 视线遮挡查询器：场景就绪时由接线侧一次性喂遮挡体（<see cref="StealthSight.SetOccluders"/>），
        /// tick 里只做纯几何求交，**不做任何物理查询**（`docs/architecture.md` 的确定性内核要求）。
        /// </summary>
        public StealthSight Sight => sight;

        /// <summary>接入身份状态（S1）：身份生效中时敌人不攻击（两层判据见 <see cref="IdentityAttackRules"/>）。</summary>
        public void BindIdentity(IdentityState state) => identity = state;

        /// <summary>
        /// 接入埋点（本波新增 `battle_settled`）。做成后置装配的理由同 <see cref="UseStealth"/>；
        /// 不接时用 <see cref="NullTelemetryScope"/>，不影响任何行为。
        /// </summary>
        public void UseTelemetry(ITelemetryScope scope) => telemetry = scope ?? NullTelemetryScope.Instance;

        /// <summary>
        /// 接入击杀掉落口（<see cref="IMonsterDropSink"/>，实现方在 <c>Game.Loot</c>）。
        /// 做成后置装配的理由同 <see cref="UseStealth"/>：<b>本类构造参数不能变</b>
        /// （TypeAnalyzer 取参数最多的构造函数且不看默认值）。
        /// <para>
        /// <b>不接时什么都不掉</b>，与接线前逐字一致——独立原型场景与纯 Monster 测试作用域没有 Loot，
        /// 那里不该凭空多出一条掉落。传 null 等于断开。
        /// </para>
        /// </summary>
        public void BindDropSink(IMonsterDropSink sink) => dropSink = sink;

        /// <summary>
        /// 用正式配置装配潜行关口（`StealthConfig` 装配的 <see cref="StealthKernel"/>）。
        /// 配置非法当场抛（配置错误不许静默跑，见 <see cref="StealthKernel.Validate"/>）。
        /// </summary>
        public void UseStealth(StealthKernel kernel)
        {
            if (kernel == null)
            {
                throw new ArgumentNullException(nameof(kernel));
            }

            string issue = kernel.Validate();
            if (issue != null)
            {
                throw new ArgumentException("潜行配置非法：" + issue, nameof(kernel));
            }

            sight = kernel.Sight;
            sightSettings = kernel.SightSettings;
            assassination = kernel.Assassination;
            knockdown = kernel.Knockdown;
            chase = kernel.Chase;
            knockdownLowMax = kernel.Config.KnockdownCountLowMax;
            knockdownMidMax = kernel.Config.KnockdownCountMidMax;
            if (player != null)
            {
                player.UseKnockdown(knockdown);
            }

            ApplyMonsterOcclusion();
        }

        /// <summary>
        /// 把 <see cref="Sight"/> 注入到**每一只**巡逻怪，让视锥那两档也吃遮挡
        /// （2026-10-08「掩体贯通到怪物察觉」）。
        /// <para>
        /// <b>三处调用点，缺一处就有怪漏判</b>：① <see cref="UseStealth"/>（换成内核那只 sight 之后）；
        /// ② <see cref="ConfigureTaming"/>（<see cref="MonsterRules.CreateForActor"/> 造出来的巡逻者）；
        /// ③ <see cref="MonsterInstaller"/> 的构建回调里 <c>BindOcclusion</c> 之后。
        /// 场景就绪时 <c>MonsterEncounterState</c> 才把遮挡体喂进 <see cref="Sight"/>，那时装配早就走完了，
        /// 所以本方法只搬引用、不查任何数据。
        /// </para>
        /// <para>
        /// **多只怪共享同一个 <see cref="StealthSight"/> 实例是有意的**：它的查询是「观察者位置 + 目标位置」
        /// 的纯函数、零分配、不持查询状态（见 <see cref="MonsterRules.UseOcclusion"/> 的注释）。
        /// </para>
        /// </summary>
        public void BindOcclusion(StealthSight occlusion)
        {
            if (occlusion == null)
            {
                throw new ArgumentNullException(nameof(occlusion));
            }

            sight = occlusion;
            ApplyMonsterOcclusion();
        }

        // 把当前 sight 分发给主怪与所有巡逻者。语义上「所有巡逻怪」= Taming.TargetIds 那一组（主怪也是其中一只），
        // 所以遍历它就够；monster 那一句是防 Taming 为空或列表为空的兜底，**不是**为了给主怪特判。
        private void ApplyMonsterOcclusion()
        {
            if (monster != null)
            {
                monster.UseOcclusion(sight);
            }

            if (Taming == null)
            {
                return;
            }

            for (int i = 0; i < Taming.TargetIds.Count; i++)
            {
                Taming.GetTarget(Taming.TargetIds[i]).UseOcclusion(sight);
            }
        }

        // 对已经在探索场景中的双方建立战斗关联，不重置生命和位置。
        public void StartBattle(long encounterId, long activationId)
        {
            if (!IsActive || encounterId < 1 || activationId < 1) throw new System.InvalidOperationException("战斗未准备或身份非法");
            if (EncounterId == encounterId && ActivationId == activationId) return;
            if (EncounterId != 0 && !ResultConsumed) throw new System.InvalidOperationException("旧战斗尚未结算");
            EncounterId = encounterId;
            ActivationId = activationId;
            PendingResult = Result.None;
            BattleSettlement = null;
            ResultConsumed = false;
        }
        public bool ConsumeResult(long encounterId, long activationId)
        {
            if (EncounterId != encounterId || ActivationId != activationId || PendingResult == Result.None || ResultConsumed) return false;
            ResultConsumed = true;
            return true;
        }
        public void AbortBattle()
        {
            if (EncounterId != 0 && PendingResult == Result.None) PendingResult = Result.Aborted;
        }

        /// <summary>
        /// 记录一次战斗结算（C5 的唯一入口）。返回值：false = 没接受（<c>default</c> 结果、或本场已结算过）。
        /// <para>
        /// 两处调用方：<see cref="Step"/> 的胜负那一处（Victory / Downed）与潜行侧的 <c>ExecutionResolver</c>
        /// （背后处决命中即写 Victory，见它的注释）。消费方是 <c>EncounterBattleBridge</c>。
        /// </para>
        /// <para>
        /// <b>只记结果，不推进剧情</b>：推进只走 <c>NarrativeRules.CompleteBattle</c>
        /// （PRP §2.1「唯一写入方」，身份四项对不上由剧情侧拒绝）。
        /// </para>
        /// </summary>
        public bool SettleBattle(BattleResult result)
        {
            if (result.Kind == null || BattleSettlement.HasValue)
            {
                return false;
            }

            BattleSettlement = result;
            telemetry.Track("battle_settled", ("result", result.ExitKey), ("encounter", EncounterId), ("activation", ActivationId));
            return true;
        }

        public EncounterSaveData Capture(long tick) => new EncounterSaveData
        {
            Tick = tick, Active = IsActive, EncounterId = EncounterId, ActivationId = ActivationId,
            Result = PendingResult, ResultConsumed = ResultConsumed, Player = player.Model.Capture(), Monster = monster.Capture(),
            TamingTargets = Taming.Capture(), ControlledActorId = CurrentControlId, PlayerActorId = Taming.PlayerId, PreviousTame = Taming.PreviousToggle,
        };
        public void Restore(EncounterSaveData saved)
        {
            if (saved == null) throw new System.ArgumentNullException(nameof(saved));
            saved.Validate();
            if (saved.TamingTargets != null && saved.PlayerActorId != Taming.PlayerId)
                throw new ArgumentException("存档玩家稳定标识与场景不匹配");
            Taming.ValidateRestore(saved.TamingTargets);
            player.Model.Restore(saved.Player);
            if (saved.TamingTargets == null)
            {
                ResetAdditionalTargets();
                monster.Restore(saved.Monster);
            }
            Taming.Restore(saved.TamingTargets, saved.ControlledActorId, saved.PreviousTame);
            IsActive = saved.Active;
            EncounterId = saved.EncounterId;
            ActivationId = saved.ActivationId;
            PendingResult = saved.Result;
            ResultConsumed = saved.ResultConsumed;
        }

        public void Begin(Vector2 playerSpawn, Vector2[] patrolPoints)
        {
            player.Reset(playerSpawn);
            monster.Reset(patrolPoints);
            ResetAdditionalTargets();
            Taming.Reset();
            EncounterId = ActivationId = 0;
            PendingResult = Result.None;
            BattleSettlement = null;
            ResultConsumed = false;
            chasePhase = ChasePhase.Idle;
            chase.Reset(true);
            facts.Clear();
            // 掉落闸门随遭遇重开：上一场结过的掉落不该让下一场的同一只怪不掉。
            killDropSettled = false;
            IsActive = true;
        }

        public void End()
        {
            IsActive = false;
            if (Taming != null) Taming.TryControl(Taming.PlayerId);
            // 离场清瞬时事实，避免泄漏到下一场遭遇。
            chasePhase = ChasePhase.Idle;
            facts.Clear();
        }

        private void ResetAdditionalTargets()
        {
            if (authoredRoutes == null) return;
            for (int i = 1; i < authoredRoutes.Length; i++) Taming.GetTarget(Taming.TargetIds[i]).Reset(authoredRoutes[i]);
        }

        /// <summary>
        /// 把玩家逻辑位置改成表现层碰撞解算后的结果（PRP/exploration-whitebox 波 9）。
        /// 只允许 EncounterSceneView.OnPlayerBlocked 的回写调用：它是白盒阶段「障碍不在确定性内核里」的补丁，
        /// 其他玩法不要借它挪人（挪人用 PlayerRules.Reset）。未激活时忽略。
        /// <para>
        /// 视图传来的是分轴合成值：被挡的轴是插值点扫掠后的修正值，没被挡的轴原样是逻辑值。
        /// 被改写的轴同时把 PreviousPosition 设成同一值（下一帧不会从墙里倒插回来）；
        /// 没改写的轴保留 PreviousPosition，贴墙滑动时沿墙那一轴继续平滑插值、不损失速度。
        /// </para>
        /// </summary>
        public void CorrectPlayerPosition(Vector2 logicPosition)
        {
            if (!IsActive) return;
            PlayerModel model = player.Model;
            Vector2 current = model.Position;
            Vector2 previous = model.PreviousPosition;
            model.PreviousPosition = new Vector2(
                EncounterProjection.CorrectPreviousAxis(previous.x, current.x, logicPosition.x),
                EncounterProjection.CorrectPreviousAxis(previous.y, current.y, logicPosition.y));
            model.Position = logicPosition;
        }

        public void Step(in SimulationContext context)
        {
            if (!IsActive || (PendingResult != Result.None && !ResultConsumed))
            {
                // 本 tick 双方规则都不推进：仍把上一 tick 位置对齐，否则视图会在结算前最后一步的两点间来回插值。
                player.Model.SyncPreviousPosition();
                monster.Model.SyncPreviousPosition();
                foreach (string id in Taming.TargetIds) Taming.GetTarget(id).Model.SyncPreviousPosition();
                return;
            }

            InputCommand command = context.Input;
            string requested = SelectionId(command.Axis1.x);
            if (command.HasButton(InputCommand.ButtonSelectControl)) Taming.TryControl(requested);
            if (command.HasButton(InputCommand.ButtonTamePressed)) Taming.ProcessToggle(false);
            Taming.ProcessToggle(command.HasButton(InputCommand.ButtonTame) && (command.Axis1.x == 0f || requested != null), requested);
            bool enemyControlled = Taming.IsControllingEnemy;
            var playerIntent = new PlayerIntent(
                enemyControlled ? Vector2.zero : command.Axis0,
                !enemyControlled && command.HasButton(InputCommand.ButtonSneak),
                !enemyControlled && command.HasButton(InputCommand.ButtonDisguise),
                !enemyControlled && command.HasButton(InputCommand.ButtonAttack),
                !enemyControlled && command.HasButton(InputCommand.ButtonRun));
            bool attacked = player.Step(in playerIntent, context.DeltaTime);
            PlayerSnapshot target = player.Model.Snapshot;
            foreach (string id in Taming.TargetIds)
            {
                MonsterRules rules = Taming.GetTarget(id);
                MonsterModel enemy = rules.Model;
                if (!attacked || Taming.IsTargetTamed(id) || !Taming.IsAvailable(id) || enemy.Health <= 0
                    || GameMath.Distance(target.Position, enemy.Position) > player.AttackRange) continue;
                Vector2 difference = enemy.Position - target.Position;
                if (GameMath.SqrMagnitude(difference) == 0f
                    || GameMath.Dot(target.Facing, GameMath.Normalize(difference)) >= 0f)
                {
                    var damage = new DamageIntent(player.AttackDamage);
                    rules.ApplyDamage(in damage, in target);
                }
            }

            bool enemyAttacked = Taming.AdvanceTargets(command.Axis0, context.DeltaTime, identity != null && identity.IsInEffect);
            if (EncounterId != 0 && Taming.IsTamed && PendingResult == Result.None) AbortBattle();
            // 双方规则推进之后再做潜行结算（StealthKernel 的接线建议）：先把这一 tick 的结果合成事实键。
            SettleStealth(in target, monster.Model, enemyAttacked, context.DeltaTime);
            // 掉落：**死亡那一处**结算，与「这算不算一场战斗」无关——遭遇里没有 EncounterId 也会掉。
            // 放在 Step 末尾（双方规则都推完之后）是为了让「主怪这一 tick 死了没有」这个判断拿到终态，
            // 而不是在某个中间分支里提前读一次生命。
            SettleKillDropOnce();
            if (EncounterId != 0 && PendingResult == Result.None)
            {
                PendingResult = player.Model.Health == 0 ? Result.Defeat : monster.Model.Health == 0 ? Result.Victory : Result.None;
                if (PendingResult == Result.Victory)
                {
                    // 小怪清空 / BOSS 收押 → Victory（BattleOutcome 的四种之一），唯一无歧义、不依赖未拍板口径的映射。
                    SettleBattle(new BattleResult(BattleOutcome.Victory));
                }
                else if (PendingResult == Result.Defeat)
                {
                    // 玩家生命归零 → Downed（被击倒）。口径由用户 2026-10-09 拍板：
                    // **一场实时遭遇打赢或被击倒，等于这一关结束**，失败侧不再是「留空等策划」。
                    // 阶段没声明 Downed 时剧情侧会正常拒绝（CompleteBattle → battle_result_rejected），
                    // 这里不预判内容：战斗侧只负责如实报出「玩家被击倒了」。
                    SettleBattle(new BattleResult(BattleOutcome.Downed));
                }
                // Result.Aborted 不在这里：它由 AbortBattle 单独写（收服收尾，不是战斗结果，不承载 BattleSettlement）。
            }
        }

        /// <summary>
        /// 潜行结算：把「看不看得见 / 能不能处决 / 击倒状态 / 追逐状态」合成事实键。
        /// <para>
        /// 贴墙纪律：本方法在固定 tick 里，只做纯几何与查表——遮挡体是场景一次性喂进来的纯数据
        /// （<see cref="EncounterSceneView.CollectSightOccluders"/>），没有任何物理查询与场景查找。
        /// </para>
        /// </summary>
        private void SettleStealth(in PlayerSnapshot target, MonsterModel enemy, bool enemyAttacked, float deltaTime)
        {
            // 「察觉」这一个量要同时满足两件事，两个来源各自的分工写在下面——
            // 2026-10-08 主窗口复跑抓到过一次「两层各算各的」在这里分叉（见第 3 点），所以现在的取法是：
            // 「行为层说什么，事实层就抄什么」，从结构上不可能再分叉。
            //
            // 1) **答案取行为层**：`perceives` 直接抄 `MonsterRules.Detects`，也就是警戒 / 敌对 / 追击用的那个答案。
            //    它内部已经正确地做到了「视锥两档吃遮挡、贴身那一档不吃」
            //    （`06_怪物状态与交互设计文档.md:71-73`：近距察觉要潜行才免，掩体不免）。
            //    ⚠️ 不能在这里再叠一层 `StealthDecisionGate.PerceivesThroughCover`：那个门的输入若是**整支**感知
            //    （`PerceivedBySenses` 已经含着不分朝向的贴身察觉），它就会拿「对视线的遮挡」去掐掉贴身那一档，
            //    于是「怪已经贴到你身上进了警戒」与「`stealth.hidden` 为真」同时成立——正是本波要消灭的
            //    自相矛盾，只是方向反了。事实层与行为层必须同向，这条比「走哪个门」重要。
            //
            // 2) **`coverBlocks` 仍要算那一对差**：`reachesTarget` 用不含遮挡的整支感知（`PerceivedBySenses`），
            //    减去行为层的答案。「视线本来够得着、结果没察觉」= 这一 tick 处于掩体遮挡下。
            //    ⚠️ 这个减法里的减数**必须**含遮挡、被减数**必须**不含——两个都含或都不含，相减恒为 0/1，
            //    `stealth.cover` 就会永远点不亮或永远点亮（「躲进掩体」那条摆脱条件 `04:104` R23 的判据落点）。
            //
            // 3) 副作用（修②带出的口径变化，需主窗口确认，本次修复的报告里已点名）：贴身察觉穿透掩体这一类现场里
            //    `reachesTarget && perceives` 都是真，于是 `coverBlocks` 为假——「敌人已经贴身发现了你」时
            //    不再对外声称「你处于掩体遮挡下」。变化只发生在这一个窄格子里，且方向更对：掩体此刻并没有帮到你。
            bool reachesTarget = monster.PerceivedBySenses(in target) > 0;
            bool perceives = monster.Detects(in target);
            bool coverBlocks = reachesTarget && !perceives;

            bool aware = enemy.Mode != MonsterMode.PatrolWalk && enemy.Mode != MonsterMode.PatrolPause;
            var assassinationInput = new AssassinationInput(target.Position, target.Facing, enemy.Position, enemy.Facing,
                enemy.Health > 0, aware, target.IsSneaking);
            var input = new StealthGateInput(
                perceives,
                assassination.IsBehindAndInRange(target.Position, enemy.Position, enemy.Facing),
                assassination.Evaluate(in assassinationInput).Allowed,
                knockdown.Phase,
                coverBlocks);
            StealthGateVerdict verdict = StealthDecisionGate.Evaluate(in input, in sightSettings, knockdown.Tier(knockdownLowMax, knockdownMidMax));

            // 键名全部来自判定结果与 StealthFactKeys，不在这里手打字符串（字典 §5 的 V3 就是防手打拼错的）。
            // 不用 StealthDecisionGate.ToFacts：它每 tick 会 new 一个 6 元素数组，tick 路径零分配是硬要求。
            facts.Set(StealthFactKeys.Hidden, verdict.Hidden);
            facts.Set(StealthFactKeys.Behind, verdict.Behind);
            facts.Set(StealthFactKeys.Knockdown, verdict.Knockdown);
            facts.Set(StealthFactKeys.Cover, verdict.Cover);
            // 档位键互斥（StealthFactSnapshot.Emit 的同一条纪律）：三档一起按当前档重算，不清旧档会留下两个同时为真。
            KnockdownTier tier = verdict.KnockdownTier;
            facts.Set(StealthFactKeys.KnockdownCountLow, tier == KnockdownTier.Low);
            facts.Set(StealthFactKeys.KnockdownCountMid, tier == KnockdownTier.Mid);
            facts.Set(StealthFactKeys.KnockdownCountHigh, tier == KnockdownTier.High);
            // **刻意不写 `stealth.assassinated`**：判定门的 `Assassinated` 字段取的是「这一刀此刻能不能下」
            //（AssassinationAllowed），而字典 §4.2 把这个键定义为「已用背后暗杀解决过目标（本阶段计数用），命中即写」。
            // 拿「能不能下」去点亮一个永久的「已经暗杀过」标记，会让「站在守卫背后」直接把内容条件弄假成真。
            // 真口径要等暗杀交互拍板（`00_功能总览.md` §8.1 #5、`03_潜行与暗杀.md` Q9）后，由「命中即写一次」补上。

            SettleChase(in target, enemy, perceives, enemyAttacked, deltaTime);
        }

        /// <summary>
        /// 追逐结算（`chase.*` 四键的写入方＝Chase / Monster，字典 §4.3）：本类只写 active / escaped / caught。
        /// <para>
        /// 触发口径 [推断]：怪物进入敌对追击（<see cref="MonsterMode.Hostile"/>，即红区或橙区警戒升满）
        /// 就是起追（`04_追逐.md:87-88` R12/R13）。内容侧的其它追逐来源（账簿超量、怀疑度、揭露、
        /// 脚本化固定追逐 T1–T8）本波不接线，因此 `chase.fixed` **恒不写**（与 `stealth.cover`
        /// 「遮挡没做时恒不写」同一处理，字典里已有先例）。
        /// </para>
        /// </summary>
        private void SettleChase(in PlayerSnapshot target, MonsterModel enemy, bool pursuerSees, bool enemyAttacked, float deltaTime)
        {
            bool pursuerAlive = enemy.Health > 0;
            float distance = GameMath.Distance(enemy.Position, target.Position);
            if (chasePhase != ChasePhase.Chasing)
            {
                ChaseTriggerVerdict trigger = chase.TryStart(
                    new ChaseTriggerInput(enemy.Position, target.Position, pursuerAlive, enemy.Mode == MonsterMode.Hostile), chasePhase);
                if (trigger.Severity == ChaseRejectSeverity.Fatal)
                {
                    // 配置错误必须当场炸，不许静默跑（ChaseRules.TryStart 的契约）；速度默认值在合法区间内。
                    throw new InvalidOperationException("追逐配置非法：" + ChaseRules.Describe(trigger.Reject));
                }

                if (trigger.Triggered)
                {
                    chasePhase = ChasePhase.Chasing;
                }
            }
            else
            {
                ChaseVerdict verdict = chase.Tick(
                    new ChaseInput(pursuerSees, distance, target.IsAlive, pursuerAlive), chasePhase, deltaTime);
                chasePhase = verdict.Phase;
                if (verdict.EscapedThisTick)
                {
                    facts.Set(StealthFactKeys.ChaseEscaped, true);
                }

                // 被抓的判据 [推断]：追逐进行中，追兵这一 tick 打到人 = 追上（`04:108-112` R26 的三处原文
                // 只说后果、没说判定）。后果策略（ChaseCaughtRules）本波不注入，所以这里**只写事实**，
                // 不改流程、不写 Result：口径未拍板（`04:190` Q3 / `00` §8.1 #4）。
                if (verdict.CaughtThisTick || (enemyAttacked && target.IsAlive))
                {
                    facts.Set(StealthFactKeys.ChaseCaught, true);
                }
            }

            facts.Set(StealthFactKeys.ChaseActive, chasePhase == ChasePhase.Chasing);
        }

        /// <summary>
        /// 主怪死亡时结算一次击杀掉落（<see cref="IMonsterDropSink"/>，实现在 <c>Game.Loot</c>）。
        /// <para>
        /// <b>边界（2026-10-08 明写，别当成漏了）</b>：只结**主怪**（<c>Taming.TargetIds[0]</c>，也就是
        /// 场景里那只 <c>enerme</c>）。<see cref="Taming"/> 管着的其它巡逻者这一轮不结——它们没有
        /// 「这一场遭遇的主体是谁」的语义，掉落文案与埋点也按主怪记；要扩到全部巡逻者得先决定
        /// 「一只巡逻者死了算不算这次遭遇的战果」，那是内容侧的事。
        /// </para>
        /// <para>
        /// <b>一只怪只结一次</b>：<see cref="killDropSettled"/> 是闸门。它在 <see cref="Begin"/> 里清零，
        /// 所以重开一场遭遇能再掉；同一场里连点多少次都只掉一次（重复结算会让掉落翻倍）。
        /// </para>
        /// <para>
        /// <b>种类为 null 时跳过</b>：<c>MonsterKind</c> 为 null 表示表没就绪或这只怪没接种类
        /// （<see cref="MonsterRules.Kind"/> 的注释）——那时没有 <c>yaoId</c>，也没有掉落列表，
        /// <b>不猜一个 id 出来</b>。
        /// </para>
        /// <para>
        /// 副作用边界（已知，接受）：掉落是写背包的副作用，**不进确定性内核、不进快照**。
        /// 回放时被驱动方重放「死亡那一 tick」会让 <see cref="killDropSettled"/> 从 false 起步、
        /// 于是再掉一次——与本模块既有的「遭遇回放不覆盖 Game.Loot」同一类边界（`Runtime/Battle` 的
        /// 回合制战斗同样不进回放，见 turnbased guide「随机数与回放」）。
        /// </para>
        /// </summary>
        /// <returns>本次真的结算了返回 true。</returns>
        public bool SettleKillDropOnce()
        {
            if (killDropSettled || dropSink == null || monster == null || monster.Model.Health > 0)
            {
                return false;
            }

            // 「有没有种类」先判，再落闸：表没就绪时 Kind 恒为 null（它在 Reset 里查一次、整场不变），
            // 这时落闸与不落闸的结果一样，但先判能让「闸门 = 确实结算过一次」这个含义保持干净。
            MonsterKind kind = monster.Kind;
            if (kind == null)
            {
                // 表没就绪 / 这只怪没接种类：没有 yaoId 就没有掉落内容，跳过而不是猜一个 id 出来。
                return false;
            }

            // 先落闸再调外部口：万一实现方抛异常，也不会在同一场里被反复重试成多次掉落。
            killDropSettled = true;
            dropSink.SettleKillDrop(kind.YaoId, monster.DropItemIds);
            telemetry.Track("kill_drop_settled", ("yao", kind.YaoId), ("items", monster.DropItemIds.Count),
                ("encounter", EncounterId));
            return true;
        }

        // ───────────────────────── 进战斗的局势快照（IBattleEntryContext） ─────────────────────────
        //
        // 五个量的来源与**推断成分**（2026-10-08 接线，逐条写清楚是哪一种，别让下一个人以为是原文给的）：
        //   ① PlayerSneaking    —— 现取 `player.Model.Snapshot.IsSneaking`，实读。
        //   ② SneakAttackLanded —— 现取事实集里的 `stealth.assassinated`（字典 §4.2「命中即写」），实读。
        //   ③ PlayerInAlertZone / PlayerInHostileZone —— 由怪物当前模式反推（警戒态 / 敌对态）。
        //      **推断**：遭遇内核里没有单独的「玩家在不在某个半径里」查询，而 `07:22`、`:27` 把
        //      「玩家在警戒 / 敌对区域内」与「怪物正处于警戒 / 敌对状态」写成并列条件（`or`），
        //      且本工程里怪物只在玩家进入对应区域时才会进这两态。所以反推与正查等价，且不会自动进敌对。
        //   ④ MonsterState      —— 由 `MonsterMode` 逐值翻译（Monster 的通用状态 → 内核的三档枚举）。
        //   ⑤ Initiator         —— 见 `DeriveInitiator`，**含一处显式推断**。

        /// <summary>`07:17` 偷袭条件的一半：玩家此刻在潜行。</summary>
        public bool PlayerSneaking => player != null && player.Model.Snapshot.IsSneaking;

        /// <summary>
        /// `07:17` 的另一半：这一场里偷袭**已经命中**过目标。
        /// 判据是持久事实 `stealth.assassinated`（字典 §4.2），写入方是潜行侧的处决结算（`ExecutionResolver`）。
        /// <para>
        /// ⚠️ <b>现状下这一支几乎到不了</b>：处决成功会让目标当场死亡（`MonsterRules.ApplyExecution`），
        /// 而 BOSS 死了就不会再进战斗阶段。等 `00_功能总览.md` §8.1 #5（暗杀交互形式）拍板、
        /// 「偷袭」有独立于处决的表现之后这条才是活的。**接线本身是对的，缺的是内容侧的定义。**
        /// </para>
        /// </summary>
        public bool SneakAttackLanded => facts.IsTrue(StealthFactKeys.Assassinated);

        /// <summary>`07:22`：玩家在警戒区域内。由怪物处于警戒态反推，见本节顶部注释 ③。</summary>
        public bool PlayerInAlertZone => monster != null && monster.Model.Mode == MonsterMode.Alert;

        /// <summary>`07:22`：玩家在敌对区域内。由怪物处于敌对态反推，见本节顶部注释 ③。</summary>
        public bool PlayerInHostileZone => monster != null && monster.Model.Mode == MonsterMode.Hostile;

        /// <summary>怪物当前的警觉档位（`MonsterMode` → <c>Game.TurnBased.MonsterAlertState</c>）。</summary>
        public Game.TurnBased.MonsterAlertState MonsterState => ToAlertState(monster == null ? MonsterMode.Dead : monster.Model.Mode);

        /// <summary>
        /// 这只巡逻怪此刻察觉到玩家了吗（＝它处于警戒 / 敌对态）。
        /// <para>
        /// 巡逻怪有状态机，所以这一条与 <see cref="MonsterState"/> 是同一件事的两种写法；
        /// 契约把它单列出来是为了**没有状态机的那一侧**（剧情 BOSS 的 `BossEntryContext`）——
        /// 那边只有「这一刻的感知结果」，见 `IBattleEntryContext.MonsterAware` 的注释。
        /// </para>
        /// </summary>
        public bool MonsterAware => monster != null
            && (monster.Model.Mode == MonsterMode.Alert || monster.Model.Mode == MonsterMode.Hostile);

        /// <summary>
        /// 谁先动的手。口径见 <see cref="DeriveInitiator"/>——那里有一条**显式推断**（本工程没有「BOSS 攻击玩家开战」这一路）。
        /// </summary>
        public Game.TurnBased.BattleInitiator Initiator => DeriveInitiator(
            monster == null ? MonsterMode.Dead : monster.Model.Mode, PlayerSneaking);

        /// <summary>
        /// 怪物模式 → 内核的三档警觉枚举。**逐值写全**（含 Dead 与 PatrolPause）而不是给个 <c>default</c>：
        /// 将来 <see cref="MonsterMode"/> 加值时会在这里编译期暴露，不会静默算成「未察觉」。
        /// </summary>
        private static Game.TurnBased.MonsterAlertState ToAlertState(MonsterMode mode)
        {
            switch (mode)
            {
                case MonsterMode.Alert:
                    return Game.TurnBased.MonsterAlertState.Alert;
                case MonsterMode.Hostile:
                    return Game.TurnBased.MonsterAlertState.Hostile;
                case MonsterMode.PatrolWalk:
                case MonsterMode.PatrolPause:
                case MonsterMode.Dead:
                    return Game.TurnBased.MonsterAlertState.Unaware;
                default:
                    // 加了新的 MonsterMode 而这里没跟上：当场炸，不要静默算成「未察觉」。
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的怪物模式，MonsterAlertState 的映射没跟上");
            }
        }

        /// <summary>
        /// 先手方。三条触发路径（`07:15-28`）里只有两条能落到本工程：
        /// <list type="bullet">
        /// <item>怪物已敌对 + 玩家没在潜行 → <see cref="Game.TurnBased.BattleInitiator.BossAttacked"/>
        /// （「被打」：BOSS 已经动手打你了 → BOSS 先手）。</item>
        /// <item>其余 → <see cref="Game.TurnBased.BattleInitiator.PlayerAttacked"/>（玩家先动手 → 玩家先手）。</item>
        /// </list>
        /// <para>
        /// <b>⚠️ 显式推断（不是原文给的）</b>：现在的开战时机是「剧情停在 Battle 阶段 → 战斗侧订阅通知开打」，
        /// **没有「玩家物理挥刀」这一路**（`EncounterStep` 的进战斗登记 `StartBattle` 至今没有生产调用方）。
        /// 所以「谁先动手」只能从局势反推：怪物已经进入敌对追击（`MonsterMode.Hostile`，即红区或警戒升满）
        /// 且玩家并没有在潜行 → 按现场读作「它先动的手」。
        /// </para>
        /// <para>
        /// <b>代价与边界</b>：玩家在敌对态下主动上前开战，也会被判成「被打」（BOSS 先手）。
        /// 要分辨这两者，需要内容侧给一个「玩家主动攻击开战」的入口（例如 BOSS NPC 的交互动词）；
        /// 那属于 `00_功能总览.md` §8.1 #7 与 `09_BOSS战.md` 的拍板范围，本波不替策划决定。
        /// </para>
        /// </summary>
        private static Game.TurnBased.BattleInitiator DeriveInitiator(MonsterMode mode, bool playerSneaking) =>
            mode == MonsterMode.Hostile && !playerSneaking
                ? Game.TurnBased.BattleInitiator.BossAttacked
                : Game.TurnBased.BattleInitiator.PlayerAttacked;

        private string SelectionId(float selection)
        {
            // 0 表示没有定向请求；1 对应玩家，后续槽位按场景显式数组顺序映射稳定 ID。
            if (float.IsNaN(selection) || float.IsInfinity(selection) || selection < 1f
                || selection > Taming.TargetIds.Count + 1 || selection != (int)selection) return null;
            int index = (int)selection - 1;
            return index == 0 ? Taming.PlayerId : index <= Taming.TargetIds.Count ? Taming.TargetIds[index - 1] : null;
        }

        public void Serialize(IStateWriter writer)
        {
            writer.WriteBool(IsActive);
            writer.WriteLong(EncounterId);
            writer.WriteLong(ActivationId);
            writer.WriteByte((byte)PendingResult);
            writer.WriteBool(ResultConsumed);
            WriteId(writer, Taming.PlayerId);
            WriteId(writer, CurrentControlId);
            writer.WriteBool(Taming.PreviousToggle);
            writer.WriteInt(Taming.TargetIds.Count);
            foreach (string id in Taming.TargetIds)
            {
                WriteId(writer, id);
                writer.WriteBool(Taming.IsTargetTamed(id));
                Taming.GetTarget(id).Serialize(writer);
            }
        }

        public void Deserialize(IStateReader reader)
        {
            IsActive = reader.ReadBool();
            EncounterId = reader.ReadLong();
            ActivationId = reader.ReadLong();
            PendingResult = (Result)reader.ReadByte();
            ResultConsumed = reader.ReadBool();
            if (ReadId(reader) != Taming.PlayerId) throw new InvalidOperationException("回放玩家标识与场景不匹配");
            string controlled = ReadId(reader);
            bool held = reader.ReadBool();
            int count = reader.ReadInt();
            if (count != Taming.TargetIds.Count) throw new InvalidOperationException("回放巡逻者数量与场景不匹配");
            var flags = new bool[count];
            for (int i = 0; i < count; i++)
            {
                string id = ReadId(reader);
                if (id != Taming.TargetIds[i]) throw new InvalidOperationException("回放巡逻者标识与场景不匹配");
                flags[i] = reader.ReadBool();
                Taming.GetTarget(id).Deserialize(reader);
            }
            Taming.RestoreReplayFlags(flags, controlled, held);
        }

        private static void WriteId(IStateWriter writer, string id)
        {
            writer.WriteInt(id.Length);
            foreach (char c in id) writer.WriteUShort(c);
        }

        private static string ReadId(IStateReader reader)
        {
            int count = reader.ReadInt();
            if (count < 1 || count > 128) throw new InvalidOperationException("回放标识长度非法");
            var chars = new char[count];
            for (int i = 0; i < count; i++) chars[i] = (char)reader.ReadUShort();
            return new string(chars);
        }
    }
}

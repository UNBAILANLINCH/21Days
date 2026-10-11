// 职责：在同一固定 tick 中先应用玩家意图，再处理怪物感知与战斗，最后把潜行 / 追逐判定合成事实键。
// 为什么新建：SimulationRunner 只负责调度步骤，Player 与 Monster 的先后属于遭遇玩法。
// 本波（S 组接线，2026-10-07）在原有职责之外接了三件事，全部按「不接线时行为逐字不变」的边界做：
//   1. 身份 → 敌人攻击许可（S1）：身份状态经 BindIdentity 进来，逐 tick 填进 MonsterIntent；
//   2. 潜行 → 事实与追逐（S3/S4）：双方规则推进之后调一次 StealthDecisionGate，结果写进内存事实集
//      （stealth.*），追逐四键由本类（Monster 侧）写，见 SettleChase；
//   3. 战斗结果（C5）：Result 枚举仍然只管流程收尾，新增 BattleSettlement 承载 BattleResult。
// 装配方式：本类由 VContainer 构造，**构造参数不能变**（TypeAnalyzer 取参数最多的构造函数且不看默认值），
//   所以潜行内核、身份状态、埋点都走后置装配方法（UseStealth / BindIdentity / UseTelemetry），
//   不装配时用各内核的占位默认值。
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

    public sealed class EncounterStep : ISimulationStep, IReplayState
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
            if (EncounterId != 0 && PendingResult == Result.None)
            {
                PendingResult = player.Model.Health == 0 ? Result.Defeat : monster.Model.Health == 0 ? Result.Victory : Result.None;
                if (PendingResult == Result.Victory)
                {
                    // 小怪清空 / BOSS 收押 → Victory（BattleOutcome 的四种之一），是唯一无歧义、不依赖未拍板口径的映射。
                    // 失败侧（Downed / Exposed）属于 §8.1 #3/#4 的策略注入点，本波不替策划决定，见交付报告「待策划拍板」。
                    SettleBattle(new BattleResult(BattleOutcome.Victory));
                }
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
            // 「看得见」= 距离 / 夹角够得着（MonsterRules.Detects）**再叠一层视线遮挡**（StealthSight 纯几何）。
            bool perceivesByCone = monster.Detects(in target);
            bool perceives = StealthDecisionGate.PerceivesThroughCover(sight, enemy.Position, target.Position, perceivesByCone);
            // 视线本来够得着、被遮挡体切断 = 这一 tick 处于掩体遮挡下。
            bool coverBlocks = perceivesByCone && !perceives;

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

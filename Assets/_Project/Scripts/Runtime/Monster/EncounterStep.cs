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
//   7. 一场遭遇多种怪（PRP/combat-iteration D2，2026-10-14）：ConfigureTaming 多收「种类覆盖 / 完整参与者」两个可选数组。
//      「完整参与者」= 主怪恒是，附加怪要场景里勾上；只有它们会被背后处决、死亡掉落（掉落闸门按 id 各一次）。
//      **整场遭遇的胜负只认主怪**（主怪死 → Victory，玩家归零 → Defeat，维持现状）。
//      存档与回放快照改为按稳定 id 对齐（不再因数量不同抛错），见 Restore / Deserialize。
//      不传两个数组 = 加之前的行为逐字不变（巡逻者乙靠这条）。
//   8. 露馅后果与被抓后果（PRP/combat-iteration D1 / D4，2026-10-14）：
//      露馅——BindExposure 接身份规则三件套，每 tick 汇总「主怪 + 完整参与者」的红 / 橙信号交给 IdentityRules.ResolveExposure，
//      后果为追逐或身份失效时惊动本 tick 察觉到玩家的完整参与者，为死亡时玩家生命置 0（见 SettleExposure）；
//      被抓——UseStealth 存下内核的 ChaseOutcomeSettings，被抓时按实时血量调 ChaseCaughtRules，判定失败则生命置 0
//      （走既有的生命归零收尾，不新增 Result 值），被抓判据从只认主怪放宽到任一完整参与者（见 SettleChase）。
//      不装配时（没有 BindExposure / UseStealth）露馅不结算、被抓只写事实——与加之前逐字一致。
//      读档两处缺陷一并修：恢复后主怪重新解析种类与视锥（MonsterRules.Restore / Deserialize）、已死目标的掉落闸门直接落下。
//   9. 实时遭遇升级回合制（PRP/combat-iteration D5，2026-10-14）：BindEscalation 接战斗侧的 IEncounterEscalationPort；
//      两个拦截点——玩家普攻扣血之前、怪物出手扣血之前（经 TamingRules.AdvanceTargets 的「接管这一下」回调）——
//      条件全满足就把这一下交给回合制，吞掉伤害、进入「升级在途」（tick 像「结果待消费」那样早退，瞬态、不进快照与存档）；
//      战斗侧打完在收场黑幕下调 CompleteEscalation 交回胜负与剩余血量（开不成 / 中途失败调 AbortEscalation）。
//      不装配端口（独立原型场景、纯 Monster 测试）或三个开关全关（MonsterConfig 代码默认）时两个拦截点逐字走原路径。
//      2026-10-16 代码审查后补：**回放期间整个升级关掉**（BindReplay 接播放器，判据见 IsReplaying）——
//      升级的结果在模拟循环之外写回，重放时端口还绑着就会走一条录制时没走过的路；同「处决不进回放」一条规矩。
// 装配方式：本类由 VContainer 构造，**构造参数不能变**（TypeAnalyzer 取参数最多的构造函数且不看默认值），
//   所以潜行内核、身份状态、埋点、遮挡、掉落口、露馅规则、升级端口、回放播放器都走后置装配方法
//   （UseStealth / BindIdentity / UseTelemetry / BindOcclusion / BindDropSink / BindExposure / BindEscalation / BindReplay），
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
        private Func<float> playerSpeed; // 店铺只提供倍率，角色仍由本步骤推进。
        public event Action<float> OnAdvanced;
        private Func<bool> playerCanSneak;
        public void BindPlayerModifiers(Func<float> speed, Func<bool> canSneak)
        { playerSpeed = speed; playerCanSneak = canSneak; }

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

        // —— 露馅结算的接线位（PRP/combat-iteration D1）。三件都在才结算；不接 = 露馅不结算（加之前的行为）。
        private IdentityRules exposureRules;
        private IdentityLedger exposureLedger;
        private SuspicionState exposureSuspicion;
        // 上一次结算过的露馅原因（边沿闸）：同一组原因持续成立时只结算一次——站在红区里不会每 tick 再判一次、
        // 再惊动一次、再记一组埋点。原因变了（多了一条 / 离开后再进来）才再结算。瞬时态：不进快照、不进存档。
        private ExposureCause exposureLatch;

        // 被抓后果口径（PRP/combat-iteration D4）：UseStealth 时取内核那一份（StealthConfig.asset）。
        // 不装配时口径为 None = 加之前的行为（被抓只写 chase.caught 事实，不改血量）。
        // 其中的 PlayerHealth 是配置上限，结算时按实时血量重建，不用这一份的值。
        private ChaseOutcomeSettings chaseOutcome = new ChaseOutcomeSettings(ChaseCaughtPolicy.None, 0, 0);
        private readonly EncounterFactLog facts = new EncounterFactLog();

        // 击杀掉落口（Game.Loot 实现，本模块只持一个可空引用）：不接时什么都不掉（独立原型场景 / 纯 Monster 测试）。
        private IMonsterDropSink dropSink;

        // 按目标下标（与 Taming.TargetIds 同序）的两组标记。下标 0 恒为主怪。
        // participantFlags：是否完整参与者——主怪恒真，附加怪取 ConfigureTaming 的参数（不传 = 假 = 旧行为）。
        // killDropSettled：这只怪本场的掉落有没有结过。**一只怪只结一次**由它把关（重复结算会让掉落翻倍）；
        //   不进快照、不进存档：它管的是「这一次死亡有没有结」，Begin 时清零（下一场要能再掉）；
        //   读档 / 快照恢复时按恢复后的生死对齐（已死 = 已结算，见 AlignDropGatesWithHealth）。
        // 未 ConfigureTaming 时 Taming 里只有构造时登记的那一只主怪，所以初值长度为 1。
        private bool[] participantFlags = { true };
        private bool[] killDropSettled = new bool[1];

        // —— 升级回合制（PRP/combat-iteration D5）。端口为 null = 不升级（加之前的行为）。
        // 拒绝原因短码（埋点用）：本类自己判的两种；端口判的见 IEncounterEscalationPort 的注释。
        private const string EscalationRejectDisabled = "disabled";
        private const string EscalationRejectInFlight = "in_flight";
        private const string EscalationRejectNotConfigured = "not_configured";
        private const string EscalationRejectByPort = "rejected";

        private IEncounterEscalationPort escalationPort;
        // 回放播放器（由 MonsterInstaller 在构建回调里 BindReplay 接上；不接 = 永远不当自己在回放，行为与接线前一致）。
        // 回放期间整个升级功能关掉，理由与判据见 IsReplaying。
        private ReplayPlayer replayPlayer;
        // 升级在途：从端口接受那一刻起，到 CompleteEscalation / AbortEscalation 为止。瞬态：不进快照、不进存档，
        // Begin / Restore / Deserialize 一律清掉（存档里不可能有在途的升级——在途期间 Session 不许存，见 SessionStateAdapter）。
        private bool escalating;
        private string escalationTargetId;
        // 本 tick 有一下被交给了回合制：同一 tick 里余下的结算（露馅 / 潜行 / 被抓 / 掉落 / 胜负）一律不再因这一下触发。
        // 单独记一个标记而不只看 escalating：端口可能在接受的调用里就同步打完整场（测试的假表现层），那时 escalating 已经回落。
        private bool escalationTookOverThisTick;
        // 「接管这一下」回调：构造时缓存成字段，tick 里传给 TamingRules.AdvanceTargets，不每 tick 分配委托。
        private readonly Func<string, MonsterRules, bool> monsterHitInterceptor;

        public EncounterStep(PlayerRules player, MonsterRules monster)
        {
            this.player = player;
            this.monster = monster;
            Taming = player != null && monster != null ? new TamingRules(player, monster, NullTelemetryScope.Instance) : null;
            chase.ConfigureChaseSpeed(PlaceholderChaseSpeed);
            if (player != null) player.UseKnockdown(knockdown);
            monsterHitInterceptor = InterceptMonsterHit;
        }

        public TamingRules Taming { get; }
        public string CurrentControlId => Taming.CurrentControlId;

        /// <summary>
        /// 按场景显式数组登记巡逻者：下标 0 = 主怪（构造时注入的那份 <see cref="MonsterRules"/>），其余由
        /// <see cref="MonsterRules.CreateForActor"/> 造。
        /// <para>
        /// 一场遭遇多种怪（PRP/combat-iteration D2，2026-10-14）多收两个<b>可选</b>数组，长度须与 <paramref name="ids"/> 相同：
        /// <paramref name="kindIds"/>——每只的种类覆盖，<c>0</c> = 沿用主怪种类；
        /// <paramref name="fullParticipants"/>——每只是否完整参与者，<c>false</c> = 只巡逻 / 感知 / 攻击的附加巡逻者。
        /// <b>两个数组的第 0 位都被忽略</b>：主怪的种类由 <c>MonsterInstaller</c> 的种类 id 定，主怪恒为完整参与者。
        /// 不传（null）= 全部取默认值 = 加这两个参数之前的行为逐字不变（巡逻者乙靠这一条）。
        /// </para>
        /// </summary>
        public void ConfigureTaming(string playerId, string playerName, string[] ids, string[] names, Vector2[][] routes,
            int[] kindIds = null, bool[] fullParticipants = null)
        {
            if (ids == null || names == null || routes == null || ids.Length == 0 || ids.Length > 128
                || ids.Length != names.Length || ids.Length != routes.Length)
                throw new ArgumentException("巡逻者身份和路线数量不匹配");
            if ((kindIds != null && kindIds.Length != ids.Length) || (fullParticipants != null && fullParticipants.Length != ids.Length))
                throw new ArgumentException("巡逻者种类或完整参与者标记的数量与身份数量不匹配");
            var unique = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { playerId };
            for (int i = 0; i < ids.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(ids[i]) || ids[i].Length > 128 || !unique.Add(ids[i]) || string.IsNullOrWhiteSpace(names[i])
                    || routes[i] == null || routes[i].Length == 0 || routes[i].Length > 1024)
                    throw new ArgumentException("场景巡逻者稳定标识缺失或重复：" + ids[i]);
                if (kindIds != null && kindIds[i] < 0)
                    throw new ArgumentException("场景巡逻者种类 Id 不能为负数：" + ids[i]);
            }

            Taming.Configure(playerId, playerName);
            authoredRoutes = new Vector2[routes.Length][];
            participantFlags = new bool[ids.Length];
            killDropSettled = new bool[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                MonsterRules rules = i == 0 ? monster : monster.CreateForActor(ids[i], kindIds == null ? 0 : kindIds[i]);
                participantFlags[i] = i == 0 || (fullParticipants != null && fullParticipants[i]);
                authoredRoutes[i] = (Vector2[])routes[i].Clone();
                Taming.RegisterTarget(ids[i], names[i], rules);
            }

            // 巡逻者是在这里才造出来的，所以遮挡要在这里补发一次（UseStealth 可能跑在它前面，见 BindOcclusion 注释）。
            ApplyMonsterOcclusion();
        }

        /// <summary>主怪的稳定标识（<c>Taming.TargetIds[0]</c>）；没有驯服表时为 null。</summary>
        public string PrimaryTargetId => Taming == null || Taming.TargetIds.Count == 0 ? null : Taming.TargetIds[0];

        /// <summary>
        /// 这份规则是不是主怪（构造时注入的那份）。处决结算（<c>ExecutionResolver</c>）手里只有目标的规则对象，
        /// 用它判定「这一刀要不要结算整场遭遇」——<b>整场胜负只认主怪</b>（PRP/combat-iteration D2）。
        /// </summary>
        public bool IsPrimaryTarget(MonsterRules rules) => rules != null && monster != null && ReferenceEquals(rules, monster);

        /// <summary>
        /// 某个稳定标识的巡逻者是不是完整参与者：主怪恒为真；附加怪看 <see cref="ConfigureTaming"/> 的标记（默认假）；
        /// 场景里没有这个标识时为假。零分配，可在 tick 路径上调。
        /// </summary>
        public bool IsFullParticipant(string targetId)
        {
            int index = IndexOfTarget(targetId);
            return index == 0 || (index > 0 && index < participantFlags.Length && participantFlags[index]);
        }

        /// <summary>
        /// 主怪 + 全部完整参与者的规则对象（主怪在首位，其余按场景数组顺序）。**每次调用新建数组**，
        /// 只给接线时用（如 <c>MonsterEncounterState.BindExecution</c> 喂处决目标），不要在 tick 里调。
        /// </summary>
        public MonsterRules[] CollectFullParticipants()
        {
            if (Taming == null)
            {
                return monster == null ? Array.Empty<MonsterRules>() : new[] { monster };
            }

            IReadOnlyList<string> ids = Taming.TargetIds;
            int count = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (IsFullParticipantAt(i)) count++;
            }

            var result = new MonsterRules[count];
            int next = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (IsFullParticipantAt(i)) result[next++] = Taming.GetTarget(ids[i]);
            }

            return result;
        }

        private bool IsFullParticipantAt(int index) => index == 0 || (index < participantFlags.Length && participantFlags[index]);

        // 在驯服表里按稳定标识找下标（Ordinal 比较，零分配）；找不到为 -1。
        private int IndexOfTarget(string id)
        {
            if (Taming == null || id == null) return -1;
            IReadOnlyList<string> ids = Taming.TargetIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal)) return i;
            }

            return -1;
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
        /// 接入露馅结算（PRP/combat-iteration D1）：身份规则 + 账簿 + 怀疑度，与 <see cref="BindIdentity"/> 的身份状态配合使用。
        /// 每 tick 在怪物推进之后、潜行结算之前判一次（见 <see cref="SettleExposure"/>）。
        /// <para>
        /// 做成后置装配的理由同 <see cref="UseStealth"/>（构造参数不能变）。三件任一为 null、或没 <see cref="BindIdentity"/>，
        /// 露馅就不结算——独立原型场景与纯 Monster 测试作用域没有 Identity，行为与加之前逐字一致。
        /// </para>
        /// </summary>
        public void BindExposure(IdentityRules rules, IdentityLedger ledger, SuspicionState suspicion)
        {
            exposureRules = rules;
            exposureLedger = ledger;
            exposureSuspicion = suspicion;
            exposureLatch = ExposureCause.None;
        }

        /// <summary>
        /// 当前生效的被抓后果口径（<see cref="UseStealth"/> 存下的内核那一份；不装配时口径为 <see cref="ChaseCaughtPolicy.None"/>）。
        /// 其中的 <see cref="ChaseOutcomeSettings.PlayerHealth"/> 是配置上限——被抓结算时按实时血量重建，不读它。
        /// </summary>
        public ChaseOutcomeSettings ChaseOutcomeSettings => chaseOutcome;

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
        /// 接入升级回合制的端口（PRP/combat-iteration D5；实现方是 <c>Game.Battle.BattleFlow</c>，
        /// 由 <c>BattleInstaller</c> 的构建回调与 <c>flow.BindEntryContext(step)</c> 同处调用——Battle → Monster 单向，本类不认识 Battle）。
        /// <para>
        /// 做成后置装配的理由同 <see cref="UseStealth"/>（构造参数不能变）。<b>不接 = 不升级</b>，两个拦截点逐字走原路径：
        /// 独立原型场景与纯 Monster 测试作用域没有战斗侧。传 null 等于断开。
        /// </para>
        /// </summary>
        public void BindEscalation(IEncounterEscalationPort port) => escalationPort = port;

        /// <summary>当前装配的升级端口（null = 没装配）。只给接线自检读（容器接线测试，pitfalls.md:225）。</summary>
        public IEncounterEscalationPort EscalationPort => escalationPort;

        /// <summary>
        /// 接入回放播放器（2026-10-16 代码审查后补）：<c>MonsterInstaller</c> 的构建回调里 <c>TryResolve</c> 后调，
        /// 与 <c>ReplayStateRegistry</c> 同处（两者都在 Boot 的同一个容器里）。
        /// <b>不接 = 永远不当自己在回放</b>，行为与接线前逐字一致（独立原型场景、纯 Monster 测试作用域、Showcase 都不接）。
        /// 做成后置装配的理由同 <see cref="UseStealth"/>：构造参数不能变。传 null 等于断开。
        /// </summary>
        public void BindReplay(ReplayPlayer player) => replayPlayer = player;

        /// <summary>当前装配的回放播放器（null = 没装配）。只给接线自检读（容器接线测试，pitfalls.md:225）。</summary>
        public ReplayPlayer ReplayPlayer => replayPlayer;

        /// <summary>
        /// 此刻在放回放（<see cref="ReplayPlayer.IsActive"/>：放完 / <c>Stop</c> 之后为假，载入未播时为假）。
        /// <para>
        /// <b>回放期间整个升级回合制关掉</b>（2026-10-16 代码审查指出、当场改）。原因：
        /// 升级的结果是在**模拟循环之外**写回的（回合制战斗异步跑，结束时才回调 <see cref="CompleteEscalation"/>），
        /// 而回放只重放输入与 tick——录制里如果发生了升级，重放时端口还绑着的话，会在「同一 tick」走一条
        /// 录制时没走过的路（战斗不在回放的确定性序列里，见 <c>BattleSetup</c> 文件头「回放不覆盖回合制战斗」）。
        /// 关掉之后两个拦截点都退回原路径，回放与录制逐字节一致。
        /// </para>
        /// <para>与「处决不进回放」同一条规矩：**副作用不进确定性序列**。判据用 <c>IsActive</c> 而不是
        /// 「有回放载入」——载入未播、放完停止之后，实时玩法要照常能升级。</para>
        /// </summary>
        public bool IsReplaying => replayPlayer != null && replayPlayer.IsActive;

        /// <summary>
        /// 升级在途：一下伤害已经交给回合制，战斗还没交回结果。在途期间 <see cref="Step"/> 像「结果待消费」那样早退
        /// （双方规则都不推进），<c>SessionStateAdapter.BattleResultPending</c> 把它并进存档闸（战中不可存档）。
        /// 瞬态：不进快照、不进存档。
        /// </summary>
        public bool IsEscalating => escalating;

        /// <summary>在途那一只怪的稳定 id；不在途时为 null。</summary>
        public string EscalationTargetId => escalating ? escalationTargetId : null;

        /// <summary>
        /// 战斗侧交回升级的结果（PRP/combat-iteration D5；调用方是 <c>BattleFlow</c>，时机是收场黑幕下、恢复世界之后、揭幕之前）。
        /// <b>只接受一次</b>：不在途时调用无效（返回 false 并埋一条 W 级 <c>escalation_completion_ignored</c>）。
        /// <list type="bullet">
        /// <item><b>胜</b>（用户拍板「打赢视同击杀」）：目标强制死亡（<see cref="MonsterRules.ApplyBattleDefeat"/>，无视 killable），
        /// 掉落走按目标的闸门结一次（<see cref="SettleKillDropOnce"/>）；实时生命按比例写回
        /// （<see cref="EncounterEscalationRequest.ScaleHealthBack"/>：<c>round(剩余 / 战斗上限 × 实时上限)</c>，至少 1，经 <c>PlayerRules.SetHealth</c>）。
        /// 进场方向仍向上取整（<see cref="EncounterEscalationRequest.ScaleHealth"/>）——出场改四舍五入是为了让原样打完的血量不走样，
        /// 理由写在 <c>ScaleHealthBack</c> 上（2026-10-16 代码审查）。</item>
        /// <item><b>负</b>：玩家生命置 0，走既有的生命归零收尾（剧情阶段内判 Downed；自由探索由镜碎页接手）。</item>
        /// </list>
        /// 之后立即跑一遍 tick 末尾那段胜负结算（<see cref="SettleEncounterOutcome"/>）：目标是主怪且这一场登记过「一仗」时判 Victory，
        /// 玩家归零判 Defeat——<c>EncounterBattleBridge</c> 照常在下一 tick 取走、回写剧情（桥不改）。
        /// 不等下一 tick 再判，是因为镜碎页在渲染帧里看到生命归零就会 <c>End()</c>，抢在下一 tick 之前把遭遇停掉。
        /// </summary>
        /// <param name="victory">回合制里玩家赢了没有。</param>
        /// <param name="battleHealth">玩家在回合制里的剩余生命（胜时用来按比例写回）。</param>
        /// <param name="battleMaxHealth">回合制里的玩家生命上限，必须大于 0。</param>
        /// <returns>这次交回被接受了没有。</returns>
        public bool CompleteEscalation(bool victory, int battleHealth, int battleMaxHealth)
        {
            if (battleMaxHealth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(battleMaxHealth), battleMaxHealth, "回合制里的玩家生命上限必须大于 0");
            }

            if (!escalating)
            {
                telemetry.TrackWarn("escalation_completion_ignored",
                    TelemetryProps.Of(("victory", victory), ("hp", battleHealth)));
                return false;
            }

            string targetId = escalationTargetId;
            escalating = false;
            escalationTargetId = null;
            int healthBefore = player.Model.Health;
            if (victory)
            {
                MonsterRules target = Taming == null ? null : Taming.GetTarget(targetId);
                if (target != null)
                {
                    target.ApplyBattleDefeat();
                }

                player.SetHealth(EncounterEscalationRequest.ScaleHealthBack(battleHealth, battleMaxHealth, player.MaxHealth));
                SettleKillDropOnce();
            }
            else
            {
                player.SetHealth(0);
            }

            SettleEncounterOutcome();
            telemetry.Track("escalation_completed", ("target", targetId), ("victory", victory),
                ("hp_before", healthBefore), ("hp_after", player.Model.Health));
            return true;
        }

        /// <summary>
        /// 战斗没开成或中途失败（场景缺失、表现层报错、取消）时，战斗侧调它退出「升级在途」：
        /// <b>不改血量、不判胜负</b>，被吞掉的那一下就此作废，遭遇从下一 tick 起照常推进。不在途时什么都不做，返回 false。
        /// </summary>
        public bool AbortEscalation()
        {
            if (!escalating)
            {
                return false;
            }

            string targetId = escalationTargetId;
            escalating = false;
            escalationTargetId = null;
            telemetry.TrackWarn("escalation_aborted", TelemetryProps.Of(("target", targetId)));
            return true;
        }

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
            // 被抓后果口径（PRP/combat-iteration D4）：存下内核那一份，被抓时按实时血量重建再裁决（见 ResolveCaught）。
            chaseOutcome = kernel.ChaseOutcomeSettings;
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
        /// （背后处决**主怪**命中即写 Victory，见它的注释；处决附加怪不调这里，见 <see cref="IsPrimaryTarget"/>）。
        /// 消费方是 <c>EncounterBattleBridge</c>。
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

        /// <summary>
        /// 读档恢复。校验全部放在改写之前（错误存档不能部分改写现场）。
        /// <para>
        /// <b>巡逻者按稳定 id 对齐</b>（PRP/combat-iteration D2，2026-10-14；此前要求与场景数量、顺序一致，否则抛）：
        /// 附加巡逻者先回到场景初始状态（<see cref="ResetAdditionalTargets"/>），再由 <c>Taming.Restore</c> 按 id 覆盖存档里有的——
        /// 于是<b>存档里没有、场景里有的按场景初始状态开局</b>（场景加了怪，读旧档）；
        /// <b>存档里有、场景里没有的丢弃</b>（场景删了怪）。两种都记一条 <c>encounter_restore_aligned</c> 的 Warn。
        /// </para>
        /// <para>
        /// 主怪的口径：先取存档的 <see cref="EncounterSaveData.Monster"/>（按构造它就是存档那一场的主怪，
        /// <see cref="EncounterSaveData.Validate"/> 保证它等于 <c>TamingTargets[0]</c>；v1 旧档走的也是这一句），
        /// 再按 id 覆盖。所以主怪的稳定 id 即便改了名，主怪仍拿到存档的主怪状态（那一条按 id 对不上，记进 dropped）。
        /// </para>
        /// </summary>
        public void Restore(EncounterSaveData saved)
        {
            if (saved == null) throw new System.ArgumentNullException(nameof(saved));
            saved.Validate();
            if (saved.TamingTargets != null && saved.PlayerActorId != Taming.PlayerId)
                throw new ArgumentException("存档玩家稳定标识与场景不匹配");
            Taming.ValidateRestore(saved.TamingTargets);
            // —— 以上全是校验，以下才改写现场。
            ResetAdditionalTargets();
            player.Model.Restore(saved.Player);
            ResetAdditionalTargets();
            monster.Restore(saved.Monster);
            Taming.Restore(saved.TamingTargets, saved.ControlledActorId, saved.PreviousTame);
            if (saved.TamingTargets != null)
            {
                TrackSavedAlignment(saved.TamingTargets);
            }

            // 读档不走 Begin：掉落闸门按恢复后的生死对齐（已死 = 已结算，防重复发放），露馅边沿闸清零。
            AlignDropGatesWithHealth();
            exposureLatch = ExposureCause.None;
            // 升级在途是瞬态，存档里不可能有（在途期间不可存档）：恢复出来的现场一律不在途。
            ClearEscalation();
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
            Array.Clear(killDropSettled, 0, killDropSettled.Length);
            exposureLatch = ExposureCause.None;
            // 新的一场遭遇不继承上一场的升级在途（那一场的战斗若之后才交回结果，会因「不在途」被忽略并留痕）。
            ClearEscalation();
            IsActive = true;
        }

        public void End()
        {
            IsActive = false;
            if (Taming != null) Taming.TryControl(Taming.PlayerId);
            // 离场清瞬时事实，避免泄漏到下一场遭遇。
            chasePhase = ChasePhase.Idle;
            facts.Clear();
            exposureLatch = ExposureCause.None;
        }

        /// <summary>
        /// 读档 / 快照恢复之后对齐掉落闸门（PRP/combat-iteration 执行中修订，2026-10-14 用户同意并进 W2）：
        /// 恢复出来<b>已经死了</b>的目标直接标为「已结算」——它的掉落在存档那一场已经结过，读档后再结一次就是重复发放；
        /// 活着的目标闸门打开，等它这一场死时照常结一次。读档不走 <see cref="Begin"/>，所以这一步只能放在恢复路径上。
        /// </summary>
        private void AlignDropGatesWithHealth()
        {
            if (Taming == null)
            {
                return;
            }

            IReadOnlyList<string> ids = Taming.TargetIds;
            int count = GameMath.Min(ids.Count, killDropSettled.Length);
            for (int i = 0; i < count; i++)
            {
                MonsterRules target = Taming.GetTarget(ids[i]);
                killDropSettled[i] = target != null && target.Model.Health <= 0;
            }
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
            // 回放开始时把实时玩法留下的「升级在途」清掉，再走早退：否则升级在途会让本类**整个回放期都不推进**
            // （那个标记只在 CompleteEscalation / AbortEscalation / 恢复快照时回落，而回放里没人会来交回结果）。
            AbortStaleEscalation();
            // 升级在途（PRP/combat-iteration D5）与「结果待消费」同一个早退：世界暂停真正生效之前的那几帧里，
            // 双方都不推进，不会再触发一次升级，也不会继续挨打。
            if (!IsActive || (PendingResult != Result.None && !ResultConsumed) || escalating)
            {
                // 本 tick 双方规则都不推进：仍把上一 tick 位置对齐，否则视图会在结算前最后一步的两点间来回插值。
                player.Model.SyncPreviousPosition();
                SyncMonsterPreviousPositions();
                return;
            }

            escalationTookOverThisTick = false;
            InputCommand command = context.Input;
            string requested = SelectionId(command.Axis1.x);
            if (command.HasButton(InputCommand.ButtonSelectControl)) Taming.TryControl(requested);
            if (command.HasButton(InputCommand.ButtonTamePressed)) Taming.ProcessToggle(false);
            Taming.ProcessToggle(command.HasButton(InputCommand.ButtonTame) && (command.Axis1.x == 0f || requested != null), requested);
            bool enemyControlled = Taming.IsControllingEnemy;
            Vector2 movement = command.Axis0;
            if (GameMath.SqrMagnitude(movement) > 1f) movement = GameMath.Normalize(movement);
            if (playerSpeed != null) movement *= playerSpeed();
            var playerIntent = new PlayerIntent(
                enemyControlled ? Vector2.zero : movement,
                !enemyControlled && command.HasButton(InputCommand.ButtonSneak) && (playerCanSneak == null || playerCanSneak()),
                !enemyControlled && command.HasButton(InputCommand.ButtonDisguise),
                !enemyControlled && command.HasButton(InputCommand.ButtonAttack),
                !enemyControlled && command.HasButton(InputCommand.ButtonRun));
            bool attacked = player.Step(in playerIntent, context.DeltaTime);
            PlayerSnapshot target = player.Model.Snapshot;
            foreach (string id in Taming.TargetIds)
            {
                MonsterRules rules = Taming.GetTarget(id);
                MonsterModel enemy = rules.Model;
                if (!attacked || Taming.IsPassive(id) || Taming.IsObservationOnly(id) || Taming.IsTargetTamed(id) || !Taming.IsAvailable(id) || enemy.Health <= 0
                    || GameMath.Distance(target.Position, enemy.Position) > player.AttackRange) continue;
                Vector2 difference = enemy.Position - target.Position;
                if (GameMath.SqrMagnitude(difference) == 0f
                    || GameMath.Dot(target.Facing, GameMath.Normalize(difference)) >= 0f)
                {
                    // 拦截点一（PRP/combat-iteration D5）：扣血**之前**问一次要不要升级回合制；接管了就吞掉这一下，
                    // 本 tick 余下的目标也不再结算（这一刀交给回合制了）。
                    if (TryEscalate(id, rules, PlayerHitTrigger(enemy.Mode)))
                    {
                        break;
                    }

                    var damage = new DamageIntent(player.AttackDamage);
                    rules.ApplyDamage(in damage, in target);
                }
            }

            if (escalationTookOverThisTick)
            {
                // 怪物本 tick 没推进：对齐它们的上一 tick 位置（玩家这一 tick 已经走过，保留它自己的插值起点）。
                SyncMonsterPreviousPositions();
                return;
            }

            // 被抓裁决用的「此前被抓 / 击倒次数」要取怪物出手**之前**的值：这一 tick 的命中会在 AdvanceTargets 里先喂给击倒状态机。
            int knockdownsBefore = knockdown.KnockdownCount;
            // 拦截点二（PRP/combat-iteration D5）：怪物出手、扣玩家血之前经回调问一次；没装配端口时不传回调 = 逐字旧路径。
            bool enemyAttacked = Taming.AdvanceTargets(command.Axis0, context.DeltaTime, identity != null && identity.IsInEffect,
                escalationPort == null ? null : monsterHitInterceptor);
            if (escalationTookOverThisTick)
            {
                // 这一下交给了回合制：露馅 / 潜行 / 被抓 / 掉落 / 胜负都不再因它结算，等战斗交回结果（CompleteEscalation）。
                return;
            }

            if (EncounterId != 0 && Taming.IsTamed && PendingResult == Result.None) AbortBattle();
            // 露馅结算（PRP/combat-iteration D1）：怪物这一 tick 的感知在 AdvanceTargets 里刚算完，潜行结算之前交给身份规则——
            // 这样被惊动的怪在同一 tick 的追逐结算里就已经是敌对态。
            SettleExposure();
            bool participantCaught = AnyAdditionalParticipantAttacked();
            // 双方规则推进之后再做潜行结算（StealthKernel 的接线建议）：先把这一 tick 的结果合成事实键。
            SettleStealth(in target, monster.Model, enemyAttacked, participantCaught, knockdownsBefore, context.DeltaTime);
            // 掉落：**死亡那一处**结算，与「这算不算一场战斗」无关——遭遇里没有 EncounterId 也会掉。
            // 放在 Step 末尾（双方规则都推完之后）是为了让「主怪这一 tick 死了没有」这个判断拿到终态，
            // 而不是在某个中间分支里提前读一次生命。
            SettleKillDropOnce();
            SettleEncounterOutcome();
        }

        /// <summary>
        /// 整场遭遇的胜负结算（tick 末尾那一段；2026-10-14 起 <see cref="CompleteEscalation"/> 也走这里，口径一字不差）：
        /// 只在这一场登记过「一仗」（<see cref="EncounterId"/> 非 0）且还没出结果时判——玩家生命归零 → Defeat（Downed），
        /// 主怪生命归零 → Victory。整场胜负只认主怪（PRP/combat-iteration D2）。
        /// </summary>
        private void SettleEncounterOutcome()
        {
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
                // Result.Aborted 由 AbortBattle 单独写，不承载 BattleSettlement。
            }
            OnAdvanced?.Invoke(context.DeltaTime);
        }

        // 主怪与全部巡逻者对齐上一 tick 位置（本 tick 它们没推进时调，免得视图在两点间来回插值）。
        // 主怪那一句是 Taming 为空时的兜底（主怪也在 TargetIds 里，重复对齐无害）。
        private void SyncMonsterPreviousPositions()
        {
            monster.Model.SyncPreviousPosition();
            foreach (string id in Taming.TargetIds) Taming.GetTarget(id).Model.SyncPreviousPosition();
        }

        // ───────────────────────── 升级回合制（PRP/combat-iteration D5） ─────────────────────────

        /// <summary>
        /// 还挂着没被消费的结果（<see cref="PendingResult"/> 或 <see cref="BattleSettlement"/>）：这时不升级，
        /// 免得一场遭遇同时有两份结果在路上（桥只认一份）。
        /// </summary>
        private bool HasUnconsumedOutcome => (PendingResult != Result.None || BattleSettlement.HasValue) && !ResultConsumed;

        /// <summary>玩家打中的怪察觉了没有：警戒 / 敌对 → 正面攻击情形；巡逻走 / 巡逻停 → 偷袭情形。</summary>
        private static EncounterEscalationTrigger PlayerHitTrigger(MonsterMode mode) =>
            mode == MonsterMode.Alert || mode == MonsterMode.Hostile
                ? EncounterEscalationTrigger.PlayerHitAware
                : EncounterEscalationTrigger.PlayerHitUnaware;

        // 埋点里的触发名：写成常量串，不用 enum.ToString()（那会在命中路径上分配）。
        private static string TriggerName(EncounterEscalationTrigger trigger)
        {
            switch (trigger)
            {
                case EncounterEscalationTrigger.PlayerHitUnaware:
                    return "sneak_strike";
                case EncounterEscalationTrigger.PlayerHitAware:
                    return "frontal_strike";
                case EncounterEscalationTrigger.MonsterHitPlayer:
                    return "struck";
                default:
                    return "none";
            }
        }

        /// <summary>
        /// 拦截点二的回调（经 <see cref="TamingRules.AdvanceTargets"/>）：某只怪打中了玩家、还没扣血。
        /// 本 tick 已经有一下交给了回合制时，余下的出手一律吞掉（世界马上要按住，不该继续挨打）；否则问要不要升级。
        /// </summary>
        private bool InterceptMonsterHit(string targetId, MonsterRules rules) =>
            escalationTookOverThisTick || TryEscalate(targetId, rules, EncounterEscalationTrigger.MonsterHitPlayer);

        /// <summary>
        /// 两个拦截点共用的升级判定。<b>条件全部满足才升级</b>，否则返回 false、调用方逐字走原路径：
        /// <list type="number">
        /// <item>装配了端口；目标是完整参与者、活着、没被驯服；玩家没在操控被驯服的怪——不满足的不算「候选」，静默放行；</item>
        /// <item><b>不在回放中</b>（<see cref="IsReplaying"/>）——在回放里永远不升级，静默放行、不埋点
        /// （回放期间不是「被拒」，是这条功能整个关掉，理由见 <see cref="IsReplaying"/>）；</item>
        /// <item>这种触发情形的开关开着（<see cref="MonsterConfig.AllowsEscalation"/>）——否则 <c>disabled</c>；</item>
        /// <item>没有待消费的结果、也不在升级途中——否则 <c>in_flight</c>；</item>
        /// <item>端口说该种类能升级——否则 <c>not_configured</c>；</item>
        /// <item>端口接受——否则带端口给的原因（<c>battle_busy</c> / <c>rule_rejected</c> / …）。</item>
        /// </list>
        /// 被接受时进入「升级在途」并标记本 tick 已接管；候选的每一次判定都埋一条 <c>escalation_requested</c>（命中是离散事件，不是每 tick）。
        /// </summary>
        private bool TryEscalate(string targetId, MonsterRules rules, EncounterEscalationTrigger trigger)
        {
            if (escalationPort == null || IsReplaying || rules == null || Taming == null || Taming.IsControllingEnemy
                || !IsFullParticipant(targetId) || Taming.IsTargetTamed(targetId) || rules.Model.Health <= 0)
            {
                return false;
            }

            int kindId = rules.Kind == null ? 0 : rules.Kind.KindId;
            string reject;
            if (!rules.Config.AllowsEscalation(trigger))
            {
                reject = EscalationRejectDisabled;
            }
            else if (escalating || HasUnconsumedOutcome)
            {
                reject = EscalationRejectInFlight;
            }
            else if (!escalationPort.CanEscalate(kindId))
            {
                reject = EscalationRejectNotConfigured;
            }
            else
            {
                // 先进入在途再问端口：端口可能在这次调用里就同步打完整场并回调 CompleteEscalation（见端口契约），
                // 等返回 true 再置位的话，那次回调会被当成「不在途」丢掉。被拒或抛异常时撤回。
                var request = new EncounterEscalationRequest(targetId, kindId, trigger, player.Model.Health, player.MaxHealth);
                escalating = true;
                escalationTargetId = targetId;
                bool accepted;
                string portReject;
                try
                {
                    accepted = escalationPort.TryEscalate(this, in request, out portReject);
                }
                catch
                {
                    ClearEscalation();
                    throw;
                }

                if (accepted)
                {
                    escalationTookOverThisTick = true;
                    telemetry.Track("escalation_requested", ("target", targetId), ("kind", kindId),
                        ("trigger", TriggerName(trigger)), ("reject", "none"));
                    return true;
                }

                escalating = false;
                escalationTargetId = null;
                reject = string.IsNullOrEmpty(portReject) ? EscalationRejectByPort : portReject;
            }

            telemetry.Track("escalation_requested", ("target", targetId), ("kind", kindId),
                ("trigger", TriggerName(trigger)), ("reject", reject));
            return false;
        }

        /// <summary>
        /// 潜行结算：把「看不看得见 / 能不能处决 / 击倒状态 / 追逐状态」合成事实键。
        /// <para>
        /// 贴墙纪律：本方法在固定 tick 里，只做纯几何与查表——遮挡体是场景一次性喂进来的纯数据
        /// （<see cref="EncounterSceneView.CollectSightOccluders"/>），没有任何物理查询与场景查找。
        /// </para>
        /// </summary>
        private void SettleStealth(in PlayerSnapshot target, MonsterModel enemy, bool enemyAttacked, bool participantCaught,
            int knockdownsBefore, float deltaTime)
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

            SettleChase(in target, enemy, perceives, enemyAttacked, participantCaught, knockdownsBefore, deltaTime);
        }

        /// <summary>
        /// 追逐结算（`chase.*` 四键的写入方＝Chase / Monster，字典 §4.3）：本类只写 active / escaped / caught。
        /// <para>
        /// 触发口径 [推断]：怪物进入敌对追击（<see cref="MonsterMode.Hostile"/>，即红区或橙区警戒升满）
        /// 就是起追（`04_追逐.md:87-88` R12/R13）。内容侧的其它追逐来源（账簿超量、怀疑度、揭露、
        /// 脚本化固定追逐 T1–T8）本波不接线，因此 `chase.fixed` **恒不写**（与 `stealth.cover`
        /// 「遮挡没做时恒不写」同一处理，字典里已有先例）。
        /// </para>
        /// <para>
        /// <b>被抓判据（PRP/combat-iteration D4，2026-10-14 起放宽到任一完整参与者）</b>：
        /// <list type="bullet">
        /// <item>主怪：原判据逐字不变——追逐进行中（<c>chasePhase == Chasing</c>）且主怪这一 tick 打到活着的玩家；</item>
        /// <item>附加的完整参与者：它这一 tick 打到活着的玩家（<paramref name="participantCaught"/>）即算被抓。</item>
        /// </list>
        /// 为什么不把附加怪也塞进 <see cref="ChaseRules"/>：它是<b>单追兵</b>状态机（一份 grace、一个距离、一份视线），
        /// <c>chase.active</c> / <c>chase.escaped</c> 的语义都绑在主怪这一只身上；改成多追兵会动这两个键的口径。
        /// 而「被抓」本身不需要追逐相位——<see cref="MonsterRules.Step"/> 只在敌对（追击）态才出手，
        /// 「敌对态下贴身打中」就是「追上了」。所以只在被抓这一个判据上并入，ChaseRules 的语义一个字不动。
        /// 已知差异：附加怪抓到人时 <c>chase.caught</c> 为真而 <c>chase.active</c> 可能为假（主怪并没在追）。
        /// </para>
        /// <para>
        /// 被抓的后果交给 <see cref="ResolveCaught"/>（<see cref="ChaseCaughtRules"/>）；<c>chase.caught</c> 事实照旧每次被抓都写。
        /// </para>
        /// </summary>
        private void SettleChase(in PlayerSnapshot target, MonsterModel enemy, bool pursuerSees, bool enemyAttacked,
            bool participantCaught, int knockdownsBefore, float deltaTime)
        {
            bool pursuerAlive = enemy.Health > 0;
            float distance = GameMath.Distance(enemy.Position, target.Position);
            bool caught = false;
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
                // 只说后果、没说判定）。后果走 ResolveCaught（2026-10-14 起接 ChaseCaughtRules），不写 Result。
                caught = verdict.CaughtThisTick || (enemyAttacked && target.IsAlive);
            }

            // 放宽（D4）：附加的完整参与者这一 tick 打到活着的玩家也算被抓，不经 ChaseRules（理由见方法注释）。
            if (participantCaught && target.IsAlive)
            {
                caught = true;
            }

            if (caught)
            {
                facts.Set(StealthFactKeys.ChaseCaught, true);
                ResolveCaught(knockdownsBefore);
            }

            facts.Set(StealthFactKeys.ChaseActive, chasePhase == ChasePhase.Chasing);
        }

        /// <summary>
        /// 被抓后果（PRP/combat-iteration D4，用户 2026-10-14 拍板「先击倒，没血才判死」）：
        /// 用<b>实时血量</b>重建 <see cref="ChaseOutcomeSettings"/>（内核那一份里的生命是配置上限），
        /// 「此前被抓次数」取这一 tick 怪物出手之前的击倒次数，交给 <see cref="ChaseCaughtRules.Resolve"/>。
        /// <list type="bullet">
        /// <item>判定失败（<c>IsFailure</c>）→ 玩家生命置 0，走既有的生命归零收尾（本 tick 末尾：剧情阶段内判 Downed；
        /// 自由探索由镜碎页接手）。<b>不新增 Result 值</b>，快照结构不变。</item>
        /// <item>进击倒（<c>EntersKnockdown</c>）→ 什么都不做：这一下受击已经由 <c>PlayerRules.ApplyDamage</c> 喂过击倒状态机。</item>
        /// </list>
        /// 口径为 None（不装配内核时的默认）时什么都不发生，与加之前一致。只在被抓那一 tick 调，埋点一条 <c>chase_caught</c>。
        /// </summary>
        private void ResolveCaught(int caughtCountSoFar)
        {
            int health = player.Model.Health;
            var settings = new ChaseOutcomeSettings(chaseOutcome.Policy, health, chaseOutcome.FailAfterCaughtCount);
            ChaseOutcome outcome = ChaseCaughtRules.Resolve(in settings, caughtCountSoFar);
            if (outcome.IsFailure && health > 0)
            {
                player.SetHealth(0);
            }

            telemetry.Track("chase_caught", ("policy", (int)chaseOutcome.Policy), ("failure", outcome.IsFailure),
                ("health", health), ("count", caughtCountSoFar));
        }

        /// <summary>
        /// 下标 <paramref name="index"/> 处的目标若是「这一 tick 被 AI 推进过的完整参与者」就返回它的规则，否则 null：
        /// 完整参与者（主怪恒是）、可用、未被驯服、活着。露馅信号、惊动、被抓判据三处都只认这一组——
        /// 字段为默认值的附加巡逻者（巡逻者乙）因此不产生信号、不被惊动、不触发被抓后果。零分配。
        /// </summary>
        private MonsterRules SteppedFullParticipantAt(int index)
        {
            if (!IsFullParticipantAt(index))
            {
                return null;
            }

            string id = Taming.TargetIds[index];
            if (!Taming.IsAvailable(id) || Taming.IsTargetTamed(id))
            {
                return null;
            }

            MonsterRules rules = Taming.GetTarget(id);
            return rules == null || rules.Model.Health <= 0 ? null : rules;
        }

        /// <summary>附加的（非主怪）完整参与者里，这一 tick 有没有哪只打到了玩家（主怪走 SettleChase 的原判据）。</summary>
        private bool AnyAdditionalParticipantAttacked()
        {
            if (Taming == null)
            {
                return false;
            }

            IReadOnlyList<string> ids = Taming.TargetIds;
            for (int i = 1; i < ids.Count; i++)
            {
                MonsterRules rules = SteppedFullParticipantAt(i);
                if (rules != null && rules.AttackedThisStep)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 露馅结算（PRP/combat-iteration D1；用户 2026-10-14 拍板「露馅 → 进追逐，且借来的身份当场失效」，口径在资产里）。
        /// <list type="number">
        /// <item>没接线（<see cref="BindExposure"/> / <see cref="BindIdentity"/> 缺一）或玩家<b>没在借身份</b> → 跳过（负对照）；</item>
        /// <item>汇总「主怪 + 完整参与者」本 tick 的感知档位（<see cref="MonsterRules.SensedThisStep"/>）→ 红区 / 橙区信号；
        /// 账簿与怀疑度由身份规则自己读；</item>
        /// <item>命中原因与上一次结算的相同就不再结算（边沿闸 <c>exposureLatch</c>：站在红区里不会每 tick 再判一遍）；</item>
        /// <item>后果为追逐、或身份已失效 → 惊动本 tick 察觉到玩家的那几只完整参与者（<see cref="MonsterRules.Provoke"/>），
        /// 之后的追击沿用既有的敌对态与 ChaseRules；</item>
        /// <item>后果为死亡 → 玩家生命置 0，走既有的生命归零收尾（死亡口径的专门表现不在本次范围）。</item>
        /// </list>
        /// 每 tick 调：未命中时只做下标遍历、布尔判断与身份规则的纯判定，零分配；埋点只在真的结算那一刻记一条。
        /// </summary>
        private void SettleExposure()
        {
            if (exposureRules == null || exposureLedger == null || exposureSuspicion == null || identity == null || Taming == null)
            {
                return;
            }

            if (!identity.IsBorrowing)
            {
                exposureLatch = ExposureCause.None;
                return;
            }

            bool inRed = false;
            bool inOrange = false;
            IReadOnlyList<string> ids = Taming.TargetIds;
            for (int i = 0; i < ids.Count; i++)
            {
                MonsterRules rules = SteppedFullParticipantAt(i);
                if (rules == null)
                {
                    continue;
                }

                int sensed = rules.SensedThisStep;
                inRed |= sensed == 2;
                inOrange |= sensed == 1;
            }

            ExposureSignals signals = ExposureSignals.Alert(inRed, inOrange);
            ExposureCause cause = exposureRules.EvaluateExposure(identity, exposureLedger, exposureSuspicion, in signals);
            if (cause == exposureLatch)
            {
                return;
            }

            exposureLatch = cause;
            if (cause == ExposureCause.None)
            {
                return;
            }

            ExposureResolution resolution = exposureRules.ResolveExposure(identity, exposureLedger, exposureSuspicion, in signals);
            int provoked = 0;
            if (resolution.Outcome == ExposureOutcome.Chase || resolution.IdentityLost)
            {
                Vector2 playerPosition = player.Model.Position;
                for (int i = 0; i < ids.Count; i++)
                {
                    MonsterRules rules = SteppedFullParticipantAt(i);
                    if (rules == null || rules.SensedThisStep <= 0)
                    {
                        continue;
                    }

                    // 已敌对的怪 Provoke 是幂等的（不重置它的追击记忆），这里数的是「被惊动的察觉者」，含本来就敌对的。
                    rules.Provoke(playerPosition);
                    provoked++;
                }
            }

            if (resolution.Outcome == ExposureOutcome.Death)
            {
                player.SetHealth(0);
            }

            telemetry.Track("exposure_applied", ("cause", (int)resolution.Cause), ("outcome", (int)resolution.Outcome),
                ("identity_lost", resolution.IdentityLost), ("provoked", provoked));
        }

        /// <summary>
        /// 主怪与完整参与者死亡时，各结算一次击杀掉落（<see cref="IMonsterDropSink"/>，实现在 <c>Game.Loot</c>）。
        /// <para>
        /// <b>谁会掉（PRP/combat-iteration D2，2026-10-14 起）</b>：主怪（<c>Taming.TargetIds[0]</c>）与勾了
        /// 「完整参与者」的附加怪，<b>各按自己的种类</b>（<c>YaoId</c> / <c>DropItemIds</c>）结。
        /// 两个字段为默认值的附加巡逻者（巡逻者乙）仍然不掉——与 2026-10-08「只结主怪」时逐字一致。
        /// 掉落与「这一场算不算打赢」无关：附加怪死了只掉它自己的，不结算整场。
        /// </para>
        /// <para>
        /// <b>一只怪只结一次</b>：<see cref="killDropSettled"/> 按目标下标各一个闸门，在 <see cref="Begin"/> 里清零，
        /// 所以重开一场遭遇能再掉；同一场里连点多少次、同一只都只掉一次（重复结算会让掉落翻倍）。
        /// </para>
        /// <para>
        /// <b>种类为 null 时跳过</b>：<c>MonsterKind</c> 为 null 表示表没就绪或这只怪没接种类
        /// （<see cref="MonsterRules.Kind"/> 的注释）——那时没有 <c>yaoId</c>，也没有掉落列表，
        /// <b>不猜一个 id 出来</b>。
        /// </para>
        /// <para>
        /// 副作用边界（已知，接受）：掉落是写背包的副作用，**不进确定性内核、不进快照**。
        /// 回放时被驱动方重放「死亡那一 tick」会让闸门从 false 起步、
        /// 于是再掉一次——与本模块既有的「遭遇回放不覆盖 Game.Loot」同一类边界（`Runtime/Battle` 的
        /// 回合制战斗同样不进回放，见 turnbased guide「随机数与回放」）。
        /// </para>
        /// <para>每 tick 都会调：只做下标遍历与布尔判断，零分配；埋点只在真的结算那一刻记。</para>
        /// </summary>
        /// <returns>本次调用里至少结算了一只返回 true。</returns>
        public bool SettleKillDropOnce()
        {
            if (dropSink == null || Taming == null)
            {
                return false;
            }

            bool settledAny = false;
            IReadOnlyList<string> ids = Taming.TargetIds;
            int count = GameMath.Min(ids.Count, killDropSettled.Length);
            for (int i = 0; i < count; i++)
            {
                if (killDropSettled[i] || !IsFullParticipantAt(i))
                {
                    continue;
                }

                string id = ids[i];
                MonsterRules target = Taming.GetTarget(id);
                if (target == null || target.Model.Health > 0)
                {
                    continue;
                }

                // 「有没有种类」先判，再落闸：表没就绪时 Kind 恒为 null（它在 Reset 里查一次、整场不变），
                // 这时落闸与不落闸的结果一样，但先判能让「闸门 = 确实结算过一次」这个含义保持干净。
                MonsterKind kind = target.Kind;
                if (kind == null)
                {
                    // 表没就绪 / 这只怪没接种类：没有 yaoId 就没有掉落内容，跳过而不是猜一个 id 出来。
                    continue;
                }

                // 先落闸再调外部口：万一实现方抛异常，也不会在同一场里被反复重试成多次掉落。
                killDropSettled[i] = true;
                dropSink.SettleKillDrop(kind.YaoId, target.DropItemIds);
                telemetry.Track("kill_drop_settled", ("target", id), ("yao", kind.YaoId),
                    ("items", target.DropItemIds.Count), ("encounter", EncounterId));
                settledAny = true;
            }

            return settledAny;
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

        /// <summary>
        /// 回放快照恢复。<b>巡逻者按稳定 id 对齐</b>（PRP/combat-iteration D2，2026-10-14；此前数量或顺序不同就抛）：
        /// 快照里有、场景里没有的条目，字节照样读掉（<see cref="MonsterRules.SkipSerialized"/>）后丢弃；
        /// 场景里有、快照里没有的附加巡逻者按场景初始状态开局；对不齐时记一条 <c>encounter_restore_aligned</c> 的 Warn。
        /// <para>
        /// <b>先校验、后修改</b>（2026-10-16 代码审查指出、当场改）：整段字节先读进暂存区——每只怪一份
        /// <c>MonsterRules.ReadSerialized</c> 读出来的临时 <see cref="MonsterModel"/>，外加它的稳定 id 与驯服标记；
        /// 全部读完、验完之后才一次性写进现场。此前是边读边写，读到第 N 只发现坏数据（标识重复 / 标识长度非法 /
        /// 巡逻点数量非法）时，前面 N-1 只已经被改掉了——现场半新半旧，比直接抛更糟。
        /// </para>
        /// <para>
        /// 暂存用**已有的** <see cref="MonsterModel"/>，不另开一条「快照 → 怪物状态」的路：字节布局仍只有
        /// <see cref="MonsterRules"/> 一处知道（<c>ReadSerialized</c> 与 <c>Deserialize</c> 共用同一段解析）。
        /// 重复 id 与巡逻点数量都在读的那一段就抛掉，所以写回那一段不可能再抛。暂存的内存只在恢复快照时出现一次，不在 tick 上。
        /// </para>
        /// <para>
        /// 主怪在快照里缺席时不重置：它的状态另有来源——<c>MonsterInstaller</c> 把 <see cref="MonsterRules"/>
        /// 单独注册成回放状态、排在本类之前，已经先恢复过一遍。
        /// </para>
        /// <para>
        /// <b>字节布局没变</b>（<see cref="Serialize"/> 一个字段都没增删、没改顺序），只是读法从「按位置」放宽成「按 id」，
        /// 所以不升 <c>ReplayFormat.CurrentFormatVersion</c>：旧回放照样可读，新回放旧读法也读得懂（数量一致时）。
        /// </para>
        /// </summary>
        public void Deserialize(IStateReader reader)
        {
            IsActive = reader.ReadBool();
            EncounterId = reader.ReadLong();
            ActivationId = reader.ReadLong();
            PendingResult = (Result)reader.ReadByte();
            ResultConsumed = reader.ReadBool();
            string playerId = ReadId(reader);
            string controlled = ReadId(reader);
            bool held = reader.ReadBool();
            int count = reader.ReadInt();
            if (count < 0 || count > 128) throw new InvalidOperationException("回放巡逻者数量非法：" + count);
            IReadOnlyList<string> sceneIds = Taming.TargetIds;
            if (playerId != Taming.PlayerId) throw new InvalidOperationException("回放玩家标识与场景不匹配");
            var flags = new bool[sceneIds.Count];
            var seen = new bool[sceneIds.Count];
            // 暂存区与 **count** 等长（不是与场景等长）：快照比场景多时多出来的那几只是普通条目，
            // 照样读字节、IndexOfTarget 落空就丢弃（D2 的按 id 对齐），不能因为「sceneIds 不够长」提前抛。
            var pendingStates = new MonsterModel[count];
            var pendingRandomStates = new ulong[count];
            var pendingWaypoints = new Vector2[count][];
            var pendingIds = new string[count];
            int dropped = 0;
            for (int i = 0; i < count; i++)
            {
                string id = ReadId(reader);
                bool tamedFlag = reader.ReadBool();
                int index = IndexOfTarget(id);
                if (index < 0)
                {
                    MonsterRules.SkipSerialized(reader);
                    dropped++;
                    continue;
                }

                if (seen[index]) throw new InvalidOperationException("回放巡逻者标识重复：" + id);
                seen[index] = true;
                flags[index] = tamedFlag;
                pendingStates[i] = MonsterRules.ReadSerialized(reader, out ulong randomState, out Vector2[] waypoints);
                pendingRandomStates[i] = randomState;
                pendingWaypoints[i] = waypoints;
                pendingIds[i] = id;
            }

            int fresh = 0;
            for (int i = 1; i < sceneIds.Count; i++)
            {
                if (seen[i]) continue;
                fresh++;
            }

            // Taming 那一侧自己会校验 flags 的长度（不匹配就抛）：放在写进现场之前。
            Taming.ValidateReplayFlags(flags);
            // —— 以上全是读取与校验，以下才写进现场（这一段不再有抛出的可能）。
            for (int i = 0; i < count; i++)
            {
                if (pendingStates[i] == null) continue;
                Taming.GetTarget(pendingIds[i]).ApplySerialized(pendingStates[i], pendingRandomStates[i], pendingWaypoints[i]);
            }

            for (int i = 1; i < sceneIds.Count; i++)
            {
                if (seen[i]) continue;
                if (authoredRoutes != null && i < authoredRoutes.Length) Taming.GetTarget(sceneIds[i]).Reset(authoredRoutes[i]);
            }

            Taming.RestoreReplayFlags(flags, controlled, held);
            TrackAlignment(count, sceneIds.Count, dropped, fresh);
            // 同 Restore：快照恢复不走 Begin，掉落闸门按恢复后的生死对齐，露馅边沿闸清零、升级在途清掉（三者都不进快照）。
            AlignDropGatesWithHealth();
            exposureLatch = ExposureCause.None;
            ClearEscalation();
        }

        // 升级在途是瞬态：Begin / Restore / Deserialize 都从「不在途」起步（不埋点：这不是一次放弃，是现场被整体换掉了）。
        private void ClearEscalation()
        {
            escalating = false;
            escalationTargetId = null;
            escalationTookOverThisTick = false;
        }

        /// <summary>
        /// 回放期间的兜底：实时玩法若在「升级在途」那一瞬被回放接管（回放载入会切推进器，但没人会来交回升级结果），
        /// 这个标记就一直挂着、把 <see cref="Step"/> 钉死在早退上。发现自己在回放且还挂着，就清掉，并埋一条
        /// <c>escalation_abandoned</c> 告诉现场「有一场升级被回放接管了、结果丢了」。
        /// <para>
        /// 不用 <see cref="AbortEscalation"/> 的埋点（那一条的语义是「战斗没开成」，由战斗侧发起），这里是被动丢弃。
        /// </para>
        /// </summary>
        private void AbortStaleEscalation()
        {
            if (!escalating || !IsReplaying)
            {
                return;
            }

            string targetId = escalationTargetId;
            ClearEscalation();
            telemetry.TrackWarn("escalation_abandoned", TelemetryProps.Of(("target", targetId), ("reason", "replay")));
        }

        // 存档那一侧的对齐统计：dropped = 存档里有、场景里没有；fresh = 场景里有、存档里没有的附加巡逻者
        // （主怪不计：它恒先取存档的 Monster 字段，见 Restore 的注释）。
        private void TrackSavedAlignment(TamingTargetSaveData[] saved)
        {
            int dropped = 0;
            for (int i = 0; i < saved.Length; i++)
            {
                if (IndexOfTarget(saved[i].Id) < 0) dropped++;
            }

            IReadOnlyList<string> sceneIds = Taming.TargetIds;
            int fresh = 0;
            for (int i = 1; i < sceneIds.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < saved.Length && !found; j++)
                {
                    found = string.Equals(saved[j].Id, sceneIds[i], StringComparison.Ordinal);
                }

                if (!found) fresh++;
            }

            TrackAlignment(saved.Length, sceneIds.Count, dropped, fresh);
        }

        // 对齐了（场景与存档 / 快照一一对得上）就不记——正常读档不刷屏；对不齐（数量不同必然落在这里，
        // 数量相同但有一只改了 id 也算）记一条 W 级，读档后「怎么少了 / 多了一只怪」时靠它在现场对上号。
        private void TrackAlignment(int saved, int scene, int dropped, int fresh)
        {
            if (dropped == 0 && fresh == 0) return;
            telemetry.TrackWarn("encounter_restore_aligned",
                TelemetryProps.Of(("saved", saved), ("scene", scene), ("dropped", dropped), ("fresh", fresh)));
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

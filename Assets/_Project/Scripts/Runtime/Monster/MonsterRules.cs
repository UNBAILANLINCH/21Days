// 职责：在固定 tick 中处理巡逻、感知、警戒、追击和受伤；MonoBehaviour 只负责表现。
// 为什么新建：GameFlow 管场景而非单个怪物，Core 也不应认识玩法规则。
// 数值来源（真源 docs/design/features-spotlight/06_怪物分层.md）：
//   按种类的那部分（生命、伤害、攻击距离 / 冷却、视野角度、橙区半径、背后近距半径、警戒升满时长、丢失目标时长）
//   经 MonsterConfig.Resolve* 取「种类优先、否则全局」；全局的那部分（巡逻节奏、速度倍率、敌对半径）直接读 MonsterConfig。
//   字段归属的依据与文件头见 MonsterConfig.cs / MonsterKind.cs。
//
// 视线遮挡（2026-10-08「掩体贯通到怪物察觉」，判据变更见下）：
//   在 `Sense` 里，**视锥那两档**（红区 `config.HostileRadius`、橙区 `config.ResolveAlertRadius(kind)`）
//   要额外过一道「两点之间通不通视」（`StealthSight`，纯几何、场景一次性喂入、tick 里无物理查询）。
//   **贴身那一档（`config.ResolveNearSenseRadius(kind)`）不查**：`docs/design/spotlight/06_怪物状态与交互设计文档.md:71-73`
//   明写那是「不分朝向的近距察觉区、玩家需要进入潜行状态才不会被察觉」——近身感知不是视觉，遮挡不该改它。
//   不注入遮挡（`UseOcclusion` 从未被调）时行为与改动前逐位一致：`sight == null` 时视锥分支等同「没有遮挡」。
//   两个查询口（都在这一个文件里，只差「视锥那一支要不要过 sightClear」）：
//     `Detects`（= `Sense`）——**行为层**，含遮挡，警戒 / 敌对 / 追击走它；
//     `PerceivedBySenses`（= `SenseBySensesOnly`）——**不含遮挡**，给事实层算子用，理由见那个方法的文档。
using System;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Player;
using Game.Stealth;
using UnityEngine;

namespace Game.Monster
{
    public sealed class MonsterRules : IReplayState
    {
        private readonly MonsterConfig config;

        // 按种类数值的来源。**种类在 Reset 时才查，不在构造时查**——这是启动顺序决定的，不是风格问题：
        // MonsterInstaller 的 RegisterBuildCallback 里就会解析 MonsterRules（为注册回放状态），而那个回调
        // 跑在容器建好之后、IGameService.InitializeAsync 之前（GameLifetimeScope.cs:383 注释、GameBootstrap.cs:106 的串行初始化），
        // 那会儿 ConfigService 还没加载表，构造时查只会永远拿到「表没就绪」。
        // Reset 由 EncounterStep.Begin 在遭遇场景就绪时调（MonsterEncounterState.OnSceneReadyAsync），
        // 那时启动早就完成，也远在第一次快照序列化之前，所以种类在整场遭遇里是恒定的。
        private readonly MonsterKindCatalog kindCatalog;
        private readonly int kindId;
        private readonly Func<string> describeKind;

        // 本只怪当前生效的按种类数值；null = 没接种类表（或表里没有这个种类），全部按全局默认跑。
        private MonsterKind kind;

        // 视线遮挡查询器；null = 没接遮挡（视锥判定与接线前一致）。装配走后置方法 UseOcclusion。
        private StealthSight sight;

        private readonly MonsterModel model;
        private readonly IRandomStream patrolRandom;
        private readonly IRandomService random;
        private readonly ITelemetryScope telemetry;
        private float coneCosine;
        private Vector2[] waypoints = Array.Empty<Vector2>();

        // 本 tick 的步进结果（PRP/combat-iteration W2）：Step 写、遭遇结算读（露馅信号、被抓判据），
        // 每次 Step / MoveControlled 开头清零。**不进快照、不进存档**：它们只回答「这一 tick 发生了什么」，
        // 下一 tick 一定重算；恢复（Reset / Restore / Deserialize）时一并清零，不让上一场的结果漏到读档后。
        private int sensedThisStep;
        private bool attackedThisStep;

        /// <param name="kind">
        /// 直接指定按种类的数值；为 null（且 <paramref name="kindCatalog"/> 也为 null）表示「这份配置没接种类表」，
        /// 全部按 <paramref name="config"/> 的全局默认值跑——也就是拆分前的行为。测试与独立原型场景用这一支。
        /// </param>
        /// <param name="kindCatalog">
        /// 种类目录；非 null 时在 <see cref="Reset"/> 里按 <paramref name="kindId"/> 查一次。
        /// 正式流程走这一支（<c>MonsterInstaller</c> 注入），这样查询发生在配置表加载完之后。
        /// </param>
        /// <param name="kindId">要查的种类 id；<c>0</c> = 取表里第一条种类。</param>
        /// <param name="describeKind">
        /// 查不到种类时写进报错消息的来源说明（如「MonsterInstaller」）；为 null 时用一句通用说明。
        /// 传进来而不是在这里拼，是为了让日志能点到真正的接线点。
        /// </param>
        /// <param name="sight">
        /// 视线遮挡查询器（可空）。只有 <see cref="CreateForActor"/> 会用这个参数把主怪那一份传下去，
        /// 让多只巡逻者共享同一个实例；正式装配一律走 <see cref="UseOcclusion"/>。
        /// </param>
        public MonsterRules(MonsterConfig config, MonsterModel model, IRandomService random,
            ITelemetryScope telemetry, MonsterKind kind = null, MonsterKindCatalog kindCatalog = null,
            int kindId = 0, Func<string> describeKind = null, string patrolStream = "logic.monster.patrol",
            StealthSight sight = null)
        {
            this.config = config == null ? throw new ArgumentNullException(nameof(config)) : config;
            this.kind = kind;
            this.kindCatalog = kindCatalog;
            this.kindId = kindId;
            this.describeKind = describeKind;
            this.sight = sight;
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            patrolRandom = random.Stream(patrolStream);
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public MonsterModel Model => model;

        /// <summary>
        /// 全局配置（同一场遭遇里所有巡逻者共用同一份，见 <see cref="CreateForActor"/>）。
        /// 读方是遭遇调度（<c>EncounterStep</c> 读升级回合制的触发开关，PRP/combat-iteration D5）；只读，运行时不改 SO。
        /// </summary>
        public MonsterConfig Config => config;

        /// <summary>
        /// 为场景里的另一只巡逻者造一份规则：共用全局配置、随机服务、埋点与遮挡，巡逻随机流按稳定标识各走各的。
        /// <para>
        /// <paramref name="kindIdOverride"/>（PRP/combat-iteration D2「一场遭遇多种怪」）：
        /// <c>0</c>（默认）= <b>沿用本只怪的种类</b>，与加这个参数之前逐字一致——连已解析的 <see cref="Kind"/>
        /// 带种类目录与种类 id 一起复制（巡逻者乙靠这一支）；
        /// 非 <c>0</c> = 用覆盖的种类：<b>不复制</b>已解析的 <see cref="Kind"/>（传 null），只换种类 id，
        /// 让新规则在 <see cref="Reset"/> 里按新 id 自己查表（查表时机的理由见字段区「种类在 Reset 时才查」）。
        /// 没接种类目录（测试 / 独立原型场景）时覆盖不到任何种类，整只怪走全局默认——同 <c>kind == null</c> 的既有口径。
        /// </para>
        /// </summary>
        /// <param name="stableId">场景稳定标识；拼进巡逻随机流的名字，保证每只怪的停步节奏互不相干。</param>
        /// <param name="kindIdOverride">种类覆盖；<c>0</c> = 沿用本只怪的种类。负数视为非法。</param>
        public MonsterRules CreateForActor(string stableId, int kindIdOverride = 0)
        {
            if (kindIdOverride < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(kindIdOverride), kindIdOverride, "种类覆盖不能为负数（0 = 沿用主怪种类）");
            }

            if (kindIdOverride == 0)
            {
                return new MonsterRules(config, new MonsterModel(), random,
                    telemetry, kind, kindCatalog, kindId, describeKind, "logic.monster.patrol." + stableId, sight);
            }

            // 报错来源点到场景里那只巡逻者的字段上：查不到种类时，要改的是它的 TamingActor.kindId，不是 MonsterInstaller。
            return new MonsterRules(config, new MonsterModel(), random,
                telemetry, null, kindCatalog, kindIdOverride, () => "巡逻者 " + stableId + " 的 TamingActor.kindId",
                "logic.monster.patrol." + stableId, sight);
        }

        /// <summary>
        /// 注入视线遮挡（S3「掩体贯通到怪物察觉」，2026-10-08）。
        /// <para>
        /// <b>多只怪可以共用同一个 <see cref="StealthSight"/> 实例</b>：它的查询是「观察者位置 + 目标位置」的
        /// 纯函数（<see cref="StealthSight.HasSightNonAlloc"/>，不分配、不持有查询状态），遮挡体列表又是整场
        /// 遭遇共用的一份场景几何。所以 <see cref="EncounterStep"/> 把同一个实例喂给主怪与
        /// <see cref="CreateForActor"/> 造出来的每一只巡逻者，不需要按怪各拷一份。
        /// </para>
        /// <para>
        /// 传 null = 不查遮挡（行为与接线前逐位一致）；重复调用以最后一次为准（装配顺序见
        /// <c>EncounterStep.ApplyMonsterOcclusion</c>）。
        /// </para>
        /// </summary>
        public void UseOcclusion(StealthSight occlusion) => sight = occlusion;

        /// <summary>本只怪当前生效的攻击伤害（种类优先、否则全局）。</summary>
        public int AttackDamage => config.ResolveAttackDamage(kind);

        /// <summary>
        /// 本只怪当前生效的按种类数值；没接种类表（或表里没这个种类）时为 null。
        /// 掉落 / 可否击杀 / 层级都从这里取（<see cref="MonsterKind.DropItemIds"/> 等）。
        /// </summary>
        public MonsterKind Kind => kind;

        /// <summary>
        /// 这只怪能不能被常规击杀（tbyao 的 killable）。没接种类表时为 true——旧行为不变。
        /// <para>
        /// **只答「能不能常规杀」**；「怎么杀 / 有没有替代途径」在 <see cref="MonsterKind.DefeatMethod"/>
        /// （两列分工与依据见 <see cref="ApplyDamage"/> 的注释与 <see cref="ApplyExecution"/>）。
        /// 为 false 不等于「杀不掉」：`defeat_method = 暗杀` 的怪只能走 <see cref="ApplyExecution"/>。
        /// </para>
        /// </summary>
        public bool IsKillable => kind == null || kind.IsKillable;

        /// <summary>
        /// 击杀 / 暗杀掉落的 item id（tbyao 的 drop_items）。没接种类表或没配掉落时为空列表。
        /// <b>本类只交 id，不写背包、不发事件</b>：进背包由 Game.Loot 的结算入口做，
        /// 免得确定性规则里出现存档 / 事件副作用。
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<int> DropItemIds =>
            kind == null ? Array.Empty<int>() : kind.DropItemIds;

        /// <summary>
        /// 这只怪此刻有没有察觉到目标（<see cref="Sense"/> 结果大于 0）。
        /// <para>
        /// 2026-10-08 起**包含遮挡**：被掩体切断视线的视锥内目标不算被察觉（<see cref="UseOcclusion"/> 注入后）。
        /// 行为层（警戒、敌对、追击）走这一支；事实层需要「不含遮挡」的那份答案时用
        /// <see cref="PerceivedBySenses"/>，两者差的正是遮挡这一层。
        /// </para>
        /// </summary>
        public bool Detects(in PlayerSnapshot target) => model.Health > 0 && Sense(in target) > 0;

        /// <summary>
        /// **不含遮挡**的感知：视锥那两档只按「距离 + 夹角（+ 伪装）」判、贴身那一档只按「距离 + 潜行」判，
        /// 一条视线查询都不做，返回 <c>2</c> / <c>1</c> / <c>0</c> 的同一套档位。
        /// <para>
        /// <b>为什么必须留这一支</b>（2026-10-08）：`EncounterStep.SettleStealth` 要算
        /// 「视线本来够得着、被遮挡体切断」= <c>perceivesByCone &amp;&amp; !perceives</c>（即 <c>stealth.cover</c>），
        /// 它是一个**布尔相减**。如果两个输入都含遮挡，被挡住时前者就已经是 false，相减恒为 0，
        /// `stealth.cover` 在正式流程里永远点不亮——「躲进掩体」那条摆脱条件（`04_追逐.md:104` R23）
        /// 与 `03_潜行与暗杀.md:129-136` R24–R28 的判据落点会一起失效，而 EditMode 里
        /// 只在不注入遮挡的夹具上还亮着，正是「测试绿、实机假」的形状。
        /// </para>
        /// <para>
        /// 分层口径（2026-10-08）：**行为层**由 <see cref="Sense"/> 自己吃遮挡（怪不再穿墙看人）；
        /// **事实层**（`EncounterStep.SettleStealth`）**不自己再算一遍**——它的 `perceives` 直接取
        /// <see cref="Detects"/>，「两层取同一个答案」。主窗口复跑抓到过一次两层各算各的而分叉：
        /// 事实说「你藏得好好的」、行为说「我已经贴到你身上进了警戒」。
        /// 所以 `StealthDecisionGate.PerceivesThroughCover` 不在那条路径上（理由见它的文档注释）。
        /// 本方法只服务「不含遮挡的那一半」——`stealth.cover` 那个布尔相减的被减数。
        /// </para>
        /// </summary>
        public int PerceivedBySenses(in PlayerSnapshot target) => model.Health > 0 && target.IsAlive ? SenseBySensesOnly(in target) : 0;

        /// <summary>
        /// 本 tick <see cref="Step"/> 里对玩家的感知档位（<b>行为层</b>口径，含遮挡，与 <see cref="Detects"/> 同一个答案）：
        /// <c>2</c> = 红区、<c>1</c> = 橙区或贴身察觉、<c>0</c> = 没察觉。
        /// <para>
        /// 复用 Step 已经算好的结果，不再算一遍（零分配、零开方）；没被 Step 推进的 tick（死亡、被驯服操控、
        /// 还没 Reset）为 0。读方是遭遇结算的露馅信号（PRP/combat-iteration D1）——<b>只在同一 tick 的 Step 之后读才有意义</b>。
        /// </para>
        /// </summary>
        public int SensedThisStep => sensedThisStep;

        /// <summary>
        /// 本 tick <see cref="Step"/> 是否出手打了玩家（Step 返回 true 的那一 tick）。只在敌对态出手，
        /// 所以「这一 tick 打中了」就是「追上了」——遭遇结算拿它判附加完整参与者的「被抓」（PRP/combat-iteration D4）。
        /// 没被 Step 推进的 tick 为 false。
        /// </summary>
        public bool AttackedThisStep => attackedThisStep;

        /// <summary>
        /// 「被惊动」入口（PRP/combat-iteration D1：露馅 → 察觉者转敌对）：让这只怪立刻转敌对、把玩家当前位置记为追击目标，
        /// 之后的追击沿用既有的敌对态与 ChaseRules。内部走与红区 / 警戒升满 / 受击<b>同一个</b> <see cref="EnterHostile"/>。
        /// <para>
        /// <b>幂等</b>：已死亡（生命 ≤ 0）或已经敌对时什么都不改，返回 false——已敌对的怪不重置它的追击记忆与丢失计时。
        /// </para>
        /// </summary>
        /// <returns>这一次是否真的让它进入了敌对态。</returns>
        public bool Provoke(Vector2 target)
        {
            if (model.Health <= 0 || model.Mode == MonsterMode.Hostile)
            {
                return false;
            }

            EnterHostile(target);
            return true;
        }

        public MonsterSaveData Capture()
        {
            var saved = new MonsterSaveData
            {
                PositionX = model.Position.x,
                PositionY = model.Position.y,
                FacingX = model.Facing.x,
                FacingY = model.Facing.y,
                TargetX = model.LastKnownTarget.x,
                TargetY = model.LastKnownTarget.y,
                Mode = model.Mode,
                Health = model.Health,
                WaypointIndex = model.WaypointIndex,
                Alert = model.Alert,
                PatrolWalkElapsed = model.PatrolWalkElapsed,
                NextPauseAfter = model.NextPauseAfter,
                PatrolPauseLeft = model.PatrolPauseLeft,
                AlertAtLoss = model.AlertAtLoss,
                AlertDecayElapsed = model.AlertDecayElapsed,
                HostileLostElapsed = model.HostileLostElapsed,
                AttackCooldownLeft = model.AttackCooldownLeft,
                PatrolRandomState = patrolRandom.State,
                WaypointX = new float[waypoints.Length],
                WaypointY = new float[waypoints.Length],
            };
            for (int i = 0; i < waypoints.Length; i++)
            { saved.WaypointX[i] = waypoints[i].x; saved.WaypointY[i] = waypoints[i].y; }
            return saved;
        }

        public void Restore(MonsterSaveData saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            saved.Validate();
            // 读档不走 Reset（Begin），种类与视锥只在 Reset 里解析——这里补上，放在改写现场之前（配置非法时不部分改写）。
            ResolveKindAndCone();
            ClearStepResults();
            var restoredPoints = new Vector2[saved.WaypointX.Length];
            for (int i = 0; i < restoredPoints.Length; i++) restoredPoints[i] = new Vector2(saved.WaypointX[i], saved.WaypointY[i]);
            model.Position = new Vector2(saved.PositionX, saved.PositionY);
            model.SyncPreviousPosition();
            model.Facing = new Vector2(saved.FacingX, saved.FacingY);
            model.LastKnownTarget = new Vector2(saved.TargetX, saved.TargetY);
            model.Mode = saved.Mode;
            model.Health = saved.Health;
            model.WaypointIndex = saved.WaypointIndex;
            model.Alert = saved.Alert;
            model.PatrolWalkElapsed = saved.PatrolWalkElapsed;
            model.NextPauseAfter = saved.NextPauseAfter;
            model.PatrolPauseLeft = saved.PatrolPauseLeft;
            model.AlertAtLoss = saved.AlertAtLoss;
            model.AlertDecayElapsed = saved.AlertDecayElapsed;
            model.HostileLostElapsed = saved.HostileLostElapsed;
            model.AttackCooldownLeft = saved.AttackCooldownLeft;
            patrolRandom.State = saved.PatrolRandomState;
            waypoints = restoredPoints;
        }

        public void Reset(Vector2[] patrolPoints)
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                throw new ArgumentException("怪物至少需要一个巡逻点", nameof(patrolPoints));
            }

            ResolveKindAndCone();
            ClearStepResults();

            waypoints = (Vector2[])patrolPoints.Clone();
            model.Position = waypoints[0];
            model.SyncPreviousPosition();
            model.Facing = waypoints.Length > 1
                ? GameMath.Normalize(waypoints[1] - waypoints[0]) : Vector2.right;
            if (model.Facing == Vector2.zero)
            {
                model.Facing = Vector2.right;
            }

            model.LastKnownTarget = model.Position;
            model.Mode = MonsterMode.PatrolWalk;
            model.Health = config.ResolveMaxHealth(kind);
            model.WaypointIndex = waypoints.Length > 1 ? 1 : 0;
            model.Alert = 0f;
            model.PatrolWalkElapsed = 0f;
            model.NextPauseAfter = NextPauseInterval();
            model.PatrolPauseLeft = 0f;
            model.AlertAtLoss = 0f;
            model.AlertDecayElapsed = 0f;
            model.HostileLostElapsed = 0f;
            model.AttackCooldownLeft = 0f;
        }

        /// <summary>
        /// 解析种类并按解析结果重算视锥余弦。<see cref="Reset"/>、<see cref="Restore"/>、<see cref="Deserialize"/> 三处都走它。
        /// <para>
        /// 修的缺陷（PRP/combat-iteration 执行中修订，2026-10-14 用户同意并进 W2）：读档与快照恢复都不走 <see cref="Reset"/>，
        /// 此前恢复出来的主怪种类为 null（数值 / 可否击杀 / 掉落全退回全局默认），<c>coneCosine</c> 停在初值 0，
        /// 也就是视野变成 180°。现在恢复之后与 <see cref="Reset"/> 之后的种类与视锥逐字一致；
        /// 种类已解析过时 <see cref="ResolveKind"/> 不重查，所以重复调用是幂等的。
        /// </para>
        /// </summary>
        private void ResolveKindAndCone()
        {
            ResolveKind();
            ValidateResolvedConfig();
        }

        private void ClearStepResults()
        {
            sensedThisStep = 0;
            attackedThisStep = false;
        }

        // 种类只查一次，且只在 Reset 时查（理由见字段区与 MonsterKindCatalog 的类文档）。
        // 查不到 → kind 为 null → 全部走全局默认；不抛，因为「数值退化成全局默认」是可玩的，
        // 而抛在这里会让整个遭遇场景进不去。**但一定要留痕**，否则就是静默兜底。
        // 这里用 UnityEngine.Debug 直报而不走 Game.Core.Logging：规则层是纯 C#（不持有场景 / 容器上下文），
        // 报错文本里已经有「哪只怪、哪个种类、怎么修」，带不带 UnityEngine.Object 上下文不影响定位。
        private void ResolveKind()
        {
            if (kind != null || kindCatalog == null)
            {
                return;
            }

            if (!kindCatalog.IsReady)
            {
                UnityEngine.Debug.LogWarning("MonsterRules：配置表还没就绪，本场遭遇按 MonsterConfig 的全局默认值跑。");
                return;
            }

            if (kindId == 0)
            {
                System.Collections.Generic.IReadOnlyList<MonsterKind> all = kindCatalog.All;
                if (all.Count == 0)
                {
                    UnityEngine.Debug.LogWarning("MonsterRules：monster_species 表里一条种类都没有，本场遭遇按全局默认值跑。");
                    return;
                }

                kind = all[0];
                return;
            }

            if (!kindCatalog.TryGet(kindId, out kind))
            {
                UnityEngine.Debug.LogError(
                    $"MonsterRules：monster_species 表里没有种类 {kindId}，本场遭遇按全局默认值跑。"
                    + "检查 Tables/Data/monster_species/ 下有没有这个种类，改完跑一次 scripts/gen-tables.ps1。"
                    + (describeKind == null ? string.Empty : "来源：" + describeKind()));
            }
        }

        // 校验的是**解析后**的值：全局那一份可能在 Inspector 里被改坏，种类那一份可能在表里被写坏，
        // 两者都要挡住（表里那一份在 MonsterKindCatalog 读表时已经先挡过一遍）。
        private void ValidateResolvedConfig()
        {
            float visionAngle = config.ResolveVisionAngle(kind);
            if (config.PatrolSpeed <= 0f || config.AlertSpeedMultiplier <= 0f || config.HostileSpeedMultiplier <= 0f
                || visionAngle <= 0f || visionAngle >= 360f || config.HostileRadius <= 0f
                || config.ResolveAlertRadius(kind) <= config.HostileRadius || config.ResolveNearSenseRadius(kind) <= 0f
                || config.ResolveAttackRange(kind) <= 0f || config.ResolveAlertFillSeconds(kind) <= 0f
                || config.AlertFallSeconds <= 0f
                || config.ResolveHostileLoseSeconds(kind) <= 0f || config.PatrolPauseSeconds <= 0f
                || config.PatrolMinSeconds <= 0 || config.PatrolMaxSeconds < config.PatrolMinSeconds
                || config.PatrolMaxSeconds == int.MaxValue || config.ResolveAttackCooldown(kind) < 0f
                || config.ResolveMaxHealth(kind) <= 0 || config.ResolveAttackDamage(kind) <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(config),
                    kind == null
                        ? "MonsterConfig 含非法范围或非正数"
                        : $"MonsterConfig 与种类 {kind.KindId}（{kind.Name}）解析后含非法范围或非正数");
            }

            coneCosine = GameMath.Cos(visionAngle * 0.5f * 0.01745329252f);
        }

        public bool Step(in MonsterIntent intent)
        {
            float deltaTime = intent.DeltaTime;
            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(intent), "固定步长不可为负");
            }

            // 表现层插值的起点：放在任何提前返回之前，死亡 / 停步的 tick 也要对齐，否则视图会在旧两点间来回插。
            model.SyncPreviousPosition();
            ClearStepResults();
            if (model.Health <= 0 || waypoints.Length == 0)
            {
                return false;
            }

            model.AttackCooldownLeft = GameMath.Max(0f, model.AttackCooldownLeft - deltaTime);
            int sensed = Sense(intent.Target, intent.AwarenessScale);
            sensedThisStep = sensed;
            if (sensed == 2)
            {
                EnterHostile(intent.Target.Position);
            }
            else if (model.Mode == MonsterMode.Hostile)
            {
                if (sensed == 1)
                {
                    model.HostileLostElapsed = 0f;
                    model.LastKnownTarget = intent.Target.Position;
                }
                else
                {
                    model.HostileLostElapsed += deltaTime;
                    if (model.HostileLostElapsed >= config.ResolveHostileLoseSeconds(kind))
                    {
                        SetMode(MonsterMode.Alert);
                        model.Alert = 1f;
                        model.AlertAtLoss = 1f;
                        model.AlertDecayElapsed = 0f;
                        return false;
                    }
                }
            }
            else if (sensed == 1)
            {
                SetMode(MonsterMode.Alert);
                model.AlertDecayElapsed = 0f;
                model.Alert = GameMath.Clamp01(model.Alert + deltaTime / config.ResolveAlertFillSeconds(kind));
                if (model.Alert >= 1f)
                {
                    EnterHostile(intent.Target.Position);
                }
            }
            else if (model.Mode == MonsterMode.Alert)
            {
                if (model.AlertDecayElapsed == 0f)
                {
                    model.AlertAtLoss = model.Alert;
                }

                model.AlertDecayElapsed += deltaTime;
                float fraction = model.AlertDecayElapsed / config.AlertFallSeconds;
                model.Alert = GameMath.Max(0f, model.AlertAtLoss - fraction * fraction);
                if (model.Alert <= 0f)
                {
                    SetMode(MonsterMode.PatrolWalk);
                    model.PatrolWalkElapsed = 0f;
                    model.NextPauseAfter = NextPauseInterval();
                    model.WaypointIndex = FindNearestWaypoint();
                }
            }

            switch (model.Mode)
            {
                case MonsterMode.PatrolWalk:
                    MoveTowards(waypoints[model.WaypointIndex], config.PatrolSpeed * intent.MovementScale, deltaTime);
                    if (GameMath.Distance(model.Position, waypoints[model.WaypointIndex]) <= 0.001f)
                    {
                        model.WaypointIndex = (model.WaypointIndex + 1) % waypoints.Length;
                    }

                    model.PatrolWalkElapsed += deltaTime;
                    if (model.PatrolWalkElapsed >= model.NextPauseAfter)
                    {
                        SetMode(MonsterMode.PatrolPause);
                        model.PatrolPauseLeft = config.PatrolPauseSeconds;
                    }
                    break;
                case MonsterMode.PatrolPause:
                    model.PatrolPauseLeft -= deltaTime;
                    if (model.PatrolPauseLeft <= 0f)
                    {
                        SetMode(MonsterMode.PatrolWalk);
                        model.PatrolWalkElapsed = 0f;
                        model.NextPauseAfter = NextPauseInterval();
                    }
                    break;
                case MonsterMode.Alert:
                    if (sensed > 0)
                    {
                        MoveTowards(intent.Target.Position, config.PatrolSpeed * config.AlertSpeedMultiplier * intent.MovementScale, deltaTime);
                    }
                    break;
                case MonsterMode.Hostile:
                    MoveTowards(model.LastKnownTarget, config.PatrolSpeed * config.HostileSpeedMultiplier * intent.MovementScale, deltaTime);
                    if (sensed > 0 && intent.Target.IsAlive
                        // 攻击许可的两层：旧伪装布尔（DisguiseRules，语义原样保留）+ 身份生效中（S1）。
                        // 身份那一层的判据在 Game.Identity.IdentityAttackRules，本类不认识身份状态对象。
                        && IdentityAttackRules.AllowsEnemyAttack(intent.Target.IsDisguised, intent.IsIdentityInEffect)
                        && model.AttackCooldownLeft <= 0f
                        && GameMath.Distance(model.Position, intent.Target.Position) <= config.ResolveAttackRange(kind))
                    {
                        model.AttackCooldownLeft = config.ResolveAttackCooldown(kind);
                        telemetry.Track("attack", ("damage", config.ResolveAttackDamage(kind)));
                        attackedThisStep = true;
                        return true;
                    }
                    break;
            }

            return false;
        }

        public void ChangePatrolRoute(Vector2[] points)
        {
            if (points == null || points.Length == 0 || points.Length > 1024) throw new ArgumentException("巡逻路线不能为空");
            foreach (Vector2 point in points)
                if (float.IsNaN(point.x) || float.IsInfinity(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.y))
                    throw new ArgumentException("巡逻点必须有限");
            waypoints = (Vector2[])points.Clone();
            model.WaypointIndex = FindNearestWaypoint();
        }

        // 驯服验证的公开移动入口；调用方接管期间不再调用 AI Step。
        public void MoveControlled(Vector2 movement, float deltaTime)
        {
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            model.SyncPreviousPosition();
            // 被操控期间它不感知、不出手：本 tick 的步进结果清零，露馅信号与被抓判据都不会算到它头上。
            ClearStepResults();
            if (model.Health <= 0) return;
            if (GameMath.SqrMagnitude(movement) > 1f) movement = GameMath.Normalize(movement);
            if (GameMath.SqrMagnitude(movement) > 0f)
            {
                model.Facing = GameMath.Normalize(movement);
                model.Position += movement * config.PatrolSpeed * deltaTime;
            }
        }

        public bool ApplyDamage(in DamageIntent intent, in PlayerSnapshot attacker)
        {
            if (intent.Amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intent), "伤害必须为正数");
            }

            // 下面这个 `!IsKillable` 拒的是**常规伤害**，不是「这只怪不可能死」——两条列答的是两个问题：
            //   killable      答「能不能按常规一路杀掉」（`06_怪物分层.md:121` R9）；
            //   defeat_method 答「为什么不能常规杀、以及有没有替代途径」（同文 `:130` 表 3.2–3.5 的「怎么杀」列）。
            // 依据写在 `Game.Monster.MonsterKind.cs:101-106`（那两个属性的文档）与 `Tables/Defines/yao.xml`
            // 的 killable / defeat_method 列注释里：查勘使是 false + 特殊条件（能用地形隐匿杀）、籍中吏是
            // false + 不可杀（没有任何途径）、殁吏一类是 false + **暗杀（只能绕背处决）**。
            // 所以**「ApplyDamage 拒伤」与「处决能杀死它」是并存的**，后者正是前者的设计后果：
            // `defeat_method = 暗杀` 的怪只有 ApplyExecution 这一条击杀途径。看到这里别去「修」它。
            if (model.Health <= 0 || !IsKillable)
            {
                return false;
            }

            model.Health = GameMath.Max(0, model.Health - intent.Amount);
            telemetry.Track("hit", ("hp", model.Health), ("damage", intent.Amount));
            if (model.Health == 0)
            {
                SetMode(MonsterMode.Dead);
            }
            else
            {
                EnterHostile(attacker.Position);
            }

            return true;
        }

        /// <summary>
        /// **显式处决入口**：背后暗杀真的命中时由交互层调（`PRP/stealth-execution/prp.md` §2.3 的第 2 件事）。
        /// <para>
        /// <b>为什么不复用 <see cref="ApplyDamage"/></b>：<c>defeat_method = 暗杀</c> 的怪通常
        /// <c>killable = false</c>（`Tables/Defines/yao.xml` 的 killable 列），而 ApplyDamage 对它们**拒伤**。
        /// 若处决也走 ApplyDamage，绕背处决这条**唯一击杀途径**就永远杀不掉它们——把「唯一解」堵死，
        /// 而这不是 bug 是设计（同 `MonsterKind.cs:101-106` 的两列分工）。所以单开一个入口，
        /// 而不是把 ApplyDamage 的拒伤条件放宽（放宽会让常规攻击也能打 `killable = false` 的怪，
        /// 那才是真的把设计改坏）。
        /// </para>
        /// <para>
        /// <b>本方法不判物种门槛</b>：门槛（<c>defeat_method == 暗杀</c>）归 <c>Game.Stealth</c> 的
        /// <c>ExecutionRules</c>——Monster 不认识潜行语义，也不知道「背后」是什么（PRP §2.3 的落点选择）。
        /// 本方法只做「让它死」这一件事，走的是既有的死亡流程（<see cref="SetMode"/> + <see cref="MonsterMode.Dead"/>），
        /// 与 ApplyDamage 打到 0 血时同一个收尾。
        /// </para>
        /// <para>
        /// 返回 false = 没执行：目标已经死了（<c>Health &lt;= 0</c>）。已经死了不算一次处决，
        /// 调用方据此**不写** <c>stealth.assassinated</c>、不埋 <c>stealth_executed</c>。
        /// </para>
        /// </summary>
        public bool ApplyExecution()
        {
            if (model.Health <= 0)
            {
                return false;
            }

            model.Health = 0;
            SetMode(MonsterMode.Dead);
            telemetry.Track("executed", ("hp", model.Health), ("kind", kind == null ? 0 : kind.KindId));
            return true;
        }

        /// <summary>
        /// <b>回合制战斗打赢</b>的死亡入口（PRP/combat-iteration D5，用户 2026-10-14 拍板「打赢视同击杀，无视 killable」）：
        /// 遭遇升级成回合制、玩家在回合制里赢了，由 <c>EncounterStep.CompleteEscalation</c> 调。
        /// <para>
        /// 与 <see cref="ApplyExecution"/> 同一个收尾（生命置 0 + <see cref="SetMode"/> 进 <see cref="MonsterMode.Dead"/>），
        /// 同样<b>不看 <see cref="IsKillable"/></b>——回合制本身就是这只怪的「打法」，常规伤害的拒伤条件管不到它。
        /// 单开入口而不直接复用处决：两者的埋点要分得开（处决记 <c>executed</c>，这里记 <c>battle_defeated</c>），
        /// 否则倒推现场时会把「回合制打赢」读成「被背后处决」。
        /// </para>
        /// <para>返回 false = 没执行：目标已经死了。</para>
        /// </summary>
        public bool ApplyBattleDefeat()
        {
            if (model.Health <= 0)
            {
                return false;
            }

            model.Health = 0;
            SetMode(MonsterMode.Dead);
            telemetry.Track("battle_defeated", ("kind", kind == null ? 0 : kind.KindId));
            return true;
        }

        public void Serialize(IStateWriter writer)
        {
            model.Serialize(writer);
            writer.WriteULong(patrolRandom.State);
            writer.WriteInt(waypoints.Length);
            for (int i = 0; i < waypoints.Length; i++)
            {
                writer.WriteVector2(waypoints[i]);
            }
        }

        /// <summary>
        /// 回放快照恢复一只怪。<b>先读完、验完，再写进现场</b>（2026-10-16 代码审查指出：原先边读边写，
        /// 读到中途发现坏数据时这只怪已经被改掉一半）。解析与写回分别见 <see cref="ReadSerialized"/>
        /// 与 <see cref="ApplySerialized"/>——它们也供 <c>EncounterStep</c> 的「先校验、后修改」复用，
        /// 字节布局因此只在本文件里写一遍。
        /// </summary>
        public void Deserialize(IStateReader reader)
        {
            MonsterModel pending = ReadSerialized(reader, out ulong pendingPatrolRandom, out Vector2[] pendingWaypoints);
            ApplySerialized(pending, pendingPatrolRandom, pendingWaypoints);
        }

        /// <summary>
        /// 读一段由 <see cref="Serialize"/> 写出的怪物状态，<b>不改任何一只怪</b>：模型读进一只临时实例返回，
        /// 巡逻随机流状态与巡逻点经 <c>out</c> 交出；巡逻点数量非法（&lt; 0 或 &gt; 1024）在这里就抛
        /// ——这是「先校验、后修改」的校验那一半。
        /// <para>
        /// 读的是**快照字节**、不是 <c>MonsterSaveData</c>：快照允许中间态（例如还没 Reset 过的怪、空巡逻点），
        /// 而存档校验（<c>MonsterSaveData.Validate</c>）比快照格式严，借用它会把合法快照判死。
        /// </para>
        /// <para>临时对象是必要的拷贝：直接读进现场那只就又变成边读边写了。只在恢复快照起点调用，不在 tick 上。</para>
        /// </summary>
        public static MonsterModel ReadSerialized(IStateReader reader, out ulong patrolRandomState, out Vector2[] restoredWaypoints)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            // Serialize 的顺序是「模型 → 巡逻随机流 → 巡逻点」，读必须逐字段同序：先读模型。
            var pending = new MonsterModel();
            pending.Deserialize(reader);
            patrolRandomState = reader.ReadULong();
            int count = reader.ReadInt();
            if (count < 0 || count > 1024)
            {
                throw new InvalidOperationException($"巡逻点数量非法：{count}");
            }

            restoredWaypoints = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                restoredWaypoints[i] = reader.ReadVector2();
            }

            return pending;
        }

        /// <summary>
        /// 把 <see cref="ReadSerialized"/> 读出来的东西<b>一次性</b>写进现场（含解析种类与视锥、清 tick 结果，
        /// 同 <see cref="Restore"/> 与 <see cref="Reset"/> 的恢复口径）。三个参数全部来自同一段字节。
        /// <b>不校验</b>：坏数据在 <see cref="ReadSerialized"/> 那一步已经抛过了。
        /// </summary>
        public void ApplySerialized(MonsterModel pending, ulong patrolRandomState, Vector2[] restoredWaypoints)
        {
            if (pending == null)
            {
                throw new ArgumentNullException(nameof(pending));
            }

            if (restoredWaypoints == null)
            {
                throw new ArgumentNullException(nameof(restoredWaypoints));
            }

            // 解析种类与视锥、清 tick 结果是写入而不是校验，所以放在最后这一段（同 Restore）。
            ResolveKindAndCone();
            ClearStepResults();
            model.CopyFrom(pending);
            // 上一 tick 位置对齐到**恢复后**的位置（`ReadSerialized` 已经把它在临时模型上对齐过），
            // 不跨瞬移插值。这条必须在 CopyFrom 之后：CopyFrom **有意不搬** PreviousPosition，
            // 在它之前调只会把旧的 Position 写进 PreviousPosition（2026-10-16 的实测就是这一格读成了 (0,0)）。
            model.SyncPreviousPosition();
            patrolRandom.State = patrolRandomState;
            waypoints = restoredWaypoints;
        }

        /// <summary>
        /// 读过（并丢弃）一段由 <see cref="Serialize"/> 写出的怪物状态，不改任何一只怪。
        /// <para>
        /// 给回放快照「按稳定 id 对齐」用（PRP/combat-iteration D2）：快照里有、场景里已经没有的巡逻者，
        /// 它那一段字节仍要读掉，后面的条目才对得上。读法与 <see cref="Serialize"/> **逐字段同序**——
        /// 放在本类里而不是调用方，是为了让「一只怪的快照长什么样」只在这一个文件里写一遍；
        /// 也不借一只临时 <see cref="MonsterRules"/> 来读：那会向随机服务登记一条多余的巡逻随机流。
        /// </para>
        /// <para>
        /// 实现就是 <see cref="ReadSerialized"/> 读完把结果丢掉——**读法只有那一处**（含巡逻点数量校验），
        /// 这里不重抄一遍布局。差别只在「读出来的东西要不要留下」：这只怪已经不在场景里，留下也没处放。
        /// </para>
        /// </summary>
        public static void SkipSerialized(IStateReader reader) =>
            ReadSerialized(reader, out _, out _);

        /// <summary>
        /// 这一 tick 对这个目标的感知（**行为层**口径）：<c>2</c> = 红区（敌对）、<c>1</c> = 橙区 / 贴身（警戒）、
        /// <c>0</c> = 没察觉。
        /// <para>
        /// <b>视锥两档受遮挡，贴身一档不受</b>（2026-10-08 掩体贯通，判据见文件头）：
        /// 红区与橙区都要求「视线没被切断」，贴身察觉不查遮挡。
        /// </para>
        /// <para>
        /// 判定顺序保持不变：先夹角（纯数学、便宜），再查遮挡（几个几何求交），最后才比半径。
        /// 遮挡查询用 <see cref="StealthSight.HasSightNonAlloc"/>——**零分配**，tick 路径上不许出现
        /// <c>TrySight</c>（它在有遮挡体时会 new 一个数组）。
        /// </para>
        /// </summary>
        private int Sense(in PlayerSnapshot target, float scale = 1f)
        {
            if (!target.IsAlive)
            {
                return 0;
            }

            Vector2 difference = target.Position - model.Position;
            float distance = GameMath.Magnitude(difference);
            bool inCone = distance == 0f || GameMath.Dot(model.Facing, difference / distance) >= coneCosine;
            // 视线：只有「在视锥里」才值得问（锥外本来就不看），零长度线段的退化情形再由 StealthSight 自己挡。
            bool sightClear = !inCone || sight == null || sight.HasSightNonAlloc(model.Position, target.Position);
            return ClassifyByRanges(target, distance, inCone && sightClear, scale);
        }

        /// <summary>
        /// 不含遮挡的同一套档位（口径与用途见 <see cref="PerceivedBySenses"/>）：
        /// 与 <see cref="Sense"/> 只差「视锥那一支要不要过 sightClear」。
        /// </summary>
        private int SenseBySensesOnly(in PlayerSnapshot target)
        {
            Vector2 difference = target.Position - model.Position;
            float distance = GameMath.Magnitude(difference);
            bool inCone = distance == 0f || GameMath.Dot(model.Facing, difference / distance) >= coneCosine;
            return ClassifyByRanges(target, distance, inCone);
        }

        /// <summary>
        /// 一套「从某点朝某向、按这份配置与这个种类」的感知判定——<b>供不跑 tick 的调用方复用同一套档位</b>。
        /// <para>
        /// 2026-10-10 新增（③ 方案 2「给 boss 自己的局势」）：BOSS 是个剧情 NPC，没有巡逻 / 状态机，
        /// 但它开战时同样要回答「玩家在不在我的警戒 / 敌对范围内」。**判据必须与巡逻怪逐字相同**，
        /// 否则同一个玩家位置会因对手不同而得到两种答案（这正是本波要消掉的那类分叉）。
        /// 所以把档位判定与视锥几何抽成静态纯函数，<see cref="Sense"/> / <see cref="SenseBySensesOnly"/> 与本方法
        /// 三者只在「传进来的位置 / 朝向 / 种类是谁的」上有区别。
        /// </para>
        /// <para>
        /// <b>不做遮挡查询</b>：调用方（<c>BossEntryContext</c>）没有也不会拿到遭遇侧的遮挡体集合，
        /// 而这里要的是「站在这只 boss 面前算不算进了它的警戒圈」。这是**有意的简化**，
        /// 写在契约里而不是悄悄漏掉。
        /// </para>
        /// </summary>
        /// <param name="config">数值来源（全局兜底）。</param>
        /// <param name="kind">按种类的数值；null = 全用全局值（同本类其余解析口径）。</param>
        /// <param name="senseFrom">感知方所在位置（巡逻怪 = 它的 <c>model.Position</c>，BOSS = NPC 位置）。</param>
        /// <param name="senseFacing">感知方朝向（单位向量；零向量会让视锥那一支恒不成立 = 只留贴身档）。</param>
        /// <param name="target">被感知者快照（玩家）。</param>
        /// <returns>2 = 红区（敌对）、1 = 橙区 / 贴身（警戒）、0 = 没察觉。与 <see cref="Sense"/> 同一口径。</returns>
        public static int SenseFrom(MonsterConfig config, MonsterKind kind, Vector2 senseFrom, Vector2 senseFacing,
            in PlayerSnapshot target)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!target.IsAlive)
            {
                return 0;
            }

            Vector2 difference = target.Position - senseFrom;
            float distance = GameMath.Magnitude(difference);
            // 朝向为零 = 调用方声明「这一侧没有视线方向」（剧情 NPC：纸片角色的旋转是朝向相机的）。
            // 那就不做视锥判定：半径之内一律算「看见了」——**没有背后的盲区**。
            // ⚠️ 连带的取舍：贴身那一档本来就要求「没在潜行」（`Classify` 的第二支），
            // 所以潜行玩家在警戒半径内同样会被察觉，绕到 BOSS 背后躲不开它。
            // 要恢复「背后盲区」得先有「NPC 面朝哪边」的表现规则（归类 S6），不是在这里补一个假朝向。
            float coneCosine = GameMath.Cos(config.ResolveVisionAngle(kind) * 0.5f * 0.01745329252f);
            bool inCone = senseFacing == Vector2.zero
                          || distance == 0f
                          || GameMath.Dot(senseFacing, difference / distance) >= coneCosine;
            return Classify(config, kind, target, distance, inCone);
        }

        /// <summary>
        /// 按半径分档：<paramref name="inSightCone"/> = 「在正面视野锥里**并且**看得见」。
        /// 红区用全局 <c>HostileRadius</c>、橙区与贴身用按种类解析后的值——种类表不开放敌对半径，
        /// 改它会动「红区优先于伪装与潜行」的语义（见 <c>MonsterConfig.hostileRadius</c> 的注释）。
        /// <para>
        /// 贴身那一档 <b>不看 <paramref name="inSightCone"/></b>：它是「不分朝向的近距察觉」，
        /// 免它的办法是潜行（`docs/design/spotlight/06_怪物状态与交互设计文档.md:71-73`），不是掩体。
        /// </para>
        /// <para><paramref name="distance"/> 由调用方算好传进来，本方法不再算一次（tick 路径上少一次开方）。</para>
        /// </summary>
        private int ClassifyByRanges(in PlayerSnapshot target, float distance, bool inSightCone, float scale = 1f)
            => Classify(config, kind, in target, distance, inSightCone, scale);

        /// <summary>
        /// 档位本体（静态）：<see cref="ClassifyByRanges"/> 与 <see cref="SenseFrom"/> 共用这一份，
        /// 保证「巡逻怪此刻算不算察觉」与「某只 NPC 算不算察觉」永远是同一套判据。
        /// </summary>
        private static int Classify(MonsterConfig config, MonsterKind kind, in PlayerSnapshot target,
            float distance, bool inSightCone, float scale = 1f)
        {
            if (inSightCone && distance <= config.HostileRadius * scale)
            {
                return 2;
            }

            if ((inSightCone && distance <= config.ResolveAlertRadius(kind) * scale && !target.IsDisguised)
                || (distance <= config.ResolveNearSenseRadius(kind) * scale && !target.IsSneaking))
            {
                return 1;
            }

            return 0;
        }

        private void EnterHostile(Vector2 target)
        {
            SetMode(MonsterMode.Hostile);
            model.Alert = 1f;
            model.LastKnownTarget = target;
            model.HostileLostElapsed = 0f;
        }

        private void SetMode(MonsterMode next)
        {
            if (model.Mode == next)
            {
                return;
            }

            MonsterMode previous = model.Mode;
            model.Mode = next;
            telemetry.Track("state_changed", ("from", (int)previous), ("to", (int)next));
        }

        private void MoveTowards(Vector2 target, float speed, float deltaTime)
        {
            Vector2 direction = target - model.Position;
            float distance = GameMath.Magnitude(direction);
            if (distance <= 0f)
            {
                return;
            }

            model.Facing = direction / distance;
            model.Position += model.Facing * GameMath.Min(distance, speed * deltaTime);
        }

        private int FindNearestWaypoint()
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < waypoints.Length; i++)
            {
                float distance = GameMath.SqrMagnitude(waypoints[i] - model.Position);
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }

            return nearest;
        }

        private int NextPauseInterval() => patrolRandom.Range(config.PatrolMinSeconds, config.PatrolMaxSeconds + 1);
    }
}

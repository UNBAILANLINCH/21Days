// 职责：在固定 tick 中处理巡逻、感知、警戒、追击和受伤；MonoBehaviour 只负责表现。
// 为什么新建：GameFlow 管场景而非单个怪物，Core 也不应认识玩法规则。
// 数值来源（真源 docs/design/features-spotlight/06_怪物分层.md）：
//   按种类的那部分（生命、伤害、攻击距离 / 冷却、视野角度、橙区半径、背后近距半径、警戒升满时长、丢失目标时长）
//   经 MonsterConfig.Resolve* 取「种类优先、否则全局」；全局的那部分（巡逻节奏、速度倍率、敌对半径）直接读 MonsterConfig。
//   字段归属的依据与文件头见 MonsterConfig.cs / MonsterKind.cs。
using System;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Player;
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

        private readonly MonsterModel model;
        private readonly IRandomStream patrolRandom;
        private readonly ITelemetryScope telemetry;
        private float coneCosine;
        private Vector2[] waypoints = Array.Empty<Vector2>();

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
        public MonsterRules(MonsterConfig config, MonsterModel model, IRandomService random,
            ITelemetryScope telemetry, MonsterKind kind = null, MonsterKindCatalog kindCatalog = null,
            int kindId = 0, Func<string> describeKind = null)
        {
            this.config = config == null ? throw new ArgumentNullException(nameof(config)) : config;
            this.kind = kind;
            this.kindCatalog = kindCatalog;
            this.kindId = kindId;
            this.describeKind = describeKind;
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            patrolRandom = (random ?? throw new ArgumentNullException(nameof(random))).Stream("logic.monster.patrol");
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public MonsterModel Model => model;

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

        public bool Detects(in PlayerSnapshot target) => model.Health > 0 && Sense(in target) > 0;

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

            ResolveKind();
            ValidateResolvedConfig();

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
            if (model.Health <= 0 || waypoints.Length == 0)
            {
                return false;
            }

            model.AttackCooldownLeft = GameMath.Max(0f, model.AttackCooldownLeft - deltaTime);
            int sensed = Sense(intent.Target);
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
                    MoveTowards(waypoints[model.WaypointIndex], config.PatrolSpeed, deltaTime);
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
                        MoveTowards(intent.Target.Position, config.PatrolSpeed * config.AlertSpeedMultiplier, deltaTime);
                    }
                    break;
                case MonsterMode.Hostile:
                    MoveTowards(model.LastKnownTarget, config.PatrolSpeed * config.HostileSpeedMultiplier, deltaTime);
                    if (sensed > 0 && intent.Target.IsAlive
                        // 攻击许可的两层：旧伪装布尔（DisguiseRules，语义原样保留）+ 身份生效中（S1）。
                        // 身份那一层的判据在 Game.Identity.IdentityAttackRules，本类不认识身份状态对象。
                        && IdentityAttackRules.AllowsEnemyAttack(intent.Target.IsDisguised, intent.IsIdentityInEffect)
                        && model.AttackCooldownLeft <= 0f
                        && GameMath.Distance(model.Position, intent.Target.Position) <= config.ResolveAttackRange(kind))
                    {
                        model.AttackCooldownLeft = config.ResolveAttackCooldown(kind);
                        telemetry.Track("attack", ("damage", config.ResolveAttackDamage(kind)));
                        return true;
                    }
                    break;
            }

            return false;
        }

        // 驯服验证的公开移动入口；调用方接管期间不再调用 AI Step。
        public void MoveControlled(Vector2 movement, float deltaTime)
        {
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            model.SyncPreviousPosition();
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

        public void Deserialize(IStateReader reader)
        {
            model.Deserialize(reader);
            patrolRandom.State = reader.ReadULong();
            int count = reader.ReadInt();
            if (count < 0 || count > 1024)
            {
                throw new InvalidOperationException($"巡逻点数量非法：{count}");
            }

            waypoints = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                waypoints[i] = reader.ReadVector2();
            }
        }

        private int Sense(in PlayerSnapshot target)
        {
            if (!target.IsAlive)
            {
                return 0;
            }

            Vector2 difference = target.Position - model.Position;
            float distance = GameMath.Magnitude(difference);
            bool inCone = distance == 0f || GameMath.Dot(model.Facing, difference / distance) >= coneCosine;
            // 红区用全局 HostileRadius、橙区与背后近距用按种类解析后的值：种类表不开放敌对半径，
            // 改它会动「红区优先于伪装与潜行」的语义（见 MonsterConfig.hostileRadius 的注释）。
            if (inCone && distance <= config.HostileRadius)
            {
                return 2;
            }

            if ((inCone && distance <= config.ResolveAlertRadius(kind) && !target.IsDisguised)
                || (distance <= config.ResolveNearSenseRadius(kind) && !target.IsSneaking))
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

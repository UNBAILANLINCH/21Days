// 职责：BOSS 主动发难（PRP/combat-iteration D6；2026-10-15 用户拍板补「发难半径 + 反应时间 + 拴绳 + 头顶提示」）——
//   玩家进 BOSS 的发难半径（以**原位**为圆心的水平距离）→ 警觉（头顶出提示、开始计反应时间）
//   → 愣满反应时间玩家还在半径里 → 冲上来 → 碰到就先把开战理由置为「BOSS 先动手」，再以剧情入口的名义发起与按 E 挑战
//   **完全相同**的交互（嘲讽对白 → Battle 阶段 → BattleFlow 开仗时现取局势，得到「被打、BOSS 先手」）
//   → 战斗场景在场（玩家看不见世界）时瞬移回原位 → 战后开战理由复位、解除武装，玩家离开察觉范围再进来才会再警觉。
//   反应时间内玩家走到跟前按 E：理由不动（仍是「玩家先动手」）→ 正面攻击、玩家先手，BOSS 不冲。
//   冲刺中 BOSS 离原位超过拴绳 → 放弃、走回原位；到了玩家还在发难半径里就解除武装，否则待机。
//
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有「剧情 NPC 自己走动、主动找玩家」的组件；巡逻怪的状态机（MonsterRules）跑在确定性内核里，
//      而 BOSS 的位置不进内核、不进存档（D6：读档后 BOSS 在原位）。
//   2. 扩展不行：`BossEntryContext` 明确「只回答局势、无副作用」，`NarrativeTrigger` 管剧情入口（距离守卫 + 交互转交）；
//      把移动和状态机塞进任何一个都会破坏它们的职责。所以新建，与两者并列挂在 BOSS 物体上，只经它们的公开口说话：
//      问 `BossEntryContext` 玩家在不在察觉范围（解除武装何时恢复）、告诉它谁先动的手；读 `NarrativeTrigger` 的交互半径、以它的名义交互。
//
// 边界（有意的取舍，不是漏掉）：
//   · 起不起冲**不看** `BossEntryContext` 的红区档位，只看「玩家离原位的水平距离 ≤ 发难半径」+ 反应时间（2026-10-15 拍板）；
//     开仗时的局势判断照旧由 `BossEntryContext` 给：接触距离 = 交互半径 × 0.6（1.2 米）必落在红区（敌对半径 2）里，「被打」成立。
//   · 发难半径以原位为圆心而不是 BOSS 当前位置：配合拴绳，BOSS 不会被玩家「牵着」越走越远。
//   · 只在地面平面上走：世界 XZ，高度保持原位（世界场景的逻辑平面固定 XZ，同 `WorldSpawnPlacement`）。
//     每个渲染帧按帧间隔积分（Update 传进来），不经逻辑 tick，所以不需要再插值、也不会拖影（pitfalls.md「逻辑 tick 与渲染帧脱节」）；
//     世界暂停时 timeScale = 0、步长与计时都为 0，自然停住。朝向不读也不写 transform：BOSS 小人的 ChibiPuppetMotion
//     按根的位移自己转向（= 移动方向），这里只管位移。
//   · 守卫（对白 / 演出 / 别的战斗 / 暂停 / 剧情忙或不可用）挡住时：待机不警觉；警觉**暂停计时**；冲刺**原地停住**；
//     放行后接着走——不回待机，免得 BOSS 停在半路成了新的「原位」。被击败 = `NarrativeFlagVisibility` 关掉整个物体，本组件随之不再步进。
//     玩家倒地（外部世界报「没有玩家」）时不警觉，警觉中则回待机。
//   · 跟这只 BOSS 的仗不论谁开的（它冲上来，或玩家按 E 挑战）都走同一个战后流程：回原位、解除武装、等玩家离开再武装——
//     否则按 E 打输撤退后，玩家就站在它半径里，马上又被警觉一次。
//   · 碰到之后交互被剧情拒掉（忙 / 条件不满足）也解除武装，而不是回待机：玩家就站在接触距离内，回待机等于每帧重冲重试。
//
// Update 只调 <see cref="Tick"/>：零查找、零日志、稳态零分配；埋点只在状态迁移 / 守卫第一次挡住时记，属性全是字面量。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.TurnBased;
using UnityEngine;
// 两个同名 BattleOutcome（Game.TurnBased / Game.Narrative）：剧情侧类型用别名（PRP/turnbased-battle §7 坑 4）。
using NarrativeTrigger = Game.Narrative.NarrativeTrigger;

namespace Game.Battle
{
    /// <summary>
    /// BOSS 主动发难。挂在 BOSS 场景物体上，与 <see cref="BossEntryContext"/>、<c>NarrativeTrigger</c> 同物体；
    /// 由 <see cref="BossAggroSceneBinder"/> 在场景加载时绑定（交外部世界与名册里这一行）。
    /// 名册那一行没开 <see cref="BossDefinition.AggroEnabled"/> 时完全不动（= 加组件之前的行为）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAggro : MonoBehaviour
    {
        /// <summary>
        /// 接触距离占交互半径的比例：冲到「交互半径 × 0.6」就算碰到。留四成余量是为了让自己发起的交互稳稳落在
        /// <c>NarrativeTrigger.InteractAsync</c> 的距离守卫之内（那一道按玩家锚点到 BOSS 的直线距离判，含高度差）。
        /// </summary>
        public const float ContactRadiusRatio = 0.6f;

        // 冲到接触距离那一步的浮点容差（米）：按「剩余距离 - 接触距离」走，落点可能差一丝。
        private const float ContactTolerance = 0.001f;

        // 「回到原位」的容差（米）：进了这个范围就吸附到原位，免得浮点永远差一点、一直判「没到家」。
        private const float HomeTolerance = 0.01f;

        [Tooltip("这只 BOSS 在 BossRosterConfig 里的 id（= 剧情 Battle 阶段的 payload，如 sample_boss）。"
                 + "发难开关、半径、反应时间、冲刺速度、拴绳都按它从名册取；查不到或那一行没开 aggroEnabled 时本组件完全不动。")]
        [SerializeField] private string bossId = string.Empty;

        [Tooltip("同物体上的 BOSS 局势（BossEntryContext）。留空 = 绑定时取同物体上那一个。")]
        [SerializeField] private BossEntryContext entryContext;

        [Tooltip("同物体上的剧情入口（NarrativeTrigger）。留空 = 绑定时取同物体上那一个。接触距离 = 它的交互半径 × 0.6。")]
        [SerializeField] private NarrativeTrigger trigger;

        [Tooltip("头顶「要冲过来了」的提示物体（可空）：警觉与冲刺期间打开，其余状态关闭。建议做成 BOSS 下的子物体，"
                 + "照 MarkerFocus 的做法（SpriteRenderer + CameraBillboard），默认关着。")]
        [SerializeField] private GameObject alertIndicator;

        private IBossAggroWorld world;
        private ITelemetryScope telemetry = NullTelemetryScope.Instance;
        private BossAggroState state;
        private BossAggroGuard reportedGuard;
        private Vector3 home;
        private bool homeCaptured;
        private bool armed;
        private float chargeSpeed;
        private float aggroRadius;
        private float reactionSeconds;
        private float leashDistance;
        private float reactionElapsed;
        private int contactGeneration;
        private ContactResult pendingContact;
        private CancellationTokenSource lifetime;

        /// <summary>接触交互的回音（异步返回后由下一次 <see cref="Tick"/> 处理，状态迁移只在 Tick 里发生）。</summary>
        private enum ContactResult
        {
            None = 0,
            Pending = 1,
            Accepted = 2,
            Rejected = 3,
            Cancelled = 4,
            Failed = 5,
        }

        /// <summary>名册里这只 BOSS 的 id（序列化字段）。</summary>
        public string BossId => bossId ?? string.Empty;

        /// <summary>当前发难状态。</summary>
        public BossAggroState State => state;

        /// <summary>已经被场景绑定接过（拿到了外部世界）。</summary>
        public bool IsBound => world != null;

        /// <summary>绑定时判定可以发难（名册有这一行且开了发难、局势与剧情入口都在、局势已接配置）。为假时完全不动。</summary>
        public bool IsArmed => armed;

        /// <summary>原位（绑定那一刻的位置：场景加载时 BOSS 还没动过）。</summary>
        public Vector3 Home => home;

        /// <summary>接触距离（米）= 剧情入口的交互半径 × <see cref="ContactRadiusRatio"/>；没有剧情入口时为 0。</summary>
        public float ContactDistance => trigger != null ? trigger.Radius * ContactRadiusRatio : 0f;

        /// <summary>警觉以来已经计了多少秒（守卫挡着时不走；离开半径 / 开冲 / 复位时清零）。</summary>
        public float ReactionElapsed => reactionElapsed;

        private bool IsHome => (transform.position - home).sqrMagnitude <= HomeTolerance * HomeTolerance;

        /// <summary>
        /// 场景绑定（<see cref="BossAggroSceneBinder"/>）在场景加载时调：交外部世界、名册里这一行（可空）与埋点。
        /// 在这里判定能不能发难并记一条 <c>boss_aggro_bound</c>（没发难时一眼看得出是「名册没这行」还是「没开」还是「局势没接上」）；
        /// 发难半径不大于交互半径时照样武装，但记 W 级并告警（玩家没有「走近按 E」的空档，名册里校验不了交互半径）。
        /// 原位只在第一次绑定时记；提示物体在这里先关上。
        /// </summary>
        public void Bind(IBossAggroWorld aggroWorld, BossDefinition definition, ITelemetryScope scope)
        {
            CacheParts();
            world = aggroWorld;
            telemetry = scope ?? NullTelemetryScope.Instance;
            if (!homeCaptured)
            {
                home = transform.position;
                homeCaptured = true;
            }

            string notArmed = ResolveNotArmedReason(definition);
            armed = notArmed == null;
            chargeSpeed = armed ? definition.AggroChargeSpeed : 0f;
            aggroRadius = armed ? definition.AggroRadius : 0f;
            reactionSeconds = armed ? definition.AggroReactionSeconds : 0f;
            leashDistance = armed ? definition.AggroLeashDistance : 0f;
            ApplyIndicator();
            if (armed && aggroRadius <= trigger.Radius)
            {
                Log.Warn($"{name}：BOSS 发难半径 {aggroRadius} 米不大于交互半径 {trigger.Radius} 米——玩家一靠近就同时进了两个圈，"
                         + "没有「走近按 E 挑战」的空档。改 BossRosterConfig 里这一行的 aggroRadius（应大于交互半径）。");
                telemetry.TrackWarn("boss_aggro_bound", TelemetryProps.Of(("boss", BossId), ("armed", true),
                    ("reason", "radius_within_interact"), ("radius", aggroRadius)));
                return;
            }

            telemetry.Track("boss_aggro_bound", ("boss", BossId), ("armed", armed), ("reason", notArmed ?? "armed"));
        }

        /// <summary>
        /// 推进一步（<c>Update</c> 每帧以受缩放的帧间隔调；测试直接调）。没武装、没绑定、物体关着（被击败隐藏）、
        /// 外部世界已释放时什么都不做。
        /// </summary>
        public void Tick(float deltaTime)
        {
            // 被击败 = NarrativeFlagVisibility 关掉整个物体：Update 本来就不跑，这一句给直接驱动的调用方兜底。
            if (!armed || world == null || !enabled || !gameObject.activeInHierarchy || !world.IsAvailable)
            {
                return;
            }

            if (state == BossAggroState.Contact)
            {
                ResolveContact();
                return;
            }

            // 跟这只 BOSS 的仗不是它自己冲上来开的（玩家按 E 挑战，含警觉期间）：同样进战斗中，理由不动，
            // 战后一样回原位、解除武装。
            if (state != BossAggroState.InBattle && world.IsFighting(bossId))
            {
                TransitionTo(BossAggroState.InBattle, "challenged");
                return;
            }

            switch (state)
            {
                case BossAggroState.Idle:
                    TryAlert();
                    break;
                case BossAggroState.Alerted:
                    TickAlerted(deltaTime);
                    break;
                case BossAggroState.Charging:
                    TickCharging(deltaTime);
                    break;
                case BossAggroState.InBattle:
                    TickInBattle();
                    break;
                case BossAggroState.Returning:
                    TickReturning(deltaTime);
                    break;
                case BossAggroState.Disarmed:
                    TickDisarmed(deltaTime);
                    break;
            }
        }

        private void Awake() => CacheParts();

        private void Update() => Tick(Time.deltaTime); // lint-ok: BOSS 发难是场景表现，位置不进确定性内核与回放（PRP/combat-iteration D6）

        private void OnDisable()
        {
            if (lifetime != null) lifetime.Cancel();
            if (state == BossAggroState.Idle)
            {
                return;
            }

            // 半路被关（被击败隐藏 / 场景卸载）：作废在途的接触、复位开战理由、回原位，重新显示时从待机开始。
            contactGeneration++;
            pendingContact = ContactResult.None;
            reactionElapsed = 0f;
            if (entryContext != null) entryContext.ResetInitiator();
            if (homeCaptured) transform.position = home;
            TransitionTo(BossAggroState.Idle, "disabled");
        }

        private void OnDestroy()
        {
            if (lifetime == null)
            {
                return;
            }

            lifetime.Cancel();
            lifetime.Dispose();
            lifetime = null;
        }

        private void CacheParts()
        {
            if (entryContext == null) entryContext = GetComponent<BossEntryContext>();
            if (trigger == null) trigger = GetComponent<NarrativeTrigger>();
        }

        // null = 可以发难；否则是不发难的原因（埋点 boss_aggro_bound 的 reason，蛇形短词）。
        private string ResolveNotArmedReason(BossDefinition definition)
        {
            if (world == null) return "no_world";
            if (definition == null) return "boss_not_found";
            if (!definition.AggroEnabled) return "aggro_disabled";
            if (entryContext == null) return "no_entry_context";
            if (trigger == null) return "no_trigger";
            if (!entryContext.IsReady) return "entry_context_unbound";
            return null;
        }

        // 待机：玩家进发难半径且守卫全过就警觉（计时从 0 开始）。守卫挡住时只记第一次。
        private void TryAlert()
        {
            if (!PlayerWithinAggroRadius())
            {
                return;
            }

            BossAggroGuard guard = world.CheckGuards();
            if (guard != BossAggroGuard.None)
            {
                ReportBlocked(guard);
                return;
            }

            reportedGuard = BossAggroGuard.None;
            reactionElapsed = 0f;
            TransitionTo(BossAggroState.Alerted, "player_in_aggro_radius");
        }

        // 警觉：玩家离开半径 → 回待机、计时清零；守卫挡着 → 计时暂停；计满反应时间 → 冲。
        private void TickAlerted(float deltaTime)
        {
            if (!PlayerWithinAggroRadius())
            {
                reactionElapsed = 0f;
                TransitionTo(BossAggroState.Idle, "left_radius");
                return;
            }

            BossAggroGuard guard = world.CheckGuards();
            if (guard != BossAggroGuard.None)
            {
                ReportBlocked(guard);
                return;
            }

            reportedGuard = BossAggroGuard.None;
            if (deltaTime > 0f)
            {
                reactionElapsed += deltaTime;
            }

            if (reactionElapsed >= reactionSeconds)
            {
                reactionElapsed = 0f;
                TransitionTo(BossAggroState.Charging, "reaction_elapsed");
            }
        }

        private void TickCharging(float deltaTime)
        {
            BossAggroGuard guard = world.CheckGuards();
            if (guard != BossAggroGuard.None)
            {
                ReportBlocked(guard); // 原地停住，守卫放行后接着冲（见文件头）
                return;
            }

            reportedGuard = BossAggroGuard.None;
            if (!world.TryGetPlayerPosition(out Vector3 target))
            {
                TransitionTo(BossAggroState.Returning, "player_missing");
                return;
            }

            Vector3 position = transform.position;
            float dx = target.x - position.x;
            float dz = target.z - position.z;
            float distance = GameMath.Sqrt(dx * dx + dz * dz);
            float contact = ContactDistance;
            if (distance > contact)
            {
                // 走到接触距离为止，不越过去贴到玩家身上；高度保持原位（地面平面上走）。
                float travel = GameMath.Min(chargeSpeed * deltaTime, distance - contact);
                if (travel > 0f)
                {
                    float scale = travel / distance;
                    transform.position = new Vector3(position.x + dx * scale, position.y, position.z + dz * scale);
                    distance -= travel;
                }
            }

            if (distance <= contact + ContactTolerance)
            {
                BeginContact();
                return;
            }

            // 拴绳：离原位（水平）超过拴绳距离就放弃，走回去。按原位量，玩家跑得比它快也不会把它越引越远。
            if (HorizontalDistanceSqr(transform.position, home) > leashDistance * leashDistance)
            {
                TransitionTo(BossAggroState.Returning, "leash");
            }
        }

        // 接触：先置「BOSS 先动手」，再发起交互（同一次接触只发一次；返回由下一次 Tick 处理）。
        private void BeginContact()
        {
            entryContext.SetInitiator(BattleInitiator.BossAttacked);
            TransitionTo(BossAggroState.Contact, "touched_player");
            pendingContact = ContactResult.Pending;
            RunContactAsync(++contactGeneration, LifetimeToken()).Forget();
        }

        private async UniTaskVoid RunContactAsync(int generation, CancellationToken ct)
        {
            ContactResult result;
            try
            {
                result = await world.InteractAsync(trigger, ct) ? ContactResult.Accepted : ContactResult.Rejected;
            }
            catch (OperationCanceledException)
            {
                result = ContactResult.Cancelled;
            }
            catch (Exception e)
            {
                result = ContactResult.Failed;
                Log.Error($"{name}：BOSS 发难发起的交互抛了异常，这一次作废（开战理由复位、回原位、解除武装）：{e}");
                telemetry.TrackError("boss_aggro_contact_failed", e, TelemetryProps.Of(("boss", BossId)));
            }

            // 期间被关掉 / 作废（OnDisable 递增了代号）：这份回音不再属于当前状态。
            if (generation == contactGeneration)
            {
                pendingContact = result;
            }
        }

        private void ResolveContact()
        {
            if (pendingContact == ContactResult.Pending)
            {
                return; // 交互还在走（嘲讽对白播着）
            }

            ContactResult result = pendingContact;
            pendingContact = ContactResult.None;
            if (result == ContactResult.Accepted && world.IsFighting(bossId))
            {
                TransitionTo(BossAggroState.InBattle, "battle_started");
                return;
            }

            // 交互没受理 / 被取消 / 出错 / 受理了却没开成仗：这一次作废。理由复位，免得之后按 E 被判成「被打」。
            entryContext.ResetInitiator();
            TransitionTo(BossAggroState.Disarmed, ContactReason(result));
        }

        private void TickInBattle()
        {
            if (world.IsFighting(bossId))
            {
                // 战斗场景在场 = 世界被黑幕或战斗舞台挡着：这时瞬移回原位，玩家看不到它「闪回去」。
                // 世界此刻是暂停的（timeScale = 0），小人的位移驱动按「时停」处理，不会把这一跳算成一步奔跑。
                if (world.IsWorldHiddenByBattle && !IsHome)
                {
                    transform.position = home;
                }

                return;
            }

            // 打完了（或没开成）：开战理由复位——之后按 E 挑战仍是「正面攻击、玩家先手」。
            // 战斗场景没进成（开场失败）时 BOSS 还在接触点，解除武装那一段会走回去。
            entryContext.ResetInitiator();
            TransitionTo(BossAggroState.Disarmed, "battle_over");
        }

        // 撤回：走回原位；到了玩家还在发难半径里就解除武装（等他离开再武装，免得一到家又立刻警觉），否则待机。
        private void TickReturning(float deltaTime)
        {
            if (!StepHome(deltaTime))
            {
                return;
            }

            if (PlayerWithinAggroRadius())
            {
                TransitionTo(BossAggroState.Disarmed, "player_still_near");
            }
            else
            {
                TransitionTo(BossAggroState.Idle, "arrived_home");
            }
        }

        // 解除武装：不在原位就先走回去；玩家离开察觉范围（且出了发难半径）才重新武装
        // ——到家了直接待机，没到家转撤回、到家再按玩家位置定。
        private void TickDisarmed(float deltaTime)
        {
            bool atHome = StepHome(deltaTime);
            if (entryContext.MonsterAware || PlayerWithinAggroRadius())
            {
                return;
            }

            TransitionTo(atHome ? BossAggroState.Idle : BossAggroState.Returning, "player_left");
        }

        // 玩家在不在发难半径里（以原位为圆心的水平距离）；没有玩家 / 玩家倒地算不在。
        private bool PlayerWithinAggroRadius() =>
            world.TryGetPlayerPosition(out Vector3 player) && HorizontalDistanceSqr(player, home) <= aggroRadius * aggroRadius;

        private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        // 朝原位走一步；返回「已在原位」。
        private bool StepHome(float deltaTime)
        {
            if (IsHome)
            {
                transform.position = home;
                return true;
            }

            float step = chargeSpeed * deltaTime;
            if (step <= 0f)
            {
                return false;
            }

            transform.position = Vector3.MoveTowards(transform.position, home, step);
            if (!IsHome)
            {
                return false;
            }

            transform.position = home;
            return true;
        }

        private CancellationToken LifetimeToken()
        {
            if (lifetime == null || lifetime.IsCancellationRequested)
            {
                if (lifetime != null) lifetime.Dispose();
                lifetime = new CancellationTokenSource();
            }

            return lifetime.Token;
        }

        // 同一守卫持续挡着只记一次；守卫放行（调用方把 reportedGuard 清回 None）后再挡才再记。
        private void ReportBlocked(BossAggroGuard guard)
        {
            if (guard == reportedGuard)
            {
                return;
            }

            reportedGuard = guard;
            telemetry.Track("boss_aggro_blocked", ("boss", BossId), ("guard", GuardName(guard)), ("state", StateName(state)));
        }

        private void TransitionTo(BossAggroState next, string reason)
        {
            if (next == state)
            {
                return;
            }

            BossAggroState from = state;
            state = next;
            ApplyIndicator();
            telemetry.Track("boss_aggro_state", ("boss", BossId), ("from", StateName(from)), ("to", StateName(next)),
                ("reason", reason));
        }

        // 头顶提示只在警觉与冲刺期间亮；没配提示物体时跳过。只在状态迁移 / 绑定时调，不每帧调。
        private void ApplyIndicator()
        {
            if (alertIndicator == null)
            {
                return;
            }

            bool show = armed && (state == BossAggroState.Alerted || state == BossAggroState.Charging);
            if (alertIndicator.activeSelf != show)
            {
                alertIndicator.SetActive(show);
            }
        }

        private static string ContactReason(ContactResult result) => result switch
        {
            ContactResult.Accepted => "no_battle",
            ContactResult.Rejected => "interact_rejected",
            ContactResult.Cancelled => "interact_cancelled",
            _ => "interact_failed",
        };

        // 埋点用的字面量（不走 enum.ToString()：那一下会分配）。
        private static string StateName(BossAggroState value) => value switch
        {
            BossAggroState.Idle => "idle",
            BossAggroState.Alerted => "alerted",
            BossAggroState.Charging => "charging",
            BossAggroState.Contact => "contact",
            BossAggroState.InBattle => "in_battle",
            BossAggroState.Returning => "returning",
            BossAggroState.Disarmed => "disarmed",
            _ => "unknown",
        };

        private static string GuardName(BossAggroGuard value) => value switch
        {
            BossAggroGuard.Dialogue => "dialogue",
            BossAggroGuard.Performance => "performance",
            BossAggroGuard.Battle => "battle",
            BossAggroGuard.WorldPaused => "world_paused",
            BossAggroGuard.NarrativeBusy => "narrative_busy",
            BossAggroGuard.NarrativeUnavailable => "narrative_unavailable",
            _ => "none",
        };
    }
}

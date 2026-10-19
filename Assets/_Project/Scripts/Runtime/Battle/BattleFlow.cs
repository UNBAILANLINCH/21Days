// 职责：回合制战斗的流程编排（PRP/turnbased-battle §3 数据流第 1–7 步）——
//   订阅剧情的 BattleStageEnteredEvent → 按 payload 查 BOSS → 登记「战斗在途」（压住自动存档）→ 等世界就绪
//   → 按住世界（暂停令牌 / 关 Gameplay 图 / 藏 HUD）→ 黑幕下叠加加载战斗场景、表现开场 → 开仗并驱动 BattleSession
//   → 收场：黑幕下关表现、卸场景、恢复世界，**再按 BattleSession.ExitKey 回写 NarrativeService.CompleteBattleAsync**，最后揭幕。
//   回写排在揭幕之前：胜利走 cleared 写「已击败」标记，NarrativeFlagVisibility 在幕下就把 BOSS NPC 藏好，揭幕后世界里已经没有它
//   （原先揭幕后才回写，玩家会看到 BOSS 凭空消失）。回写被拒 / 抛异常照样揭幕；幕下最多等 1 秒（DefaultWriteBackCoverSeconds），
//   剧情忙或战后接了一段对白时先揭幕、回写在幕后继续，不卡黑屏。
//   开战失败（场景缺失 / 表现开场失败 / 取消）也是同一顺序：BattleArena.EnterAsync 在黑幕下先恢复世界，再揭幕，
//   不会揭开一小段被暂停的世界画面。
//
// 分工（2026-10-09）：本流程只认 **payload 非空**的 Battle 阶段（roster 里查得到 BOSS 定义）；
//   **payload 为空的活遭遇档归 Monster 侧的 `EncounterBattleBridge`**（实时遭遇打赢 / 被击倒 → 回写剧情），
//   本类对它是安静跳过（一条 Info + 一条 stage_live_encounter 埋点），不再记 boss_not_found 的 Error。
//
// 第二个开战入口（2026-10-14，PRP/combat-iteration D5「实时遭遇升级回合制」）：本类实现 Monster 侧声明的
//   `Game.Monster.IEncounterEscalationPort`（扩展而不新建：「开一场仗」本来就是本类的职责）。
//   RunAsync 抽出与剧情无关的内核 RunCoreAsync（等门闸 → 按住世界 → 开场 → 回合循环 → 收场），两个外壳各包一层：
//   · 剧情版（RunAsync）：TryBeginBattle / CompleteBattleAsync / ReleaseBattle 逐字照旧；
//   · 遭遇版（RunEncounterAsync）：不碰剧情三方法，按怪物种类查名册，在**同一时机**（收场黑幕下、恢复世界之后、揭幕之前）
//     回调 EncounterStep.CompleteEscalation 交回胜负与战斗剩余生命；没打完（开场失败 / 取消 / 出错）调 AbortEscalation。
//   一次仍只跑一场：已有仗在途（IsBattleRunning）时拒绝升级，Monster 侧逐字走原路径。
//
// 作用域销毁时的收场（2026-10-15，PRP/combat-iteration W6a 补修，用户同意）：根作用域释放时 UIService / InputService 与本类一起销毁，
//   而取消路径上的收场是异步的（黑幕淡入要等帧），轮到「恢复世界」时 UI 服务往往已经释放，SetLayerVisible 抛
//   ObjectDisposedException，被外壳记成 battle_failed。修法：Dispose 先**同步**撤掉这一仗的世界锁
//   （BattleWorldLock.Engagement.Abandon：只交还暂停令牌，不还原 HUD 层与输入图——世界本身也在销毁），再取消；
//   之后收场里的 engagement.Dispose 都是空操作，剧情版与遭遇版共用 RunCoreAsync，一处覆盖两者。
//   选「Dispose 时撤锁并标记」而不是「收场时 catch ObjectDisposedException」：不靠异常做分支、不吞任何异常，
//   且与释放顺序无关（本类先释放、UI 先释放两种都走同一条路）；正常 / 出错收场照旧逐项还原。
//
// 为什么新建（project-root「加能力的顺序」）：
//   1. 复用不行：工程里没有任何「一场回合制战斗怎么开、怎么推、怎么收」的流程；EncounterStep 管的是巡逻怪的实时遭遇
//      （确定性 tick），不是回合；InventoryPanelController / DialogueService 只示范了「按住世界再还原」这一小段。
//   2. 扩展不行：Runtime/TurnBased 必须保持纯规则、不依赖 Narrative / Loot / Player / Core.UI（turnbased-module-guide
//      「依赖方向」），塞进去就测不了内核；Runtime/Narrative 只发通知、不管胜负（battle-to-narrative §2.1「唯一写入方」）；
//      Runtime/Monster 本波禁改。所以按 PRP D1 新建 Game.Battle（走 Game.Runtime 程序集，不建独立 asmdef）。
//
// 不做：舞台美术、相机、BattleView、悬停详情、闪现提示——全在 IBattlePresenter 的实现里（W2）。
// 两个同名 BattleOutcome（Game.TurnBased / Game.Narrative）：本文件只 using TurnBased，剧情事件用别名，结果只走 ExitKey 字符串（PRP §7 坑 4）。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.TurnBased;
using MessagePipe;
using VContainer.Unity;
using StageEvent = Game.Narrative.BattleStageEnteredEvent;

namespace Game.Battle
{
    /// <summary>
    /// 战斗流程（根作用域入口点）。一次只跑一场；剧情在同一身份上重复通知会被忽略，换了身份的新通知会顶掉还在等世界就绪的旧请求。
    /// 同时是实时遭遇的升级端口（<see cref="Game.Monster.IEncounterEscalationPort"/>，PRP/combat-iteration D5）。
    /// </summary>
    public sealed class BattleFlow : IStartable, IDisposable, Game.Monster.IEncounterEscalationPort
    {
        /// <summary>升级被拒：该种类在名册里没有回合制战斗数据。</summary>
        public const string EscalationRejectNotConfigured = "not_configured";

        /// <summary>升级被拒：已有一场仗在途（含还在等世界就绪的剧情 BOSS 战）。</summary>
        public const string EscalationRejectBusy = "battle_busy";

        /// <summary>升级被拒：开战规则或回合制配置不让开（BattleSetup.TryCreate 返回 false）。</summary>
        public const string EscalationRejectRule = "rule_rejected";

        /// <summary>升级被拒：战斗侧不可用（已释放，或没有注册战斗表现）。</summary>
        public const string EscalationRejectUnavailable = "unavailable";

        /// <summary>表现层连续给出被拒指令的上限：超过即视为表现层坏了，放弃这一场（防同步死循环）。</summary>
        private const int MaxConsecutiveRejections = 100;

        /// <summary>
        /// 剧情回写在黑幕下最多等多久（秒，真实时间）。常态下回写同步走完（结局是 End 阶段），一帧都不等；
        /// 只有剧情正忙（NarrativeBattlePort 要等它空闲）或战后接了一段对白（对白要玩家看得见才推得动）才会等到上限。
        /// </summary>
        private const double DefaultWriteBackCoverSeconds = 1.0;

        private readonly ISubscriber<StageEvent> stages;
        private readonly BossRoster roster;
        private readonly IBattleNarrative narrative;
        private readonly IBattleWorldGate gate;
        private readonly BattleWorldLock worldLock;
        private readonly BattleArena arena;
        private readonly BattleSetup setup;
        private readonly BattleItemInventory items;
        private readonly IBattlePresenter presenter;
        private readonly ITelemetryScope telemetry;
        private readonly TimeSpan writeBackCoverLimit;
        private readonly List<BattleItemSlot> slots = new List<BattleItemSlot>();
        private IDisposable subscription;
        private Run active;
        private bool disposed;

        // 进战斗的局势快照来源（遭遇侧实现）。null = 没有遭遇侧（独立原型场景 / 纯 Battle 测试作用域），
        // 那时 BattleSetup 退回「正面攻击、玩家先手」的硬编码请求，与接线前逐字一致。
        // 走 BindEntryContext 后置装配：本类的依赖都经 RegisterEntryPoint 的工厂显式给出，
        // 加构造参数会动到那处注册与全部流程测试，而后置装配是本工程既有的写法（MonsterInstaller 的 TryResolve 那一批）。
        private Game.Monster.IBattleEntryContext entryContext;

        // 按需解析的来源（见 BindEntryContextProvider）：非 null 时每次开仗现取一次，取到的结果盖过 entryContext。
        // 为什么要有这一层：BOSS 自己的快照在玩法场景里，而绑定发生在 Boot 场景（同上面的注释）。
        private Func<string, Game.Monster.IBattleEntryContext> entryContextProvider;

        /// <param name="presenter">战斗表现；W2 之前没有实现，传 null 时开战会报错并放弃（不卡住剧情）。</param>
        /// <param name="writeBackCoverLimit">剧情回写在黑幕下最多等多久；null = 默认 1 秒，≤ 0 = 回写没当场落定就直接揭幕（测试用）。</param>
        public BattleFlow(ISubscriber<StageEvent> stages, BossRoster roster, IBattleNarrative narrative, IBattleWorldGate gate,
            BattleWorldLock worldLock, BattleArena arena, BattleSetup setup, BattleItemInventory items, IBattlePresenter presenter,
            ITelemetryScope telemetry, TimeSpan? writeBackCoverLimit = null)
        {
            this.stages = stages ?? throw new ArgumentNullException(nameof(stages));
            this.roster = roster ?? throw new ArgumentNullException(nameof(roster));
            this.narrative = narrative ?? throw new ArgumentNullException(nameof(narrative));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
            this.worldLock = worldLock ?? throw new ArgumentNullException(nameof(worldLock));
            this.arena = arena ?? throw new ArgumentNullException(nameof(arena));
            this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
            this.items = items ?? throw new ArgumentNullException(nameof(items));
            this.presenter = presenter;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            this.writeBackCoverLimit = writeBackCoverLimit ?? TimeSpan.FromSeconds(DefaultWriteBackCoverSeconds);
        }

        /// <summary>有一场战斗在流程里（含等世界就绪）。W2 的暂停菜单可据此不开（PRP D7）。</summary>
        public bool IsBattleRunning => active != null;

        /// <summary>战斗已经越过门闸、世界被按住（舞台 / 界面在场或正在进出）。</summary>
        public bool IsBattleEngaged => active != null && active.Engaged;

        /// <summary>
        /// 流程里（含等门闸）的这一仗是不是跟 <paramref name="bossId"/> 那一行打的（按 <see cref="BossDefinition.Id"/> 逐字比）。
        /// 给 BOSS 发难组件分辨「是不是在跟我打」（PRP/combat-iteration D6）：按 E 挑战开的仗与它自己冲上来开的仗，
        /// 战后都要回原位、等玩家离开再重新武装；别处的仗（实时遭遇升级）只当守卫挡一挡。只读、零分配。
        /// </summary>
        public bool IsFightingBoss(string bossId) =>
            active != null && !string.IsNullOrEmpty(bossId) && string.Equals(active.Boss.Id, bossId, StringComparison.Ordinal);

        /// <summary>
        /// 接入进战斗的局势快照（`Game.Monster.IBattleEntryContext`，实现方是遭遇侧的 `EncounterStep`）。
        /// <para>
        /// 由 `BattleInstaller` 的构建回调在 `TryResolve&lt;EncounterStep&gt;` 之后调用；
        /// **不接（或传 null）= 开战方式与接线前逐字一致**（正面攻击、玩家先手），
        /// 独立原型场景与纯 Battle 测试作用域走的正是那一支。
        /// </para>
        /// <para>
        /// 接线后 07 的偷袭（BOSS −20% 生命）与被打（BOSS 先手）两条分支才第一次有生产入口；
        /// 三个量的来源与其中的推断成分见 `IBattleEntryContext` 与 `EncounterStep` 的实现区注释。
        /// </para>
        /// </summary>
        public void BindEntryContext(Game.Monster.IBattleEntryContext context) => entryContext = context;

        /// <summary>
        /// 接入局势快照的**来源**（2026-10-10，③ 方案 2）：每次要开仗时才调一次，取到谁就用谁。
        /// <para>
        /// <b>为什么不是「绑一次就定」</b>：BOSS 自己的那份快照挂在**玩法场景**的 NPC 上
        /// （`Game.Battle.BossEntryContext`），而容器构建回调跑在 **Boot 场景**里——那一刻
        /// `FindObjectOfType` 只能看见 Boot 里的东西，实测拿到的是 null（日志：
        /// 「BossEntryContext 查找结果 = null」）。所以解析必须发生在「已经站在要打的那个人面前」的时刻，
        /// 也就是收到战斗阶段通知的时候；这里存的是那个解析动作，不是解析结果。
        /// </para>
        /// <para>
        /// 解析动作每次开仗调一次（不是每帧），实现方自己保证廉价与幂等；参数是这一仗的 BOSS payload，
        /// 多只 BOSS 时实现方据此挑「这一份局势属于谁」。
        /// </para>
        /// </summary>
        public void BindEntryContextProvider(Func<string, Game.Monster.IBattleEntryContext> provider) =>
            entryContextProvider = provider;

        /// <summary>
        /// 开仗那一刻的局势快照：先问按需来源（BOSS 自己那份在玩法场景里，绑定时刻它还不在），
        /// 取不到再退回构建期绑定的那一份（巡逻遭遇），两份都够不上发言权时返回 null = 硬编码缺省。
        /// </summary>
        /// <param name="bossId">这一仗的 BOSS payload；按需来源据此确认「这一份局势属于这只 BOSS」。</param>
        private Game.Monster.IBattleEntryContext ResolveEntryContext(string bossId)
        {
            Game.Monster.IBattleEntryContext candidate = null;
            if (entryContextProvider != null)
            {
                try
                {
                    candidate = entryContextProvider(bossId);
                }
                catch (Exception e)
                {
                    // 解析失败不该把开仗一起炸掉：退回构建期那一份，并留痕。
                    Log.Error($"BattleFlow：局势快照来源解析失败，这一仗按缺省口径开：{e}");
                    telemetry.TrackError("battle_entry_provider_failed", e);
                }
            }

            if (candidate == null)
            {
                candidate = entryContext;
            }

            return BattleSetup.IsEncounterEngaged(candidate) ? candidate : null;
        }

        /// <summary>
        /// 这个怪物种类有没有回合制战斗数据（名册里有一行 <see cref="BossDefinition.MonsterKindId"/> 等于它）。
        /// 0 恒为否（没接种类表的怪不升级）；已释放时恒为否。只读、零分配，遭遇侧在 tick 里调。
        /// </summary>
        public bool CanEscalate(int kindId) => !disposed && roster.TryGetByKind(kindId, out _);

        /// <summary>
        /// 实时遭遇发起升级（PRP/combat-iteration D5）。<b>同步</b>回答接受与否，战斗本身异步进行：
        /// <list type="bullet">
        /// <item>已释放 / 没有战斗表现 → <see cref="EscalationRejectUnavailable"/>；</item>
        /// <item>已有一场仗在途（<see cref="IsBattleRunning"/>，含剧情 BOSS 战）→ <see cref="EscalationRejectBusy"/>；</item>
        /// <item>名册里没有这个种类 → <see cref="EscalationRejectNotConfigured"/>；</item>
        /// <item>开仗装配被拒（规则 / 配置 / 血量）→ <see cref="EscalationRejectRule"/>。</item>
        /// </list>
        /// 接受时当场按请求开好会话（触发情形 → 开战方式、实时血量 → 战斗血量，见 <see cref="BattleSetup.TryCreate(BossDefinition, in Game.Monster.EncounterEscalationRequest, out BattleSession, out string)"/>），
        /// 登记为「在途」，再走与剧情版同一个内核；打完在收场黑幕下回调 <paramref name="encounter"/> 恰好一次。
        /// </summary>
        public bool TryEscalate(Game.Monster.EncounterStep encounter, in Game.Monster.EncounterEscalationRequest request,
            out string rejectReason)
        {
            if (encounter == null) throw new ArgumentNullException(nameof(encounter));

            if (disposed || presenter == null)
            {
                rejectReason = EscalationRejectUnavailable;
                return false;
            }

            if (active != null)
            {
                rejectReason = EscalationRejectBusy;
                return false;
            }

            if (!roster.TryGetByKind(request.KindId, out BossDefinition boss))
            {
                rejectReason = EscalationRejectNotConfigured;
                return false;
            }

            if (!setup.TryCreate(boss, in request, out BattleSession session, out string error))
            {
                // 正常数据下到不了这里（三种翻译都能进战斗）；真到了说明配置坏了，留一条能倒推的 W 级痕迹。
                telemetry.TrackWarn("escalation_refused", TelemetryProps.Of(("boss", boss.Id), ("kind", request.KindId),
                    ("error", error)));
                rejectReason = EscalationRejectRule;
                return false;
            }

            var run = new Run(boss, encounter, in request, session);
            active = run;
            rejectReason = null;
            RunEncounterAsync(run).Forget();
            return true;
        }

        public void Start()
        {
            if (disposed || subscription != null) return;
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            stages.Subscribe(HandleStage).AddTo(bag);
            subscription = bag.Build();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            subscription?.Dispose();
            subscription = null;
            // 先撤锁再取消（见文件头「作用域销毁时的收场」）：取消会把收场推起来，那时 UI / 输入服务可能已经释放，
            // 撤锁之后收场里的 engagement.Dispose 是空操作，不再碰它们。还在等门闸的仗没有锁，这一句是空操作。
            active?.Engagement?.Abandon();
            active?.Cancel.Cancel();
        }

        // 回调在 NarrativeService.DriveAsync / ReloadFromSave 里同步执行：这里绝不许往外抛，否则会把剧情推进一起炸掉。
        private void HandleStage(StageEvent stage)
        {
            try
            {
                Begin(stage);
            }
            catch (Exception e)
            {
                Log.Error($"BattleFlow：处理战斗阶段通知失败（{stage.StageId} / payload「{stage.Payload}」）：{e}");
                telemetry.TrackError("stage_failed", e, TelemetryProps.Of(("stage", stage.StageId), ("payload", stage.Payload)));
            }
        }

        private void Begin(StageEvent stage)
        {
            if (disposed) return;

            // payload 为空的 Battle 阶段是**活遭遇档**（没有 BOSS 可查）：归遭遇侧的 EncounterBattleBridge
            // （实时遭遇打赢 / 被击倒 → 回写剧情），本流程一个字都不碰。
            // 位置在 active 判断之前：正在等世界就绪的 BOSS 战不该被一个活遭遇阶段顶掉。
            // 接线前这里会一路走到 roster.TryGet("") 并记一条 Error（boss_not_found），每进一次活遭遇档刷一条。
            if (string.IsNullOrEmpty(stage.Payload))
            {
                Log.Info($"BattleFlow：战斗阶段 {stage.StageId} 的 payload 为空，这是活遭遇档，交给遭遇侧处理（本流程只管 BOSS 战）。");
                telemetry.Track("stage_live_encounter", ("stage", stage.StageId), ("activation", stage.ActivationId));
                return;
            }

            if (active != null)
            {
                if (active.IsEncounter)
                {
                    // 实时遭遇升级出来的那一仗在途（PRP/combat-iteration D5）：遭遇侧已经冻结、等它交回结果，
                    // 不能像「等门闸的旧剧情请求」那样被顶掉——留痕后忽略，与「世界已按住」同一口径。
                    Log.Warn($"BattleFlow：一场遭遇升级的战斗正在进行，忽略新的战斗阶段通知 {stage.StageId}（payload「{stage.Payload}」）");
                    telemetry.TrackWarn("stage_ignored_busy", TelemetryProps.Of(("stage", stage.StageId), ("payload", stage.Payload)));
                    return;
                }

                if (SameIdentity(active.Stage, stage))
                {
                    telemetry.Track("stage_duplicate", ("stage", stage.StageId));
                    return;
                }

                if (active.Engaged)
                {
                    // 世界已被按住、舞台在场：这时不可能有合法的新战斗阶段（输入关着、暂停菜单关着），留痕后忽略。
                    Log.Warn($"BattleFlow：一场战斗正在进行，忽略新的战斗阶段通知 {stage.StageId}（payload「{stage.Payload}」）");
                    telemetry.TrackWarn("stage_ignored_busy", TelemetryProps.Of(("stage", stage.StageId), ("payload", stage.Payload)));
                    return;
                }

                // 还在等世界就绪（读档途中剧情又换了身份）：旧请求已是旧身份，让位给新的。
                active.Cancel.Cancel();
                active = null;
            }

            telemetry.Track("stage_received", ("stage", stage.StageId), ("payload", stage.Payload), ("activation", stage.ActivationId));
            if (!roster.TryGet(stage.Payload, out BossDefinition boss))
            {
                Log.Error($"BattleFlow：战斗阶段 {stage.StageId} 的 payload「{stage.Payload}」在 BossRosterConfig 里找不到，不开仗。" +
                          "检查 Data/Battle/BossRosterConfig.asset 的 id 与 Tables/Data/narrative 里该阶段的 payload 是否一致。");
                telemetry.TrackWarn("boss_not_found", TelemetryProps.Of(("stage", stage.StageId), ("payload", stage.Payload)));
                return;
            }

            if (presenter == null)
            {
                Log.Error($"BattleFlow：没有注册战斗表现（IBattlePresenter），「{boss.DisplayName}」这一仗开不了。" +
                          "W2 要在 BattleInstaller 里注册 BattleArena 场景的表现实现。剧情停在战斗阶段，可再交互重试。");
                telemetry.TrackWarn("presenter_missing", TelemetryProps.Of(("boss", boss.Id)));
                return;
            }

            if (!narrative.TryBeginBattle(stage))
            {
                telemetry.Track("stage_stale", ("stage", stage.StageId), ("activation", stage.ActivationId));
                return;
            }

            var run = new Run(stage, boss);
            active = run;
            RunAsync(run).Forget();
        }

        // 剧情版外壳（行为与抽出内核之前逐字一致）：登记在途由 Begin 里的 TryBeginBattle 做，这里负责揭幕后读回写结果、
        //   没回写成功就 ReleaseBattle。
        private async UniTaskVoid RunAsync(Run run)
        {
            CancellationToken ct = run.Cancel.Token;
            bool completed = false;
            try
            {
                string exitKey = await RunCoreAsync(run, ct);

                // 已揭幕。常态下回写早在幕下落定，这里直接读结果；超过幕下上限的在这里接着等（异常也在这里抛出）。
                completed = run.WriteBack != null && await run.WriteBack.Task;
                if (!completed)
                {
                    Log.Warn($"BattleFlow：剧情没接受战斗结果 {exitKey}（{run.Stage.StageId}，激活号 {run.Stage.ActivationId}）——" +
                             "多半是读档 / 换主线后的旧结果；阶段原地不动，可重打。");
                    telemetry.TrackWarn("result_rejected", TelemetryProps.Of(("result", exitKey), ("stage", run.Stage.StageId)));
                }
            }
            catch (OperationCanceledException)
            {
                telemetry.Track("battle_cancelled", ("boss", run.Boss.Id), ("engaged", run.Engaged));
            }
            catch (Exception e)
            {
                Log.Error($"BattleFlow：「{run.Boss.DisplayName}」这一仗失败，世界已还原、剧情停在 {run.Stage.StageId}（可再交互重试）：{e}");
                telemetry.TrackError("battle_failed", e, TelemetryProps.Of(("boss", run.Boss.Id), ("stage", run.Stage.StageId)));
            }
            finally
            {
                if (!completed) narrative.ReleaseBattle(run.Stage);
                if (active == run) active = null;
                // 不 Dispose 取消源：取消路径上这段 finally 是在 Cancel() 的回调里同步跑到的，回调中途释放取消源不安全；
                // 它没有计时器、没取过 WaitHandle，交给 GC 即可。
            }
        }

        // 遭遇版外壳（PRP/combat-iteration D5）：不碰剧情三方法（TryBeginBattle / CompleteBattleAsync / ReleaseBattle）。
        //   结果在内核收场的幕下交回（CompleteEscalationUnderCurtain）；没交回就在这里让遭遇退出「升级在途」，世界照常继续。
        private async UniTaskVoid RunEncounterAsync(Run run)
        {
            CancellationToken ct = run.Cancel.Token;
            try
            {
                await RunCoreAsync(run, ct);
            }
            catch (OperationCanceledException)
            {
                telemetry.Track("battle_cancelled", ("boss", run.Boss.Id), ("engaged", run.Engaged));
            }
            catch (Exception e)
            {
                Log.Error($"BattleFlow：遭遇升级的「{run.Boss.DisplayName}」这一仗失败，世界已还原、遭遇照常继续（这一下作废）：{e}");
                telemetry.TrackError("battle_failed", e, TelemetryProps.Of(("boss", run.Boss.Id), ("target", run.Request.TargetId)));
            }
            finally
            {
                if (!run.EscalationSettled) run.Encounter.AbortEscalation();
                if (active == run) active = null;
                // 取消源不 Dispose：理由同剧情版外壳。
            }
        }

        // 与剧情无关的内核：等门闸 → 按住世界 → 开场 → 回合循环 → 收场（黑幕下关界面、卸场景、恢复世界 → 交回结果 → 揭幕）。
        //   两个外壳唯一的差别在「会话从哪来」（OpenNarrativeSession / OpenEncounterSession）与「幕下交回给谁」（BeforeReveal）。
        //   返回出口键；没打完（异常 / 取消）时异常原样抛给外壳。
        private async UniTask<string> RunCoreAsync(Run run, CancellationToken ct)
        {
            await gate.WaitUntilReadyAsync(ct);
            // 门闸实现不认取消令牌时的兜底：本类已释放（作用域销毁）就不再按住一个正在销毁的世界。
            ct.ThrowIfCancellationRequested();
            run.Engaged = true;
            string exitKey = null;
            BattleWorldLock.Engagement engagement = worldLock.Engage(this);
            run.Engagement = engagement; // Dispose 要能同步撤掉它（见 Dispose）
            try
            {
                BattleSession session = run.IsEncounter ? OpenEncounterSession(run) : OpenNarrativeSession(run);
                telemetry.Track("battle_started", ("boss", run.Boss.Id), ("activation", run.Stage.ActivationId));
                // 回放测试口换过数值（BattleSetup.OverrideSettings）：这一仗用的不是配置资产的数值，留 W 级痕迹（只会出现在编辑器 / 开发版）。
                if (setup.IsOverridden)
                    telemetry.TrackWarn("settings_overridden", TelemetryProps.Of(("boss", run.Boss.Id), ("activation", run.Stage.ActivationId)));
                // 开场失败时 EnterAsync 在黑幕下先跑 engagement.Dispose（恢复世界）再揭幕，与正常收场同序。
                await arena.EnterAsync(presenter, run.Boss, session, engagement.Dispose, ct,
                    run.IsEncounter ? run.Request.TargetId : run.Stage.TargetId);
                try
                {
                    exitKey = await FightAsync(session, ct);
                    telemetry.Track("battle_finished", ("boss", run.Boss.Id), ("result", exitKey));
                }
                finally
                {
                    // 黑幕盖着时（PRP D6）：关界面、卸场景、恢复相机 / 输入 / 暂停 → 交回结果 → 揭幕。
                    // 先恢复世界再交回：战后若接对白，对白看到的是正常世界，不会把「战斗中」的输入 / HUD 状态当成进来前的状态记下。
                    // 没打完（异常 / 取消，exitKey 为空）不交回。
                    await arena.ExitAsync(presenter, engagement.Dispose,
                        exitKey == null ? (Func<UniTask>)null : BeforeReveal(run, exitKey, session, ct));
                }
            }
            finally
            {
                engagement.Dispose(); // 幂等：正常收场与开场失败都已在黑幕下还原过，这里兜没碰黑幕的路径（开仗被拒等）
            }

            return exitKey;
        }

        // 幕下交回给谁：剧情版回写剧情（WriteBackUnderCurtainAsync），遭遇版回调遭遇（CompleteEscalationUnderCurtain）。
        private Func<UniTask> BeforeReveal(Run run, string exitKey, BattleSession session, CancellationToken ct) =>
            run.IsEncounter
                ? (Func<UniTask>)(() => CompleteEscalationUnderCurtain(run, exitKey, session))
                : (Func<UniTask>)(() => WriteBackUnderCurtainAsync(run, exitKey, ct));

        // 剧情版的会话：开仗那一刻现取局势快照（按需来源优先），按快照开仗；埋点两条与抽出内核之前逐字一致。
        private BattleSession OpenNarrativeSession(Run run)
        {
            // 局势快照只在**遭遇真的有话说**的时候才交出去（判定见 BattleSetup.IsEncounterEngaged 的文档）：
            // 正式流程里唯一的开战触发是剧情 Battle 阶段（demo 的 `Npc_SampleBoss`「挑战」），跟巡逻遭遇是两回事；
            // 把十几米外巡逻怪的平静快照交出去，规则会判「不在警戒 / 敌对区域」而把这一仗拒掉（实测 BattleShowcase 全红）。
            Game.Monster.IBattleEntryContext situation = ResolveEntryContext(run.Boss.Id);
            if (!setup.TryCreate(run.Boss, situation, out BattleSession session, out string error))
                throw new InvalidOperationException($"「{run.Boss.DisplayName}」开不了仗：{error}");
            // 记下这一仗是按哪种方式进的（07 的偷袭 / 正面攻击 / 被打）。没有遭遇侧快照时记 context=false +
            // 「正面攻击」——那是硬编码缺省，不是「现场判定出正面攻击」，两者在日志里必须分得开。
            // `source` 是**谁报的局势**：boss = 这只 BOSS 自己（③ 方案 2）、encounter = 巡逻遭遇、none = 缺省。
            // 三者混在一个 context=false 里就没法回答「开战方式到底是谁说了算」。
            telemetry.Track("battle_entry", ("boss", run.Boss.Id), ("context", situation != null),
                ("kind", session.Entry.Kind.ToString()),
                ("source", situation == null ? "none"
                    : situation is Game.Battle.BossEntryContext ? "boss" : "encounter"));
            // 现场五量另起一条，别把语义混进上面那条（TelemetryProps 定长 4 槽，一条事件超 4 个属性就该拆）。
            // 「遭遇快照被采纳了吗」与「快照说现场是什么样」是两个问题：前者读 battle_entry 的 context，
            // 后者读这里——**没有这一条，日志里只看得见硬编码缺省，分不清「没交快照」与「交了但落进缺省分支」**。
            // 只有真采纳了才记：被 `IsEncounterEngaged` 挡回去的那份快照不是这一仗的理由，记了会误导。
            if (situation != null)
            {
                telemetry.Track("battle_entry_snapshot",
                    ("sneaking", situation.PlayerSneaking),
                    ("sneak_attack", situation.SneakAttackLanded),
                    ("zones", (situation.PlayerInAlertZone ? "alert" : "-")
                              + (situation.PlayerInHostileZone ? "+hostile" : string.Empty)),
                    ("aware", situation.MonsterAware));
            }

            return session;
        }

        // 遭遇版的会话：TryEscalate 接受时已经按请求开好（触发情形与实时血量都在那一刻定死）。
        // battle_entry 沿用同一个事件名，`source` 记 escalation——与剧情版的 boss / encounter / none 并列，
        // 一眼分得出「这一仗是实时遭遇升级出来的」；第 4 槽给触发情形（TelemetryProps 定长 4 槽，context 在这一路恒为真，不占槽）。
        private BattleSession OpenEncounterSession(Run run)
        {
            BattleSession session = run.PreparedSession;
            telemetry.Track("battle_entry", ("boss", run.Boss.Id), ("kind", session.Entry.Kind.ToString()),
                ("source", "escalation"), ("trigger", (int)run.Request.Trigger));
            return session;
        }

        // 遭遇版的幕下交回（与剧情版回写同一时机：恢复世界之后、揭幕之前）：胜负 + 战斗里玩家的剩余生命与上限
        //   （「剩余比例」用整数对交回，按比例写回由遭遇侧做，见 EncounterEscalationRequest.ScaleHealth）。
        //   交回被接受才算「已结算」；遭遇侧抛异常时外壳的 finally 会让它退出在途。
        private UniTask CompleteEscalationUnderCurtain(Run run, string exitKey, BattleSession session)
        {
            bool victory = string.Equals(exitKey, BattleExitKeys.Victory, StringComparison.Ordinal);
            run.EscalationSettled = run.Encounter.CompleteEscalation(victory, session.Player.Health, session.Player.MaxHealth);
            return UniTask.CompletedTask;
        }

        // 揭幕前一步（BattleArena.ExitAsync 的 beforeReveal）：发起回写，等它落定或等到上限。结果与异常不在这里读——
        //   RunAsync 揭幕后统一 await run.WriteBack（回写被拒 / 抛异常时 BattleArena 已照样揭幕）。
        //   回写转进 UniTaskCompletionSource：幕下与幕后各 await 一次，普通 UniTask 只许 await 一次。
        private async UniTask WriteBackUnderCurtainAsync(Run run, string exitKey, CancellationToken ct)
        {
            run.WriteBack = new UniTaskCompletionSource<bool>();
            RelayAsync(narrative.CompleteBattleAsync(run.Stage, exitKey, ct), run.WriteBack).Forget();
            if (run.WriteBack.Task.Status != UniTaskStatus.Pending) return;

            if (writeBackCoverLimit > TimeSpan.Zero)
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await UniTask.WhenAny(SettleAsync(run.WriteBack.Task),
                    UniTask.Delay(writeBackCoverLimit, true, PlayerLoopTiming.Update, limit.Token).SuppressCancellationThrow().AsUniTask());
                limit.Cancel();
                ct.ThrowIfCancellationRequested();
                if (run.WriteBack.Task.Status != UniTaskStatus.Pending) return;
            }

            Log.Warn($"BattleFlow：剧情回写 {exitKey} 在黑幕下 {writeBackCoverLimit.TotalSeconds:0.##} 秒内没落定（剧情正忙，或战后接了对白），" +
                     "先揭幕、回写在幕后继续；BOSS 退场可能落在揭幕之后。");
            telemetry.TrackWarn("write_back_outlived_curtain", TelemetryProps.Of(("result", exitKey), ("stage", run.Stage.StageId)));
        }

        private static async UniTaskVoid RelayAsync(UniTask<bool> source, UniTaskCompletionSource<bool> target)
        {
            try
            {
                target.TrySetResult(await source);
            }
            catch (OperationCanceledException e)
            {
                target.TrySetCanceled(e.CancellationToken);
            }
            catch (Exception e)
            {
                target.TrySetException(e);
            }
        }

        // 只等它落定，不读结果也不抛：结果与异常留给 RunAsync 揭幕后的那次 await。
        private static async UniTask SettleAsync(UniTask<bool> task)
        {
            try
            {
                await task;
            }
            catch (Exception)
            {
                // 有意吞掉：同一个异常揭幕后会在 RunAsync 里再抛一次并记日志。
            }
        }

        // 一场仗的回合循环：BOSS 回合自动推进，玩家回合等表现层给指令；每次动作后把事件交给表现层播完再往下走。
        private async UniTask<string> FightAsync(BattleSession session, CancellationToken ct)
        {
            await presenter.PlayAsync(session, TakeEvents(session), ct); // 开场事件（偷袭扣血等；正面攻击时为空）
            string rejection = null;
            int rejectedInRow = 0;
            while (!session.IsOver)
            {
                ct.ThrowIfCancellationRequested();
                if (session.Phase == BattlePhase.BossTurn)
                {
                    session.RunBossTurn();
                    await presenter.PlayAsync(session, TakeEvents(session), ct);
                    continue;
                }

                items.ListSlots(session.Items, slots);
                BattleCommand command = await presenter.WaitCommandAsync(new BattleCommandMenu(session, slots, rejection), ct);
                rejection = Apply(session, command);
                if (rejection == null)
                {
                    rejectedInRow = 0;
                    await presenter.PlayAsync(session, TakeEvents(session), ct);
                    continue;
                }

                telemetry.Track("command_rejected", ("kind", command.Kind.ToString()), ("reason", rejection));
                if (++rejectedInRow >= MaxConsecutiveRejections)
                    throw new InvalidOperationException($"战斗表现连续 {MaxConsecutiveRejections} 次给出被拒的指令，放弃这一场（最后一次：{rejection}）");
            }

            return session.ExitKey ?? throw new InvalidOperationException("战斗已结束却没有出口键（BattleSession.ExitKey 为空）");
        }

        /// <returns>null = 指令被接受；否则是被拒原因（中文）。</returns>
        private string Apply(BattleSession session, BattleCommand command)
        {
            switch (command.Kind)
            {
                case BattleCommandKind.CastSkill:
                {
                    SkillCastResult cast = session.TryCastSkill(command.Skill);
                    return cast.Accepted ? null : "招式不能用：" + cast.Reject;
                }

                case BattleCommandKind.UseItem:
                {
                    ItemUseDecision use = session.TryUseItem(command.ItemId);
                    if (!use.Allowed) return use.Describe();
                    // 规则已记账（本场用过）且效果已生效；从背包扣 1（PRP D10）。扣不动说明背包在战斗中被别处改了，只留痕。
                    if (!items.TryConsume(command.ItemId))
                    {
                        Log.Warn($"BattleFlow：道具 {command.ItemId} 已在战斗里用掉，但背包扣减失败（数量不够？）");
                        telemetry.TrackWarn("item_consume_failed", TelemetryProps.Of(("item", command.ItemId)));
                    }

                    return null;
                }

                case BattleCommandKind.EndTurn:
                    if (session.Phase != BattlePhase.PlayerTurn) return "现在不是玩家回合";
                    session.SkipPlayerTurn();
                    return null;

                default:
                    return "未知指令：" + command.Kind;
            }
        }

        // 事件列表每次动作前会被清空，交给表现层的是拷贝（表现层可以跨 await 持有）。
        private static BattleEvent[] TakeEvents(BattleSession session)
        {
            IReadOnlyList<BattleEvent> source = session.Events;
            var copy = new BattleEvent[source.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            return copy;
        }

        private static bool SameIdentity(in StageEvent a, in StageEvent b) =>
            a.Generation == b.Generation && a.ActivationId == b.ActivationId && string.Equals(a.TargetId, b.TargetId, StringComparison.Ordinal);

        /// <summary>
        /// 一场仗的流程句柄：身份、BOSS、取消源、是否已越过门闸。
        /// 两种来源：剧情 Battle 阶段（<see cref="Stage"/> 有值）与实时遭遇升级（<see cref="Encounter"/> 非空，<see cref="Stage"/> 为默认值）。
        /// </summary>
        private sealed class Run
        {
            public Run(StageEvent stage, BossDefinition boss)
            {
                Stage = stage;
                Boss = boss;
            }

            public Run(BossDefinition boss, Game.Monster.EncounterStep encounter, in Game.Monster.EncounterEscalationRequest request,
                BattleSession session)
            {
                Boss = boss;
                Encounter = encounter;
                Request = request;
                PreparedSession = session;
            }

            public StageEvent Stage { get; }
            public BossDefinition Boss { get; }

            /// <summary>发起升级的遭遇（结果交回给它）；剧情版为 null。</summary>
            public Game.Monster.EncounterStep Encounter { get; }

            /// <summary>升级请求（目标、种类、触发情形、实时血量）；剧情版为默认值。</summary>
            public Game.Monster.EncounterEscalationRequest Request { get; }

            /// <summary>升级被接受时当场开好的会话；剧情版为 null（开仗那一刻现开）。</summary>
            public BattleSession PreparedSession { get; }

            /// <summary>这一仗是实时遭遇升级出来的。</summary>
            public bool IsEncounter => Encounter != null;

            /// <summary>遭遇版：结果已在幕下交回并被遭遇接受（否则外壳收尾时让遭遇退出在途）。</summary>
            public bool EscalationSettled { get; set; }
            public CancellationTokenSource Cancel { get; } = new CancellationTokenSource();
            public bool Engaged { get; set; }

            /// <summary>这一仗按住世界的令牌（越过门闸后才有；之前为 null）。宿主销毁时由 <see cref="BattleFlow.Dispose"/> 撤掉。</summary>
            public BattleWorldLock.Engagement Engagement { get; set; }

            /// <summary>剧情回写（打完才有；没打完为 null）。可 await 多次。</summary>
            public UniTaskCompletionSource<bool> WriteBack { get; set; }
        }
    }
}

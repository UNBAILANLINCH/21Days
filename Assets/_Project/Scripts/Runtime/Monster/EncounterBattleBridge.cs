// 职责：把「一场实时遭遇打赢 / 被击倒」接进剧情阶段机（roadmap C5 剩下的那一半，PRP/battle-to-narrative）。
//   订阅 BattleStageEnteredEvent，只认 **payload 为空**的那些 Battle 阶段（= 活遭遇档；非空是 BOSS 战，归 Game.Battle 的
//   BattleFlow，本桥一个字都不碰）；把这一场遭遇标成「一仗」（EncounterStep.StartBattle），
//   打赢 / 被击倒后取走 BattleSettlement，用事件里带的三项身份回写 NarrativeService.CompleteBattleAsync。
//
// 为什么新建（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：`EncounterStep` 只承载结果（PRP §2.1「唯一写入方」），它不认识剧情服务；`BattleFlow` 认的是
//      BOSS 名册（payload → BossDefinition），空 payload 在名册里查不到，接线前它在那里记一条 Error 就不管了。
//   2. 扩展不行：把回写塞进 `EncounterStep.Step` 等于把剧情副作用放进确定性内核（那个类连埋点都是后置装配的）；
//      塞进 `BattleFlow` 会让 BOSS 战流程认识活遭遇档。所以按「消费方自己拉」的既有写法（`MonsterInstaller`
//      拉 `NarrativeConditionSource` 就是先例）在 Monster 侧新建一个桥：**Monster → Narrative 单向**，
//      Narrative 不反过来认识 Monster。
//
// ⚠️ 时序（本桥为什么是 ISimulationStep + ITickable 两个身份，别把其中一个当成多余的）：
//   「玩家生命归零」这条路上，结算与「结束本场遭遇」挨得极近。2026-10-09 逐行核实的事实：
//     · 结算发生在 `EncounterStep.Step` 里（同一 tick），写 `PendingResult = Defeat` + `SettleBattle(Downed)`；
//     · `MirrorCrackPresenter` 是渲染帧入口点，而 `HitCracks` 直接由 `PlayerModel.Health` 推出（`MirrorService`），
//       所以它**同一帧**就看到第三道裂痕并 `step.End()`。根作用域的入口点先注册、模块入口点后注册
//       （`GameLifetimeScope.Configure` 末尾才 InstallGameplay），所以那一下排在整段 tick 之后；
//     · `End()` 只置 `IsActive = false` 并清瞬时事实（`EncounterStep.cs:715-722`），**并不清** `PendingResult` /
//       `BattleSettlement` / `ResultConsumed`；真正抹掉它们的是下一场遭遇的 `Begin()`（镜碎页交回后重载场景，
//       `EncounterStep.cs:694-713`）与换了身份的 `StartBattle`。行号按 combat-iteration 之后的工作区版本核过
//       （2026-10-16；此前引的是 HEAD 版行号，升级那一段落地后就漂了）。
//   → 结论：**「End() 会抹掉结果」这个前提不成立**，但轮询式写法仍要同时满足两个隐式假设才安全
//     （① 读结果不能以 `IsActive` 为前提——镜碎之后它就是 false；② 必须赶在下一场 `Begin()` 之前），
//     两个都不是代码保证的。所以「取走结果」放进 `Step`，并且必须紧跟在 `EncounterStep` 之后注册
//     （`MonsterInstaller` 构建回调里的那两行 AddStep 就是那个顺序）：同一 tick 内结算、同一 tick 内取走，
//     之后谁 End / Begin 都不影响已经抄进本桥字段的那一份。
//   → 而「回写」是副作用（剧情分区 JSON 深拷贝、可能拉起对白、请求存档），**不放进确定性内核**：
//     留给 `Tick`（渲染帧）做。
//
// 已知边界（别当成漏了，都是刻意的）：
//   1. **不调 `TryBeginBattle`**：那会给阶段帧打上「战斗在途」，改变存档边界（战中不存档）。活遭遇是实时玩法，
//      存档由 Session 既有闸门管，本桥不替它改口径；代价见交付报告「残余风险」。
//   2. **`Aborted`（收服收尾）不回写**：它不是战斗结果（`BattleSettlement` 为 null），桥只消费掉它、解开冻结。
//   3. **被拒就是被拒**：阶段的 `battleResults` 没声明这个码时 `CompleteBattleAsync` 返回 false 并自己埋
//      `battle_result_rejected`——那是正常拒绝（例如阶段只声明 Victory 而玩家被击倒），桥不预判、不重试、不抛。
//   4. **不往 `EncounterStep` 里塞 targetId**：那会动快照字节布局。遭遇侧那两个编号只是为了让 `ConsumeResult`
//      对得上，剧情身份（generation / activationId / targetId）由本桥自己记着。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Narrative;
using MessagePipe;
using VContainer.Unity;

namespace Game.Monster
{
    /// <summary>
    /// 实时遭遇 → 剧情的结果桥。注册在 <c>MonsterInstaller</c>：① 作为 <see cref="ISimulationStep"/> 紧跟
    /// <see cref="EncounterStep"/>（取走结果，见文件头的时序说明）；② 作为 <see cref="ITickable"/> 回写剧情（副作用出内核）。
    /// <para>
    /// 没装 NarrativeInstaller 时不 <see cref="Bind"/>：本桥每帧只走一次布尔早退，不登记、不结算、不回写，
    /// 行为与接线前逐字一致（独立原型场景与纯 Monster 测试作用域走的正是这一支）。
    /// </para>
    /// </summary>
    public sealed class EncounterBattleBridge : ISimulationStep, ITickable, IDisposable
    {
        private readonly EncounterStep step;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private ITelemetryScope telemetry = NullTelemetryScope.Instance;

        // 剧情侧（可空）。Bind 之前整座桥不工作——「缺 Narrative 就合法降级」的落点。
        private NarrativeService narrative;
        private IDisposable subscription;

        // —— 已认领的一仗（身份三项来自 BattleStageEnteredEvent，不是 EncounterStep 的编号）——
        private bool armed;
        private long generation;
        private long activationId;
        private string targetId = string.Empty;
        private string stageId = string.Empty;

        // 「旧战斗尚未结算」只记一次 Warn：每 tick 刷会把日志淹掉。
        private bool linkRefused;

        // 本场的结算**已经交出去过**：同一次结算只许认领一次。
        // 为什么需要它：`BattleSettlement` 不进快照、而 `ConsumeResult` 只在「有终局结果可标记」时才置位
        // （`PendingResult == None` 时它返回 false），于是「已经停了但还没 Begin」的遭遇里那份结算会一直挂着；
        // 这时若又来一条同身份的阶段通知（重试 / 读档重发），没有这道闸就会把它**再交一遍**。
        // 清它的时机只有一个：重新登记这一场（`StartBattle` 那一处）——那代表 Begin 之后的新一场遭遇。
        private bool settlementTaken;

        // —— 已经从遭遇里取走、等着回写的结算（取走那一刻就抄进来，见文件头时序）——
        private bool hasSettlement;
        private BattleResult settlement;
        private long settlementGeneration;
        private long settlementActivationId;
        private string settlementTargetId = string.Empty;

        private bool disposed;

        public EncounterBattleBridge(EncounterStep step)
        {
            this.step = step ?? throw new ArgumentNullException(nameof(step));
        }

        /// <summary>
        /// 接入埋点（本波新增 `battle_linked` / `battle_result_forwarded`）。做成后置装配的理由同
        /// `EncounterStep.UseTelemetry`：本类的构造参数越少越好（`RegisterEntryPoint` 在容器构建期就解析它）。
        /// </summary>
        public void UseTelemetry(ITelemetryScope scope) => telemetry = scope ?? NullTelemetryScope.Instance;

        /// <summary>
        /// 接上剧情侧（`NarrativeService` + 事件订阅）。由 `MonsterInstaller` 的构建回调在
        /// `TryResolve` 到两者之后调用；**不调 = 本桥完全不工作**，遇结算照旧只承载不推进（与接线前逐字一致）。
        /// <para>
        /// 订阅句柄托管在 <see cref="Dispose"/> 里（EventConventions 第 5 条）。
        /// </para>
        /// </summary>
        public void Bind(NarrativeService narrativeService, ISubscriber<BattleStageEnteredEvent> battleStages)
        {
            if (disposed)
            {
                return;
            }

            narrative = narrativeService ?? throw new ArgumentNullException(nameof(narrativeService));
            if (battleStages == null)
            {
                throw new ArgumentNullException(nameof(battleStages));
            }

            if (subscription != null)
            {
                return;
            }

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            battleStages.Subscribe(HandleBattleStage).AddTo(bag);
            subscription = bag.Build();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            subscription?.Dispose();
            subscription = null;
            narrative = null;
            armed = false;
            hasSettlement = false;
        }

        // 回调在 NarrativeService.DriveAsync / ReloadFromSave 里同步执行：这里绝不许往外抛（同 BattleFlow.HandleStage 的纪律），
        // 也不做任何遭遇侧的动作——登记放在 Step 里，那才是有序（紧跟 EncounterStep）的那一处。
        private void HandleBattleStage(BattleStageEnteredEvent stage)
        {
            try
            {
                // 非空 payload = BOSS 战（roster 里查得到定义）：归 Game.Battle 的 BattleFlow，本桥不碰。
                if (!string.IsNullOrEmpty(stage.Payload))
                {
                    return;
                }

                armed = true;
                linkRefused = false;
                generation = stage.Generation;
                activationId = stage.ActivationId;
                targetId = stage.TargetId;
                stageId = stage.StageId;
            }
            catch (Exception e)
            {
                // 理论上到不了这里（只做赋值）；真到了也绝不能把剧情推进一起炸掉。
                Log.Error($"EncounterBattleBridge：处理战斗阶段通知失败（{stage.StageId}）：{e}");
                telemetry.TrackError("battle_stage_failed", e, TelemetryProps.Of(("stage", stage.StageId)));
            }
        }

        /// <summary>
        /// 每个逻辑 tick（紧跟 <see cref="EncounterStep"/> 之后）：登记这一仗 + 把已出的结果取走。
        /// 零分配、不做任何外部副作用（埋点只在登记 / 拒绝这两处触发，不是每 tick）。
        /// </summary>
        public void Step(in SimulationContext context)
        {
            if (disposed || !armed)
            {
                return;
            }

            // 场景还没活 / 已经停了。两种可能，别把它读成「还没轮到我」就完了：
            //   ① 通知先到、遭遇后到（读档：ReloadFromSave 先发布，场景跟着才 Begin）→ 等下一 tick，登记补得上；
            //   ② 这一场已经停了，但结果还挂在它名下——处决那条路是「渲染帧里 SettleBattle 先承载结果，
            //      EncounterStep.Step 还没轮到把 PendingResult 补上，遭遇就 End 了」。这时照样认它，
            //      但**只在 EncounterId 就是本场 activationId 时认**：那是「结果确实属于我登记的这一仗」的精确判据，
            //      不会把上一场遗留的结果错算到这一场头上。
            if (!step.IsActive)
            {
                if (!settlementTaken && step.EncounterId == activationId && step.BattleSettlement.HasValue
                    && !step.ResultConsumed)
                {
                    TakeSettlement();
                }

                return;
            }

            if (step.EncounterId != activationId)
            {
                // 上一场还挂着没被消费的结果（不是本桥登记的，或进程里别的调用方先登了记）：StartBattle 会抛
                // 「旧战斗尚未结算」。本 tick 不登记，记一次 Warn 就够。
                if (step.EncounterId != 0 && !step.ResultConsumed)
                {
                    if (!linkRefused)
                    {
                        linkRefused = true;
                        Log.Warn($"EncounterBattleBridge：遭遇里还挂着未结算的旧战斗（encounter={step.EncounterId}，"
                                 + $"本场 activation={activationId}），这一场不登记，等旧结果被消费。");
                        telemetry.TrackWarn("battle_link_refused",
                            TelemetryProps.Of(("stage", stageId), ("activation", activationId)));
                    }

                    return;
                }

                // 登记这一场是「一仗」：EncounterStep 之后才会在自己的 Step 里结算胜负。
                // 两个参数都给 activationId：遭遇侧那两个编号只服务于 ConsumeResult 的对账，剧情身份在桥自己身上。
                // 「已经交出去过」的标记在这里清：走到这一行说明遭遇是在 Begin 之后重新登记的（EncounterId 归零过），
                // 也就是新的一场遭遇，允许再认领一次结果。
                step.StartBattle(activationId, activationId);
                settlementTaken = false;
                linkRefused = false;
                telemetry.Track("battle_linked", ("stage", stageId), ("activation", activationId));
            }

            if (step.PendingResult == EncounterStep.Result.None)
            {
                return; // 还在打
            }

            if (step.ResultConsumed || settlementTaken)
            {
                armed = false; // 这个结果已经有人拿走了（或本桥已经交过一遍）：这一场不再管
                return;
            }

            TakeSettlement();
        }

        /// <summary>
        /// 把结果抄进本桥自己的字段，再消费掉它。
        /// <para>
        /// <b>先抄再消费、就在结算那一处抄</b>：<see cref="EncounterStep.Begin"/>（下一场遭遇）与换了身份的
        /// <c>StartBattle</c> 会把 <c>PendingResult</c> / <c>BattleSettlement</c> 一起抹掉，而回写要等到渲染帧的
        /// <see cref="Tick"/>。抄进本桥自己的字段之后，谁抹都不影响这一份（<c>End()</c> 本身不抹它们，见
        /// <see cref="EncounterStep"/> 的 <c>BattleSettlement</c> 注释）。
        /// </para>
        /// <para>
        /// 消费 = 「这个结果我拿走了」：解开 <see cref="EncounterStep"/> 的冻结（待消费期间双方规则都不推进），
        /// 也挡住下一次误登记。`Aborted` 那一支没有 BattleSettlement，消费完就结束，不回写。
        /// </para>
        /// <para>
        /// <b>`ConsumeResult` 不保证置位</b>：它的契约是「有终局结果可标记」才把 <c>ResultConsumed</c> 置真，
        /// 而上面那个「遭遇已停、结果只挂在 BattleSettlement 上」的分支里 <c>PendingResult</c> 还是 None，
        /// 于是它返回 false——**这不代表结果没取到**，结果此刻已经抄进本桥自己的字段了。
        /// 真正防「同一份结算交两遍」的是 <see cref="settlementTaken"/>。
        /// </para>
        /// </summary>
        private void TakeSettlement()
        {
            if (step.BattleSettlement.HasValue)
            {
                settlement = step.BattleSettlement.Value;
                settlementGeneration = generation;
                settlementActivationId = activationId;
                settlementTargetId = targetId;
                hasSettlement = true;
            }

            step.ConsumeResult(activationId, activationId);
            settlementTaken = true;
            armed = false;
        }

        /// <summary>
        /// 每个渲染帧：把已取走的结算交给剧情。**副作用不进确定性内核**——剧情分区深拷贝、可能拉起对白、
        /// 请求存档都发生在这一支里；结果本身早在 <see cref="Step"/> 里就抄好了，这里晚一帧也不会丢。
        /// </summary>
        public void Tick()
        {
            if (disposed || !hasSettlement)
            {
                return;
            }

            BattleResult result = settlement;
            long writeGeneration = settlementGeneration;
            long writeActivation = settlementActivationId;
            string writeTarget = settlementTargetId;
            hasSettlement = false;
            ForwardAsync(writeGeneration, writeActivation, writeTarget, result).Forget();
        }

        private async UniTaskVoid ForwardAsync(long writeGeneration, long writeActivation, string writeTarget, BattleResult result)
        {
            try
            {
                // 身份三项用事件里带的值原样带回；被拒（旧代数 / 阶段没声明这个码）由剧情侧自己埋点，
                // 这里只如实记一条转发结果，不当异常、不重试。
                bool accepted = await narrative.CompleteBattleAsync(writeGeneration, writeActivation, writeTarget,
                    result.ExitKey, lifetime.Token);
                telemetry.Track("battle_result_forwarded",
                    ("result", result.ExitKey), ("activation", writeActivation), ("accepted", accepted));
            }
            catch (OperationCanceledException)
            {
                // 作用域销毁 / 读档取消：静默（同 Game.Battle 的取消口径）。
            }
            catch (Exception e)
            {
                // 回写路径上的异常不许逃逸（未观察的 UniTask 异常会随机砸中别的用例 / 帧）：记下来，阶段原地不动。
                Log.Error($"EncounterBattleBridge：战斗结果 {result.ExitKey} 回写剧情失败（激活号 {writeActivation}）：{e}");
                telemetry.TrackError("battle_result_forward_failed", e,
                    TelemetryProps.Of(("result", result.ExitKey), ("activation", writeActivation)));
            }
        }
    }
}

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
    /// </summary>
    public sealed class BattleFlow : IStartable, IDisposable
    {
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
            if (active != null)
            {
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

        private async UniTaskVoid RunAsync(Run run)
        {
            CancellationToken ct = run.Cancel.Token;
            bool completed = false;
            try
            {
                await gate.WaitUntilReadyAsync(ct);
                run.Engaged = true;
                string exitKey = null;
                IDisposable engagement = worldLock.Engage(this);
                try
                {
                    if (!setup.TryCreate(run.Boss, out BattleSession session, out string error))
                        throw new InvalidOperationException($"「{run.Boss.DisplayName}」开不了仗：{error}");
                    telemetry.Track("battle_started", ("boss", run.Boss.Id), ("activation", run.Stage.ActivationId));
                    // 回放测试口换过数值（BattleSetup.OverrideSettings）：这一仗用的不是配置资产的数值，留 W 级痕迹（只会出现在编辑器 / 开发版）。
                    if (setup.IsOverridden)
                        telemetry.TrackWarn("settings_overridden", TelemetryProps.Of(("boss", run.Boss.Id), ("activation", run.Stage.ActivationId)));
                    // 开场失败时 EnterAsync 在黑幕下先跑 engagement.Dispose（恢复世界）再揭幕，与正常收场同序。
                    await arena.EnterAsync(presenter, run.Boss, session, engagement.Dispose, ct);
                    try
                    {
                        exitKey = await FightAsync(session, ct);
                        telemetry.Track("battle_finished", ("boss", run.Boss.Id), ("result", exitKey));
                    }
                    finally
                    {
                        // 黑幕盖着时（PRP D6）：关界面、卸场景、恢复相机 / 输入 / 暂停 → 剧情回写 → 揭幕。
                        // 先恢复世界再回写：战后若接对白，对白看到的是正常世界，不会把「战斗中」的输入 / HUD 状态当成进来前的状态记下。
                        // 没打完（异常 / 取消，exitKey 为空）不回写。
                        await arena.ExitAsync(presenter, engagement.Dispose,
                            exitKey == null ? (Func<UniTask>)null : () => WriteBackUnderCurtainAsync(run, exitKey, ct));
                    }
                }
                finally
                {
                    engagement.Dispose(); // 幂等：正常收场与开场失败都已在黑幕下还原过，这里兜没碰黑幕的路径（开仗被拒等）
                }

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

        /// <summary>一场仗的流程句柄：身份、BOSS、取消源、是否已越过门闸。</summary>
        private sealed class Run
        {
            public Run(StageEvent stage, BossDefinition boss)
            {
                Stage = stage;
                Boss = boss;
            }

            public StageEvent Stage { get; }
            public BossDefinition Boss { get; }
            public CancellationTokenSource Cancel { get; } = new CancellationTokenSource();
            public bool Engaged { get; set; }

            /// <summary>剧情回写（打完才有；没打完为 null）。可 await 多次。</summary>
            public UniTaskCompletionSource<bool> WriteBack { get; set; }
        }
    }
}

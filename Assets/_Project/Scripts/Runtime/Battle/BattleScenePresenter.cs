// 职责：IBattlePresenter 的真实实现（PRP/turnbased-battle W2a）——
//   开场：在叠加加载的 BattleArena 里找 BattleStage、摆好玩家与 BOSS 外观、接管相机（关世界相机、开战斗相机）、开 BattleView；
//   播放：把 BattleEvent 逐条翻成演出（BattleCueRules），交给舞台（冲上去 / 命中 / 退回 / 饮酒 / 醉晃 / 倒地）与界面（掉血、飘字、中央提示）；
//     头顶条与飘字锚在两人站立时的头顶（BattleActor.StandingHeadPosition），不跟动作走（理由见 BattleView 文件头）；
//   等指令：鼠标点格子；键盘 1 / 2 / 3 出招；方向键 + 回车在格子间导航（UI 图，EventSystem 原生导航）；
//   收场：容错交还一切（关界面、交还相机、恢复键位图），可重入。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：IBattlePresenter 是 W1 留的接缝，没有实现；PerformanceService 管时间轴演出，不认识战斗事件与指令。
//   2. 扩展不行：塞进 BattleFlow 会让流程依赖场景与 UI，EditMode 就测不了流程（W1 拆接缝的本意）。
// 键盘出招复用 GameInput 的 Dialogue 图 Choice1–3（键盘 1 / 2 / 3）：GameInput.inputactions 本波禁改，
//   PerformanceService 已有「复用 Dialogue 图、键位语义一致」的先例（「第几个」）；只在战斗期间启用，收场只恢复进来前的状态。
// 暂停菜单：不改 PauseMenuController（Core 不能认识 Game.Battle）。P 键走 Gameplay 图，战斗期间被 BattleWorldLock 关掉；
//   Esc 走 UI 图，栈顶是 BattleView（CloseOnCancel = false）→ UICancelRouter 判 Blocked，不抛「没东西可关」；进出场时黑幕盖着，
//   PauseMenuController 的 curtain.IsCovered 条件挡住。三段时间无缝衔接（见 BattleFlow / BattleArena 的时序）。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.TurnBased;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Game.Battle
{
    /// <summary>战斗场景表现（根作用域单例，BattleInstaller 注册为 <see cref="IBattlePresenter"/>）。一次只开一场。</summary>
    public sealed class BattleScenePresenter : IBattlePresenter
    {
        /// <summary>键盘出招借用的动作图（Choice1–3 = 键盘 1 / 2 / 3）。</summary>
        private const string KeyMap = "Dialogue";

        /// <summary>中央提示停留（秒，不受时间缩放）：BOSS 跳过回合 / 晕眩跳过 / 回合用尽。</summary>
        private const float HintHoldSeconds = 1.1f;

        /// <summary>指令被规则拒绝时的提示停留。</summary>
        private const float RejectionHoldSeconds = 0.8f;

        /// <summary>胜负大字淡入后停多久再交还流程（随后流程落幕收场，大字一直留到黑幕盖住）。</summary>
        private const float ResultHoldSeconds = 0.9f;

        /// <summary>晕眩标记出现后的停顿。</summary>
        private const float StunPauseSeconds = 0.4f;

        private readonly IUIService ui;
        private readonly IInputService input;
        private readonly BattleSettings settings;
        private readonly ITelemetryScope telemetry;
        private readonly BattleCameraHandoff cameras = new BattleCameraHandoff();
        private readonly List<Camera> cameraBuffer = new List<Camera>();
        private readonly List<BattleItemSlot> items = new List<BattleItemSlot>();

        private BattleStage stage;
        private BattleView view;
        private BossDefinition boss;
        private UniTaskCompletionSource<BattleCommand> pending;
        private GameInput.DialogueActions keyActions;
        private bool keysHooked;
        private bool keyMapWasEnabled;
        private int round = 1;
        private bool showingPlayerTurn = true;
        private bool opened;
        private bool closing;

        public BattleScenePresenter(IUIService ui, IInputService input, in BattleSettings settings, ITelemetryScope telemetry)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.settings = settings;
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>战斗舞台（开场后有值，收场后为 null；W2b 回放读）。</summary>
        public BattleStage Stage => stage;

        /// <summary>战斗界面（同上）。</summary>
        public BattleView View => view;

        /// <summary>正在等玩家指令。</summary>
        public bool IsAwaitingCommand => pending != null;

        public async UniTask OpenAsync(BattleOpening opening, CancellationToken ct)
        {
            if (opened) throw new InvalidOperationException("BattleScenePresenter：上一场还没有 CloseAsync");
            opened = true; // 从这里起失败也要走 CloseAsync（IBattlePresenter 契约，BattleArena 会调）
            boss = opening.Boss ?? throw new ArgumentException("开场缺 BOSS 定义", nameof(opening));
            BattleSession session = opening.Session ?? throw new ArgumentException("开场缺战斗会话", nameof(opening));

            stage = FindStage(opening.Arena);
            stage.Prepare(boss.StagePrefab);
            TakeCameras(opening.Arena);

            view = await ui.OpenAsync<BattleView>(null, ct);
            view.OnSkillChosen += HandleSkillChosen;
            view.OnItemChosen += HandleItemChosen;
            view.OnEndTurn += HandleEndTurn;
            round = session.CompletedRounds + 1;
            items.Clear();
            view.Configure(boss.DisplayName, session.Player.RageMax, stage.Camera, stage.Player.StandingHeadPosition, stage.Boss.StandingHeadPosition);
            showingPlayerTurn = session.Phase == BattlePhase.PlayerTurn;
            view.Render(session, items, settings, false, BattleHudRules.TurnText(session), showingPlayerTurn, false);
            HookKeys();
            telemetry.Track("presenter_opened", ("boss", boss.Id), ("cameras_off", cameras.SwitchedOffCount));
        }

        public async UniTask PlayAsync(BattleSession session, IReadOnlyList<BattleEvent> events, CancellationToken ct)
        {
            RequireOpen();
            if (events != null)
            {
                round = BattleHudRules.PlayedRound(session, IsBossBatch(events));
                for (int i = 0; i < events.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await PlayCueAsync(session, BattleCueRules.Cue(events[i]), ct);
                }
            }

            showingPlayerTurn = session.Phase == BattlePhase.PlayerTurn;
            if (view != null) view.Render(session, items, settings, false, BattleHudRules.TurnText(session), showingPlayerTurn, true);
        }

        public async UniTask<BattleCommand> WaitCommandAsync(BattleCommandMenu menu, CancellationToken ct)
        {
            RequireOpen();
            BattleSession session = menu.Session;
            CopyItems(menu.Items);
            round = session.CompletedRounds + 1;
            string turn = BattleHudRules.TurnText(session);
            showingPlayerTurn = true;
            view.Render(session, items, settings, true, turn, true, true);
            if (!string.IsNullOrEmpty(menu.Rejection))
                view.FlashHintAsync(menu.Rejection, RejectionHoldSeconds, ct).SuppressCancellationThrow().Forget();

            var source = new UniTaskCompletionSource<BattleCommand>();
            pending = source;
            CancellationTokenRegistration registration = ct.Register(() => source.TrySetCanceled(ct));
            try
            {
                return await source.Task;
            }
            finally
            {
                registration.Dispose();
                if (pending == source) pending = null;
                // 指令一给出就把格子都锁住，防连点；流程接着会 PlayAsync 或再问一次。
                if (view != null) view.Render(session, items, settings, false, turn, true, false);
            }
        }

        public async UniTask CloseAsync(CancellationToken ct)
        {
            if (!opened || closing) return;
            closing = true;
            try
            {
                pending?.TrySetCanceled();
                pending = null;
                UnhookKeys();
                BattleView target = view;
                view = null;
                if (target != null)
                {
                    target.OnSkillChosen -= HandleSkillChosen;
                    target.OnItemChosen -= HandleItemChosen;
                    target.OnEndTurn -= HandleEndTurn;
                    try
                    {
                        await ui.CloseAsync(target, CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"BattleScenePresenter：关闭战斗界面失败（相机照样交还）：{e}");
                        telemetry.TrackError("view_close_failed", e);
                    }
                }
            }
            finally
            {
                // 先交还相机再让 BattleArena 卸场景：卸载时 FallbackCamera 要看到世界相机已经开着，才不会抢回画面。
                cameras.Release();
                stage = null;
                boss = null;
                items.Clear();
                opened = false;
                closing = false;
                telemetry.Track("presenter_closed");
            }
        }

        // ── 播放 ──────────────────────────────────────────────────────────────

        private async UniTask PlayCueAsync(BattleSession session, BattleCue cue, CancellationToken ct)
        {
            BattleActor player = stage.Player;
            BattleActor foe = stage.Boss;
            switch (cue.Kind)
            {
                case BattleCueKind.SneakHit:
                    await stage.HitInPlaceAsync(foe, false, () => Impact(session, foe, cue.Amount, cue.Heavy), ct);
                    break;

                case BattleCueKind.PlayerStrike:
                    SetTurn(true);
                    await stage.StrikeAsync(player, foe, cue.Heavy, () => Impact(session, foe, cue.Amount, cue.Heavy), ct);
                    break;

                case BattleCueKind.ItemUse:
                    SetTurn(true);
                    Refresh(session);
                    view.ShowFloat(player.StandingHeadPosition, "使用 " + ItemName(cue.ItemId), BattleFloatTone.Info, false, 0);
                    if (cue.Amount > 0) view.ShowFloat(player.StandingHeadPosition, "+" + cue.Amount, BattleFloatTone.Heal, true, 1);
                    await stage.HopAsync(player, ct);
                    break;

                case BattleCueKind.BossSkip:
                    SetTurn(false);
                    Refresh(session); // 酩酊进场的醉酒值 -50 在这一刻才看得到
                    await UniTask.WhenAll(view.FlashHintAsync(cue.Text, HintHoldSeconds, ct), stage.SwayAsync(foe, ct));
                    break;

                case BattleCueKind.BossStrike:
                    SetTurn(false);
                    await stage.StrikeAsync(foe, player, cue.Heavy, () => Impact(session, player, cue.Amount, cue.Heavy), ct);
                    break;

                case BattleCueKind.BossDrink:
                    SetTurn(false);
                    Refresh(session); // 醉酒条上涨、回血
                    view.ShowFloat(foe.StandingHeadPosition, cue.Text, BattleFloatTone.Info, true, 0);
                    if (cue.SecondaryAmount > 0) view.ShowFloat(foe.StandingHeadPosition, "+" + cue.SecondaryAmount, BattleFloatTone.Heal, false, 1);
                    await stage.HopAsync(foe, ct);
                    break;

                case BattleCueKind.PlayerStun:
                    Refresh(session); // 晕眩标记与状态格出现
                    view.ShowFloat(player.StandingHeadPosition, cue.Text, BattleFloatTone.Info, true, 1);
                    await UniTask.Delay(TimeSpan.FromSeconds(StunPauseSeconds), true, PlayerLoopTiming.Update, ct);
                    break;

                case BattleCueKind.PlayerStunSkip:
                    SetTurn(true);
                    await UniTask.WhenAll(view.FlashHintAsync(cue.Text, HintHoldSeconds, ct), stage.SwayAsync(player, ct));
                    break;

                // 胜负三种结果大字留在屏上直到落幕收场（ShowResultAsync 不淡出），不像回合中的提示那样闪一下就走。
                case BattleCueKind.PlayerDown:
                    await stage.FallAsync(player, ct);
                    await view.ShowResultAsync(cue.Text, ResultHoldSeconds, ct);
                    break;

                case BattleCueKind.BossDown:
                    await stage.FallAsync(foe, ct);
                    await view.ShowResultAsync(cue.Text, ResultHoldSeconds, ct);
                    break;

                case BattleCueKind.RoundLimit:
                    await view.ShowResultAsync(cue.Text, HintHoldSeconds, ct);
                    break;
            }
        }

        // 命中瞬间：会话里的血量已是结算后的值（事件是动作之后才摆出来的），这一刻才把条刷到位并飘伤害字——
        //   出招途中不刷条，免得人还没冲到血先掉了。
        private void Impact(BattleSession session, BattleActor target, int damage, bool heavy)
        {
            Refresh(session);
            view.ShowFloat(target.StandingHeadPosition, "-" + damage, BattleFloatTone.Damage, heavy, 0);
        }

        // 播放中刷新：格子一律不可点，回合提示与招式 / 道具格的亮暗保持当前演出的归属（会话里的阶段已经走到下一步了）。
        private void Refresh(BattleSession session)
        {
            if (view != null) view.Render(session, items, settings, false, view.TurnText, showingPlayerTurn, true);
        }

        private void SetTurn(bool playerTurn)
        {
            showingPlayerTurn = playerTurn;
            if (view != null) view.SetTurnText(BattleHudRules.TurnText(round, playerTurn));
        }

        private static bool IsBossBatch(IReadOnlyList<BattleEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                BattleEventKind kind = events[i].Kind;
                if (kind == BattleEventKind.BossSkillUsed || kind == BattleEventKind.BossDrank || kind == BattleEventKind.BossTurnSkipped) return true;
            }

            return false;
        }

        private string ItemName(string itemId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].ItemId, itemId, StringComparison.Ordinal)) return items[i].DisplayName;
            }

            return "道具";
        }

        // ── 指令 ──────────────────────────────────────────────────────────────

        private void Submit(BattleCommand command, string source)
        {
            UniTaskCompletionSource<BattleCommand> target = pending;
            if (target == null) return; // 不在等指令（演出中按键 / 连点）：丢弃
            telemetry.Track("command_chosen", ("kind", command.Kind.ToString()), ("source", source));
            target.TrySetResult(command);
        }

        private void HandleSkillChosen(PlayerSkill skill) => Submit(BattleCommand.CastSkill(skill), "click");

        private void HandleItemChosen(string itemId) => Submit(BattleCommand.UseItem(itemId), "click");

        private void HandleEndTurn() => Submit(BattleCommand.EndTurn(), "click");

        private void HandleKey1(InputAction.CallbackContext context) => Submit(BattleCommand.CastSkill(PlayerSkill.Skill1), "key");

        private void HandleKey2(InputAction.CallbackContext context) => Submit(BattleCommand.CastSkill(PlayerSkill.Skill2), "key");

        private void HandleKey3(InputAction.CallbackContext context) => Submit(BattleCommand.CastSkill(PlayerSkill.Skill3), "key");

        private void HookKeys()
        {
            GameInput actions = input.Actions; // lint-ok: 战斗表现层读动作，不进确定性模拟
            if (actions == null)
            {
                Log.Warn("BattleScenePresenter：输入服务还没有动作集，键盘 1 / 2 / 3 出招不可用（鼠标点击照常）。");
                return;
            }

            keyActions = actions.Dialogue;
            keyMapWasEnabled = keyActions.enabled;
            if (!keyMapWasEnabled) input.EnableMap(KeyMap);
            keyActions.Choice1.performed += HandleKey1;
            keyActions.Choice2.performed += HandleKey2;
            keyActions.Choice3.performed += HandleKey3;
            keysHooked = true;
        }

        private void UnhookKeys()
        {
            if (!keysHooked) return;
            keysHooked = false;
            try
            {
                keyActions.Choice1.performed -= HandleKey1;
                keyActions.Choice2.performed -= HandleKey2;
                keyActions.Choice3.performed -= HandleKey3;
            }
            finally
            {
                // 只恢复进来前的状态：进来前就开着（不该发生，但不擅自关）就不动。
                if (!keyMapWasEnabled) input.DisableMap(KeyMap);
            }
        }

        // ── 开场辅助 ──────────────────────────────────────────────────────────

        private static BattleStage FindStage(Scene arena)
        {
            if (!arena.IsValid() || !arena.isLoaded)
                throw new InvalidOperationException($"战斗场景「{BattleArena.SceneKey}」没有加载成功，找不到舞台");
            foreach (GameObject root in arena.GetRootGameObjects())
            {
                BattleStage found = root.GetComponentInChildren<BattleStage>(true);
                if (found != null) return found;
            }

            throw new InvalidOperationException(
                $"战斗场景「{BattleArena.SceneKey}」里找不到 BattleStage。修法：用菜单 21Days/战斗/重建战斗白盒 重建 Assets/_Project/Scenes/BattleArena.unity。");
        }

        // 世界里正在渲染的相机（不在战斗场景里的）全部让位；底色从世界主相机拷一份，画面与探索一致。
        private void TakeCameras(Scene arena)
        {
            Camera world = Camera.main;
            cameraBuffer.Clear();
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera != stage.Camera && camera.gameObject.scene != arena) cameraBuffer.Add(camera);
            }

            BattleCameraHandoff.CopyLook(world, stage.Camera);
            cameras.Take(stage.Camera, cameraBuffer);
            cameraBuffer.Clear();
        }

        private void CopyItems(IReadOnlyList<BattleItemSlot> source)
        {
            items.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++) items.Add(source[i]); // 值拷贝：菜单列表只在本次调用内有效
        }

        private void RequireOpen()
        {
            if (!opened || view == null || stage == null)
                throw new InvalidOperationException("BattleScenePresenter：还没开场（或已收场）就被要求播放 / 等指令");
        }
    }
}

// 职责：标题页三个入口的去向——「开始」用第一个空槽开新游戏（进游戏前先过一遍开局操作说明；没有空槽开选槽面板的新游戏模式）、
//   「继续」读最近槽（没有则按新游戏处理）、「选择存档」开选槽面板的读档模式；回到标题时按有无可用存档显示 / 隐藏「继续」。
// 为什么新建：Core 的 TitleState 只把点击转成框架事件，去向由玩法层决定（TitleState 文件头）。原先的 MonsterTitleRouter
//   直接跳遭遇状态、不知道存档槽，按 PRP save-session D5 退役；这里归存档会话，Monster 不再管标题。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Narrative;
using MessagePipe;
using VContainer.Unity;
using UnityEngine;

namespace Game.Session
{
    /// <summary>
    /// 标题路由（根作用域入口点）。同一时刻只处理一个标题操作：连点「开始」不会开两次新游戏。
    /// 纯判定在 <see cref="SessionTitleRules"/>。
    /// </summary>
    public sealed class SessionTitleRouter : IStartable, IDisposable
    {
        private readonly GameSession session;
        private readonly SaveSlotsController slots;
        private readonly NewGameTutorial tutorial;
        private readonly IUIService ui;
        private readonly ISubscriber<TitleStartClickedEvent> startClicked;
        private readonly ISubscriber<TitleContinueClickedEvent> continueClicked;
        private readonly ISubscriber<TitleLoadClickedEvent> loadClicked;
        private readonly ISubscriber<GameStateChangedEvent> stateChanged;
        private readonly ITelemetryScope telemetry;
        private readonly ISaveService saves;
        private readonly SessionConfig config;
        private readonly ISubscriber<NarrativeChangedEvent> narrativeChanged;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private IDisposable subscriptions;
        private bool routing;
        private bool disposed;
        private int appearanceRequest;

        public SessionTitleRouter(
            GameSession session,
            SaveSlotsController slots,
            NewGameTutorial tutorial,
            IUIService ui,
            ISubscriber<TitleStartClickedEvent> startClicked,
            ISubscriber<TitleContinueClickedEvent> continueClicked,
            ISubscriber<TitleLoadClickedEvent> loadClicked,
            ISubscriber<GameStateChangedEvent> stateChanged,
            ITelemetryScope telemetry,
            ISaveService saves,
            SessionConfig config,
            ISubscriber<NarrativeChangedEvent> narrativeChanged)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.slots = slots ?? throw new ArgumentNullException(nameof(slots));
            this.tutorial = tutorial ?? throw new ArgumentNullException(nameof(tutorial));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.startClicked = startClicked ?? throw new ArgumentNullException(nameof(startClicked));
            this.continueClicked = continueClicked ?? throw new ArgumentNullException(nameof(continueClicked));
            this.loadClicked = loadClicked ?? throw new ArgumentNullException(nameof(loadClicked));
            this.stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.narrativeChanged = narrativeChanged;
        }

        public void Start()
        {
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            startClicked.Subscribe(_ => RouteAsync(TitleAction.NewGame).Forget()).AddTo(bag);
            continueClicked.Subscribe(_ => RouteAsync(TitleAction.Continue).Forget()).AddTo(bag);
            loadClicked.Subscribe(_ => RouteAsync(TitleAction.Load).Forget()).AddTo(bag);
            stateChanged.Subscribe(HandleStateChanged).AddTo(bag);
            if (narrativeChanged != null)
                narrativeChanged.Subscribe(_ => RefreshTitle()).AddTo(bag);
            slots.OnSlotsChanged += RefreshTitle;
            subscriptions = bag.Build();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            slots.OnSlotsChanged -= RefreshTitle;
            subscriptions?.Dispose();
            subscriptions = null;
            lifetime.Cancel();
            lifetime.Dispose();
        }

        private void HandleStateChanged(GameStateChangedEvent e)
        {
            if (e.To == typeof(TitleState)) RefreshTitle();
        }

        private void RefreshTitle()
        {
            if (disposed) return;
            RefreshTitleNextFrameAsync(++appearanceRequest, lifetime.Token).Forget();
        }

        // 下一帧再设：TitleState.EnterAsync 刚开完面板，等本帧的开面板收尾（默认选中等）走完再改按钮状态。
        private async UniTaskVoid RefreshTitleNextFrameAsync(int request, CancellationToken ct)
        {
            TitleView view = null;
            try
            {
                await UniTask.NextFrame(ct);
                if (request != appearanceRequest) return;
                view = ui.Get<TitleView>();
                if (view == null) return;
                view.SetContinueVisible(SessionTitleRules.ShouldShowContinue(session.LatestSlot));

                bool yin = false;
                if (!string.IsNullOrWhiteSpace(config.TitleYinStoryFlag))
                {
                    NarrativeSaveData narrative = null;
                    if (session.CurrentSlot > 0)
                    {
                        narrative = saves.Get<NarrativeSaveData>();
                    }
                    else if (session.LatestSlot > 0)
                    {
                        // 冷启动只读候选；查看标题不能提交存档或覆盖当前内存分区。
                        using (telemetry.BeginSpan("title_appearance_read"))
                        {
                            SaveSnapshot candidate = await saves.ReadCandidateAsync(session.LatestSlot, ct);
                            if (candidate != null && candidate.Contains<SessionSaveData>() && candidate.Contains<NarrativeSaveData>())
                                narrative = candidate.Require<NarrativeSaveData>();
                        }
                    }

                    yin = SessionTitleRules.ShouldUseYinAppearance(narrative == null ? null : narrative.StoryFlags,
                        config.TitleYinStoryFlag);
                }

                ct.ThrowIfCancellationRequested();
                // 较早的读盘结果不能覆盖后来的换档 / 删除结果，也不能操作已关闭的面板。
                if (request != appearanceRequest || view == null || ui.Get<TitleView>() != view) return;
                if (!view.SetAlternateAppearance(yin))
                    view.SetAppearanceTint(yin ? config.TitleYinTint : Color.white);
                telemetry.Track("title_appearance", ("yin", yin));
            }
            catch (OperationCanceledException)
            {
                // 作用域销毁时不再改变面板。
            }
            catch (Exception e)
            {
                if (!disposed && request == appearanceRequest && view != null)
                {
                    view.SetAlternateAppearance(false);
                    view.SetAppearanceTint(Color.white);
                }
                telemetry.TrackError("title_appearance_failed", e);
                Log.Warn($"读取标题主题失败，使用阳版：{e.Message}");
            }
        }

        private async UniTaskVoid RouteAsync(TitleAction action)
        {
            if (disposed || routing) return;
            routing = true;
            CancellationToken ct = lifetime.Token;
            try
            {
                switch (action)
                {
                    case TitleAction.Continue:
                        await ContinueAsync(ct);
                        break;
                    case TitleAction.Load:
                        telemetry.Track("title_load");
                        await slots.OpenAsync(SlotsMode.Load, ct);
                        break;
                    default:
                        await StartNewGameAsync(ct);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // 作用域销毁时的取消，不是错误。
            }
            catch (Exception e)
            {
                telemetry.TrackError("title_route_failed", e, TelemetryProps.Of(("action", action.ToString())));
                Log.Error($"SessionTitleRouter：处理标题「{action}」失败：{e}");
            }
            finally
            {
                routing = false;
            }
        }

        // 「开始」：第一个空槽直接开局；没有空槽开选槽面板让玩家选覆盖哪个（覆盖确认在面板里做）。
        private async UniTask StartNewGameAsync(CancellationToken ct)
        {
            IReadOnlyList<SlotInfo> infos = await session.ReadSlotInfosAsync(ct);
            int slot = SessionTitleRules.PickNewGameSlot(infos);
            telemetry.Track("title_new_game", ("slot", slot));
            if (slot == 0)
            {
                await slots.OpenAsync(SlotsMode.NewGame, ct);
                return;
            }

            // 先看操作说明再进游戏；面板开不出来时 ShowAsync 自己吞掉错误，不会把开局挡在这里。
            await tutorial.ShowAsync(ct);
            await session.NewGameAsync(slot, ct);
        }

        // 「继续」：读最近槽；没有可用存档（按钮本应隐藏，键盘 / 旧状态绕过时）按「开始」处理。
        // 读失败时 GameSession 已发「存档不可用」通知并保持标题，这里只留痕。
        private async UniTask ContinueAsync(CancellationToken ct)
        {
            int latest = session.LatestSlot;
            if (!SessionTitleRules.ShouldShowContinue(latest))
            {
                telemetry.Track("title_continue", ("slot", 0), ("fallback", true));
                await StartNewGameAsync(ct);
                return;
            }

            bool ok = await session.ContinueAsync(latest, ct);
            telemetry.Track("title_continue", ("slot", latest), ("success", ok));
        }

        private enum TitleAction
        {
            NewGame,
            Continue,
            Load,
        }
    }
}

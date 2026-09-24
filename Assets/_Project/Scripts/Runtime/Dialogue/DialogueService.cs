// 职责：对白的对外入口——按 id 拉起一段对白：查内容、持世界暂停、关 Gameplay 输入图、交给 Controller 展示、收尾并广播事件。
// 为什么新建：DialogueRules 只管推进语义、DialogueController 只管表现，二者都不该持有「世界暂停 / 输入图 / 重入保护」
//   这类会话级资源；把它们塞进 Controller 会让表现层依赖 Core 的暂停与输入服务，也没法被其他模块当成一行调用。
// 事件为什么不用 MessagePipe：broker 注册要根作用域的 MessagePipeOptions，而 GameplayInstaller.Install 只拿到
//   IContainerBuilder、拿不到 GameLifetimeScope 里 RegisterMessagePipe() 返回的 options（重复 RegisterMessagePipe 会冲突），
//   所以按 PRP 3.4 的二选一改用 C# event 暴露同名事件（OnStarted / OnChoiceSelected / OnEnded），不做两套。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Telemetry;
using Game.Core.Timing;

namespace Game.Dialogue
{
    /// <summary>
    /// 对白服务。其他模块直接 <c>await PlayAsync(id)</c> 即可；场景物体走 <see cref="DialogueInteractable"/>。
    /// <para>同一时刻只允许一段对白；对白期间世界暂停（timeScale = 0），表现层一律用 unscaled 时间。</para>
    /// </summary>
    public sealed class DialogueService : IDisposable
    {
        private const string TargetPrefix = "dialogue:";

        private readonly DialogueCatalog catalog;
        private readonly DialogueRules rules;
        private readonly DialogueController controller;
        private readonly DialogueConfig config;
        private readonly IDialogueConditionSource conditions;
        private readonly IWorldPauseService worldPause;
        private readonly IInputService input;
        private readonly ITelemetryScope telemetry;
        private DialoguePlaybackPolicy policy;
        private int currentId;
        private bool running;

        public DialogueService(DialogueCatalog catalog, DialogueRules rules, DialogueController controller,
            DialogueConfig config, IDialogueConditionSource conditions, IWorldPauseService worldPause,
            IInputService input, ITelemetryScope telemetry)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            this.worldPause = worldPause ?? throw new ArgumentNullException(nameof(worldPause));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            rules.OnChoiceSelected += ForwardChoice;
        }

        /// <summary>是否有对白正在进行（含打开面板、展示、收尾）。</summary>
        public bool IsRunning => running;

        /// <summary>对白开始（已暂停世界、已关 Gameplay 输入图之后）。</summary>
        public event Action<DialogueStartedEvent> OnStarted;
        /// <summary>玩家选定一个选项。</summary>
        public event Action<DialogueChoiceSelectedEvent> OnChoiceSelected;
        /// <summary>对白结束（成功、取消、失败都会触发；非成功时 Outcome 为空）。已恢复输入图、已释放暂停。</summary>
        public event Action<DialogueEndedEvent> OnEnded;

        /// <summary>播放一段对白直到结束，返回出口与是否跳过。</summary>
        /// <exception cref="ArgumentException">未知对白 id，或播放配置非法。</exception>
        /// <exception cref="InvalidOperationException">已有对白在进行。</exception>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> 取消（规则已 Cancel）或对白被外部中断。</exception>
        public async UniTask<DialogueResult> PlayAsync(int dialogueId, CancellationToken ct = default)
        {
            telemetry.Track("play_requested", ("id", dialogueId));
            if (running)
            {
                telemetry.TrackWarn("play_rejected", TelemetryProps.Of(("id", dialogueId), ("reason", "busy"), ("current", currentId)));
                throw new InvalidOperationException($"对白 {currentId} 正在进行，不能再拉起 {dialogueId}");
            }
            if (!catalog.TryGet(dialogueId, out DialogueContent content))
            {
                telemetry.TrackWarn("play_rejected", TelemetryProps.Of(("id", dialogueId), ("reason", "unknown_id")));
                throw new ArgumentException($"对白表里没有 id {dialogueId}", nameof(dialogueId));
            }
            DialoguePlaybackPolicy playback = EnsurePolicy(dialogueId);
            ct.ThrowIfCancellationRequested();

            running = true;
            currentId = dialogueId;
            string outcome = string.Empty;
            bool skipped = false;
            bool completed = false;
            try
            {
                rules.Start(content);
                playback.ResetForDialogue();
                using (worldPause.Acquire(this))
                {
                    // 只恢复进来之前的状态：调用方若本来就关着 Gameplay 图（如过场中），结束后不擅自打开。
                    // Actions 为 null（InputService 尚未初始化、或 EditMode 测试）时没有输入图可管，记录 / 禁用 / 恢复一并跳过。
                    bool hasInput = input.Actions != null; // lint-ok: 只判动作集是否已创建，不读设备输入、不影响回放
                    bool gameplayWasEnabled = hasInput && input.Actions.Gameplay.enabled; // lint-ok: 只读动作图启用状态用于收尾恢复，不读设备输入、不影响回放
                    if (hasInput) input.DisableMap(InputService.GameplayMap);
                    try
                    {
                        OnStarted?.Invoke(new DialogueStartedEvent(dialogueId));
                        outcome = await controller.PresentAsync(conditions, TargetPrefix + dialogueId, playback, ct);
                        skipped = playback.Skipping;
                        completed = true;
                    }
                    finally
                    {
                        if (gameplayWasEnabled) input.EnableMap(InputService.GameplayMap);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                telemetry.TrackError("play_failed", e, TelemetryProps.Of(("id", dialogueId)));
                throw;
            }
            finally
            {
                // 非正常结束（取消 / 中断 / 异常）一律让规则回到 Closed，避免残留在半途状态。
                if (!completed && rules.Phase != DialogueSaveData.Phase.Closed) rules.Cancel();
                running = false;
                telemetry.Track("ended", ("id", dialogueId), ("outcome", outcome), ("skipped", skipped), ("completed", completed));
                OnEnded?.Invoke(new DialogueEndedEvent(dialogueId, outcome, skipped));
            }
            return new DialogueResult(dialogueId, outcome, skipped);
        }

        public void Dispose()
        {
            rules.OnChoiceSelected -= ForwardChoice;
        }

        // 策略惰性建：非法配置（如空倍速表）在首次播放时以 ArgumentException 暴露，而不是在容器构建期让启动崩掉。
        private DialoguePlaybackPolicy EnsurePolicy(int dialogueId)
        {
            if (policy != null) return policy;
            try
            {
                policy = new DialoguePlaybackPolicy(config.ToPlaybackSettings());
            }
            catch (ArgumentException e)
            {
                telemetry.TrackError("play_rejected", e, TelemetryProps.Of(("id", dialogueId), ("reason", "invalid_config")));
                throw;
            }
            return policy;
        }

        // 规则在跳转之后才发 OnChoiceSelected，此时 Current 已是下一节点；选项所在节点取刚写入的那条选择历史。
        private void ForwardChoice(DialogueContent.Choice choice)
        {
            IReadOnlyList<DialogueSaveData.HistoryEntry> entries = rules.History;
            string nodeId = entries.Count > 0 && entries[entries.Count - 1].IsChoice
                ? entries[entries.Count - 1].NodeId
                : rules.Current?.Id;
            OnChoiceSelected?.Invoke(new DialogueChoiceSelectedEvent(currentId, nodeId, choice.Id));
        }
    }
}

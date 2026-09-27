// 职责：ISessionStateSource 的真实实现——只读 Quest / Dialogue / Monster / UIService 的公开 API，
//   把「能不能存」「遭遇现场」「进度描述」翻给 GameSession。
// 为什么新建：GameSession 要在 EditMode 里测，具体依赖不能直接进它的构造；这层薄适配器就是那道缝。
//   不塞进任何玩法模块：Session 依赖它们，反过来它们不该认识 Session。

using System;
using Game.Core.Flow;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.UI;
using Game.Dialogue;
using Game.Monster;
using Game.Quest;

namespace Game.Session
{
    /// <summary>存档会话现场的真实来源（根作用域单例，注册为 <see cref="ISessionStateSource"/>）。</summary>
    public sealed class SessionStateAdapter : ISessionStateSource
    {
        private readonly IGameFlow flow;
        private readonly DialogueService dialogue;
        private readonly UIService ui;
        private readonly EncounterStep step;
        private readonly MonsterEncounterState encounter;
        private readonly QuestService quests;
        private readonly ISaveService saves;
        private readonly SessionConfig config;

        /// <remarks>
        /// <see cref="UIService"/> 按具体类型注入（要 <see cref="UIService.TopView"/>，接口上没有），
        /// 先例 <c>UICancelRouter</c>；根作用域那条注册带 <c>AsSelf</c>。
        /// 逻辑时钟不在这里：tick 由 <see cref="GameSession"/> 在捕获时传入（<see cref="CaptureEncounter"/> 的参数）。
        /// </remarks>
        public SessionStateAdapter(
            IGameFlow flow,
            DialogueService dialogue,
            UIService ui,
            EncounterStep step,
            MonsterEncounterState encounter,
            QuestService quests,
            ISaveService saves,
            SessionConfig config)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.step = step ?? throw new ArgumentNullException(nameof(step));
            this.encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
            this.quests = quests ?? throw new ArgumentNullException(nameof(quests));
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            // SessionConfig 是 ScriptableObject，判空只用 ==。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
        }

        /// <summary>
        /// 当前状态是遭遇状态**且遭遇逻辑已在跑**。
        /// <para>
        /// 多判一个 <see cref="EncounterStep.IsActive"/>：<c>IGameFlow.Current</c> 只在目标 Enter 成功后才更新，
        /// 离开遭遇的 Exit 期间它仍指向遭遇状态，而 Exit 里 <c>step.End()</c> 已把遭遇停了——这时捕获会写进
        /// <c>Active = false</c>，读档后遭遇不推进。<c>IsActive</c> 在 Begin / Restore 后为 true、离场时为 false，
        /// 正好把「切换中」排除在外。
        /// </para>
        /// </summary>
        public bool IsGameplayState => flow.Current is MonsterEncounterState && step.IsActive;

        public bool DialogueRunning => dialogue.IsRunning;

        // UIView 是 UnityEngine.Object，判空只用 !=。
        public bool AnyPanelOpen => ui.TopView != null;

        /// <summary>
        /// 终局已出且未被消费。只看 <c>PendingResult != None</c> 不够：消费后结果值会一直留到下一场战斗，
        /// 那样整段战后探索都存不了档；判法与 <c>EncounterStep.Step</c> 的冻结条件一致。
        /// </summary>
        public bool BattleResultPending => step.PendingResult != EncounterStep.Result.None && !step.ResultConsumed;

        public void CaptureEncounter(long tick)
        {
            EncounterSaveData captured = step.Capture(tick);

            // 原地复制进分区，不换实例：别处（MonsterEncounterState 已准备的恢复）可能正拿着这个分区对象。
            EncounterSaveData target = saves.Get<EncounterSaveData>();
            target.SceneKey = captured.SceneKey;
            target.Tick = captured.Tick;
            target.Active = captured.Active;
            target.EncounterId = captured.EncounterId;
            target.ActivationId = captured.ActivationId;
            target.Result = captured.Result;
            target.ResultConsumed = captured.ResultConsumed;
            target.Player = captured.Player;
            target.Monster = captured.Monster;
        }

        public void PrepareRestore(bool fromSave)
        {
            if (!fromSave)
            {
                encounter.ClearPreparedRestore();
                return;
            }

            EncounterSaveData saved = saves.Get<EncounterSaveData>();
            try
            {
                saved.Validate();
                encounter.PrepareRestore(saved);
            }
            catch (Exception e)
            {
                // 校验失败不阻断读档：任务 / 箱子照常恢复，玩家落回出生点。
                Log.Warn($"存档里的遭遇快照不合法，本次落回出生点：{e.Message}");
                encounter.ClearPreparedRestore();
            }
        }

        public string BuildProgressText()
        {
            if (!quests.IsReady) return config.ProgressPlaceholder;

            if (quests.TryGetTracked(out QuestProgress tracked) && tracked != null)
            {
                return TitleOf(tracked.Id);
            }

            var inProgress = quests.InProgress;
            for (int i = 0; i < inProgress.Count; i++)
            {
                int id = inProgress[i].Id;
                if (quests.Content.TryGet(id, out QuestDefinition definition) && definition.Kind == QuestKind.Main)
                {
                    return TitleOf(id);
                }
            }

            return config.ProgressPlaceholder;
        }

        private string TitleOf(int id)
        {
            if (quests.Content.TryGet(id, out QuestDefinition definition) && !string.IsNullOrEmpty(definition.Title))
            {
                return definition.Title;
            }

            return "#" + id;
        }
    }
}

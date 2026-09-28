// 职责：复用 ShowcaseScenario 的真实 Boot/Session 与对白面板，演示任务条件和读档后的可见选项。
// 现有 Dialogue/Session 回放不持有 Narrative 状态；仅在本回放创建验证目标，不改正常 SampleScene。
using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Dialogue;
using Game.Monster;
using Game.Narrative;
using Game.Quest;
using Game.Session;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Narrative
{
    [Category("Showcase")]
    public sealed class NarrativeShowcase : ShowcaseScenario
    {
        private IUIService ui;
        private NarrativeService narrative;
        private DialogueService dialogue;
        private DialogueRules rules;
        private QuestService quest;
        private GameSession session;
        private IGameFlow flow;
        private NarrativeTrigger target;
        private bool playbackDone;
        private Exception playbackError;
        private DialogueEndedEvent ended;

        protected override string Module => "Narrative";
        protected override float BootShutdownSettleSeconds => 0.5f;
        protected override IEnumerator WaitForBootReady()
        {
            yield return WaitUntil("标题就绪", () =>
            {
                IUIService current = ResolveService<IUIService>();
                return current != null && current.Get<TitleView>() != null;
            }, 20f);
        }

        [TearDown]
        public void Unsubscribe()
        {
            if (dialogue != null) dialogue.OnEnded -= RecordEnded;
        }

        [UnityTest]
        public IEnumerator QuestCondition_AfterDialogueAndContinue_RemainsAvailable()
        {
            yield return EnterWorldFromTitle();
            Connect();
            yield return Step("创建验证见证人，查看尚未解锁的选项", () =>
            {
                CreateTarget();
                Begin(narrative.StartAsync("sample_options", target.TargetId));
            }, 0f);
            yield return Check("确认任务选项置灰，并显示尚未完成的原因", () =>
                Choice("确认已完成旅人任务") != null && !Choice("确认已完成旅人任务").interactable, 5f);
            yield return Snapshot("任务未完成·选项置灰");
            yield return ClickWhenReady("稍后再来", () => Choice("稍后再来"), () => playbackDone);
            yield return WaitUntil("对白退出", () => playbackDone, 5f);

            // 公共 Report 建立既有任务的前置状态；真正被验证的 TalkTo 1002 必须由 DialogueEndedEvent 推进。
            yield return Step("将既有任务推进到与旅人交谈", ReadySecondQuest);
            yield return Step("交互验证目标，拉起旅人的真实对白", () => Begin(target.InteractAsync()), 0f);
            yield return WaitUntil("旅人对白出现", () => ui.Get<DialogueView>() != null, 5f);
            yield return FinishDialogue();
            yield return Check("对白正常结束，旅人任务已完成", () =>
                playbackDone && playbackError == null && ended.Completed && !ended.Skipped && QuestCompleted(), 5f);

            yield return Step("再次查看见证人选项", () => Begin(narrative.StartAsync("sample_options", target.TargetId)), 0f);
            yield return Check("任务完成后确认选项可点击", () =>
                Choice("确认已完成旅人任务") != null && Choice("确认已完成旅人任务").interactable, 5f);
            yield return Snapshot("任务完成·选项可用");
            yield return ClickWhenReady("确认已完成旅人任务", () => Choice("确认已完成旅人任务"), () => playbackDone);
            yield return WaitUntil("确认对白结束", () => playbackDone, 5f);

            bool saved = false;
            NarrativeSaveData expected = null;
            yield return Step("在稳定等待阶段保存进度", null, 0f);
            yield return narrative.StartAsync("sample_wait", target.TargetId).ToCoroutine();
            expected = narrative.Capture();
            yield return session.SaveNowAsync("narrative_showcase").ToCoroutine(value => saved = value);
            yield return Check("保存完成", () => saved);
            bool atTitle = false;
            yield return Step("回到标题", () => BeginLeaveToTitle(flow, () => atTitle = true), 0f);
            yield return WaitUntil("标题继续按钮出现", () => atTitle && ui.Get<TitleView>() != null, 20f);
            yield return Snapshot("存档后·标题继续");
            yield return Step("点继续并恢复世界", () => RequireTitleButton("ContinueButton").onClick.Invoke(), 0f);
            yield return WaitUntil("回到世界", () => flow.Current is MonsterEncounterState && ui.Get<TitleView>() == null, 20f);

            yield return Step("恢复同一目标，检查读档后的任务选项", () =>
            {
                CreateTarget();
            }, 0f);
            yield return Check($"恢复等待 {expected.Current.StageId}，请求 {expected.Current.ActionRequestId} 与保存前一致", () =>
            {
                NarrativeSaveData actual = narrative.Capture();
                return SameFrame(expected.Current, actual.Current) && SameFrame(expected.Parent, actual.Parent) &&
                    expected.NextActivationId == actual.NextActivationId && expected.Outcome == actual.Outcome &&
                    expected.ConsumedTriggers.SetEquals(actual.ConsumedTriggers) && expected.StoryFlags.SetEquals(actual.StoryFlags);
            });
            bool repeated = true;
            yield return target.InteractAsync().ToCoroutine(value => repeated = value);
            yield return Check("已完成的同目标遭遇不再弹出旅人对白", () => !repeated && ui.Get<DialogueView>() == null);
            Begin(narrative.StartAsync("sample_options", target.TargetId));
            yield return Check("读档后仍能确认已完成的旅人任务", () =>
                Choice("确认已完成旅人任务") != null && Choice("确认已完成旅人任务").interactable, 5f);
            yield return Snapshot("继续后·任务选项保留");
            yield return ClickWhenReady("稍后再来", () => Choice("稍后再来"), () => playbackDone);
        }

        [UnityTest]
        public IEnumerator QuestCompletion_WhileChoicesOpen_RefreshesAvailability()
        {
            yield return EnterWorldFromTitle();
            Connect();
            yield return Step("打开尚未满足任务条件的选项", () =>
            {
                CreateTarget();
                ReadySecondQuest();
                Begin(narrative.StartAsync("sample_options", target.TargetId));
            }, 0f);
            yield return Check("确认选项当前置灰", () => Choice("确认已完成旅人任务") != null &&
                !Choice("确认已完成旅人任务").interactable, 5f);
            yield return Snapshot("刷新前·选项置灰");
            // 专门验证面板已打开时的服务事件边界；任务完成来源由另两条回放走真实对白。
            yield return Step("通过任务公开接口报告完成，保持选项面板打开", () => quest.Report(QuestObjectiveKind.TalkTo, "1002"), 0f);
            yield return Check("同一面板自动把确认选项变为可点", () => Choice("确认已完成旅人任务") != null &&
                Choice("确认已完成旅人任务").interactable, 5f);
            yield return Snapshot("刷新后·选项可用");
            yield return Step("选择刚解锁的确认项", null, 0f);
            yield return ClickWhenReady("确认选项", () => Choice("确认已完成旅人任务"), () => playbackDone);
            yield return Check("提交成功且对白关闭", () => playbackDone && playbackError == null &&
                ended.Completed && ended.Outcome == "Verified" && ui.Get<DialogueView>() == null, 5f);
        }

        [UnityTest]
        public IEnumerator DisableThenEnable_RetriesEncounter_AndSkipCompletesQuest()
        {
            yield return EnterWorldFromTitle();
            Connect();
            yield return Step("建立验证目标及旅人任务前置", () => { CreateTarget(); ReadySecondQuest(); });
            yield return Step("交互目标，打开对白", () => Begin(target.InteractAsync()), 0f);
            yield return WaitUntil("对白出现", () => ui.Get<DialogueView>() != null, 5f);
            NarrativeSaveData active = narrative.Capture();
            yield return Step("禁用目标，中断对白", () => target.gameObject.SetActive(false), 0f);
            yield return Check("取消收尾且任务没有被推进", () => playbackDone &&
                playbackError is OperationCanceledException && !ended.Completed && !QuestCompleted() &&
                !narrative.IsBusy && !narrative.CanSave && !narrative.Capture().Current.RequestIssued &&
                ui.Get<DialogueView>() == null, 5f);
            yield return Snapshot("目标禁用·对白取消且任务未完成");
            yield return Step("启用同一目标，再次交互", () =>
            {
                target.gameObject.SetActive(true);
                Begin(target.InteractAsync());
            }, 0f);
            yield return Check("同一已消费遭遇恢复对白，没有新建激活", () => ui.Get<DialogueView>() != null &&
                narrative.Capture().Current.ActivationId == active.Current.ActivationId &&
                narrative.Capture().ConsumedTriggers.SetEquals(active.ConsumedTriggers), 5f);
            yield return Step("点跳过", null, 0f);
            yield return ClickWhenReady("跳过", () => ViewButton<DialogueView>("SkipButton"), () => ui.Get<DialogueSkipConfirmView>() != null);
            yield return Snapshot("跳过确认");
            yield return Step("确认跳过", null, 0f);
            yield return ClickWhenReady("确认跳过", () => ViewButton<DialogueSkipConfirmView>("ConfirmButton"), () => playbackDone);
            yield return Check("对白收起且旅人任务完成", () => playbackDone && playbackError == null &&
                ended.Completed && ended.Skipped && QuestCompleted() && ui.Get<DialogueView>() == null, 5f);
            yield return Snapshot("跳过后·任务完成");
        }

        private void Connect()
        {
            ui = ResolveService<IUIService>();
            narrative = ResolveService<NarrativeService>();
            dialogue = ResolveService<DialogueService>();
            rules = ResolveService<DialogueRules>();
            quest = ResolveService<QuestService>();
            session = ResolveService<GameSession>();
            flow = ResolveService<IGameFlow>();
            Assert.That(narrative != null && narrative.IsReady, Is.True, "Boot 必须挂接 NarrativeInstaller");
            dialogue.OnEnded += RecordEnded;
        }

        private void CreateTarget()
        {
            if (target != null) UnityEngine.Object.DestroyImmediate(target.gameObject);
            DialogueInteractionActor actor = ResolveService<DialogueSceneBinder>().Actor;
            Assert.That(actor != null, Is.True);
            GameObject root = Track(new GameObject("NarrativeSampleTarget"));
            root.transform.position = actor.Anchor.position + Vector3.right * 0.8f;
            target = root.AddComponent<NarrativeTrigger>();
            target.Configure("showcase_narrative_target", "SampleTraveler", narrative, actor);
            // 玩家入口目前为点击/公开交互，无键盘焦点；本回放调用相同的 InteractAsync 距离与生命周期守卫。
            var label = new GameObject("Label").AddComponent<TextMeshPro>();
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = Vector3.up * 1.5f;
            label.text = "验证见证人";
            label.fontSize = 3;
            label.alignment = TextAlignmentOptions.Center;
            if (Camera.main != null) label.transform.rotation = Camera.main.transform.rotation;
        }

        private void ReadySecondQuest()
        {
            quest.Report(QuestObjectiveKind.TalkTo, "1001");
            quest.Report(QuestObjectiveKind.ReachLocation, "camp");
            quest.Track(1002);
        }
        private bool QuestCompleted() => quest.TryGet(1002, out QuestProgress p) && p.State == QuestState.Completed;
        private static bool SameFrame(NarrativeSaveData.Frame expected, NarrativeSaveData.Frame actual)
        {
            if (expected == null || actual == null) return expected == actual;
            return expected.StoryId == actual.StoryId && expected.StageId == actual.StageId &&
                expected.ActivationId == actual.ActivationId && expected.TargetId == actual.TargetId &&
                expected.ActionRequestId == actual.ActionRequestId && expected.RequestIssued == actual.RequestIssued &&
                expected.CompletedParts.SetEquals(actual.CompletedParts);
        }
        private void RecordEnded(DialogueEndedEvent value) => ended = value;
        private void Begin(UniTask<bool> task)
        {
            playbackDone = false;
            playbackError = null;
            Observe(task).Forget();
        }
        private async UniTaskVoid Observe(UniTask<bool> task)
        {
            try { if (!await task) throw new InvalidOperationException("验证交互未被接受"); }
            catch (Exception e) { playbackError = e; Debug.LogWarning("Narrative 回放交互失败：" + e.Message); }
            finally { playbackDone = true; }
        }
        private Button ViewButton<T>(string name) where T : UIView
        {
            T view = ui.Get<T>();
            return view == null ? null : FindDeep<Button>(view.transform, name);
        }
        private Button Choice(string text)
        {
            DialogueView view = ui.Get<DialogueView>();
            if (view == null) return null;
            Transform root = FindDeep<Transform>(view.transform, "ChoiceRoot");
            if (root == null) return null;
            foreach (Button button in root.GetComponentsInChildren<Button>(false))
            {
                TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                if (button.name != "ChoiceTemplate" && label != null && label.text.Contains(text)) return button;
            }
            return null;
        }
        private IEnumerator FinishDialogue()
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!playbackDone && Time.realtimeSinceStartup < deadline)
            {
                if (rules.Phase == DialogueSaveData.Phase.Typing || rules.Phase == DialogueSaveData.Phase.AwaitAdvance)
                {
                    Button tap = ViewButton<DialogueView>("TapArea");
                    if (tap != null) tap.onClick.Invoke();
                }
                yield return new WaitForSecondsRealtime(0.15f);
            }
        }
    }
}

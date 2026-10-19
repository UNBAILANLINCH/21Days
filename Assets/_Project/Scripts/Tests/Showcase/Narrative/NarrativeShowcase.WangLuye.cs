// 职责：沿正式新局到书库验证王录业动画、头像和关闭清理；复用现有教学、寻人与对白输入助手。
using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.UI;
using Game.Dialogue;
using Game.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Narrative
{
    public sealed partial class NarrativeShowcase
    {
        [UnityTest]
        [Timeout(400000)]
        public IEnumerator Spotlight_WangLuyeAppearsWithPortrait_AndCleansUp()
        {
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0026"); yield return AdvanceFormalDialogue();
            yield return StoryPoint("action_r0070"); yield return PlayBattle(); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0083");
            var state = ResolveService<NarrativeService>();
            var uiService = ResolveService<IUIService>();
            yield return WaitUntil("赵十四原对白已经打开", () => state.CurrentStageId == "talk_21006" && uiService.Get<DialogueView>() != null, 8);
            yield return ClickWhenReady("打开原跳过确认", () => FindDeep<Button>(uiService.Get<DialogueView>().transform, "SkipButton"),
                () => uiService.Get<DialogueSkipConfirmView>() != null, 5);
            yield return ClickWhenReady("确认后真实故事自动衔接书库21007", () => uiService.Get<DialogueSkipConfirmView>() == null ? null :
                FindDeep<Button>(uiService.Get<DialogueSkipConfirmView>().transform, "ConfirmButton"),
                () => state.CurrentStageId == "talk_21007" && uiService.Get<DialogueView>() != null, 10);
            var actor = ResolveService<SpotlightStoryFlow>().Bindings.WangLuye;
            Assert.That(actor, Is.Not.Null);
            yield return ReachStoryLine("r0134");
            yield return Check("王录业在真实镜头内，说话头像已载入", () => actor.gameObject.activeInHierarchy &&
                Camera.main.WorldToViewportPoint(actor.transform.position + Vector3.up).x > 0 &&
                Camera.main.WorldToViewportPoint(actor.transform.position + Vector3.up).x < 1 && WangPortrait("talk"), 3);
            yield return Snapshot("王录业书库首句与头像");
            yield return ReachStoryLine("r0136");
            yield return Check("崔皓说话时王录业待机，头像退出", () => actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") &&
                FindDeep<Image>(uiService.Get<DialogueView>().transform, "PortraitLeft").color.a < .01f, 3);
            yield return ReachStoryLine("r0155");
            yield return Check("时停中的王录业使用说话序列", () => Time.timeScale == 0f && actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("talk"), 2);
            float before = actor.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            yield return new WaitForSecondsRealtime(.15f);
            yield return Check("timeScale为零时序列仍推进", () => actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("talk") &&
                actor.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime > before);
            yield return Snapshot("王录业说话动作");
            yield return ReachStoryLine("r0166");
            yield return Check("既有告诫台词切严肃动作与头像", () => actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("serious") && WangPortrait("serious"), 3);
            yield return Snapshot("王录业严肃表情");
            yield return AdvanceFormalDialogue();
            yield return Check("21007结束后人物收起且世界恢复", () => !actor.gameObject.activeInHierarchy && !DialogueRunning && Time.timeScale > 0f, 5);
            using (var cancellation = new CancellationTokenSource())
            {
                // 21014在当前正式入口之后；只验已有表内对白的接入和取消，不冒充完整后续主线。
                var playback = ResolveService<DialogueService>().PlayAsync(21014, ct: cancellation.Token);
                yield return Check("已有21014通过同一服务显示正确人物与头像", () => actor.gameObject.activeInHierarchy && WangPortrait("talk"), 5);
                yield return Snapshot("王录业名簿核对原对白");
                cancellation.Cancel();
                Exception error = null;
                yield return playback.ToCoroutine(_ => { }, value => error = value);
                Assert.That(error, Is.TypeOf<OperationCanceledException>());
                yield return Check("取消后人物头像关闭、恢复时间且不推进正式阶段", () => !actor.gameObject.activeInHierarchy &&
                    uiService.Get<DialogueView>() == null && Time.timeScale > 0 && state.CurrentStageId == "enter_r0171", 3);
            }
            yield return Snapshot("王录业对白收尾恢复");
        }

        private IEnumerator ReachStoryLine(string nodeId)
        {
            var current = ResolveService<DialogueRules>();
            float deadline = Time.realtimeSinceStartup + 40;
            while (DialogueRunning && current.Current.Id != nodeId && Time.realtimeSinceStartup < deadline)
                yield return Input.Press(ResolveService<IInputService>().Actions.Dialogue.Advance);
            Assert.That(current.Current.Id, Is.EqualTo(nodeId));
            yield return WaitUntil("目标原台词完成准备", () => current.Phase != DialogueSaveData.Phase.Preparing, 3);
        }
        private bool WangPortrait(string expression)
        {
            var view = ResolveService<IUIService>().Get<DialogueView>();
            if (view == null) return false;
            var portrait = FindDeep<Image>(view.transform, "PortraitLeft");
            return portrait.sprite != null && portrait.sprite.name == "Portrait_wang_luye_" + expression && portrait.color.a > .9f;
        }
    }
}

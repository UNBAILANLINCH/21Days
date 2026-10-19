// 职责：复用真实新局、入镜和桥口路线，验证值守原交互及新外观；不直接写阶段冒充到场。
using System.Collections;
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
        [Timeout(500000)]
        public IEnumerator Spotlight_LunhuitangGuardAppearance_KeepsInteractionAndRemainsAfterTalk()
        {
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0026"); yield return NarrationToMirror();
            var story = ResolveService<SpotlightStoryFlow>();
            var humanMirror = story.Bindings.Find("enter_r0171").Anchor;
            Assert.That(humanMirror.GetComponentInChildren<Game.CharacterPuppet.ChibiPuppet>(true).name, Is.EqualTo("Chibi_chen"), "人界入镜点不当作值守替换");
            yield return StoryPoint("enter_r0171"); yield return WaitCurtainRevealed();
            var state = ResolveService<NarrativeService>();
            var actor = story.Bindings.LunhuitangGuard;
            Assert.That(actor, Is.Not.Null);
            Assert.That(actor.name, Is.EqualTo("Chibi_lunhuitang_guard"));
            var point = story.Bindings.Find("enter_r0171");
            Vector3 originalPosition = point.Anchor.position;
            yield return Check("入镜后仍等现场问询，值守保持显示", () => !story.Bindings.IsHumanStory &&
                actor.gameObject.activeInHierarchy && !DialogueRunning && state.CurrentStageId == "enter_r0171", 5);
            yield return StoryPoint("enter_r0171", false);
            yield return Snapshot("轮回堂值守持卷外观与原交互点");
            yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Interact);
            yield return WaitUntil("现场E进入原21008问询", () => state.CurrentStageId == "talk_21008" && DialogueRunning, 5);
            yield return ReachStoryLine("r0175");
            yield return Check("值守开口且头像为独立值守素材", () => actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("talk") && GuardPortrait(), 3);
            float before = actor.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            yield return new WaitForSecondsRealtime(.1f);
            yield return Check("问询时停中持卷说话动画继续", () => Time.timeScale == 0f &&
                actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("talk") && actor.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime > before);
            yield return Snapshot("轮回堂值守原对白与头像");
            yield return ReachStoryLine("r0177");
            yield return Check("崔皓说话时值守待机、头像收起", () => actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") &&
                FindDeep<Image>(ResolveService<IUIService>().Get<DialogueView>().transform, "PortraitLeft").color.a < .01f, 3);
            yield return AdvanceFormalDialogue();
            yield return Check("原出口到188，值守仍在原地且不重复抢交互", () => state.CurrentStageId == "enter_r0188" &&
                actor.gameObject.activeInHierarchy && point.Anchor.position == originalPosition && !point.CanInteract &&
                actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") && Time.timeScale > 0f, 5);
            yield return Snapshot("轮回堂问询结束值守留场");
        }

        private bool GuardPortrait()
        {
            var view = ResolveService<IUIService>().Get<DialogueView>();
            if (view == null) return false;
            var image = FindDeep<Image>(view.transform, "PortraitLeft");
            return image.sprite != null && image.sprite.name == "Portrait_lunhuitang_guard_talk" && image.color.a > .9f;
        }
    }
}

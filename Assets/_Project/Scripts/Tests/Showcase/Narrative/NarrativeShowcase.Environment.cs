// 职责：验证正式新局的剧情光照、读档与日转夜取消；复用已有主线回放助手，不另造测试地图。
using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Save;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Monster;
using Game.Narrative;
using Game.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Narrative
{
    public sealed partial class NarrativeShowcase
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator Spotlight_EnvironmentFollowsStoryAndContinue()
        {
            yield return EnterStoryWorld();
            var environment = Object.FindObjectOfType<SpotlightEnvironmentView>();
            Assert.That(environment, Is.Not.Null, "正式场景须接环境入口");
            yield return Check("梦中月夜与雪已应用", () => environment.HasApplied && environment.CurrentState.Snow, 5);
            yield return Snapshot("剧情环境_梦中夜雪");
            yield return AdvanceFormalDialogue();
            yield return Check("梦醒转黎明，雪与祭火结束", () => environment.CurrentState.Lighting == SpotlightEnvironmentRules.Preset.Dawn &&
                !environment.CurrentState.Snow && !environment.CurrentState.AltarFire, 5);
            yield return Snapshot("剧情环境_客店黎明");
            yield return ReachZhaoInDaylight();
            yield return Snapshot("剧情环境_赵十四白昼");
            yield return StoryPoint("enter_r0083");
            yield return AdvanceFormalDialogue();
            yield return WaitUntil("日转夜完成后打开真实书库对白", () => ResolveService<NarrativeService>().CurrentStageId == "talk_21007" && DialogueRunning, 10);
            yield return AdvanceFormalDialogue();
            yield return WaitUntil("书库夜晚已揭幕且输入恢复", () => ResolveService<NarrativeService>().CurrentStageId == "enter_r0171" &&
                environment.HasApplied && environment.CurrentState.Lighting == SpotlightEnvironmentRules.Preset.MoonNight &&
                ResolveService<SpotlightStoryFlow>().CanSave && !ResolveService<ILoadingCurtain>().IsCovered &&
                ResolveService<IInputService>().Actions.Gameplay.enabled, 10);
            yield return Snapshot("剧情环境_书库后月夜");
            bool saved = false;
            yield return ResolveService<GameSession>().SaveNowAsync("environment_night").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return ResolveService<GameSession>().ContinueAsync(1).ToCoroutine();
            yield return WaitCurtainRevealed();
            environment = Object.FindObjectOfType<SpotlightEnvironmentView>();
            yield return Check("夜档继续直接恢复夜色与操作", () => environment.HasApplied &&
                environment.CurrentState.Lighting == SpotlightEnvironmentRules.Preset.MoonNight &&
                ResolveService<IInputService>().Actions.Gameplay.enabled && !ResolveService<IWorldPauseService>().IsPaused, 5);
            yield return Snapshot("剧情环境_夜档继续");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Spotlight_EnvironmentCurtainCancelledByTitle_RestoresInput()
        {
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            yield return ReachZhaoInDaylight();
            bool saved = false;
            yield return ResolveService<GameSession>().SaveNowAsync("environment_before_night").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            // 自动保存会更新当前槽；用服务复制隔离槽的白昼检查点，不能把同槽旧状态当成仍然存在。
            yield return ResolveService<ISaveService>().SaveAsync(2).ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return StoryPoint("enter_r0083");
            var story = ResolveService<SpotlightStoryFlow>();
            var narrative = ResolveService<NarrativeService>();
            var ui = ResolveService<IUIService>();
            float deadline = Time.realtimeSinceStartup + 90f;
            while (!(narrative.CurrentStageId == "enter_r0129" && !story.CanSave && !DialogueRunning && !narrative.IsBusy &&
                !ResolveService<IInputService>().Actions.Gameplay.enabled && ResolveService<IWorldPauseService>().IsPaused) &&
                Time.realtimeSinceStartup < deadline)
            {
                var confirm = ui.Get<DialogueSkipConfirmView>();
                if (confirm != null) FindDeep<Button>(confirm.transform, "ConfirmButton").onClick.Invoke();
                else
                {
                    var view = ui.Get<DialogueView>();
                    var skip = view == null ? null : FindDeep<Button>(view.transform, "SkipButton");
                    if (skip != null && skip.interactable) skip.onClick.Invoke();
                }
                yield return null;
            }
            Assert.That(narrative.CurrentStageId, Is.EqualTo("enter_r0129"), "须等到真实日转夜操作再取消");
            Assert.That(story.CanSave, Is.False);
            Assert.That(ResolveService<IInputService>().Actions.Gameplay.enabled, Is.False);
            bool atTitle = false;
            BeginLeaveToTitle(ResolveService<IGameFlow>(), () => atTitle = true);
            yield return WaitUntil("日转夜期间返回标题，旧转场已取消", () => atTitle && ui.Get<Game.Core.UI.Views.TitleView>() != null, 30);
            yield return WaitCurtainRevealed();
            bool continued = false;
            yield return ResolveService<GameSession>().ContinueAsync(2).ToCoroutine(value => continued = value);
            Assert.That(continued, Is.True, "必须实际接受切档，不能把拒绝加载后的原对白当成恢复结果");
            yield return Step("从标题继续另一槽白昼检查点", null, 0f);
            yield return WaitCurtainRevealed();
            var environment = Object.FindObjectOfType<SpotlightEnvironmentView>();
            yield return Check("旧转场不覆盖新档，仍在白昼等待赵十四", () => narrative.CurrentStageId == "enter_r0083" &&
                environment.HasApplied && environment.CurrentState.Lighting == SpotlightEnvironmentRules.Preset.Day &&
                story.CanSave && ResolveService<IInputService>().Actions.Gameplay.enabled &&
                !ResolveService<IWorldPauseService>().IsPaused && !((MonsterEncounterState)ResolveService<IGameFlow>().Current).NavigationBlocked, 5);
            Vector2 before = State.ControlledPosition;
            yield return Walk(Vector2.left, .2f);
            yield return Check("恢复后真实移动有效", () => Vector2.Distance(State.ControlledPosition, before) > .05f, 3);
            yield return Snapshot("剧情环境_取消夜转场恢复白昼");
        }

        private IEnumerator ReachZhaoInDaylight()
        {
            yield return StoryPoint("enter_r0026"); yield return AdvanceFormalDialogue();
            yield return StoryPoint("action_r0070"); yield return PlayBattle(); yield return AdvanceFormalDialogue();
            var environment = Object.FindObjectOfType<SpotlightEnvironmentView>();
            yield return Check("教学完成后赵十四段为白昼", () => ResolveService<NarrativeService>().CurrentStageId == "enter_r0083" &&
                environment.HasApplied && environment.CurrentState.Lighting == SpotlightEnvironmentRules.Preset.Day, 5);
        }
    }
}

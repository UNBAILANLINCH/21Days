// 职责：正式新游戏至会面的连续流程；复用五店输入助手，不能用任务回写或发道具冒充完成。
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.UI;
using Game.Forging;
using Game.Narrative;
using Game.Quest;
using Game.Rhythm;
using Game.Session;
using Game.Tavern;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Game.Tests.Showcase.Narrative
{
    public sealed partial class NarrativeShowcase
    {
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Spotlight_MirrorArrivalRequiresLunhuitangInteraction()
        {
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            var story = ResolveService<SpotlightStoryFlow>();
            var narrative = ResolveService<NarrativeService>();
            var session = ResolveService<GameSession>();
            var quests = ResolveService<QuestService>();
            var ui = ResolveService<IUIService>();
            var interact = ResolveService<IInputService>().Actions.Gameplay.Interact;
            var originalView = Object.FindObjectOfType<Game.Monster.EncounterSceneView>();
            int originalScene = originalView.gameObject.scene.handle;
            var step = ResolveService<Game.Monster.EncounterStep>();
            foreach (string id in step.Taming.TargetIds) Assert.That(step.Taming.IsAvailable(id), Is.False, "人界不能运行妖界演员：" + id);
            yield return Check("人界地图只显示当前布局", () => ui.Get<Game.World.SceneMapView>() != null &&
                ui.Get<Game.World.SceneMapView>().GetComponentInChildren<Game.World.SceneMapGraphic>(true).WorldBounds.size.z < 150 &&
                ui.Get<Game.World.SceneMapView>().GetComponentInChildren<Game.World.SceneMapGraphic>(true).WorldBounds.center.z > 200, 5);
            yield return Check("人间原任务栏显示当前交谈目标", () => ui.Get<QuestHudView>() != null &&
                FindDeep<TMPro.TMP_Text>(ui.Get<QuestHudView>().transform, "Objective").text.Contains("李诏阳"), 5);
            yield return StoryPoint("enter_r0026"); yield return NarrationToMirror();
            yield return StoryPoint("enter_r0171"); yield return WaitCurtainRevealed();
            yield return Step("只穿越，不代替到轮回堂问询");
            Assert.That(story.Bindings.IsHumanStory, Is.False);
            Assert.That(Object.FindObjectOfType<Game.Monster.EncounterSceneView>(), Is.SameAs(originalView), "入镜必须复用玩家及移动驱动");
            Assert.That(story.Bindings.gameObject.scene.handle, Is.EqualTo(originalScene), "入镜不能重载场景");
            Assert.That(Vector3.Distance(originalView.PlayerScenePosition, originalView.MirrorArrival.position), Is.LessThan(.15f));
            Assert.That(ResolveService<Game.Player.PlayerModel>().PreviousPosition, Is.EqualTo(ResolveService<Game.Player.PlayerModel>().Position));
            yield return Check("换界后小地图使用妖界坐标，不合并300米间距", () =>
                ui.Get<Game.World.SceneMapView>().GetComponentInChildren<Game.World.SceneMapGraphic>(true).WorldBounds.size.z < 150 &&
                ui.Get<Game.World.SceneMapView>().GetComponentInChildren<Game.World.SceneMapGraphic>(true).WorldBounds.center.z < 100, 5);
            for (int i = 0; i < 3; i++) yield return Input.Press(interact);
            foreach (var point in story.Bindings.Points) if (point.StageId != "enter_r0171") point.Interact();
            yield return new WaitForSecondsRealtime(.5f);
            yield return Check("刚穿越、远处E和错误对象均不播放值守", () => narrative.CurrentStageId == "enter_r0171" && !DialogueRunning &&
                quests.PresentationObjective.Contains("轮回堂"), 3);
            yield return Snapshot("妖界入场_仍等待轮回堂");
            bool saved = false;
            yield return session.SaveNowAsync("before_lunhuitang").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return session.ContinueAsync(1).ToCoroutine(); yield return WaitCurtainRevealed();
            yield return Check("妖界同编号节点继续仍等待轮回堂，不重穿也不播对白", () => !story.Bindings.IsHumanStory &&
                narrative.CurrentStageId == "enter_r0171" && !DialogueRunning, 5);
            yield return StoryPoint("enter_r0171", false);
            var guard = story.Bindings.Find("enter_r0171");
            Assert.That(guard.Position.x, Is.LessThan(-20), "必须位于上层左侧桥后的轮回堂");
            Assert.That(guard.Position.z, Is.GreaterThan(10));
            Assert.That(guard.Prompt.Verb, Is.EqualTo("问询"));
            yield return Snapshot("真实轮回堂门前_主动问询之前");
            yield return MoveTo(new Vector2(-20f, 17.7f));
            yield return Input.Press(interact); guard.Interact();
            yield return Check("离开值守范围后E不推进", () => narrative.CurrentStageId == "enter_r0171" && !DialogueRunning);
            yield return MoveTo(new Vector2(guard.Position.x, 19.8f), .15f);
            yield return Input.Press(interact);
            yield return WaitUntil("实际值守旁E才播放21008", () => DialogueRunning && narrative.CurrentStageId == "talk_21008", 5);
            yield return Snapshot("真实值守问询");
            yield return AdvanceFormalDialogue();
            for (int i = 0; i < 3; i++) yield return Input.Press(interact);
            yield return Check("值守结束仍需走到坊市，不自动播放窗边剧情", () => narrative.CurrentStageId == "enter_r0188" && !DialogueRunning &&
                quests.PresentationObjective.Contains("坊市"), 5);
            yield return StoryPoint("enter_r0188"); yield return AdvanceFormalDialogue();
            yield return Check("实际坊市交互后才恢复三店目标，旧地点教程不残留", () => narrative.CurrentStageId == "action_r0197" &&
                quests.PresentationObjective.Length == 0 &&
                FindDeep<TMPro.TMP_Text>(ui.Get<QuestHudView>().transform, "Objective").text.Contains("戏班"), 5);
            yield return Snapshot("坊市到达后_原目标栏恢复");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Spotlight_HumanMirrorSkinAndContinue()
        {
            yield return EnterStoryWorld();
            yield return AdvanceFormalDialogue();
            var story = ResolveService<SpotlightStoryFlow>();
            var session = ResolveService<GameSession>();
            var narrative = ResolveService<NarrativeService>();
            yield return Step("人间出生：同一布局，只有对白教学");
            yield return Check("同场景人界保留左下出生，无五店人物", () => story.Bindings.IsHumanStory && State.World == null &&
                Vector2.Distance(State.ControlledPosition, new Vector2(-11, 276.2f)) < .1f && ResolveService<QuestService>().PresentationObjective.Contains("李诏阳") &&
                Object.FindObjectsOfType<Game.ShopPlaytest.ShopPlaytestActor>().Length == 0, 5);
            yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Journal);
            yield return WaitUntil("人间可打开原任务列表", () => ResolveService<IUIService>().Get<QuestPanelView>() != null, 5);
            yield return ResolveService<QuestPanelController>().CloseAsync().ToCoroutine();
            yield return Snapshot("人间出生_无额外任务");
            bool saved = false;
            yield return session.SaveNowAsync("human_checkpoint").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return session.ContinueAsync(1).ToCoroutine();
            yield return WaitCurtainRevealed();
            yield return Check("继续仍在人间，保持对白前等待而不重播序章", () => story.Bindings.IsHumanStory && narrative.CurrentStageId == "enter_r0026", 5);
            yield return StoryPoint("enter_r0026");
            yield return NarrationToMirror();
            yield return Step("前情与线索连播后，只前往戏班右侧穿越平台");
            Assert.That(story.Bindings.Points.Length, Is.EqualTo(2));
            yield return StoryPoint("enter_r0171");
            yield return WaitUntil("妖界场景与五店接线完成", () => !story.Bindings.IsHumanStory && State.World != null, 60);
            Assert.That(narrative.CurrentStageId, Is.EqualTo("enter_r0171"), "入妖界不能代替到轮回堂问询");
            yield return DemonArrivalToShops();
            float deadline = Time.realtimeSinceStartup + 100;
            while (narrative.CurrentStageId != "action_r0197" && Time.realtimeSinceStartup < deadline)
            { yield return AdvanceFormalDialogue(); yield return null; }
            yield return Check("同场景入妖界后恢复玩法目标", () => !UnityEngine.SceneManagement.SceneManager.GetSceneByName("SpotlightHuman").isLoaded &&
                UnityEngine.SceneManagement.SceneManager.GetSceneByName("SampleScene").isLoaded &&
                narrative.CurrentStageId == "action_r0197", 5);
            yield return Snapshot("穿越后_妖界五店");
            yield return Step("妖界通过实际潜行绕背取皮");
            yield return GoToShop(1); yield return Borrow(1);
            yield return session.SaveNowAsync("demon_skin").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return session.ContinueAsync(1).ToCoroutine();
            yield return WaitCurtainRevealed();
            yield return Check("妖界存读档保留已取皮与剧情，不回到人间", () => !story.Bindings.IsHumanStory &&
                ResolveService<Game.Loot.LootService>().Items.ContainsKey(1905) && narrative.CurrentStageId == "action_r0197", 8);
            yield return Snapshot("妖界取皮后继续");
        }
        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Spotlight_ThreeShopsGuessBossAndReunion()
        {
            yield return Ready(0);
            yield return Step("三店自由探索：戏班、衣肆、金银行");
            yield return CompleteStoryShop(0);
            yield return GoToShop(1); yield return CompleteStoryShop(1);
            yield return GoToShop(2); yield return CompleteStoryShop(2);
            yield return ContinueToReunion();
        }
        [UnityTest]
        [Timeout(400000)]
        public IEnumerator Spotlight_MirrorFailureAndCancellationRemainRetryable()
        {
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0026"); yield return NarrationToMirror();
            yield return StoryPoint("enter_r0171", false);
            var story = ResolveService<SpotlightStoryFlow>();
            var session = ResolveService<GameSession>();
            var narrative = ResolveService<NarrativeService>();
            var actions = ResolveService<IInputService>().Actions.Gameplay;
            yield return Snapshot("镜前_保存之前");
            bool saved = false;
            yield return session.SaveNowAsync("mirror_checkpoint").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return session.ContinueAsync(1).ToCoroutine(); yield return WaitCurtainRevealed();
            yield return Check("镜前继续保持高台高度和E交互", () => story.Bindings.IsHumanStory && narrative.CurrentStageId == "enter_r0171" &&
                Mathf.Abs(Object.FindObjectOfType<Game.Monster.EncounterSceneView>().PlayerScenePosition.y - 3.21f) < .15f && story.Bindings.Find("enter_r0171").CanInteract, 5);
            var restoredView = Object.FindObjectOfType<Game.Monster.EncounterSceneView>();
            var worldCamera = System.Array.Find(Object.FindObjectsOfType<Camera>(), camera => camera.gameObject.scene == restoredView.gameObject.scene && camera.CompareTag("MainCamera"));
            Vector3 screenPoint = worldCamera.WorldToViewportPoint(restoredView.PlayerScenePosition + Vector3.up);
            Assert.That(screenPoint.x, Is.InRange(.1f, .9f), "继续后角色应在画面内，世界=" + restoredView.PlayerScenePosition + " 相机=" + worldCamera.transform.position);
            Assert.That(screenPoint.y, Is.InRange(.1f, .9f), "继续后角色应在画面内，世界=" + restoredView.PlayerScenePosition + " 相机=" + worldCamera.transform.position);
            yield return Check("镜前继续后不显示黑底操作提示", () => ResolveService<IUIService>().Get<StoryGuideHudView>() == null, 5);
            yield return Snapshot("镜前继续_无黑底提示");
            ExpectErrorLogs("故意破坏本次妖界接线以验证换界回滚", message => message.Contains("入镜目标未接入妖界五店"));
            yield return Step("临时移除本次运行的五店接线，确认回到镜前");
            var worlds = Object.FindObjectsOfType<Game.ShopPlaytest.ShopPlaytestWorld>(true);
            Assert.That(worlds.Length, Is.EqualTo(1));
            Object.DestroyImmediate(worlds[0]);
            yield return Input.Press(actions.Interact);
            yield return WaitUntil("换界失败恢复镜前并释放保存锁", () => story.Bindings != null && story.Bindings.IsHumanStory && story.CanSave, 45);
            yield return WaitCurtainRevealed();
            Assert.That(narrative.CurrentStageId, Is.EqualTo("enter_r0171"));
            yield return session.SaveNowAsync("mirror_retry").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return Snapshot("失败回到镜前可重试");
            yield return session.ContinueAsync(1).ToCoroutine(); yield return WaitCurtainRevealed();
            yield return Step("再次按E，在穿越途中继续存档，旧请求应取消");
            yield return Input.Press(actions.Interact);
            yield return session.ContinueAsync(1).ToCoroutine(); yield return WaitCurtainRevealed();
            yield return Check("切槽取消穿越后仍可保存且保留镜前节点", () => story.Bindings != null && story.Bindings.IsHumanStory && story.CanSave && narrative.CurrentStageId == "enter_r0171", 15);
            yield return session.SaveNowAsync("mirror_cancelled").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return Input.Press(actions.Interact);
            yield return WaitUntil("取消后再次E可以真正进入妖界", () => story.Bindings != null && !story.Bindings.IsHumanStory && State.World != null, 45);
            yield return Snapshot("取消后重新穿越成功");
            yield return AdvanceFormalDialogue();
        }
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Spotlight_ReverseShopOrderAlsoGrantsTavernEntry()
        {
            yield return Ready(2);
            yield return Step("换顺序完成：金银行、衣肆、戏班");
            yield return CompleteStoryShop(2);
            yield return Check("只完成金银行仍不能进酒肆", () => !State.CanEnterTavern);
            yield return GoToShop(1); yield return CompleteStoryShop(1);
            yield return Check("还缺戏班成果仍不能进酒肆", () => !State.CanEnterTavern);
            yield return GoToShop(0); yield return CompleteStoryShop(0);
            yield return Check("另一顺序也取得三组真实成果", () => State.CanEnterTavern && State.Theater.Completed && State.Clothing.Received && State.Forging.Submitted, 5);
            yield return Snapshot("反向三店完成");
        }
        private IEnumerator CompleteStoryShop(int shop)
        {
            if (shop == 0)
            {
            yield return Borrow(0); yield return Command(0);
            yield return Borrow(1); yield return Command(1);
            yield return Borrow(2); yield return Command(2, false);
            yield return CompleteRhythm(); yield return FinishReplies();
            }
            else if (shop == 1) { yield return Borrow(1); yield return Command(4); }
            else
            {
            yield return Borrow(1);
            yield return OpenStoryForge();
            for (int product = 0; product < 3; product++)
            {
                var forge = ResolveService<IUIService>().Get<ForgingView>();
                FindDeep<Button>(forge.transform, "Product" + product).onClick.Invoke();
                for (int count = 0; count <= product; count++)
                {
                    if (State.Forging.Available < State.Forging.ProductCost)
                    {
                        FindDeep<Button>(forge.transform, "BackButton").onClick.Invoke();
                        yield return WaitUntil("打造台关闭", () => ResolveService<IUIService>().Get<ForgingView>() == null, 5);
                        yield return Borrow(0); yield return Command(0); yield return Borrow(1); yield return OpenStoryForge();
                        forge = ResolveService<IUIService>().Get<ForgingView>();
                        FindDeep<Button>(forge.transform, "Product" + product).onClick.Invoke();
                    }
                    FindDeep<Button>(forge.transform, "CraftButton").onClick.Invoke(); yield return null;
                    while (State.Forging.IsForging)
                    { FindDeep<Button>(forge.transform, "Point" + State.Forging.NextPoint).onClick.Invoke(); yield return null; }
                }
            }
            var completedForge = ResolveService<IUIService>().Get<ForgingView>();
            FindDeep<Button>(completedForge.transform, "SubmitButton").onClick.Invoke();
            FindDeep<Button>(completedForge.transform, "BackButton").onClick.Invoke();
            yield return WaitUntil("打造提交并回到街面", () => State.Forging.Submitted && ResolveService<IUIService>().Get<ForgingView>() == null, 5);
            }
        }
        private IEnumerator ContinueToReunion()
        {
            yield return WaitUntil("三店成果齐备", () => State.CanEnterTavern && ResolveService<IUIService>().Get<ForgingView>() == null, 5);
            yield return Snapshot("三店真实成果进入背包");
            bool saved = false;
            yield return ResolveService<GameSession>().SaveNowAsync("spotlight_three_shops").ToCoroutine(value => saved = value);
            yield return Check("主线稳定等待处可保存", () => saved);
            yield return GoToShop(4); yield return Borrow(0); yield return Command(1, false);
            yield return WaitUntil("划拳可以操作", () => ResolveService<IUIService>().Get<TavernFingerGuessView>() != null, 5);
            var guess = ResolveService<IUIService>().Get<TavernFingerGuessView>();
            var random = new Game.Core.Simulation.XorShiftRandomStream(85);
            for (int round = 0; round < 400 && State.Tavern.BossDrunk < 100; round++)
            {
                if (!guess.IsAwaitingGuess) FindDeep<Button>(guess.transform, "Next").onClick.Invoke();
                FindDeep<Button>(guess.transform, "Finger" + random.Range(0, 6)).onClick.Invoke();
                FindDeep<Button>(guess.transform, "Call" + random.Range(0, 6)).onClick.Invoke();
                FindDeep<Button>(guess.transform, "Confirm").onClick.Invoke(); yield return null;
            }
            yield return Check("划拳获胜使主人喝酒并解锁挑战", () => State.CanChallengeTavern);
            yield return Snapshot("划拳揭晓");
            FindDeep<Button>(guess.transform, "Back").onClick.Invoke();
            yield return WaitUntil("离席恢复街面", () => ResolveService<IUIService>().Get<TavernFingerGuessView>() == null, 5);
            yield return Command(3, false); yield return AdvanceFormalDialogue(); yield return PlayBattle(); yield return AdvanceFormalDialogue();
            yield return Check("实际胜利续回正式主线", () => State.TavernCompleted && ResolveService<NarrativeService>().CurrentStageId == "enter_r0198", 5);
            yield return StoryPoint("enter_r0198"); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0215"); yield return AdvanceFormalDialogue();
            yield return Check("会面完成，停在下一段前", () => ResolveService<NarrativeService>().CurrentStageId == "enter_r0293", 5);
            yield return Snapshot("会面后本段完成");
            yield return ResolveService<GameSession>().SaveNowAsync("spotlight_reunion").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return ResolveService<GameSession>().ContinueAsync(1).ToCoroutine();
            yield return WaitCurtainRevealed();
            yield return Check("继续保持会面后的目标，不重播开场", () => ResolveService<NarrativeService>().CurrentStageId == "enter_r0293" &&
                ResolveService<QuestService>().CurrentMainId == 0 && ResolveService<QuestService>().TryGet(900, out var completed) && completed.State == QuestState.Completed, 8);
            yield return Snapshot("继续后保留正式进度");
        }
        private IEnumerator OpenStoryForge()
        {
            yield return OpenConversation(System.Array.Find(State.World.Actors, actor => actor.StableId == "shop_station_2"));
            yield return Choose("forge");
            yield return WaitUntil("打造界面打开", () => ResolveService<IUIService>().Get<ForgingView>() != null, 5);
        }
        private IEnumerator CompleteRhythm()
        {
            var rhythm = ResolveService<RhythmState>();
            for (int attempt = 0; attempt < 3 && !State.Theater.Completed; attempt++)
            {
            if (attempt > 0) { yield return FinishReplies(); yield return Command(2, false); }
            yield return WaitUntil("完整演奏就绪", () => rhythm.IsActivityOpen && ResolveService<IUIService>().Get<RhythmView>() != null && !DialogueRunning, 10);
            var view = ResolveService<IUIService>().Get<RhythmView>();
            yield return ClickWhenReady("开始演奏", () => FindDeep<Button>(view.transform, "StartButton"), () => rhythm.IsPlaying, 8);
            var edges = new List<(double time, int lane, bool press)>();
            for (int i = 0; i < rhythm.Rules.Count; i++)
            {
                edges.Add((rhythm.Rules.NoteTime(i) - .02, rhythm.Rules.NoteLane(i), true));
                edges.Add((rhythm.Rules.NoteType(i) == RhythmNoteType.Hold ? rhythm.Rules.NoteEnd(i) + .02 : rhythm.Rules.NoteTime(i) + .07, rhythm.Rules.NoteLane(i), false));
            }
            edges.Sort((a, b) => a.time.CompareTo(b.time));
            foreach (var edge in edges)
            {
                yield return WaitUntil("演奏按键时刻", () => !rhythm.IsPlaying || rhythm.SongSeconds >= edge.time, 8);
                if (!rhythm.IsPlaying) break;
                if (edge.press) yield return Input.Hold(rhythm.LaneAction(edge.lane)); else yield return Input.Release(rhythm.LaneAction(edge.lane));
            }
            yield return WaitUntil("本次演奏收尾", () => !rhythm.IsActivityOpen, 55);
            }
            yield return Check("演奏达标并正常回店", () => State.Theater.Completed && !rhythm.IsActivityOpen);
        }
    }
}

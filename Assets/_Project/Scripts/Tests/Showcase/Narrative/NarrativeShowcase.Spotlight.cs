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
        [Timeout(300000)]
        public IEnumerator Spotlight_ZhaoShisiWaitsBesideHumanTheater()
        {
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0026"); yield return AdvanceFormalDialogue();
            yield return StoryPoint("action_r0070"); yield return PlayBattle(); yield return AdvanceFormalDialogue();
            var narrative = ResolveService<NarrativeService>();
            var story = ResolveService<SpotlightStoryFlow>();
            var quests = ResolveService<QuestService>();
            var interact = ResolveService<IInputService>().Actions.Gameplay.Interact;
            yield return Step("李诏阳线索结束，任务指向人间戏台旁的赵十四");
            yield return Check("留在人间等待寻人，没有自动播放赵十四对白", () => story.Bindings.IsHumanStory &&
                narrative.CurrentStageId == "enter_r0083" && !DialogueRunning && quests.PresentationObjective == "寻找并与赵十四交谈", 5);
            yield return Input.Press(interact);
            yield return Check("远处按E不跳过寻人", () => narrative.CurrentStageId == "enter_r0083" && !DialogueRunning, 2);
            bool saved = false;
            yield return ResolveService<GameSession>().SaveNowAsync("before_zhao_shisi").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return ResolveService<GameSession>().ContinueAsync(1).ToCoroutine(); yield return WaitCurtainRevealed();
            yield return Check("继续后仍须到戏台旁主动交谈", () => narrative.CurrentStageId == "enter_r0083" && !DialogueRunning, 5);
            yield return StoryPoint("enter_r0083", false);
            var point = story.Bindings.Find("enter_r0083");
            var puppet = point.Anchor.GetComponentInChildren<Game.CharacterPuppet.ChibiPuppet>();
            Assert.That(puppet, Is.Not.Null);
            Assert.That(puppet.name, Is.EqualTo("Chibi_zhao_shisi"));
            Assert.That(point.Prompt.Name, Is.EqualTo("赵十四"));
            yield return Snapshot("人间戏台旁_赵十四等待交谈");
            yield return Input.Press(interact);
            yield return WaitUntil("赵十四旁按E才打开原21006对白", () => DialogueRunning && narrative.CurrentStageId == "talk_21006", 5);
            yield return Check("对白期间赵十四仍在原地且不可重复触发", () => point.Anchor.gameObject.activeInHierarchy && !point.CanInteract, 3);
            yield return WaitUntil("赵十四首句完整显示", () => ResolveService<Game.Dialogue.DialogueRules>().Current != null &&
                ResolveService<Game.Dialogue.DialogueRules>().Current.Id == "r0085" &&
                ResolveService<Game.Dialogue.DialogueRules>().Phase == Game.Dialogue.DialogueSaveData.Phase.AwaitAdvance, 8);
            yield return Snapshot("人间戏台旁_赵十四原剧情");
            yield return AdvanceFormalDialogue();
            yield return Check("原线索剧情结束才转入入镜目标，赵十四仍留在戏台旁", () =>
                narrative.CurrentStageId == "enter_r0171" && point.Anchor.gameObject.activeInHierarchy && !point.CanInteract, 5);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spotlight_MentorTeachingRequiresInteractionAndBattle()
        {
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0026"); yield return AdvanceFormalDialogue();
            var narrative = ResolveService<NarrativeService>();
            var battle = ResolveService<Game.Battle.BattleFlow>();
            yield return Step("教学节点保留，等待与李诏阳切磋");
            yield return Check("教学不自动跳过，现场显示切磋目标", () => narrative.CurrentStageId == "action_r0070" &&
                !battle.IsBattleRunning && ResolveService<QuestService>().PresentationObjective.Contains("切磋"), 5);
            yield return StoryPoint("action_r0070");
            Assert.That(narrative.CurrentStoryId, Is.EqualTo("spotlight_mentor_duel"));
            var presenter = ResolveService<Game.Battle.IBattlePresenter>() as Game.Battle.BattleScenePresenter;
            yield return Check("教学战斗界面已显示并等待出招", () => battle.IsBattleEngaged &&
                presenter != null && presenter.View != null && presenter.IsAwaitingCommand, 15);
            yield return Snapshot("李诏阳回合制教学入口恢复");
            yield return PlayBattle(); yield return AdvanceFormalDialogue();
            yield return Check("教学胜利后续回正式剧情", () => narrative.CurrentStoryId == "spotlight_current" &&
                narrative.CurrentStageId != "action_r0070" && !battle.IsBattleRunning, 10);
            yield return Snapshot("教学胜利后续回主线");
        }

        [UnityTest]
        [Timeout(400000)]
        public IEnumerator Spotlight_PatrolHitRemainsRealtimeCaught()
        {
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0026"); yield return NarrationToMirror();
            yield return StoryPoint("enter_r0171"); yield return WaitCurtainRevealed();
            var step = ResolveService<Game.Monster.EncounterStep>();
            var patrol = step.Taming.GetTarget("patrol-a");
            var player = ResolveService<Game.Player.PlayerModel>();
            var battle = ResolveService<Game.Battle.BattleFlow>();
            var arena = ResolveService<Game.Battle.BattleArena>();
            Assert.That(battle.CanEscalate(patrol.Kind.KindId), Is.False);
            // 受控命中夹具：原通路由轮回堂用例实走；此处只隔离真实Boot配置下的命中与抓捕结算。
            Vector2 start = player.Position + Vector2.left * .6f;
            patrol.Reset(new[] { start, start + Vector2.right * .1f });
            int health = player.Health;
            bool hostile = false, escalated = false;
            yield return WaitUntil("现场普通巡逻真实命中玩家", () =>
            {
                hostile |= patrol.Model.Mode == Game.Monster.MonsterMode.Hostile;
                escalated |= step.IsEscalating || battle.IsBattleRunning || arena.IsLoaded;
                return player.Health < health;
            }, 5);
            yield return Check("普通巡逻受控命中后仍为实时抓捕", () => hostile && player.Health < health &&
                step.Facts.IsTrue(Game.Stealth.StealthFactKeys.ChaseCaught) &&
                ResolveService<Game.Player.PlayerRules>().Knockdown.IsDowned &&
                !escalated && !step.IsEscalating && !battle.IsBattleRunning && !arena.IsLoaded, 3);
            Assert.That(ResolveService<NarrativeService>().CurrentStageId, Is.EqualTo("enter_r0171"));
            yield return Snapshot("普通巡逻命中_实时抓捕无回合制");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Spotlight_ShopsBeforeUnlock_ExplainNextStepWithoutGrantingGameplay()
        {
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0026"); yield return NarrationToMirror();
            yield return StoryPoint("enter_r0171"); yield return WaitCurtainRevealed();
            var narrative = ResolveService<NarrativeService>();
            var saves = ResolveService<Game.Core.Save.ISaveService>();
            var loot = ResolveService<Game.Loot.LootService>();
            var registry = ResolveService<Game.Interaction.IInteractionRegistry>();
            var focus = ResolveService<Game.Interaction.InteractionFocus>();
            var input = ResolveService<IInputService>();
            var ui = ResolveService<IUIService>();
            var dialogueRules = ResolveService<Game.Dialogue.DialogueRules>();
            Assert.That(ResolveService<SpotlightStoryFlow>().Bindings.IsHumanStory, Is.False);
            Assert.That(narrative.CurrentStageId, Is.EqualTo("enter_r0171"));
            for (int phase = 0; phase < 2; phase++)
            {
                string stage = phase == 0 ? "enter_r0171" : "enter_r0188";
                if (phase > 0)
                {
                    // 后续节点是受控夹具，只验交互门槛，不冒充已走过轮回堂至坊市的完整通路。
                    narrative.CaptureIntoPartition(); saves.Get<NarrativeSaveData>().Current.StageId = stage;
                    narrative.ReloadFromSave(); yield return null;
                }
                yield return GoToShop(1);
                var customer = System.Array.Find(State.World.Actors, actor => actor.StableId == "shop_customer1");
                Assert.That(customer, Is.Not.Null);
                yield return MoveTo(Object.FindObjectOfType<Game.Monster.EncounterSceneView>().ToLogicPosition(customer.Position));
                focus.RequestFocus(customer); yield return null;
                yield return Check("前置未完仍显示顾客乙的E交互，F未开放", () => focus.Current == customer && customer.CanInteract &&
                    !customer.CanInteractSecondary && ui.Get<Game.Interaction.InteractPromptHudView>() != null &&
                    !ui.Get<Game.Interaction.InteractPromptHudView>().IsSecondaryShown, 5);
                Assert.That(input.Actions.Gameplay.enabled, Is.True);
                Assert.That(registry.Candidates, Does.Contain(customer));
                Assert.That(Vector3.Distance(registry.Actor.Anchor.position, customer.Position), Is.LessThanOrEqualTo(customer.InteractionRadius));
                var itemsBefore = new Dictionary<int, int>(loot.Items);
                var model = State.Taming.GetTarget(customer.StableId).Model;
                int healthBefore = model.Health;
                yield return Input.Hold(input.Actions.Gameplay.Sneak);
                yield return Input.Press(input.Actions.Gameplay.Execute);
                yield return Input.Release(input.Actions.Gameplay.Sneak);
                customer.InteractSecondary();
                Assert.That(DialogueRunning, Is.False, "F不能退化为E对白");
                Assert.That(model.Health, Is.EqualTo(healthBefore), "前置未完不能刺杀取皮");
                yield return Input.Press(input.Actions.Gameplay.Interact);
                yield return WaitUntil("顾客乙说明当前剧情去向", () => DialogueRunning, 5);
                yield return ShowChoices();
                Assert.That(dialogueRules.Current.Text, Does.Contain(phase == 0 ? "轮回堂" : "坊市入口"));
                Assert.That(dialogueRules.Current.Choices.Length, Is.EqualTo(1));
                Assert.That(dialogueRules.Current.Choices[0].Id, Is.EqualTo("close"), "不能提前开放店铺玩法选项");
                yield return Snapshot("前置店铺交互_" + stage);
                yield return Choose("close"); yield return FinishReplies();
                Assert.That(narrative.CurrentStageId, Is.EqualTo(stage), "询问店铺不能代替主线现场交互");
                CollectionAssert.AreEquivalent(itemsBefore, loot.Items);
                Assert.That(State.Clothing.Received || State.Theater.Completed || State.Forging.Submitted, Is.False);
            }
            narrative.CaptureIntoPartition(); saves.Get<NarrativeSaveData>().Current.StageId = "action_r0197";
            narrative.ReloadFromSave(); yield return null;
            yield return GoToShop(1);
            var unlockedCustomer = System.Array.Find(State.World.Actors, actor => actor.StableId == "shop_customer1");
            yield return OpenConversation(unlockedCustomer); yield return ShowChoices();
            Assert.That(System.Array.Exists(dialogueRules.Current.Choices, choice => choice.Id == "leave"), Is.True, "197恢复正常人物对白");
            yield return Choose("leave"); yield return FinishReplies();
            Assert.That(unlockedCustomer.CanInteractSecondary, Is.True, "197恢复F取皮入口");
            yield return Borrow(1);
            Assert.That(loot.Items.ContainsKey(1905), Is.True);
        }

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
            AssertLegacyExamplesHidden(story.Bindings.gameObject.scene);
            yield return StoryPoint("enter_r0171", false);
            var guard = story.Bindings.Find("enter_r0171");
            Assert.That(guard.Position.x, Is.LessThan(-20), "必须位于上层左侧桥后的轮回堂");
            Assert.That(guard.Position.z, Is.GreaterThan(10));
            Assert.That(guard.Prompt.Verb, Is.EqualTo("问询"));
            var guardParts = guard.Anchor.GetComponentsInChildren<SpriteRenderer>(true);
            Assert.That(guardParts, Is.Not.Empty, "值守必须使用现场已有小人");
            System.Func<bool> guardVisible = () => guard.Anchor.gameObject.activeInHierarchy &&
                System.Array.Exists(guardParts, part => part != null && part.enabled && part.gameObject.activeInHierarchy);
            yield return Check("问询前值守小人在原地显示", guardVisible, 3);
            yield return Snapshot("真实轮回堂门前_主动问询之前");
            yield return MoveTo(new Vector2(-20f, 17.7f));
            yield return Input.Press(interact); guard.Interact();
            yield return Check("离开值守范围后E不推进", () => narrative.CurrentStageId == "enter_r0171" && !DialogueRunning);
            yield return MoveTo(new Vector2(guard.Position.x, 19.8f), .15f);
            yield return Input.Press(interact);
            yield return WaitUntil("实际值守旁E才播放21008", () => DialogueRunning && narrative.CurrentStageId == "talk_21008", 5);
            yield return Check("21008首句时值守仍显示且不能重复交互", () => guardVisible() && !guard.CanInteract, 3);
            var dialogueRules = ResolveService<Game.Dialogue.DialogueRules>();
            yield return WaitUntil("问询首句显示完成", () => dialogueRules.Current != null && dialogueRules.Current.Id == "r0173" &&
                dialogueRules.Phase == Game.Dialogue.DialogueSaveData.Phase.AwaitAdvance, 8);
            yield return Input.Press(ResolveService<IInputService>().Actions.Dialogue.Advance);
            yield return WaitUntil("值守说出方相面具台词", () => dialogueRules.Current != null && dialogueRules.Current.Id == "r0175" &&
                dialogueRules.Phase == Game.Dialogue.DialogueSaveData.Phase.AwaitAdvance, 8);
            yield return Check("方相面具台词时值守小人持续显示", guardVisible, 3);
            yield return Snapshot("真实值守问询");
            yield return AdvanceFormalDialogue();
            for (int i = 0; i < 3; i++) yield return Input.Press(interact);
            yield return Check("值守结束仍需走到坊市，不自动播放窗边剧情", () => narrative.CurrentStageId == "enter_r0188" && !DialogueRunning &&
                quests.PresentationObjective.Contains("坊市"), 5);
            yield return Check("问询结束后值守仍在原地且不再接受171交互", () => guardVisible() && !guard.CanInteract, 3);
            yield return Snapshot("值守问询结束_人物保留");
            yield return StoryPoint("enter_r0188"); yield return AdvanceFormalDialogue();
            yield return Check("实际坊市交互后才恢复三店目标，旧地点教程不残留", () => narrative.CurrentStageId == "action_r0197" &&
                quests.PresentationObjective.Length == 0 &&
                FindDeep<TMPro.TMP_Text>(ui.Get<QuestHudView>().transform, "Objective").text.Contains("戏班"), 5);
            guard.Interact();
            narrative.ReloadFromSave();
            yield return null;
            AssertLegacyExamplesHidden(story.Bindings.gameObject.scene);
            yield return Check("197阶段值守实体仍在，错误阶段交互不重播或推进", () => guardVisible() && !guard.CanInteract &&
                narrative.CurrentStageId == "action_r0197" && !DialogueRunning, 3);
            yield return Snapshot("坊市到达后_原目标栏恢复");
        }

        private static void AssertLegacyExamplesHidden(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (string name in new[] { "Npc_SampleBoss", "Npc_Stage9Boss", "Npc_BattleDrill", "Trigger_VillageEntrance" })
            {
                Transform found = null;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var child in root.GetComponentsInChildren<Transform>(true))
                        if (child.name == name) found = child;
                Assert.That(found != null, Is.True, "缺少旧示例负对照：" + name);
                Assert.That(found.gameObject.activeInHierarchy, Is.False, "正式流程不能重新开启旧示例：" + name);
            }
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
            yield return Step("人间出生：同一布局，保留对白与回合制教学");
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
            Assert.That(story.Bindings.Points.Length, Is.EqualTo(3));
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
            InspectStoryPresentation = true;
            yield return Ready(0);
            yield return Step("三店自由探索：戏班、衣肆、金银行");
            yield return CompleteStoryShop(0);
            yield return GoToShop(1); yield return CompleteStoryShop(1);
            yield return GoToShop(2); yield return CompleteStoryShop(2);
            yield return ContinueToReunion();
        }
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spotlight_WindowCloseupRestoresInteractionAndDoesNotRepeat()
        {
            InspectStoryPresentation = true;
            yield return EnterStoryWorld(); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0026"); yield return NarrationToMirror();
            yield return StoryPoint("enter_r0171"); yield return WaitCurtainRevealed();
            var narrative = ResolveService<NarrativeService>();
            var story = ResolveService<SpotlightStoryFlow>();
            var entrance = story.Bindings.Find("enter_r0188");
            yield return Step("先走过坊市入口，轮回堂问询前不能出现光点或特写", hold: 0);
            yield return GoToWorldPoint(entrance.Position, .4f);
            yield return Check("轮回堂未完成：入口光点隐藏，窗边人影隐藏，镜头仍跟随玩家", () =>
                narrative.CurrentStageId == "enter_r0171" && !entrance.Marker.activeInHierarchy &&
                !story.Bindings.WindowFigure.activeInHierarchy && story.CanSave &&
                Camera.main.GetComponent<Game.IsometricExploration.SmoothCameraFollow>().enabled, 3);
            yield return Snapshot("轮回堂前_坊市入口不触发");
            yield return StoryPoint("enter_r0171");
            yield return Check("轮回堂问询中：入口光点与窗边人影仍隐藏", () =>
                narrative.CurrentStageId == "talk_21008" && !entrance.Marker.activeInHierarchy &&
                !story.Bindings.WindowFigure.activeInHierarchy, 3);
            yield return AdvanceFormalDialogue();
            yield return Check("轮回堂问询结束才出现入口光点，远处不运镜", () =>
                narrative.CurrentStageId == "enter_r0188" && entrance.Marker.activeInHierarchy &&
                story.Bindings.WindowFigure.activeInHierarchy && story.CanSave, 3);
            yield return StoryPoint("enter_r0188");
            yield return AdvanceFormalDialogue();
            yield return Check("回镜后E接原剧情并开放三店", () => narrative.CurrentStageId == "action_r0197", 5);
            yield return Snapshot("窗边特写后_原剧情接续");
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
            Assert.That(ResolveService<SpotlightStoryFlow>().Bindings.WindowFigure.activeSelf, Is.False, "战后窗边人影已离场");
            var wife = ResolveService<SpotlightStoryFlow>().Bindings.Find("enter_r0215").Anchor.GetComponentInChildren<Game.CharacterPuppet.ChibiPuppet>(true);
            Assert.That(wife.name, Is.EqualTo("Chibi_wife"), "会面使用何素拆件动画");
            yield return Snapshot("战后何素现身会面");
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

// 职责：五店回放走正常Boot、原3D场景和真实移动/交互，不再打开独立试玩。
using System.Collections;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.UI;
using Game.Interaction;
using Game.Dialogue;
using TMPro;
using Game.Monster;
using Game.Narrative;
using Game.Battle;
using Game.ShopPlaytest;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace Game.Tests.Showcase.ShopPlaytest
{
    public abstract class ShopPlaytestShowcase : ShowcaseScenario
    {
        protected override string ScenePath => null;
        protected override bool LoadBootScene => true;
        protected ShopPlaytestState State { get; private set; }
        protected bool DialogueRunning => ResolveService<DialogueService>().IsRunning;
        private int originalActors;
        protected IEnumerator Ready(int shop)
        {
            yield return EnterStoryWorld();
            yield return FormalPrologue();
            Assert.That(State.World, Is.Not.Null);
            yield return GoToShop(shop);
            originalActors = Object.FindObjectsOfType<ShopPlaytestActor>(true).Length;
        }
        protected IEnumerator EnterStoryWorld()
        {
            yield return EnterWorldFromTitle(enterTimeout: 60f, expectedScene: "SampleScene");
            State = ResolveService<ShopPlaytestState>();
            Assert.That(ResolveService<IGameFlow>().Current, Is.TypeOf<MonsterEncounterState>());
        }
        protected IEnumerator GoToShop(int shop)
        {
            Input.Prime();
            var station = State.World.Station(shop);
            yield return GoToWorldPoint(station.position);
            yield return Check("走入店前范围显示对应人物", () => State.ActiveShop == shop, 3);
            yield return Check("当前店前操作点或本店NPC成为统一交互焦点", () =>
                ResolveService<IInteractionFocus>().Current is ShopPlaytestActor actor && actor.Shop == shop && actor.CanInteract, 3);
            yield return Check("五店不加载操作面板", () => ResolveService<IUIService>().Get<ShopPlaytestView>() == null);
        }
        protected IEnumerator GoToWorldPoint(Vector3 position, float stopDistance = .25f)
        {
            var encounter = Object.FindObjectOfType<EncounterSceneView>();
            Vector2 target = encounter.ToLogicPosition(position);
            // 路线按模型局部坐标摆定，随整张地图的旋转同步。
            Transform map = GameObject.Find("Environment_FiveShopsPlaceholder").transform;
            Vector3 currentLocal = map.InverseTransformPoint(encounter.ToScenePosition(State.ControlledPosition));
            Vector3 targetLocal = map.InverseTransformPoint(position);
            float currentStreet = currentLocal.z > 404 ? 884 : 284;
            float targetStreet = targetLocal.z > 404 ? 884 : 284;
            yield return MoveTo(MapPoint(map, currentLocal.x, currentStreet));
            if (currentStreet != targetStreet)
            {
                yield return MoveTo(MapPoint(map, 584, currentStreet));
                yield return MoveTo(MapPoint(map, 584, targetStreet));
            }
            yield return MoveTo(MapPoint(map, targetLocal.x, targetStreet));
            yield return MoveTo(target, stopDistance);
        }
        protected IEnumerator FormalPrologue()
        {
            var story = ResolveService<SpotlightStoryFlow>();
            if (story == null || story.Bindings == null) yield break;
            yield return AdvanceFormalDialogue();
            if (ResolveService<NarrativeService>().CurrentStageId == "enter_r0026")
            {
                yield return StoryPoint("enter_r0026");
            }
            yield return NarrationToMirror();
            yield return StoryPoint("enter_r0171");
            yield return WaitUntil("真实切换至妖界五店", () => State.World != null && !ResolveService<SpotlightStoryFlow>().Bindings.IsHumanStory, 60);
            yield return DemonArrivalToShops();
        }
        protected IEnumerator DemonArrivalToShops()
        {
            yield return StoryPoint("enter_r0171"); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0188"); yield return AdvanceFormalDialogue();
            float deadline = Time.realtimeSinceStartup + 100;
            while (ResolveService<NarrativeService>().CurrentStageId != "action_r0197" && Time.realtimeSinceStartup < deadline)
            { yield return AdvanceFormalDialogue(); yield return null; }
            yield return Check("序章与入镜完成，主线等待三店和酒肆真实结果", () => ResolveService<NarrativeService>().CurrentStageId == "action_r0197", 5);
        }
        protected IEnumerator NarrationToMirror()
        {
            float deadline = Time.realtimeSinceStartup + 100;
            while (ResolveService<NarrativeService>().CurrentStageId != "enter_r0171" && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(ResolveService<BattleFlow>().IsBattleRunning, Is.False, "人间不应进入强制战斗");
                yield return AdvanceFormalDialogue(); yield return null;
            }
            yield return WaitUntil("人间对白自然衔接到唯一穿越点", () => ResolveService<NarrativeService>().CurrentStageId == "enter_r0171" && ResolveService<SpotlightStoryFlow>().CanSave, 5);
        }
        protected IEnumerator StoryPoint(string stage, bool interact = true)
        {
            var narrative = ResolveService<NarrativeService>();
            yield return WaitUntil("正式剧情到达 " + stage, () => narrative.CurrentStageId == stage && !narrative.IsBusy, 10);
            var point = ResolveService<SpotlightStoryFlow>().Bindings.Find(stage);
            bool crossing = ResolveService<SpotlightStoryFlow>().Bindings.IsHumanStory && stage == "enter_r0171";
            Assert.That(point, Is.Not.Null, stage);
            foreach (var other in ResolveService<SpotlightStoryFlow>().Bindings.Points)
                if (other.StageId != stage) Assert.That(other.CanInteract, Is.False, "错误节点不能交互：" + other.StageId);
            // 人物有实体碰撞，走到交互范围即可；不能要求穿进人物中心。
            if (ResolveService<SpotlightStoryFlow>().Bindings.IsHumanStory)
            {
                if (stage == "enter_r0171")
                {
                    yield return MoveTo(new Vector2(-8f, 282.5f));
                    yield return MoveTo(new Vector2(14f, 282.5f));
                    yield return MoveTo(new Vector2(22.5f, 282.5f));
                    yield return MoveTo(new Vector2(22.5f, 286.8f));
                }
                yield return MoveTo(new Vector2(point.Position.x, point.Position.z), .8f);
            }
            else if (stage == "enter_r0171")
            {
                yield return GoToWorldPoint(new Vector3(10.33f, 4.85f, 4.4f));
                yield return MoveTo(new Vector2(10.33f, 10.5f));
                yield return MoveTo(new Vector2(10.33f, 22f));
                // 桥口在东侧；从北侧切入会撞到模型上的木栏杆。
                yield return MoveTo(new Vector2(-8.2f, 22f));
                yield return MoveTo(new Vector2(-8.2f, 17.7f));
                yield return MoveTo(new Vector2(-20f, 17.7f));
                yield return MoveTo(new Vector2(point.Position.x, 19.8f), .15f);
            }
            else if (stage == "enter_r0188")
            {
                yield return MoveTo(new Vector2(-20f, 19.8f));
                yield return MoveTo(new Vector2(-20f, 17.7f));
                yield return MoveTo(new Vector2(-8.2f, 17.7f));
                yield return MoveTo(new Vector2(-8.2f, 22f));
                yield return MoveTo(new Vector2(10.33f, 22f));
                yield return MoveTo(new Vector2(10.33f, 10.5f));
                yield return MoveTo(new Vector2(10.33f, 4.4f));
                yield return GoToWorldPoint(point.Position, .8f);
            }
            else yield return GoToWorldPoint(point.Position, .8f);
            yield return Check("节点与区域一致，现场交互可用", () => ResolveService<IInteractionFocus>().Current == point && point.CanInteract, 5);
            var guide = ResolveService<IUIService>().Get<StoryGuideHudView>();
            yield return Check("对应节点教程显示且不挡现场交互", () => guide != null && guide.Text.Length > 0 && !guide.GetComponent<CanvasGroup>().blocksRaycasts, 3);
            if (stage == "enter_r0026") yield return Snapshot("节点区域软引导");
            if (!interact) yield break;
            yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Interact);
            yield return WaitUntil("真实交互推进节点或切换妖界", () => crossing
                ? ResolveService<SpotlightStoryFlow>().Bindings != null && !ResolveService<SpotlightStoryFlow>().Bindings.IsHumanStory
                : narrative.CurrentStageId != stage, crossing ? 60 : 5);
        }
        protected IEnumerator AdvanceFormalDialogue()
        {
            var narrative = ResolveService<NarrativeService>();
            var actions = ResolveService<IInputService>().Actions;
            float deadline = Time.realtimeSinceStartup + 90;
            yield return null;
            while ((narrative.IsBusy || DialogueRunning || narrative.CurrentStoryId.Length == 0) && Time.realtimeSinceStartup < deadline)
            {
                if (DialogueRunning)
                {
                    var rules = ResolveService<DialogueRules>();
                    var ui = ResolveService<IUIService>();
                    var confirm = ui.Get<DialogueSkipConfirmView>();
                    if (confirm != null) FindDeep<Button>(confirm.transform, "ConfirmButton").onClick.Invoke();
                    else
                    {
                        var view = ui.Get<DialogueView>();
                        var skip = view == null ? null : FindDeep<Button>(view.transform, "SkipButton");
                        if (skip != null && skip.interactable) skip.onClick.Invoke();
                        else if (rules.Phase == DialogueSaveData.Phase.AwaitChoice) yield return Choose(rules.Current.Choices[0].Id);
                        else yield return Input.Press(actions.Dialogue.Advance);
                    }
                    yield return null;
                }
                else yield return null;
            }
            Assert.That(narrative.IsBusy || DialogueRunning, Is.False, "正式对白没有正常收尾");
        }
        protected IEnumerator PlayBattle()
        {
            var battle = ResolveService<BattleFlow>();
            var presenter = ResolveService<IBattlePresenter>() as BattleScenePresenter;
            var input = ResolveService<IInputService>();
            yield return WaitUntil("战斗舞台打开", () => battle.IsBattleEngaged && presenter != null && presenter.View != null, 15);
            float deadline = Time.realtimeSinceStartup + 120;
            while (battle.IsBattleEngaged && Time.realtimeSinceStartup < deadline)
            {
                if (DialogueRunning) yield return AdvanceFormalDialogue();
                else if (presenter.IsAwaitingCommand && presenter.View != null)
                    yield return Input.Press(presenter.View.IsSkillLit(2) ? input.Actions.Dialogue.Choice3 : input.Actions.Dialogue.Choice1);
                else yield return null;
            }
            yield return WaitUntil("战斗结案完成，允许战后对白接续", () => !battle.IsBattleRunning, 15);
        }
        private static Vector2 MapPoint(Transform map, float x, float z)
        {
            Vector3 point = map.TransformPoint(new Vector3(x, 0, z));
            return new Vector2(point.x, point.z);
        }
        protected Vector2 MapDirection(Vector3 direction)
        {
            Vector3 world = GameObject.Find("Environment_FiveShopsPlaceholder").transform.TransformDirection(direction);
            return new Vector2(world.x, world.z);
        }
        protected IEnumerator MoveTo(Vector2 target, float stopDistance = .25f)
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (Vector2.Distance(State.ControlledPosition, target) > stopDistance && Time.realtimeSinceStartup < deadline)
            {
                var performance = ResolveService<Game.Performance.IPerformanceService>();
                if (performance.IsRunning || DialogueRunning)
                {
                    Input.ReleaseStick();
                    float pausedAt = Time.realtimeSinceStartup;
                    float talkDeadline = pausedAt + 25;
                    while ((performance.IsRunning || DialogueRunning) && Time.realtimeSinceStartup < talkDeadline)
                    {
                        // 五店回放不验沿途演出，复用已有回放跳过入口，仍等待正常清理。
                        if (performance.IsRunning) { performance.Skip(); yield return null; }
                        else if (DialogueRunning)
                        {
                            var rules = ResolveService<DialogueRules>();
                            if (rules.Phase == DialogueSaveData.Phase.AwaitChoice) yield return Choose(rules.Current.Choices[0].Id);
                            else yield return Input.Press(ResolveService<IInputService>().Actions.Dialogue.Advance);
                        }
                        else yield return null;
                    }
                    Assert.That(performance.IsRunning || DialogueRunning, Is.False, "途中对白或演出未收尾");
                    deadline += Time.realtimeSinceStartup - pausedAt;
                }
                Vector2 delta = target - State.ControlledPosition;
                yield return Walk(delta.normalized, Mathf.Min(.15f, delta.magnitude / 4));
            }
            Input.ReleaseStick();
            float tolerance = Mathf.Max(.5f, stopDistance + .1f);
            if (Vector2.Distance(State.ControlledPosition, target) >= tolerance) yield return Snapshot("实际移动受阻");
            Assert.That(Vector2.Distance(State.ControlledPosition, target), Is.LessThan(tolerance),
                "原主场景实际移动到目标失败：当前=" + State.ControlledPosition + "，目标=" + target);
        }
        protected IEnumerator Borrow(int roleIndex)
        {
            int index = 0; ShopPlaytestActor target = null;
            foreach (ShopPlaytestActor actor in State.World.Actors)
                if (actor.Shop == State.ActiveShop && actor.Role.Length > 0) { if (index++ == roleIndex) { target = actor; break; } }
            Assert.That(target, Is.Not.Null);
            Assert.That(ShopSkinRules.ItemId(target.Role), Is.Not.Zero, "该对象只供交谈，不能取皮");
            var model = State.Taming.GetTarget(target.StableId).Model;
            var actions = ResolveService<IInputService>().Actions.Gameplay;
            var loot = ResolveService<Game.Loot.LootService>();
            int itemId = ShopSkinRules.ItemId(target.Role);
            if (!loot.Items.ContainsKey(itemId))
            {
                yield return MoveTo(model.Position - model.Facing.normalized * .8f);
                yield return SelectActor(target);
                yield return Input.Press(actions.Execute);
                yield return FinishReplies();
                yield return Check("没有潜行不能刺杀或取得皮", () => model.Health > 0 && !loot.Items.ContainsKey(itemId));
                yield return SelectActor(target);
                yield return Input.Hold(actions.Sneak);
                yield return Input.Press(actions.Execute);
                yield return Input.Release(actions.Sneak);
                yield return FinishReplies();
                yield return Check("潜行绕背刺杀后皮入包，尚未取得身份", () =>
                    model.Health == 0 && loot.Items.ContainsKey(itemId) && State.CurrentRole != target.Role, 3);
            }
            yield return MoveTo(model.Position);
            yield return SelectActor(target);
            yield return Check("借用按钮对应目标NPC", () => ResolveService<IInteractionFocus>().Current == target, 3);
            var hud = ResolveService<IUIService>().Get<InteractPromptHudView>();
            Assert.That(hud.IsSecondaryShown, Is.True);
            Assert.That(DialogueRunning, Is.False, "无需先对话即可借身份");
            yield return Snapshot("F键借用身份_" + target.Role);
            yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Execute);
            yield return FinishReplies();
            yield return Check("换皮用玩家本体移动，职业与外观归属同步", () => State.Taming.CurrentControlId == State.Taming.PlayerId &&
                State.CurrentSkinActorId == target.StableId && State.CurrentRole == target.Role, 3);
            Assert.That(State.Taming.TryTame(target.StableId), Is.False, "旧驯服入口不能绕过取皮");
            Vector2 beforeWear = State.ControlledPosition;
            State.ReturnBody();
            using (ResolveService<Game.Core.Timing.IWorldPauseService>().Acquire(this))
            {
                var used = ResolveService<Game.Inventory.ItemUseService>().TryUse(itemId, null);
                Assert.That(used.IsOk, Is.True, used.Effect.Reason);
                Assert.That(used.ConsumedCount, Is.Zero);
            }
            Assert.That(State.CurrentSkinActorId, Is.EqualTo(target.StableId));
            Assert.That(loot.Items[itemId], Is.EqualTo(1), "重复使用不销毁皮");
            Assert.That(Vector2.Distance(beforeWear, State.ControlledPosition), Is.LessThan(.01f), "穿皮不传送");
            var view = Object.FindObjectOfType<EncounterSceneView>();
            var appearance = view.PlayerBody.GetComponentInChildren<Game.CharacterPuppet.ChibiPuppetMotion>();
            Assert.That(appearance, Is.Not.Null);
            Assert.That(appearance.TrackedRoot, Is.EqualTo(view.PlayerBody));
            yield return Snapshot("换皮后外观与玩家位移绑定_" + target.Role);
            yield return MoveTo(Object.FindObjectOfType<EncounterSceneView>().ToLogicPosition(State.World.Station(State.ActiveShop).position));
        }
        protected IEnumerator CheckPreplacedObjects() => Check("各操作没有新生成占位角色", () => Object.FindObjectsOfType<ShopPlaytestActor>(true).Length == originalActors);
        protected IEnumerator OpenConversation(ShopPlaytestActor actor)
        {
            yield return FinishReplies();
            yield return MoveTo(Object.FindObjectOfType<EncounterSceneView>().ToLogicPosition(actor.Position));
            if (ResolveService<IInteractionFocus>().Current == actor)
                yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Interact);
            else
            {
                yield return Check("点击对应人物：" + actor.StableId + "，可交互=" + actor.CanInteract,
                    () => actor.CanInteract, 3);
                yield return SelectActor(actor);
                yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Interact);
            }
            yield return Check("现场人物打开正式对白", () => DialogueRunning, 4);
        }
        private IEnumerator SelectActor(ShopPlaytestActor actor)
        {
            ExecuteEvents.Execute(actor.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
            yield return null;
            yield return Check("点选对应人物成为统一焦点", () => ResolveService<IInteractionFocus>().Current == actor, 3);
        }
        protected IEnumerator ShowChoices()
        {
            var rules = ResolveService<DialogueRules>();
            var input = ResolveService<IInputService>();
            float deadline = Time.realtimeSinceStartup + 12;
            while (rules.Phase != DialogueSaveData.Phase.AwaitChoice && Time.realtimeSinceStartup < deadline)
                yield return Input.Press(input.Actions.Dialogue.Advance);
            yield return WaitUntil("对白选项可操作", () => ResolveService<DialogueController>().IsStable &&
                ResolveService<IUIService>().Get<DialogueView>() != null &&
                ResolveService<IUIService>().Get<DialogueView>().GetComponent<CanvasGroup>().interactable, 5);
        }
        protected IEnumerator Choose(string id)
        {
            yield return ShowChoices();
            var rules = ResolveService<DialogueRules>();
            var choice = System.Array.Find(rules.Current.Choices, item => item.Id == id);
            Assert.That(choice, Is.Not.Null, "当前人物没有选项：" + id);
            Button button = null;
            foreach (var row in ResolveService<IUIService>().Get<DialogueView>().GetComponentsInChildren<Button>())
            {
                var label = row.GetComponentInChildren<TMP_Text>();
                if (label != null && label.text.EndsWith(". " + choice.Text)) { button = row; break; }
            }
            Assert.That(button, Is.Not.Null);
            long generation = rules.Generation;
            button.onClick.Invoke();
            yield return WaitUntil("人物对白收尾", () => !DialogueRunning || rules.Generation != generation, 5);
            yield return null;
        }
        protected IEnumerator Command(int index, bool finishReply = true)
        {
            ShopPlaytestActor target = null;
            string id = State.ActiveShop == 4 && index != 0 ? index == 1 || index == 3 ? "shop_boss" :
                index == 4 ? "shop_customer_b" : "shop_customer_a" : State.ActiveShop == 1 ? "shop_tailor" : "shop_station_" + State.ActiveShop;
            foreach (var actor in State.World.Actors) if (actor.StableId == id) target = actor;
            Assert.That(target, Is.Not.Null);
            yield return OpenConversation(target);
            yield return Choose("command" + index);
            if (finishReply) yield return FinishReplies();
        }
        protected IEnumerator FinishReplies()
        {
            float deadline = Time.realtimeSinceStartup + 15;
            yield return null;
            while ((DialogueRunning || State.HasPendingReply) && Time.realtimeSinceStartup < deadline)
            {
                if (DialogueRunning) yield return Choose("close");
                else yield return null;
            }
            Assert.That(DialogueRunning || State.HasPendingReply, Is.False, "结果对白未收尾");
        }
    }
}

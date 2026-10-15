// 职责：五店回放走正常Boot、原3D场景和真实移动/交互，不再打开独立试玩。
using System.Collections;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.UI;
using Game.Interaction;
using Game.Dialogue;
using TMPro;
using Game.Monster;
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
            yield return EnterWorldFromTitle(enterTimeout: 60f);
            State = ResolveService<ShopPlaytestState>();
            Assert.That(State.World, Is.Not.Null);
            Assert.That(ResolveService<IGameFlow>().Current, Is.TypeOf<MonsterEncounterState>());
            yield return GoToShop(shop);
            originalActors = Object.FindObjectsOfType<ShopPlaytestActor>(true).Length;
        }
        protected IEnumerator GoToShop(int shop)
        {
            Input.Prime();
            var station = State.World.Station(shop);
            var encounter = Object.FindObjectOfType<EncounterSceneView>();
            Vector2 target = encounter.ToLogicPosition(station.position);
            // 路线按模型局部坐标摆定，随整张地图的旋转同步。
            Transform map = GameObject.Find("Environment_FiveShopsPlaceholder").transform;
            Vector3 currentLocal = map.InverseTransformPoint(encounter.ToScenePosition(State.ControlledPosition));
            Vector3 targetLocal = map.InverseTransformPoint(station.position);
            float currentStreet = currentLocal.z > 404 ? 884 : 284;
            float targetStreet = targetLocal.z > 404 ? 884 : 284;
            yield return MoveTo(MapPoint(map, currentLocal.x, currentStreet));
            if (currentStreet != targetStreet)
            {
                yield return MoveTo(MapPoint(map, 584, currentStreet));
                yield return MoveTo(MapPoint(map, 584, targetStreet));
            }
            yield return MoveTo(MapPoint(map, targetLocal.x, targetStreet));
            yield return MoveTo(target);
            yield return Check("走入店前范围显示对应人物", () => State.ActiveShop == shop, 3);
            yield return Check("当前店前操作点或本店NPC成为统一交互焦点", () =>
                ResolveService<IInteractionFocus>().Current is ShopPlaytestActor actor && actor.Shop == shop && actor.CanInteract, 3);
            yield return Check("五店不加载操作面板", () => ResolveService<IUIService>().Get<ShopPlaytestView>() == null);
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
        protected IEnumerator MoveTo(Vector2 target)
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (Vector2.Distance(State.ControlledPosition, target) > .25f && Time.realtimeSinceStartup < deadline)
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
            if (Vector2.Distance(State.ControlledPosition, target) >= .5f) yield return Snapshot("实际移动受阻");
            Assert.That(Vector2.Distance(State.ControlledPosition, target), Is.LessThan(.5f),
                "原主场景实际移动到目标失败：当前=" + State.ControlledPosition + "，目标=" + target);
        }
        protected IEnumerator Borrow(int roleIndex)
        {
            int index = 0; ShopPlaytestActor target = null;
            foreach (ShopPlaytestActor actor in State.World.Actors)
                if (actor.Shop == State.ActiveShop && actor.Role.Length > 0) { if (index++ == roleIndex) { target = actor; break; } }
            Assert.That(target, Is.Not.Null);
            Vector2 position = State.Taming.GetTarget(target.StableId).Model.Position;
            yield return MoveTo(position);
            yield return SelectActor(target);
            yield return Check("借用按钮对应目标NPC", () => ResolveService<IInteractionFocus>().Current == target, 3);
            var hud = ResolveService<IUIService>().Get<InteractPromptHudView>();
            Assert.That(hud.IsSecondaryShown, Is.True);
            Assert.That(DialogueRunning, Is.False, "无需先对话即可借身份");
            yield return Snapshot("F键借用身份_" + target.Role);
            yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Execute);
            yield return Check("原控制系统与职业同步", () => State.Taming.CurrentControlId == target.StableId && State.CurrentRole == target.Role, 3);
            yield return Check("F借身份不会同时打开对白", () => !DialogueRunning);
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

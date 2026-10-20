// 职责：五店回放走正常Boot、原3D场景和真实移动/交互，不再打开独立试玩。
using System.Collections;
using System.Collections.Generic;
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
        protected bool InspectStoryPresentation { get; set; }
        private bool archiveChecked;
        private bool meetingChecked;
        private bool windowShotChecked;
        protected IEnumerator Ready(int shop)
        {
            yield return EnterStoryWorld();
            yield return FormalPrologue();
            Assert.That(State.World, Is.Not.Null);
            yield return GoToShop(shop);
        }
        protected IEnumerator EnterStoryWorld()
        {
            archiveChecked = meetingChecked = windowShotChecked = false;
            yield return EnterWorldFromTitle(enterTimeout: 60f, expectedScene: "SampleScene");
            State = ResolveService<ShopPlaytestState>();
            originalActors = Object.FindObjectsOfType<ShopPlaytestActor>(true).Length;
            Assert.That(ResolveService<IGameFlow>().Current, Is.TypeOf<MonsterEncounterState>());
        }
        protected IEnumerator ReadyUnlockedShop(int shop)
        {
            yield return EnterStoryWorld();
            yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0026");
            yield return NarrationToMirror();
            yield return StoryPoint("enter_r0171");
            yield return WaitUntil("进入妖界店铺测试现场", () => State.World != null &&
                !ResolveService<SpotlightStoryFlow>().Bindings.IsHumanStory, 60);
            // 只定位玩法阶段，不代表轮回堂至坊市整段主线通过。
            yield return Step("定位到五店已开放阶段（测试夹具）", () =>
            {
                var narrative = ResolveService<NarrativeService>();
                narrative.CaptureIntoPartition();
                ResolveService<Game.Core.Save.ISaveService>().Get<NarrativeSaveData>().Current.StageId = "action_r0197";
                narrative.ReloadFromSave();
            }, 0);
            yield return GoToShop(shop);
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
                if (ResolveService<NarrativeService>().CurrentStageId == "action_r0070")
                {
                    yield return StoryPoint("action_r0070");
                    Assert.That(ResolveService<NarrativeService>().CurrentStoryId, Is.EqualTo("spotlight_mentor_duel"));
                    yield return PlayBattle();
                    yield return AdvanceFormalDialogue();
                    Assert.That(ResolveService<NarrativeService>().CurrentStoryId, Is.EqualTo("spotlight_current"));
                    Assert.That(ResolveService<NarrativeService>().CurrentStageId, Is.Not.EqualTo("action_r0070"), "教学胜利后才能继续主线");
                    deadline = Time.realtimeSinceStartup + 100;
                }
                if (ResolveService<NarrativeService>().CurrentStageId == "enter_r0083")
                    yield return StoryPoint("enter_r0083");
                yield return AdvanceFormalDialogue(); yield return null;
            }
            yield return WaitUntil("完成教学与人间对白后到达穿越点", () => ResolveService<NarrativeService>().CurrentStageId == "enter_r0171" && ResolveService<SpotlightStoryFlow>().CanSave, 5);
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
                if (stage == "enter_r0083")
                {
                    yield return MoveTo(new Vector2(-8f, 282.5f));
                    yield return MoveTo(new Vector2(14f, 282.5f));
                }
                if (stage == "enter_r0171")
                {
                    yield return MoveTo(new Vector2(22.5f, 286.8f));
                }
                yield return MoveTo(new Vector2(point.Position.x, point.Position.z), .8f);
            }
            else if (stage == "enter_r0171")
            {
                yield return GoToWorldPoint(new Vector3(10.33f, 4.85f, 4.4f));
                yield return Input.Hold(ResolveService<IInputService>().Actions.Gameplay.Sneak);
                yield return MoveTo(new Vector2(10.33f, 10.5f));
                yield return MoveTo(new Vector2(10.33f, 22.8f));
                // 桥口在东侧；北缘木栏阻挡，短距离奔跑穿过巡逻视野后再潜行。
                yield return MoveTo(new Vector2(-9.8f, 22.8f));
                yield return MoveTo(new Vector2(-8.2f, 22.8f));
                yield return CrossBridge(true);
                yield return MoveTo(new Vector2(point.Position.x, 19.8f), .15f);
                yield return Input.Release(ResolveService<IInputService>().Actions.Gameplay.Sneak);
            }
            else if (stage == "enter_r0188")
            {
                yield return Input.Hold(ResolveService<IInputService>().Actions.Gameplay.Sneak);
                yield return MoveTo(new Vector2(-20f, 19.8f));
                yield return CrossBridge(false);
                yield return MoveTo(new Vector2(10.33f, 22.8f));
                yield return MoveTo(new Vector2(10.33f, 10.5f));
                yield return MoveTo(new Vector2(point.Position.x, point.Position.z), .4f);
                yield return Input.Release(ResolveService<IInputService>().Actions.Gameplay.Sneak);
            }
            else yield return GoToWorldPoint(point.Position, .8f);
            if (InspectStoryPresentation && (stage == "enter_r0198" || stage == "enter_r0215"))
                yield return Check("战后顾客不抢当前主线地点交互", () => System.Array.TrueForAll(State.World.Actors, actor => !actor.CanInteract), 3);
            yield return Check("节点与区域一致，现场交互可用", () => ResolveService<IInteractionFocus>().Current == point && point.CanInteract, 5);
            yield return Check("现场交互可用且不显示黑底操作提示", () => ResolveService<IUIService>().Get<StoryGuideHudView>() == null, 3);
            if (stage == "enter_r0026") yield return Snapshot("节点交互_无黑底提示");
            if (InspectStoryPresentation && stage == "enter_r0188")
                yield return InspectWindowEntrance(point);
            if (!interact) yield break;
            yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Interact);
            yield return WaitUntil("真实交互推进节点或切换妖界", () => crossing
                ? ResolveService<SpotlightStoryFlow>().Bindings != null && !ResolveService<SpotlightStoryFlow>().Bindings.IsHumanStory
                : narrative.CurrentStageId != stage, crossing ? 60 : 5);
        }
        private IEnumerator CrossBridge(bool towardsLunhuitang)
        {
            var player = ResolveService<Game.Player.PlayerModel>();
            var actions = ResolveService<IInputService>().Actions.Gameplay;
            bool running = player.IsRunning;
            yield return Input.Release(actions.Sneak);
            if (!running) yield return Input.PressUntil(actions.Run, () => player.IsRunning);
            if (towardsLunhuitang)
            {
                yield return MoveTo(new Vector2(-8.2f, 17.7f));
                yield return MoveTo(new Vector2(-20f, 17.7f));
            }
            else
            {
                yield return MoveTo(new Vector2(-20f, 17.7f));
                yield return MoveTo(new Vector2(-8.2f, 17.7f));
                yield return MoveTo(new Vector2(-8.2f, 22.8f));
            }
            if (!running) yield return Input.PressUntil(actions.Run, () => !player.IsRunning);
            yield return Input.Hold(actions.Sneak);
        }
        protected IEnumerator InspectWindowEntrance(SpotlightSceneBindings.Point point)
        {
            Assert.That(windowShotChecked, Is.True, "实走到入口必须已触发窗边特写");
            Assert.That(point.Marker != null && point.Marker.activeInHierarchy, Is.True, "当前楼梯节点须显示光点");
            Assert.That(ResolveService<SpotlightStoryFlow>().Bindings.WindowFigure.activeInHierarchy, Is.True);
            var window = ResolveService<SpotlightStoryFlow>().Bindings.WindowFigure.transform.position + Vector3.up * 1.25f;
            var screen = Camera.main.WorldToViewportPoint(window);
            Assert.That(screen.x, Is.InRange(.06f, .94f), "窗边何素须在实际镜头内");
            Assert.That(screen.y, Is.InRange(.06f, .94f), "窗边何素须在实际镜头内");
            yield return Snapshot("二三层楼梯光点_酒肆二楼窗边何素");
            yield return MoveTo(new Vector2(10.33f, 10.5f));
            float deadline = Time.realtimeSinceStartup + 10f;
            Vector2 entrance = new Vector2(point.Position.x, point.Position.z);
            while (Vector2.Distance(State.ControlledPosition, entrance) > .8f && Time.realtimeSinceStartup < deadline)
            {
                yield return Input.HoldStick(Vector2.ClampMagnitude((entrance - State.ControlledPosition) * 5f, 1f), .1f);
                Assert.That(ResolveService<SpotlightStoryFlow>().CanSave, Is.True, "返回入口不能重播特写");
            }
            Input.ReleaseStick();
            Assert.That(Vector2.Distance(State.ControlledPosition, entrance), Is.LessThan(1f));
            yield return Check("再次进入光点直接恢复E交互", () => ResolveService<IInteractionFocus>().Current == point && point.CanInteract, 3);
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
                    if (InspectStoryPresentation && !meetingChecked && narrative.CurrentStageId == "talk_21011")
                    {
                        yield return Check("何素在会面对白期间持续显示", () => ResolveService<SpotlightStoryFlow>().Bindings.Find("enter_r0215").Anchor.gameObject.activeInHierarchy, 3);
                        yield return Snapshot("何素会面对白_人物持续显示");
                        meetingChecked = true;
                    }
                    if (InspectStoryPresentation && !archiveChecked && narrative.CurrentStageId == "talk_21007")
                    {
                        if (rules.Current != null && rules.Current.Id == "r0144" && rules.Phase == DialogueSaveData.Phase.AwaitAdvance)
                        {
                            var body = FindDeep<TMP_Text>(ui.Get<DialogueView>().transform, "Body");
                            body.ForceMeshUpdate();
                            Assert.That(body.text.Split('\n').Length, Is.EqualTo(4), "案卷四行在同一框");
                            Assert.That(body.isTextOverflowing, Is.False, "完整案卷不能溢出对话框");
                            yield return Snapshot("泾阳案卷_同框四行");
                            archiveChecked = true;
                        }
                        else { yield return Input.Press(actions.Dialogue.Advance); continue; }
                    }
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
            Vector2 destination = target;
            foreach (Vector2 point in WalkPath(target))
            {
                target = point;
                float arrivalDistance = point == destination ? stopDistance : .03f;
                float deadline = Time.realtimeSinceStartup + 30;
                while (Vector2.Distance(State.ControlledPosition, target) > arrivalDistance && Time.realtimeSinceStartup < deadline)
                {
                    var story = ResolveService<SpotlightStoryFlow>();
                    if (ResolveService<NarrativeService>().CurrentStageId == "enter_r0188" && !story.CanSave)
                    {
                        Input.ReleaseStick();
                        float pausedAt = Time.realtimeSinceStartup;
                        if (InspectStoryPresentation && !windowShotChecked)
                        {
                            yield return Check("入口自动推近窗边，暂停移动和交互", () =>
                                Mathf.Abs(Camera.main.fieldOfView - story.Bindings.Config.WindowFieldOfView) < .001f &&
                                !ResolveService<IInputService>().Actions.Gameplay.enabled && !story.Bindings.Find("enter_r0188").CanInteract, 3);
                            var screen = Camera.main.WorldToViewportPoint(story.Bindings.WindowFigure.transform.position +
                                Vector3.up * story.Bindings.Config.WindowLookHeight);
                            Assert.That(screen.x, Is.EqualTo(.5f).Within(.01f));
                            Assert.That(screen.y, Is.EqualTo(.5f).Within(.01f));
                            yield return Snapshot("坊市入口_何素窗边特写");
                            windowShotChecked = true;
                        }
                        yield return WaitUntil("特写回到玩家，恢复操作但仍等E推进", () => story.CanSave &&
                            ResolveService<IInputService>().Actions.Gameplay.enabled, 6);
                        Assert.That(ResolveService<NarrativeService>().CurrentStageId, Is.EqualTo("enter_r0188"));
                        deadline += Time.realtimeSinceStartup - pausedAt;
                    }
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
                    yield return Input.HoldStick(Vector2.ClampMagnitude(delta * 10f, 1f), Mathf.Min(.1f, delta.magnitude / 4));
                }
                Input.ReleaseStick();
                if (Vector2.Distance(State.ControlledPosition, point) >= Mathf.Max(.15f, arrivalDistance + .1f))
                    yield return Snapshot("移动失败_检查生命与巡逻");
                Assert.That(Vector2.Distance(State.ControlledPosition, point), Is.LessThan(Mathf.Max(.15f, arrivalDistance + .1f)),
                    "实际移动受阻：" + State.ControlledPosition + " → " + point + "，生命=" + ResolveService<Game.Player.PlayerModel>().Health +
                    "，击倒=" + ResolveService<Game.Player.PlayerRules>().Knockdown.IsDowned);
            }
            float tolerance = Mathf.Max(.5f, stopDistance + .1f);
            target = destination;
            if (Vector2.Distance(State.ControlledPosition, target) >= tolerance) yield return Snapshot("实际移动受阻");
            Assert.That(Vector2.Distance(State.ControlledPosition, target), Is.LessThan(tolerance),
                "原主场景实际移动到目标失败：当前=" + State.ControlledPosition + "，目标=" + target);
        }
        // 回放按真实地面与胶囊绕开新增桌椅，移动仍经摇杆，不穿障碍或瞬移。
        private List<Vector2> WalkPath(Vector2 target)
        {
            const float spacing = .25f;
            Vector2 start = State.ControlledPosition;
            Vector2 min = Vector2.Min(start, target) - Vector2.one * 6f;
            Vector2 max = Vector2.Max(start, target) + Vector2.one * 6f;
            int width = Mathf.CeilToInt((max.x - min.x) / spacing) + 1;
            int height = Mathf.CeilToInt((max.y - min.y) / spacing) + 1;
            var points = new Vector3[width * height];
            var free = new bool[points.Length];
            var parent = new int[points.Length];
            int obstacle = LayerMask.GetMask("Obstacle"), ground = LayerMask.GetMask("Ground");
            int first = -1, last = -1;
            float fromDistance = float.MaxValue, toDistance = float.MaxValue;
            Physics.SyncTransforms();
            for (int x = 0; x < width; x++) for (int z = 0; z < height; z++)
            {
                int index = x * height + z; parent[index] = -1;
                Vector3 p = new Vector3(min.x + x * spacing, 25f, min.y + z * spacing);
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit hit, 50f, ground, QueryTriggerInteraction.Ignore)) continue;
                p.y = hit.point.y; points[index] = p;
                if (Physics.CheckCapsule(p + Vector3.up * .65f, p + Vector3.up * 1.2f, .3f, obstacle, QueryTriggerInteraction.Ignore)) continue;
                free[index] = true;
                Vector2 flat = new Vector2(p.x, p.z);
                float from = (flat - start).sqrMagnitude, to = (flat - target).sqrMagnitude;
                if (from < fromDistance) { first = index; fromDistance = from; }
                if (to < toDistance) { last = index; toDistance = to; }
            }
            // shortcut: 入镜落点可在Ground外，先接最近地面；落点地面补齐后收紧起终点约束。
            Assert.That(first >= 0 && last >= 0, Is.True, "回放范围内必须有可行走地面：" + start + " → " + target);
            var queue = new Queue<int>(); queue.Enqueue(first); parent[first] = first;
            while (queue.Count > 0 && parent[last] < 0)
            {
                int a = queue.Dequeue();
                foreach (int b in new[] { a - height, a + height, a - 1, a + 1 })
                {
                    if (b < 0 || b >= points.Length || !free[b] || parent[b] >= 0 ||
                        Mathf.Abs(b / height - a / height) + Mathf.Abs(b % height - a % height) != 1 ||
                        Mathf.Abs(points[b].y - points[a].y) > .45f) continue;
                    Vector3 slid = EncounterCollision.Slide(points[a], points[b], .3f, .35f, 1.5f, obstacle);
                    if (new Vector2(slid.x - points[b].x, slid.z - points[b].z).sqrMagnitude > .001f) continue;
                    parent[b] = a; queue.Enqueue(b);
                }
            }
            Assert.That(parent[last], Is.GreaterThanOrEqualTo(0), "回放起终点必须由真实通路连通：" + start + " → " + target);
            var route = new List<Vector2>();
            for (int at = last; at != first; at = parent[at]) route.Add(new Vector2(points[at].x, points[at].z));
            route.Add(new Vector2(points[first].x, points[first].z));
            route.Reverse();
            for (int i = route.Count - 2; i > 0; i--)
            {
                Vector2 before = route[i] - route[i - 1], after = route[i + 1] - route[i];
                if (Mathf.Abs(before.x * after.y - before.y * after.x) < .0001f) route.RemoveAt(i);
            }
            route.Add(target);
            return route;
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
                yield return Check("刺杀后人物、名字和脚下环全部退场", () => !target.IsVisible &&
                    System.Array.TrueForAll(target.GetComponentsInChildren<Renderer>(true), renderer => !renderer.enabled), 3);
                yield return FinishReplies();
                yield return Check("潜行绕背刺杀后皮入包，尚未取得身份", () =>
                    model.Health == 0 && loot.Items.ContainsKey(itemId) && State.CurrentRole != target.Role, 3);
            }
            yield return Check("死者不再显示对话或换皮入口", () => !target.CanInteract && !target.CanInteractSecondary &&
                ResolveService<IInteractionFocus>().Current != target, 3);
            yield return Step("在背包使用已取得的皮：" + target.DisplayName, () =>
            {
                var used = ResolveService<Game.Inventory.ItemUseService>().TryUse(itemId, null);
                Assert.That(used.IsOk, Is.True, used.Effect.Reason);
                Assert.That(used.ConsumedCount, Is.Zero);
            }, 0);
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
            var encounter = Object.FindObjectOfType<EncounterSceneView>();
            if (actor.StableId == "shop_patrol")
            {
                float deadline = Time.realtimeSinceStartup + 20f;
                while (Vector3.Distance(encounter.ToScenePosition(State.ControlledPosition), actor.Position) > actor.InteractionRadius * .75f &&
                    Time.realtimeSinceStartup < deadline)
                    yield return MoveTo(encounter.ToLogicPosition(actor.Position), .4f);
            }
            else yield return MoveTo(encounter.ToLogicPosition(actor.Position));
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
                index == 4 ? "shop_customer_b" : "shop_customer_a" : State.ActiveShop == 0 && index < 2 ? "shop_patrol" :
                State.ActiveShop == 1 ? "shop_tailor" : "shop_station_" + State.ActiveShop;
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

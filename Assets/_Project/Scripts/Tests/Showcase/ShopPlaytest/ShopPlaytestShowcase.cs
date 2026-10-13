// 职责：五店回放走正常Boot、原3D场景和真实移动/交互，不再打开独立试玩。
using System.Collections;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.UI;
using Game.Interaction;
using Game.Monster;
using Game.ShopPlaytest;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Tests.Showcase.ShopPlaytest
{
    public abstract class ShopPlaytestShowcase : ShowcaseScenario
    {
        protected override string ScenePath => null;
        protected override bool LoadBootScene => true;
        protected ShopPlaytestState State { get; private set; }
        protected ShopPlaytestView View { get; private set; }
        private int originalActors;
        protected IEnumerator Ready(int shop)
        {
            yield return EnterWorldFromTitle();
            State = ResolveService<ShopPlaytestState>(); View = ResolveService<IUIService>().Get<ShopPlaytestView>();
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
                ResolveService<IInteractionFocus>().Current is ShopPlaytestActor actor && actor.Shop == shop && actor.Role.Length == 0, 3);
            yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Interact);
            yield return Check("现场交互打开操作面板", () => View.gameObject.activeSelf, 4);
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
            yield return Step("借 " + target.DisplayName, () => Click("Role" + roleIndex), 0);
            yield return Check("原控制系统与职业同步", () => State.Taming.CurrentControlId == target.StableId && State.CurrentRole == target.Role, 3);
            yield return MoveTo(Object.FindObjectOfType<EncounterSceneView>().ToLogicPosition(State.World.Station(State.ActiveShop).position));
        }
        protected IEnumerator CheckPreplacedObjects() => Check("各操作没有新生成占位角色", () => Object.FindObjectsOfType<ShopPlaytestActor>(true).Length == originalActors);
        protected void Click(string name) => FindDeep<Button>(View.transform, name).onClick.Invoke();
    }
}

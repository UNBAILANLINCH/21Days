// 职责：现有3D主场景的五店预摆引用与分组显隐；不生成地图、不推进角色、不切相机。
using UnityEngine;
using Game.Monster;
namespace Game.ShopPlaytest
{
    public sealed class ShopPlaytestWorld : MonoBehaviour
    {
        [SerializeField] private BoxCollider[] activityAreas;
        [SerializeField] private Transform[] stations;
        [SerializeField] private ShopPlaytestActor[] actors;
        [SerializeField] private Transform[] redirectedRoute;
        [SerializeField] private Transform[] originalRoute;
        [SerializeField] private float exitPadding = .4f;
        [SerializeField] private float stationRadius = 3.5f;
        public ShopPlaytestActor[] Actors => actors;
        public int ActiveShop { get; private set; } = -1;
        public Transform Station(int shop) => stations[shop];
        public bool NearStation(int shop, Vector3 position) =>
            shop >= 0 && shop < stations.Length && Vector3.Distance(position, stations[shop].position) <= stationRadius;
        private bool Contains(int shop, Vector3 position, bool keepCurrent)
        {
            Bounds bounds = activityAreas[shop].bounds;
            if (keepCurrent) bounds.Expand(new Vector3(exitPadding * 2, 0, exitPadding * 2));
            position.y = bounds.center.y;
            return bounds.Contains(position);
        }
        public int ShopAt(Vector3 position)
        {
            if (ActiveShop >= 0 && Contains(ActiveShop, position, true)) return ActiveShop;
            int next = -1; float nearest = float.MaxValue;
            for (int i = 0; i < activityAreas.Length; i++)
            {
                if (!Contains(i, position, false)) continue;
                float distance = (position - stations[i].position).sqrMagnitude;
                if (distance < nearest) { next = i; nearest = distance; }
            }
            return next;
        }
        public void Display(int shop, string controlledId)
        {
            ActiveShop = shop;
            foreach (ShopPlaytestActor actor in actors)
                if (actor != null) actor.SetVisible(actor.Shop == shop || actor.StableId == controlledId);
        }
        public Vector2[] Route(EncounterSceneView view, bool redirected)
        {
            Transform[] points = redirected ? redirectedRoute : originalRoute;
            var result = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) result[i] = view.ToLogicPosition(points[i].position);
            return result;
        }
        private void Awake()
        {
            if (activityAreas == null || activityAreas.Length != 5 || stations == null || stations.Length != 5 || actors == null)
                throw new System.InvalidOperationException("主场景五店引用不完整");
            foreach (BoxCollider area in activityAreas)
                if (area == null || !area.isTrigger) throw new System.InvalidOperationException("五店活动区必须预摆且为Trigger");
            Display(-1, "player");
        }
    }
}

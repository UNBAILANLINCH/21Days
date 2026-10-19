// 职责：左上角常驻小地图——俯视轮廓、玩家位置、可用出口与楼梯；随共用 HUD 显隐，不接管操作。
// 为什么新建：现有 UIView 没有地图内容可复用；任务面板只显示任务，扩展它会混入场景职责。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.World
{
    public sealed class SceneMapView : UIView
    {
        [SerializeField] private SceneMapGraphic map;
        [SerializeField] private TMP_Text title;
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private RectTransform playerMarker;
        [SerializeField] private Image markerTemplate;
        [SerializeField] private Color groundColor = new Color(0.22f, 0.29f, 0.26f);
        [SerializeField] private Color buildingColor = new Color(0.48f, 0.58f, 0.51f);
        [SerializeField] private Color portalColor = new Color(1f, 0.78f, 0.35f);
        [SerializeField] private Color stairColor = new Color(0.8f, 0.67f, 0.5f);
        private readonly List<Bounds> footprints = new List<Bounds>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<RectTransform> markers = new List<RectTransform>();
        private readonly List<Vector3> markerPositions = new List<Vector3>();
        private Transform player;
        public override UILayer Layer => UILayer.Hud;
        public override bool IsFullScreen => false;
        public bool IsShowing => player != null && contentRoot != null && contentRoot.activeSelf;
        public int PortalCount { get; private set; }
        public int StairCount { get; private set; }
        public int FootprintCount => map == null ? 0 : map.FootprintCount;
        public string SceneTitle => title == null ? string.Empty : title.text;
        public Vector2 PlayerMapPosition => playerMarker == null ? Vector2.zero : playerMarker.anchoredPosition;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (map == null || title == null || contentRoot == null || playerMarker == null || markerTemplate == null)
                throw new InvalidOperationException("SceneMapView：地图、标题、内容根或标记引用未接线。");
            contentRoot.SetActive(false);
            markerTemplate.gameObject.SetActive(false);
            // 动态字体的图集会扩容；打开时重建文字，不依赖预制体生成时的文字网格。
            foreach (TMP_Text label in GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
            return UniTask.CompletedTask;
        }

        /// <summary>只在打开地图时扫描当前玩家所在场景；不依赖场景名，也不修改关卡或存档。</summary>
        public void SetScene(Transform anchor, string displayName, WorldCatalog catalog)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            ClearMarkers();
            player = anchor;
            contentRoot.SetActive(true);
            title.text = displayName;
            footprints.Clear();
            colors.Clear();
            var geometry = new List<MeshRenderer>();
            var stairs = new Dictionary<Transform, Bounds>();
            var portals = new List<PortalAnchor>();
            foreach (GameObject root in anchor.gameObject.scene.GetRootGameObjects())
            {
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.transform.IsChildOf(anchor)) continue;
                    geometry.Add(renderer);
                    // shortcut: 楼梯暂按场景作者的 Stair/楼梯命名识别；正式关卡采用语义组件时改读组件。
                    Transform stair = renderer.transform;
                    if (!IsStair(stair.name)) continue;
                    while (stair.parent != null && IsStair(stair.parent.name)) stair = stair.parent;
                    if (stairs.TryGetValue(stair, out Bounds stairBounds))
                    {
                        stairBounds.Encapsulate(renderer.bounds);
                        stairs[stair] = stairBounds;
                    }
                    else stairs.Add(stair, renderer.bounds);
                }
                foreach (PortalAnchor portal in root.GetComponentsInChildren<PortalAnchor>())
                {
                    if (portal.isActiveAndEnabled && !portal.HasTriggered && catalog.CanLoadScene(portal.TargetSceneKey))
                        portals.Add(portal);
                }
            }
            // 地面先画，较高的建筑在上；只读世界包围盒，不改 Renderer。
            geometry.Sort((a, b) => a.bounds.max.y.CompareTo(b.bounds.max.y));
            for (int i = 0; i < geometry.Count; i++)
            {
                Bounds bounds = geometry[i].bounds;
                // shortcut: 最小版使用轴对齐矩形轮廓；正式美术要求精确外形时换专用底图。
                footprints.Add(bounds);
                colors.Add(bounds.size.y <= 0.5f ? groundColor : buildingColor);
            }
            map.SetGeometry(footprints, colors, anchor.position);
            foreach (PortalAnchor portal in portals) AddMarker(portal.transform.position, portalColor);
            PortalCount = portals.Count;
            foreach (Bounds stair in stairs.Values) AddMarker(stair.center, stairColor);
            StairCount = stairs.Count;
            playerMarker.SetAsLastSibling();
            RefreshPositions();
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            ClearScene();
            return UniTask.CompletedTask;
        }

        public void ClearScene()
        {
            player = null;
            ClearMarkers();
            if (contentRoot != null) contentRoot.SetActive(false);
        }

        private static bool IsStair(string value) => value.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0
            || value.Contains("楼梯");

        private void AddMarker(Vector3 position, Color tint)
        {
            Image marker = Instantiate(markerTemplate, map.transform, false);
            marker.color = tint;
            marker.gameObject.SetActive(true);
            markers.Add(marker.rectTransform);
            markerPositions.Add(position);
        }

        private void LateUpdate()
        {
            if (player != null) RefreshPositions();
        }

        private void RefreshPositions()
        {
            Rect area = map.rectTransform.rect;
            playerMarker.anchoredPosition = SceneMapGraphic.Project(map.WorldBounds, area, player.position);
            for (int i = 0; i < markers.Count; i++)
                markers[i].anchoredPosition = SceneMapGraphic.Project(map.WorldBounds, area, markerPositions[i]);
        }

        private void ClearMarkers()
        {
            for (int i = 0; i < markers.Count; i++)
                if (markers[i] != null) Destroy(markers[i].gameObject);
            markers.Clear();
            markerPositions.Clear();
            PortalCount = 0;
            StairCount = 0;
        }
    }
}

// 职责：地图投影与打开门槛的边界检查；场景接线和按键路径由 SceneMapShowcase 验证。
// 为什么新建：现有世界测试只验转场与镜头约束，没有地图的等比投影或输入让位规则。
using Game.World;
using Game.Editor.World;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode.World
{
    public sealed class SceneMapTests
    {
        [Test]
        public void StyleLoadedMap_NullInputs_RejectsWithoutCreatingObjects()
        {
            Assert.Throws<System.ArgumentException>(() => SceneMapBuilder.StyleLoadedMap(null, null, null, null));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StyleLoadedMap_ApplyOrRepeat_PreservesOuterLayoutAndBindings(bool repeat)
        {
            var root = new GameObject("纸页样式测试", typeof(RectTransform));
            root.SetActive(false);
            TMP_FontAsset font = null;
            Texture2D texture = null;
            Sprite sprite = null;
            try
            {
                font = TMP_FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                texture = new Texture2D(2, 2);
                sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
                var view = root.AddComponent<SceneMapView>();
                RectTransform panel = StyleNode("MapPanel", root.transform, new Vector2(264f, 294f));
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
                panel.anchoredPosition = new Vector2(31f, -25f);
                panel.gameObject.AddComponent<Image>();
                RectTransform contents = StyleNode("Contents", panel, panel.sizeDelta);
                var title = StyleNode("Title", contents, new Vector2(236f, 28f)).gameObject.AddComponent<TextMeshProUGUI>();
                title.text = "Current world";
                var map = StyleNode("Map", contents, new Vector2(224f, 224f)).gameObject.AddComponent<SceneMapGraphic>();
                var player = StyleNode("Player", map.transform, Vector2.one * 8f);
                player.gameObject.AddComponent<Image>();
                var template = StyleNode("MarkerTemplate", map.transform, Vector2.one * 6f).gameObject.AddComponent<Image>();
                template.gameObject.SetActive(false);
                StyleNode("Legend", contents, new Vector2(244f, 20f)).gameObject.AddComponent<TextMeshProUGUI>();
                StyleNode("North", map.transform, new Vector2(36f, 20f)).gameObject.AddComponent<TextMeshProUGUI>();
                using (var data = new SerializedObject(view))
                {
                    data.FindProperty("map").objectReferenceValue = map;
                    data.FindProperty("title").objectReferenceValue = title;
                    data.FindProperty("contentRoot").objectReferenceValue = panel.gameObject;
                    data.FindProperty("playerMarker").objectReferenceValue = player;
                    data.FindProperty("markerTemplate").objectReferenceValue = template;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                SceneMapBuilder.StyleLoadedMap(root, font, font, sprite);
                int children = root.GetComponentsInChildren<Transform>(true).Length;
                if (repeat)
                {
                    panel.name = "CustomMapPanel";
                    SceneMapBuilder.StyleLoadedMap(root, font, font, sprite);
                }
                Assert.That(root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(children));
                Assert.That(panel.anchoredPosition, Is.EqualTo(new Vector2(31f, -25f)));
                Assert.That(panel.sizeDelta, Is.EqualTo(new Vector2(264f, 294f)));
                Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
                Assert.That(panel.pivot, Is.EqualTo(new Vector2(0f, 1f)));
                Assert.That(title.text, Is.EqualTo("Current world"));
                Assert.That(template.gameObject.activeSelf, Is.False);
                Assert.That(player.GetComponents<Outline>().Length, Is.EqualTo(1));
                Assert.That(template.GetComponents<Outline>().Length, Is.EqualTo(1));
                var guard = typeof(SceneMapBuilder).GetMethod("HasPaperStyle",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                Assert.That(guard, Is.Not.Null);
                Assert.That(guard.Invoke(null, new object[] { root }), Is.True, "改名后仍应阻止旧生成器覆盖");
                Assert.That(contents.Find("MapPaperFrame").GetSiblingIndex(), Is.Zero);
                Assert.That(contents.Find("North"), Is.Not.Null);
                Assert.That(map.GetComponent<CanvasRenderer>(), Is.Not.Null);
                foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true))
                    Assert.That(graphic.raycastTarget, Is.False);
                using (var data = new SerializedObject(view))
                {
                    Assert.That(data.FindProperty("map").objectReferenceValue, Is.SameAs(map));
                    Assert.That(data.FindProperty("title").objectReferenceValue, Is.SameAs(title));
                    Assert.That(data.FindProperty("contentRoot").objectReferenceValue, Is.SameAs(panel.gameObject));
                    Assert.That(data.FindProperty("playerMarker").objectReferenceValue, Is.SameAs(player));
                    Assert.That(data.FindProperty("markerTemplate").objectReferenceValue, Is.SameAs(template));
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                if (font != null)
                {
                    foreach (Texture2D atlas in font.atlasTextures)
                        if (atlas != null) Object.DestroyImmediate(atlas);
                    if (font.material != null) Object.DestroyImmediate(font.material);
                    Object.DestroyImmediate(font);
                }
            }
        }

        private static RectTransform StyleNode(string name, Transform parent, Vector2 size)
        {
            var node = new GameObject(name, typeof(RectTransform));
            node.transform.SetParent(parent, false);
            var rect = (RectTransform)node.transform;
            rect.sizeDelta = size;
            return rect;
        }

        [Test]
        public void Project_WorldCenter_MapsToAreaCenter()
        {
            var bounds = new Bounds(new Vector3(30f, 9f, -20f), new Vector3(20f, 1f, 10f));
            var area = new Rect(40f, 60f, 400f, 300f);
            Assert.That(SceneMapGraphic.Project(bounds, area, bounds.center), Is.EqualTo(area.center));
        }

        [Test]
        public void Project_NonSquareArea_KeepsEqualXZScaleAndNorthUp()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(20f, 1f, 20f));
            var area = new Rect(-200f, -100f, 400f, 200f);
            Vector2 east = SceneMapGraphic.Project(bounds, area, new Vector3(10f, 50f, 0f));
            Vector2 north = SceneMapGraphic.Project(bounds, area, new Vector3(0f, -50f, 10f));
            Assert.That(east, Is.EqualTo(new Vector2(100f, 0f)));
            Assert.That(north, Is.EqualTo(new Vector2(0f, 100f)));
        }

        [Test]
        public void Project_ZeroWorldExtent_DoesNotDivideByZero()
        {
            Vector2 result = SceneMapGraphic.Project(new Bounds(Vector3.zero, Vector3.zero),
                new Rect(-100f, -100f, 200f, 200f), Vector3.zero);
            Assert.That(result, Is.EqualTo(Vector2.zero));
        }

        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(false, false, false)]
        public void ShouldShow_RequiresAnExplorationSceneAndActor(bool actor, bool exploration, bool expected)
        {
            Assert.That(SceneMapController.ShouldShow(actor, exploration), Is.EqualTo(expected));
        }

        [Test]
        public void SetGeometry_ReplacingScene_DropsOldFootprintsAndBounds()
        {
            var node = new GameObject("地图投影测试", typeof(RectTransform));
            try
            {
                var map = node.AddComponent<SceneMapGraphic>();
                Assert.That(node.GetComponent<CanvasRenderer>(), Is.Not.Null, "自定义地图图形必须自带绘制组件");
                map.SetGeometry(new[] { new Bounds(new Vector3(1000f, 0f, 1000f), Vector3.one) },
                    new[] { Color.white }, Vector3.zero);
                map.SetGeometry(new[] { new Bounds(Vector3.zero, new Vector3(20f, 1f, 10f)) },
                    new[] { Color.white }, Vector3.zero);
                Assert.That(map.FootprintCount, Is.EqualTo(1));
                Assert.That(map.WorldBounds.center.x, Is.EqualTo(0f));
                Assert.That(map.WorldBounds.Contains(Vector3.zero), Is.True);
                Assert.That(map.WorldBounds.Contains(new Vector3(1000f, 0f, 1000f)), Is.False);
            }
            finally { Object.DestroyImmediate(node); }
        }

        [Test]
        public void SetScene_InactiveRootGroupDoesNotExpandMapAcrossWorlds()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("MapWorldFilterTest");
            var player = new GameObject("MapPlayer");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(player, scene);
            var first = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(first, scene);
            var hidden = new GameObject("InactiveWorld");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(hidden, scene);
            var second = GameObject.CreatePrimitive(PrimitiveType.Cube);
            second.transform.SetParent(hidden.transform); second.transform.position = new Vector3(0, 0, 300);
            hidden.SetActive(false);
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/SceneMapView.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<SceneMapView>();
                var graphic = instance.GetComponentInChildren<SceneMapGraphic>(true);
                view.SetScene(player.transform, "地图", null);
                Assert.That(view.FootprintCount, Is.EqualTo(1));
                Assert.That(graphic.WorldBounds.center.z, Is.EqualTo(0));
                first.SetActive(false); hidden.SetActive(true); player.transform.position = new Vector3(0, 0, 300);
                view.SetScene(player.transform, "地图", null);
                Assert.That(view.FootprintCount, Is.EqualTo(1));
                Assert.That(graphic.WorldBounds.center.z, Is.EqualTo(300));
            }
            finally
            {
                Object.DestroyImmediate(instance);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

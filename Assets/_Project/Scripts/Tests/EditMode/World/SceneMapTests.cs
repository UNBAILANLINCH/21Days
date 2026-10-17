// 职责：地图投影与打开门槛的边界检查；场景接线和按键路径由 SceneMapShowcase 验证。
// 为什么新建：现有世界测试只验转场与镜头约束，没有地图的等比投影或输入让位规则。
using Game.World;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.World
{
    public sealed class SceneMapTests
    {
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
    }
}

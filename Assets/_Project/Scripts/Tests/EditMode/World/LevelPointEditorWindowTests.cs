// 职责：核对人工动线的顺序和三维距离；现有世界规则测试不覆盖编辑器路线，故新增独立测试。
using Game.Editor.World;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.World
{
    public sealed class LevelPointEditorWindowTests
    {
        private GameObject root;

        [SetUp]
        public void SetUp() => root = new GameObject("路线测试");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void RouteLength_EmptyOrSinglePoint_IsZero()
        {
            Assert.That(LevelPointEditorWindow.RouteLength(null), Is.Zero);
            Assert.That(LevelPointEditorWindow.RouteLength(root.transform), Is.Zero);
            AddPoint(Vector3.one);
            Assert.That(LevelPointEditorWindow.RouteLength(root.transform), Is.Zero);
        }

        [Test]
        public void RouteLength_Stairs_IncludesHeight()
        {
            AddPoint(Vector3.zero);
            AddPoint(new Vector3(0, 3, 4));
            AddPoint(new Vector3(0, 3, 6));
            Assert.That(LevelPointEditorWindow.RouteLength(root.transform), Is.EqualTo(7).Within(0.001f));
        }

        [Test]
        public void RouteLength_Reorder_UsesHierarchyOrder()
        {
            AddPoint(Vector3.zero);
            Transform middle = AddPoint(new Vector3(3, 0, 0));
            AddPoint(new Vector3(1, 0, 0));
            Assert.That(LevelPointEditorWindow.RouteLength(root.transform), Is.EqualTo(5));
            middle.SetAsLastSibling();
            Assert.That(LevelPointEditorWindow.RouteLength(root.transform), Is.EqualTo(3));
        }

        private Transform AddPoint(Vector3 position)
        {
            Transform point = new GameObject("路点").transform;
            point.SetParent(root.transform);
            point.position = position;
            return point;
        }
    }
}

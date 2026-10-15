// 职责：整体摆位的去重、跨组归属拒绝、撤销和原层级保持；单点路线测试不覆盖这些行为，故新增测试。
using System;
using Game.Editor.World;
using Game.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.World
{
    public sealed class LevelGroupMoveToolTests
    {
        private GameObject container;
        private LevelLayoutGroup group;
        private Transform building;

        [SetUp]
        public void SetUp()
        {
            container = new GameObject("整体摆位测试");
            group = Child("店铺", container.transform).gameObject.AddComponent<LevelLayoutGroup>();
            building = Child("外部建筑", container.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(container);

        [Test]
        public void Move_ParentAndChildReferences_MovesOnceAndKeepsHierarchy()
        {
            Transform actor = Child("人员", group.transform);
            Transform patrol = Child("巡逻点", group.transform);
            Transform buildingChild = Child("建筑子物体", building);
            actor.localPosition = new Vector3(1, 2, 3);
            building.position = new Vector3(4, 0, 1);
            buildingChild.localPosition = Vector3.one;
            Links(group, building, buildingChild, actor, patrol, building);
            Vector3 actorBefore = actor.position;
            Vector3 buildingBefore = building.position;
            Vector3 childBefore = buildingChild.position;
            Vector3 offset = new Vector3(8, 3, -2);

            LevelGroupMoveTool.Move(group, offset);

            Assert.That(actor.position, Is.EqualTo(actorBefore + offset));
            Assert.That(building.position, Is.EqualTo(buildingBefore + offset));
            Assert.That(buildingChild.position, Is.EqualTo(childBefore + offset));
            Assert.That(actor.parent, Is.EqualTo(group.transform));
            Assert.That(building.parent, Is.EqualTo(container.transform));
            Assert.That(buildingChild.parent, Is.EqualTo(building));
            Assert.That(group.LinkedMembers[0], Is.EqualTo(building));
        }

        [Test]
        public void Move_SharedBuilding_RejectsBeforeMovingAnything()
        {
            LevelLayoutGroup other = Child("另一店", container.transform).gameObject.AddComponent<LevelLayoutGroup>();
            Links(group, building);
            Links(other, building);
            Assert.Throws<InvalidOperationException>(() => LevelGroupMoveTool.Move(group, Vector3.one));
            Assert.That(group.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(building.position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Move_MapAncestor_Rejects()
        {
            Links(group, container.transform);
            Assert.Throws<InvalidOperationException>(() => LevelGroupMoveTool.Move(group, Vector3.one));
            Assert.That(container.transform.position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Move_MissingLink_Rejects()
        {
            Links(group, new Transform[] { null });
            Assert.Throws<InvalidOperationException>(() => LevelGroupMoveTool.Move(group, Vector3.one));
        }

        [Test]
        public void Move_NonFinitePosition_Rejects()
        {
            Assert.Throws<InvalidOperationException>(() => LevelGroupMoveTool.Move(group, new Vector3(float.NaN, 0, 0)));
            Assert.That(group.transform.position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Move_Undo_RestoresGroupAndBuildingTogether()
        {
            Links(group, building);
            building.position = new Vector3(2, 0, 4);
            Undo.IncrementCurrentGroup();
            LevelGroupMoveTool.Move(group, Vector3.one);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(group.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(building.position, Is.EqualTo(new Vector3(2, 0, 4)));
        }

        private static Transform Child(string name, Transform parent)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void Links(LevelLayoutGroup target, params Transform[] members)
        {
            var data = new SerializedObject(target);
            SerializedProperty links = data.FindProperty("linkedMembers");
            links.arraySize = members.Length;
            for (int i = 0; i < members.Length; i++) links.GetArrayElementAtIndex(i).objectReferenceValue = members[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

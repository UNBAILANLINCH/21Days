// 职责：验证遭遇表现层在等距场景中使用 XZ，而不是误把高度 Y 当成逻辑纵轴。
// 为什么新建：MonsterRulesTests 只验证纯规则；场景坐标适配属于独立的表现契约。
using System.Reflection;
using Game.Monster;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Monster
{
    public sealed class EncounterSceneViewTests
    {
        private GameObject host;
        private GameObject spawn;
        private GameObject patrol;
        private EncounterSceneView view;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Encounter View");
            spawn = new GameObject("Player Spawn");
            patrol = new GameObject("Patrol Point");
            view = host.AddComponent<EncounterSceneView>();
            SetField("useXZPlane", true);
            SetField("playerSpawn", spawn.transform);
            SetField("patrolPoints", new[] { patrol.transform });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(patrol);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void XZPlane_MapsSceneMarkersAndBodiesWithoutChangingHeight()
        {
            spawn.transform.position = new Vector3(1f, 7f, 2f);
            patrol.transform.position = new Vector3(3f, 8f, 4f);

            Assert.That(view.PlayerStart, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(view.PatrolPositions(), Is.EqualTo(new[] { new Vector2(3f, 4f) }));

            MethodInfo method = typeof(EncounterSceneView).GetMethod(
                "ToScenePosition", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var mapped = (Vector3)method.Invoke(view, new object[] { new Vector2(5f, 6f), new Vector3(0f, 9f, 0f) });
            Assert.That(mapped, Is.EqualTo(new Vector3(5f, 9f, 6f)));
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(EncounterSceneView).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(view, value);
        }
    }
}

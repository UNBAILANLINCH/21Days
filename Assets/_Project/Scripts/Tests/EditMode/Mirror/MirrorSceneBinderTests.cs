// 职责：钉住 MirrorSceneBinder 的换算——followsMonster 的标记读 MonsterModel.Position、静态标记读场景坐标投影、
//   只收激活的标记为候选、通灵视区域的范围与条件判定（PRP/mirror-core 4 风险「扇形判定用逻辑坐标」的测试）。
// 为什么新建：一个被测类一个测试类；MirrorRulesTests 只测纯函数 CandidatePosition，这里验证绑定器真的走了那条分支。
//   不 Start 绑定器（不扫场景），标记经反射塞进登记表；场景里没有 EncounterSceneView，投影按 XY。
using System.Collections.Generic;
using System.Reflection;
using Game.Core.Telemetry;
using Game.Mirror;
using Game.Monster;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorSceneBinder"/> 的 EditMode 测试。MonsterModel 用默认值（位置在原点）。</summary>
    public sealed class MirrorSceneBinderTests
    {
        private MirrorSceneBinder binder;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            binder = new MirrorSceneBinder(new MonsterModel(), NullTelemetryScope.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            binder.Dispose();
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void LogicPositionOf_FollowsMonster_UsesModelPosition()
        {
            MirrorSubject subject = CreateSubject(new Vector3(3f, 4f, 5f), MirrorSubjectKind.Yao, 1, followsMonster: true);

            Assert.That(binder.LogicPositionOf(subject), Is.EqualTo(Vector2.zero), "巡逻怪以模型位置为准，不读场景坐标");
        }

        [Test]
        public void LogicPositionOf_Static_UsesProjectedScenePosition()
        {
            MirrorSubject subject = CreateSubject(new Vector3(3f, 4f, 5f), MirrorSubjectKind.Human, 0, followsMonster: false);

            Assert.That(binder.LogicPositionOf(subject), Is.EqualTo(new Vector2(3f, 4f)), "没有 EncounterSceneView 时按 XY 投影");
        }

        [Test]
        public void CollectCandidates_SkipsInactive_KeepsOwnersAligned()
        {
            MirrorSubject active = CreateSubject(new Vector3(1f, 0f, 0f), MirrorSubjectKind.Human, 0, false);
            MirrorSubject inactive = CreateSubject(new Vector3(2f, 0f, 0f), MirrorSubjectKind.Object, 0, false);
            MirrorSubject yao = CreateSubject(new Vector3(3f, 0f, 0f), MirrorSubjectKind.Yao, 2, false);
            inactive.gameObject.SetActive(false);
            Register(active, inactive, yao);
            var candidates = new List<MirrorCandidate>();
            var owners = new List<MirrorSubject>();

            binder.CollectCandidates(candidates, owners);

            Assert.That(candidates.Count, Is.EqualTo(2));
            Assert.That(owners, Is.EqualTo(new[] { active, yao }));
            Assert.That(candidates[1].Kind, Is.EqualTo(MirrorSubjectKind.Yao));
            Assert.That(candidates[1].YaoId, Is.EqualTo(2));
            Assert.That(candidates[1].Position, Is.EqualTo(new Vector2(3f, 0f)));
        }

        [Test]
        public void FindActiveZone_InsideDimZone_ReturnsZone()
        {
            SpiritSightZone zone = CreateZone(Vector3.zero, new Vector3(4f, 4f, 4f), dim: true);
            RegisterZones(zone);

            Assert.That(binder.FindActiveZone(new Vector2(1f, 1f)), Is.SameAs(zone));
            Assert.That(binder.FindActiveZone(new Vector2(3f, 0f)) == null, Is.True, "区外");
        }

        [Test]
        public void FindActiveZone_NoCondition_ReturnsNull()
        {
            SpiritSightZone zone = CreateZone(Vector3.zero, new Vector3(4f, 4f, 4f), dim: false);
            RegisterZones(zone);

            Assert.That(binder.FindActiveZone(Vector2.zero) == null, Is.True, "三个条件都为假的区域不生效");
        }

        private MirrorSubject CreateSubject(Vector3 position, MirrorSubjectKind kind, int yaoId, bool followsMonster)
        {
            var go = new GameObject("TestSubject");
            created.Add(go);
            go.transform.position = position;
            MirrorSubject subject = go.AddComponent<MirrorSubject>();
            var serialized = new SerializedObject(subject);
            serialized.FindProperty("kind").enumValueIndex = (int)kind;
            serialized.FindProperty("yaoId").intValue = yaoId;
            serialized.FindProperty("followsMonster").boolValue = followsMonster;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return subject;
        }

        private SpiritSightZone CreateZone(Vector3 center, Vector3 size, bool dim)
        {
            var go = new GameObject("TestZone");
            created.Add(go);
            go.transform.position = center;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            SpiritSightZone zone = go.AddComponent<SpiritSightZone>();
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("area").objectReferenceValue = box;
            serialized.FindProperty("dim").boolValue = dim;
            serialized.FindProperty("rain").boolValue = false;
            serialized.FindProperty("night").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return zone;
        }

        // 不 Start 绑定器（Start 会扫当前打开的场景、挂 SceneManager 回调），直接把标记塞进私有登记表。
        // Game.Runtime 未对测试程序集开 InternalsVisibleTo，经反射（同 DialogueInteractableTests 的做法）。
        private void Register(params MirrorSubject[] subjects)
        {
            var list = (List<MirrorSubject>)typeof(MirrorSceneBinder)
                .GetField("subjects", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(binder);
            list.AddRange(subjects);
        }

        private void RegisterZones(params SpiritSightZone[] zones)
        {
            var list = (List<SpiritSightZone>)typeof(MirrorSceneBinder)
                .GetField("zones", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(binder);
            list.AddRange(zones);
        }
    }
}

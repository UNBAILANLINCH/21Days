// 职责：钉住 PerformanceTrigger 的锚点摆放与「演出期间隐藏触发者 / 点名场景物体」——传锚点世界位姿、演出中 Renderer 与
//   Canvas 全关、结束（正常 / 异常 / 取消）后恢复原 enabled 值；默认不隐藏、不传摆放。
// 为什么新建（复用 → 扩展 → 新建）：PerformanceTriggerRulesTests 测的是纯判定静态函数，不涉及组件与服务；
//   触发器组件是另一个被测类，按「一个被测类一个测试类」新建。
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Performance;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Performance
{
    public sealed class PerformanceTriggerTests
    {
        private const string Id = "perf_trigger_test";

        private readonly List<Object> created = new List<Object>();
        private PerformanceTrigger trigger;
        private Transform anchor;
        private PerformanceTriggerActor actor;
        private SpriteRenderer visible;
        private MeshRenderer alreadyHidden;
        private PendingService service;

        [SetUp]
        public void SetUp()
        {
            var triggerGo = Track(new GameObject("perf_trigger"));
            trigger = triggerGo.AddComponent<PerformanceTrigger>();
            var anchorGo = Track(new GameObject("perf_anchor"));
            anchor = anchorGo.transform;
            anchor.SetPositionAndRotation(new Vector3(4f, 0f, 2f), Quaternion.Euler(0f, 180f, 0f));

            var actorGo = Track(new GameObject("perf_actor"));
            actor = actorGo.AddComponent<PerformanceTriggerActor>();
            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(actorGo.transform, false);
            visible = spriteGo.AddComponent<SpriteRenderer>();
            var meshGo = new GameObject("Shadow");
            meshGo.transform.SetParent(actorGo.transform, false);
            alreadyHidden = meshGo.AddComponent<MeshRenderer>();
            alreadyHidden.enabled = false;

            service = new PendingService();
            trigger.Bind(service, null);
        }

        [TearDown]
        public void TearDown()
        {
            service.Finish();
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void TryFire_WithAnchor_PassesAnchorWorldPose()
        {
            Configure(anchor, false);

            trigger.TryFire(actor);

            Assert.That(service.Calls, Is.EqualTo(1));
            Assert.That(service.LastPlacement.HasValue, Is.True);
            Assert.That(Vector3.Distance(service.LastPlacement.Position, anchor.position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(service.LastPlacement.Rotation, anchor.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void TryFire_Defaults_NoPlacementAndActorStaysVisible()
        {
            Configure(null, false);

            trigger.TryFire(actor);

            Assert.That(service.LastPlacement.HasValue, Is.False, "没接锚点不传摆放");
            Assert.That(visible.enabled, Is.True, "默认不隐藏触发者");
        }

        [Test]
        public void TryFire_HideActorVisual_HidesDuringAndRestoresAfter()
        {
            Configure(null, true);

            trigger.TryFire(actor);

            Assert.That(visible.enabled, Is.False, "演出期间触发者的渲染器应全部关掉");
            Assert.That(alreadyHidden.enabled, Is.False);

            service.Finish();

            Assert.That(visible.enabled, Is.True, "结束后恢复原值");
            Assert.That(alreadyHidden.enabled, Is.False, "本来关着的不该被打开");
        }

        [Test]
        public void TryFire_HideActorVisual_RestoresWhenPlayFails()
        {
            Configure(null, true);
            trigger.TryFire(actor);
            LogAssert.Expect(LogType.Error, new Regex("播放失败"));

            service.Fail(new InvalidOperationException("假演出失败"));

            Assert.That(visible.enabled, Is.True, "异常收尾也要恢复");
            Assert.That(alreadyHidden.enabled, Is.False);
        }

        [Test]
        public void TryFire_HiddenDuringPlay_HidesRenderersAndCanvasesThenRestores()
        {
            Configure(null, false);
            var npc = Track(new GameObject("perf_npc"));
            var npcSprite = npc.AddComponent<SpriteRenderer>();
            var tagGo = new GameObject("NameTag");
            tagGo.transform.SetParent(npc.transform, false);
            var nameTag = tagGo.AddComponent<Canvas>();
            var offTagGo = new GameObject("OffTag");
            offTagGo.transform.SetParent(npc.transform, false);
            var offTag = offTagGo.AddComponent<Canvas>();
            offTag.enabled = false;
            // 空引用、重复引用、嵌套根都不该崩，也不该把「已被关掉」记成原值。
            SetHiddenDuringPlay(npc, null, npc, tagGo);

            trigger.TryFire(actor);

            Assert.That(npcSprite.enabled, Is.False, "点名物体的 Renderer 演出期间关掉");
            Assert.That(nameTag.enabled, Is.False, "点名物体的 Canvas（名牌 / 标记）演出期间关掉");
            Assert.That(visible.enabled, Is.True, "没勾 hideActorVisual 时触发者不受影响");

            service.Finish();

            Assert.That(npcSprite.enabled, Is.True, "结束后恢复 Renderer");
            Assert.That(nameTag.enabled, Is.True, "结束后恢复 Canvas");
            Assert.That(offTag.enabled, Is.False, "本来关着的 Canvas 不该被打开");
        }

        [Test]
        public void TryFire_HiddenDuringPlay_RestoresWhenCancelled()
        {
            Configure(null, true);
            var npc = Track(new GameObject("perf_npc"));
            var npcSprite = npc.AddComponent<SpriteRenderer>();
            SetHiddenDuringPlay(npc);
            trigger.TryFire(actor);
            Assert.That(npcSprite.enabled, Is.False);

            service.Fail(new OperationCanceledException());

            Assert.That(npcSprite.enabled, Is.True, "取消收尾也要恢复");
            Assert.That(visible.enabled, Is.True);
        }

        private void SetHiddenDuringPlay(params GameObject[] roots)
        {
            using (var so = new SerializedObject(trigger))
            {
                SerializedProperty list = so.FindProperty("hiddenDuringPlay");
                list.arraySize = roots.Length;
                for (int i = 0; i < roots.Length; i++)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = roots[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private void Configure(Transform anchorValue, bool hide)
        {
            using (var so = new SerializedObject(trigger))
            {
                so.FindProperty("performanceId").stringValue = Id;
                so.FindProperty("once").boolValue = false;
                so.FindProperty("anchor").objectReferenceValue = anchorValue;
                so.FindProperty("hideActorVisual").boolValue = hide;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        /// <summary>PlayAsync 挂起直到用例调 Finish / Fail；记录调用次数与最后一次摆放。</summary>
        private sealed class PendingService : IPerformanceService
        {
            private UniTaskCompletionSource<PerformanceResult> pending;

            public int Calls { get; private set; }
            public PerformancePlacement LastPlacement { get; private set; }
            public bool IsRunning => pending != null;
            public string CurrentId => pending == null ? null : Id;
            public bool HasPlayed(string id) => false;

            public UniTask<PerformanceResult> PlayAsync(string id, CancellationToken ct = default) =>
                PlayAsync(id, PerformancePlacement.None, ct);

            public UniTask<PerformanceResult> PlayAsync(string id, PerformancePlacement placement, CancellationToken ct = default)
            {
                Calls++;
                LastPlacement = placement;
                pending = new UniTaskCompletionSource<PerformanceResult>();
                return pending.Task;
            }

            public void Finish()
            {
                UniTaskCompletionSource<PerformanceResult> source = pending;
                pending = null;
                source?.TrySetResult(new PerformanceResult(Id, PerformanceOutcome.Completed, 0f));
            }

            public void Fail(Exception error)
            {
                UniTaskCompletionSource<PerformanceResult> source = pending;
                pending = null;
                source?.TrySetException(error);
            }

            public void Confirm() { }
            public void Skip() { }
        }
    }
}

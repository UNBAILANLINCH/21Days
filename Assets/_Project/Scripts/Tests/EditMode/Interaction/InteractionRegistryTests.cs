// 职责：钉住统一交互登记表 InteractionRegistry（PRP/interaction 第二波 WARN 1、WARN 2）：
//   重复登记忽略、注销按引用比较（两个已销毁对象不会被认成同一个）、SetActor 触发 OnActorChanged（含置空、同值不触发）、
//   玩家标记与订阅方谁先到都拿得到、场景卸载后旧标记伪空时重扫找回新标记（找不到才置空）、卸载时清掉已销毁候选。
// 为什么新建：第一波只测了焦点与选择函数，登记表本身没有测试，审查留了两条 WARN；按「一个被测类一个测试类」新建。
//   扫场景经构造注入的扫描函数替身完成：EditMode 下 SceneManager 扫到的是编辑器里开着的场景（SampleScene 里就有玩家标记），结果不可控。
using System.Collections.Generic;
using Game.Interaction;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Interaction
{
    public sealed class InteractionRegistryTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<InteractionActor> actorEvents = new List<InteractionActor>();
        private InteractionActor scanResult;
        private int scanCount;
        private InteractionRegistry registry;

        [SetUp]
        public void SetUp()
        {
            scanResult = null;
            scanCount = 0;
            actorEvents.Clear();
            registry = new InteractionRegistry(() =>
            {
                scanCount++;
                return scanResult;
            });
        }

        [TearDown]
        public void TearDown()
        {
            registry.Dispose();
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        // ── 候选登记 ────────────────────────────────────────────

        [Test]
        public void Register_SameCandidateTwice_IsIgnored()
        {
            var crate = new FakeInteractable(Vector3.zero, 1f);

            registry.Register(crate);
            registry.Register(crate);
            registry.Register(null);

            Assert.That(registry.Candidates.Count, Is.EqualTo(1), "重复登记与 null 都忽略");
            Assert.That(registry.Candidates[0], Is.SameAs(crate));
        }

        [Test]
        public void Unregister_ComparesByReference_EvenForTwoDestroyedObjects()
        {
            // Unity 对象的 Equals 会把两个已销毁对象判成相等：按 Equals 找会删错成第一个。
            UnityCandidate first = NewUnityCandidate("First");
            UnityCandidate second = NewUnityCandidate("Second");
            registry.Register(first);
            registry.Register(second);
            Object.DestroyImmediate(first.gameObject);
            Object.DestroyImmediate(second.gameObject);

            registry.Unregister(second);

            Assert.That(registry.Candidates.Count, Is.EqualTo(1));
            Assert.That(ReferenceEquals(registry.Candidates[0], first), Is.True, "删的是传进来的那一个，不是 Equals 认成相等的第一个");
        }

        [Test]
        public void Unregister_NeverRegistered_IsIgnored()
        {
            var registered = new FakeInteractable(Vector3.zero, 1f);
            registry.Register(registered);

            registry.Unregister(new FakeInteractable(Vector3.zero, 1f));
            registry.Unregister(null);

            Assert.That(registry.Candidates.Count, Is.EqualTo(1));
        }

        // ── 玩家标记 ────────────────────────────────────────────

        [Test]
        public void SetActor_RaisesOnActorChanged_IncludingNull_ButNotForTheSameActor()
        {
            InteractionActor actor = NewActor("Player");
            registry.OnActorChanged += actorEvents.Add;

            registry.SetActor(actor);
            registry.SetActor(actor);
            registry.SetActor(null);

            Assert.That(actorEvents.Count, Is.EqualTo(2), "同一个标记再设一次不触发");
            Assert.That(actorEvents[0], Is.SameAs(actor));
            Assert.That(ReferenceEquals(actorEvents[1], null), Is.True, "置空也要通知，订阅方据此清掉测距角色");
            Assert.That(ReferenceEquals(registry.Actor, null), Is.True);
        }

        [Test]
        public void SubscriberFirst_ThenActorArrives_GetsNotified()
        {
            registry.OnActorChanged += actorEvents.Add;
            InteractionActor actor = NewActor("Player");

            registry.SetActor(actor);

            Assert.That(actorEvents, Is.EqualTo(new[] { actor }), "订阅方先到：标记找到时收到补通知");
        }

        [Test]
        public void ActorFirst_ThenSubscriber_ReadsActorDirectly()
        {
            InteractionActor actor = NewActor("Player");
            registry.SetActor(actor);

            registry.OnActorChanged += actorEvents.Add;

            Assert.That(actorEvents, Is.Empty, "事件只报变化，不补发历史");
            Assert.That(registry.Actor, Is.SameAs(actor), "标记先到：订阅方订阅时直接读 Actor（DialogueSceneBinder 就是这么做的）");
        }

        [Test]
        public void Start_TakesTheActorFromTheLoadedScenesScan()
        {
            scanResult = NewActor("Player");

            registry.Start();

            Assert.That(registry.Actor, Is.SameAs(scanResult));
            Assert.That(scanCount, Is.EqualTo(1), "启动时扫一遍已加载场景");
        }

        // ── 场景卸载（WARN 1） ──────────────────────────────────

        [Test]
        public void SceneUnloaded_OldActorDestroyed_RescansAndFindsTheNewSceneActor()
        {
            // 复现时序：新场景的 sceneLoaded 先到（旧标记还活着，新标记被跳过）→ 旧场景卸载、旧标记成伪空。
            InteractionActor oldActor = NewActor("OldPlayer");
            registry.SetActor(oldActor);
            InteractionActor newActor = NewActor("NewPlayer");
            scanResult = newActor;
            registry.OnActorChanged += actorEvents.Add;
            Object.DestroyImmediate(oldActor.gameObject);

            registry.HandleSceneUnloaded();

            Assert.That(registry.Actor, Is.SameAs(newActor), "重扫已加载场景，把新场景的玩家标记找回来；否则交互键整个失灵");
            Assert.That(actorEvents, Is.EqualTo(new[] { newActor }), "直接换成新标记，只通知一次");
        }

        [Test]
        public void SceneUnloaded_OldActorDestroyed_NothingLeft_ClearsToNull()
        {
            InteractionActor oldActor = NewActor("OldPlayer");
            registry.SetActor(oldActor);
            registry.OnActorChanged += actorEvents.Add;
            Object.DestroyImmediate(oldActor.gameObject);

            registry.HandleSceneUnloaded();

            Assert.That(ReferenceEquals(registry.Actor, null), Is.True, "已加载场景里没有玩家标记：显式置空（不留伪空）");
            Assert.That(actorEvents.Count, Is.EqualTo(1));
            Assert.That(ReferenceEquals(actorEvents[0], null), Is.True);
        }

        [Test]
        public void SceneUnloaded_ActorStillAlive_DoesNotRescan()
        {
            // 负对照：卸的是别的场景（如叠加的战斗场景），玩家标记还活着，不重扫、不通知。
            InteractionActor actor = NewActor("Player");
            registry.SetActor(actor);
            registry.OnActorChanged += actorEvents.Add;

            registry.HandleSceneUnloaded();

            Assert.That(registry.Actor, Is.SameAs(actor));
            Assert.That(scanCount, Is.Zero);
            Assert.That(actorEvents, Is.Empty);
        }

        [Test]
        public void SceneUnloaded_RemovesDestroyedCandidatesOnly()
        {
            UnityCandidate gone = NewUnityCandidate("Gone");
            UnityCandidate kept = NewUnityCandidate("Kept");
            var plain = new FakeInteractable(Vector3.zero, 1f);
            registry.Register(gone);
            registry.Register(kept);
            registry.Register(plain);
            Object.DestroyImmediate(gone.gameObject);

            registry.HandleSceneUnloaded();

            Assert.That(registry.Candidates.Count, Is.EqualTo(2), "已销毁的候选随场景卸载移除，活着的与纯 C# 候选留下");
            Assert.That(ReferenceEquals(registry.Candidates[0], kept), Is.True);
            Assert.That(ReferenceEquals(registry.Candidates[1], plain), Is.True);
        }

        // ── 辅助 ────────────────────────────────────────────────

        private InteractionActor NewActor(string name)
        {
            var go = new GameObject("InteractionRegistryTests_" + name);
            created.Add(go);
            return go.AddComponent<InteractionActor>();
        }

        private UnityCandidate NewUnityCandidate(string name)
        {
            var go = new GameObject("InteractionRegistryTests_" + name);
            created.Add(go);
            return go.AddComponent<UnityCandidate>();
        }

        /// <summary>挂在物体上的候选：用来造「已销毁（伪空）」的情形，纯 C# 替身销毁不了。</summary>
        private sealed class UnityCandidate : MonoBehaviour, IInteractable
        {
            public Vector3 Position => Vector3.zero;
            public float InteractionRadius => 1f;
            public bool CanInteract => true;
            public int InteractionPriority => 0;
            public InteractionPrompt Prompt => new InteractionPrompt("对话", name);
            public void Interact() { }
            public void OnFocusChanged(bool focused) { }
        }
    }
}

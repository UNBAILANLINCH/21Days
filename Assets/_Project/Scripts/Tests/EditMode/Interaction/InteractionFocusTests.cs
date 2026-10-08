// 职责：钉住统一交互焦点 InteractionFocus 的一帧判定（PRP/interaction D3、D4）：
//   四条让位条件（没有玩家标记、Gameplay 图未启用、沉浸、世界暂停）各自清空焦点且按键不响应；
//   焦点在本帧变化时按键与 HUD 点击都不响应，下一帧才响应；焦点变化回调新旧两方；焦点对象被销毁（伪空）时清焦点但不回调它；纯判定 ShouldYield。
// 为什么新建：Interaction 是新模块，按「一个被测类一个测试类」新建。Tick 要读真实设备输入，测试改调 Advance 直接喂
//   「Gameplay 图开没开、本帧按没按」，帧号由构造参数注入，时序可控；HUD 点击走 RequestInteract，读真实动作集的 Gameplay 图启用状态。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Interaction;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Interaction
{
    public sealed class InteractionFocusTests
    {
        private GameObject player;
        private InteractionRegistry registry;
        private FakeHud hud;
        private FakePause pause;
        private RealInput input;
        private InteractionFocus focus;
        private int frame;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("InteractionFocusTestPlayer");
            registry = new InteractionRegistry();
            registry.SetActor(player.AddComponent<InteractionActor>());
            hud = new FakeHud();
            pause = new FakePause();
            input = new RealInput();
            frame = 1;
            focus = new InteractionFocus(registry, new NoUI(), hud, pause, input, new NoSubscriber(), null, () => frame);
        }

        [TearDown]
        public void TearDown()
        {
            focus.Dispose();
            registry.Dispose();
            input.Dispose();
            Object.DestroyImmediate(player);
        }

        // ── 纯判定 ────────────────────────────────────────────────

        [TestCase(true, true, false, false, false, TestName = "ShouldYield_AllClear_False")]
        [TestCase(false, true, false, false, true, TestName = "ShouldYield_NoActor_True")]
        [TestCase(true, false, false, false, true, TestName = "ShouldYield_GameplayMapDisabled_True")]
        [TestCase(true, true, true, false, true, TestName = "ShouldYield_HudHidden_True")]
        [TestCase(true, true, false, true, true, TestName = "ShouldYield_WorldPaused_True")]
        public void ShouldYield_EachCondition(bool hasActor, bool gameplay, bool hudHidden, bool paused, bool expected)
        {
            Assert.That(InteractionFocus.ShouldYield(hasActor, gameplay, hudHidden, paused), Is.EqualTo(expected));
        }

        // ── 让位：每一条单独一条用例（焦点清空 + 按键不响应） ─────────────────

        [Test]
        public void Advance_NoActor_YieldsFocusAndIgnoresKey()
        {
            FakeInteractable target = AddInRange();
            Settle();
            registry.SetActor(null);

            AdvanceNextFrame(gameplay: true, pressed: true);

            Assert.That(focus.Current, Is.Null, "没有玩家标记时无焦点");
            Assert.That(target.InteractCount, Is.EqualTo(0));
            Assert.That(target.Focused, Is.False, "失焦回调已到");
        }

        [Test]
        public void Advance_GameplayMapDisabled_YieldsFocusAndIgnoresKey()
        {
            FakeInteractable target = AddInRange();
            Settle();

            AdvanceNextFrame(gameplay: false, pressed: true);

            Assert.That(focus.Current, Is.Null, "Gameplay 图关着（对白 / 演出 / 任务面板 / 镜碎页）时无焦点");
            Assert.That(target.InteractCount, Is.EqualTo(0));
        }

        [Test]
        public void Advance_HudHidden_YieldsFocusAndIgnoresKey()
        {
            FakeInteractable target = AddInRange();
            Settle();
            hud.IsHudHidden = true;

            AdvanceNextFrame(gameplay: true, pressed: true);

            Assert.That(focus.Current, Is.Null, "沉浸模式无焦点");
            Assert.That(target.InteractCount, Is.EqualTo(0));
        }

        [Test]
        public void Advance_WorldPaused_YieldsFocusAndIgnoresKey()
        {
            FakeInteractable target = AddInRange();
            Settle();
            pause.IsPaused = true;

            AdvanceNextFrame(gameplay: true, pressed: true);

            Assert.That(focus.Current, Is.Null, "世界暂停时无焦点");
            Assert.That(target.InteractCount, Is.EqualTo(0));
        }

        [Test]
        public void RequestInteract_WhileYielding_IsIgnored()
        {
            input.EnableGameplay();
            FakeInteractable target = AddInRange();
            Settle();
            hud.IsHudHidden = true;
            frame++;

            focus.RequestInteract();

            Assert.That(target.InteractCount, Is.EqualTo(0), "沉浸时 HUD 点击无效");
        }

        // ── 本帧焦点变化不触发（D4） ───────────────────────────────

        [Test]
        public void Advance_FocusChangedThisFrame_KeyIgnored_NextFrameInteracts()
        {
            FakeInteractable target = AddInRange();

            focus.Advance(true, true);
            Assert.That(focus.Current, Is.SameAs(target), "第一帧成为焦点");
            Assert.That(target.InteractCount, Is.EqualTo(0), "焦点本帧刚变：这一帧的按键不响应");

            AdvanceNextFrame(gameplay: true, pressed: true);
            Assert.That(target.InteractCount, Is.EqualTo(1), "下一帧焦点没变：按键响应一次");
        }

        [Test]
        public void RequestInteract_FocusChangedThisFrame_Ignored_NextFrameInteracts()
        {
            input.EnableGameplay();
            FakeInteractable target = AddInRange();
            focus.Advance(true, false);

            focus.RequestInteract();
            Assert.That(target.InteractCount, Is.EqualTo(0), "HUD 点击走同一判定：焦点本帧刚变不响应");

            frame++;
            focus.RequestInteract();
            Assert.That(target.InteractCount, Is.EqualTo(1));
        }

        [Test]
        public void Advance_SwitchToCloserTarget_SameFrameKeyGoesNowhere()
        {
            FakeInteractable first = AddInRange(new Vector3(1.5f, 0f, 0f));
            Settle();
            FakeInteractable second = AddInRange(new Vector3(0.5f, 0f, 0f));

            AdvanceNextFrame(gameplay: true, pressed: true);

            Assert.That(focus.Current, Is.SameAs(second), "更近的新对象抢到焦点");
            Assert.That(first.InteractCount + second.InteractCount, Is.EqualTo(0), "换焦点那一帧谁都不触发");
            Assert.That(first.Focused, Is.False);
            Assert.That(second.Focused, Is.True);
        }

        [Test]
        public void Advance_KeyPress_RaisesOnInteractedOnce()
        {
            FakeInteractable target = AddInRange();
            Settle();
            int raised = 0;
            IInteractable received = null;
            focus.OnInteracted += t =>
            {
                raised++;
                received = t;
            };

            AdvanceNextFrame(gameplay: true, pressed: true);

            Assert.That(raised, Is.EqualTo(1));
            Assert.That(received, Is.SameAs(target));
        }

        [Test]
        public void Advance_FocusChange_CallsBackOldAndNewAndRaisesEventOnce()
        {
            FakeInteractable target = AddInRange();
            int changes = 0;
            focus.OnFocusChanged += _ => changes++;

            focus.Advance(true, false);
            AdvanceNextFrame(gameplay: true, pressed: false);

            Assert.That(changes, Is.EqualTo(1), "焦点没变的帧不再触发");
            Assert.That(target.Focused, Is.True);
            Assert.That(target.FocusCallbacks, Is.EqualTo(1));

            target.CanInteract = false;
            AdvanceNextFrame(gameplay: true, pressed: false);
            Assert.That(focus.Current, Is.Null);
            Assert.That(target.Focused, Is.False);
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test]
        public void Advance_FocusedObjectDestroyed_ClearsFocusWithoutCallingBackTheDeadObject()
        {
            // 第二波 WARN 2：焦点对象随场景销毁成伪空——焦点要清掉、订阅方要收到「变为空」，但不能回调已销毁的对象。
            var host = new GameObject("InteractionFocusTests_DestroyedTarget");
            DestroyableTarget target = host.AddComponent<DestroyableTarget>();
            registry.Register(target);
            Settle();
            Assert.That(focus.Current, Is.SameAs(target), "前置：它先成为焦点");
            Assert.That(target.FalseCallbacks, Is.Zero);
            int changes = 0;
            IInteractable notified = target;
            focus.OnFocusChanged += next =>
            {
                changes++;
                notified = next;
            };

            Object.DestroyImmediate(host);
            AdvanceNextFrame(gameplay: true, pressed: true);

            Assert.That(ReferenceEquals(focus.Current, null), Is.True, "伪空对象被选择函数跳过，焦点清空");
            Assert.That(changes, Is.EqualTo(1), "订阅方收到一次焦点变化");
            Assert.That(ReferenceEquals(notified, null), Is.True, "变化后的焦点是真 null，不是伪空");
            Assert.That(target.FalseCallbacks, Is.Zero, "已销毁对象不回调 OnFocusChanged(false)");
            Assert.That(target.InteractCount, Is.Zero, "也不会对它触发交互");
        }

        private FakeInteractable AddInRange() => AddInRange(new Vector3(1f, 0f, 0f));

        private FakeInteractable AddInRange(Vector3 position)
        {
            var target = new FakeInteractable(position, 2f);
            registry.Register(target);
            return target;
        }

        // 先让焦点稳定下来（第一帧焦点变化，不计按键）。
        private void Settle() => focus.Advance(true, false);

        private void AdvanceNextFrame(bool gameplay, bool pressed)
        {
            frame++;
            focus.Advance(gameplay, pressed);
        }

        /// <summary>挂在物体上的候选（站在原点旁 1 米、半径 2）：用来造「焦点对象被销毁」的伪空情形。</summary>
        private sealed class DestroyableTarget : MonoBehaviour, IInteractable
        {
            public int FalseCallbacks { get; private set; }
            public int InteractCount { get; private set; }
            public Vector3 Position => new Vector3(1f, 0f, 0f);
            public float InteractionRadius => 2f;
            public bool CanInteract => true;
            public int InteractionPriority => 0;
            public InteractionPrompt Prompt => new InteractionPrompt("对话", "替身");
            public void Interact() => InteractCount++;

            public void OnFocusChanged(bool focused)
            {
                if (!focused) FalseCallbacks++;
            }
        }

        private sealed class FakeHud : IHudVisibility
        {
            public bool IsHudHidden { get; set; }
            public void SetHudHidden(bool hidden) => IsHudHidden = hidden;
        }

        private sealed class FakePause : IWorldPauseService
        {
            public bool IsPaused { get; set; }
            public IDisposable Acquire(object owner) => new NoDispose();
        }

        private sealed class NoDispose : IDisposable
        {
            public void Dispose() { }
        }

        /// <summary>真实动作集：RequestInteract 读 Gameplay 图的启用状态。收尾关图并销毁资产，避免析构断言。</summary>
        private sealed class RealInput : IInputService, IDisposable
        {
            private GameInput actions = new GameInput();

            public GameInput Actions => actions;
            public void EnableMap(string map) { }
            public void DisableMap(string map) { }
            public void EnableGameplay() => actions.Gameplay.Enable();

            public void Dispose()
            {
                if (actions == null) return;
                actions.Disable();
                if (actions.asset != null) Object.DestroyImmediate(actions.asset);
                actions = null;
            }
        }

        private sealed class NoUI : IUIService
        {
            public UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView =>
                UniTask.FromException<T>(new NotSupportedException("测试不开面板"));
            public UniTask CloseAsync(UIView view, CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask CloseTopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public T Get<T>() where T : UIView => null;
            public void SetLayerVisible(UILayer layer, bool visible) { }
            public bool IsLayerVisible(UILayer layer) => true;
        }

        private sealed class NoSubscriber : ISubscriber<BootCompletedEvent>
        {
            public IDisposable Subscribe(IMessageHandler<BootCompletedEvent> handler, params MessageHandlerFilter<BootCompletedEvent>[] filters) =>
                new NoDispose();
        }
    }
}

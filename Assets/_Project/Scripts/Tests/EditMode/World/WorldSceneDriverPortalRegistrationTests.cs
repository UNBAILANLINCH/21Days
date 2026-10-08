// 职责：钉住 WorldSceneDriver 的「按键型传送点登记进统一交互」——登记 / 换图注销重登 / 下发目的地显示名 / Dispose 全注销。
// 为什么新建：这段逻辑（SyncPortalSubscriptions / UnsubscribeAll）此前没有任何测试直接断言；
//   WorldInstallerTests 只验装配，PortalAnchorTests 只验组件本身，都不碰驱动与登记表的交互。
//   做法：驱动用真类型 + 假登记表（记录调用）；binder 的登记表经反射写入（binder 只在场景加载时才填，EditMode 里不进场景）。
using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Simulation;
using Game.Core.Timing;
using Game.Interaction;
using Game.Player;
using Game.World;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="WorldSceneDriver"/> 传送点登记的 EditMode 测试。</summary>
    public sealed class WorldSceneDriverPortalRegistrationTests
    {
        private readonly List<GameObject> hosts = new List<GameObject>();
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        private RecordingRegistry registry;
        private WorldSceneBinder binder;
        private WorldSceneDriver driver;

        [SetUp]
        public void SetUp()
        {
            registry = new RecordingRegistry();
            binder = new WorldSceneBinder();

            var playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            var simulationConfig = ScriptableObject.CreateInstance<SimulationConfig>();
            assets.Add(playerConfig);
            assets.Add(simulationConfig);

            var player = new PlayerModel();
            var runner = new SimulationRunner(simulationConfig, new LocalClock(), new SilentInput(),
                new RandomService(1UL), null);
            driver = new WorldSceneDriver(binder, WorldTestSupport.RealCatalog(), new UnusedTransition(),
                new UnusedFlow(), registry, player, new PlayerRules(playerConfig, player, null), runner, null);
        }

        [TearDown]
        public void TearDown()
        {
            driver.Dispose();
            for (int i = 0; i < hosts.Count; i++)
            {
                if (hosts[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(hosts[i]);
                }
            }

            for (int i = 0; i < assets.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(assets[i]);
            }
        }

        [Test]
        public void Tick_InteractPortal_Registered_EnterRangePortal_NotRegistered()
        {
            PortalAnchor interact = NewPortal(PortalTriggerKind.Interact, WorldTestSupport.YaoScene);
            PortalAnchor enter = NewPortal(PortalTriggerKind.EnterRange, WorldTestSupport.YaoScene);
            SetBinderPortals(interact, enter);

            driver.Tick();

            Assert.That(registry.Registered, Is.EqualTo(new IInteractable[] { interact }), "只有按键型进统一交互");
            Assert.That(registry.Registered.Contains(enter), Is.False, "进入范围型由驱动自己轮询，不登记");
        }

        [Test]
        public void Tick_SameVersion_DoesNotRegisterAgain()
        {
            PortalAnchor interact = NewPortal(PortalTriggerKind.Interact, WorldTestSupport.YaoScene);
            SetBinderPortals(interact);

            driver.Tick();
            driver.Tick();

            Assert.That(registry.RegisterCalls, Is.EqualTo(1), "版本号没变不重订");
            Assert.That(registry.UnregisterCalls, Is.EqualTo(0));
        }

        [Test]
        public void Tick_PortalsVersionChanged_UnregistersOldAndRegistersNew()
        {
            PortalAnchor oldA = NewPortal(PortalTriggerKind.Interact, WorldTestSupport.YaoScene);
            PortalAnchor oldB = NewPortal(PortalTriggerKind.Interact, WorldTestSupport.HumanScene);
            SetBinderPortals(oldA, oldB);
            driver.Tick();
            registry.ClearCalls();

            PortalAnchor fresh = NewPortal(PortalTriggerKind.Interact, WorldTestSupport.HumanScene);
            SetBinderPortals(fresh);
            driver.Tick();

            Assert.That(registry.Unregistered, Is.EquivalentTo(new IInteractable[] { oldA, oldB }), "旧传送点全部注销");
            Assert.That(registry.Registered, Is.EqualTo(new IInteractable[] { fresh }), "新传送点登记");
        }

        [Test]
        public void Tick_OldPortalsDestroyedOnSceneUnload_StillUnregisteredByReference()
        {
            PortalAnchor old = NewPortal(PortalTriggerKind.Interact, WorldTestSupport.YaoScene);
            SetBinderPortals(old);
            driver.Tick();
            registry.ClearCalls();

            // 模拟场景卸载：组件成了伪空，登记表清空，版本号 +1。
            UnityEngine.Object.DestroyImmediate(old.gameObject);
            SetBinderPortals();
            driver.Tick();

            Assert.That(registry.Unregistered.Count, Is.EqualTo(1));
            Assert.That(ReferenceEquals(registry.Unregistered[0], old), Is.True, "伪空的旧组件也要按引用从登记表拿掉");
            Assert.That(registry.Registered, Is.Empty);
        }

        [Test]
        public void Tick_InteractPortal_GetsDestinationDisplayName()
        {
            PortalAnchor portal = NewPortal(PortalTriggerKind.Interact, WorldTestSupport.YaoScene);
            SetBinderPortals(portal);

            driver.Tick();

            Assert.That(portal.DestinationName, Is.EqualTo("镜中妖界长安·坊市"), "名字取 TbScene.display_name");
            Assert.That(portal.Prompt.Name, Is.EqualTo("镜中妖界长安·坊市"));
        }

        [Test]
        public void Tick_UnknownDestinationScene_PromptHasVerbOnly()
        {
            PortalAnchor portal = NewPortal(PortalTriggerKind.Interact, "no_such_scene");
            SetBinderPortals(portal);

            driver.Tick();

            Assert.That(portal.DestinationName, Is.Empty, "查不到显示名给空串");
            Assert.That(portal.Prompt.Name, Is.Empty, "提示只显示动词");
            Assert.That(portal.Prompt.Verb, Is.Not.Empty);
            Assert.That(registry.Registered, Is.EqualTo(new IInteractable[] { portal }), "查不到名字也照常登记");
        }

        [Test]
        public void Dispose_UnregistersAllRegisteredPortals()
        {
            PortalAnchor a = NewPortal(PortalTriggerKind.Interact, WorldTestSupport.YaoScene);
            PortalAnchor b = NewPortal(PortalTriggerKind.Interact, WorldTestSupport.HumanScene);
            SetBinderPortals(a, b);
            driver.Tick();
            registry.ClearCalls();

            driver.Dispose();

            Assert.That(registry.Unregistered, Is.EquivalentTo(new IInteractable[] { a, b }));
            registry.ClearCalls();
            driver.Tick();
            Assert.That(registry.RegisterCalls, Is.EqualTo(0), "Dispose 之后 Tick 不再登记");
        }

        // ---------------------------------------------------------------- 辅助

        private PortalAnchor NewPortal(PortalTriggerKind kind, string targetSceneKey)
        {
            var host = new GameObject("PortalForDriverTests");
            hosts.Add(host);
            PortalAnchor portal = host.AddComponent<PortalAnchor>();
            SetField(portal, "triggerKind", kind);
            SetField(portal, "targetSceneKey", targetSceneKey);
            return portal;
        }

        // binder 的传送点表只在场景加载时才填；EditMode 里不进场景，经反射替换内容并 +1 版本号（等同它登记 / 移除时的行为）。
        private void SetBinderPortals(params PortalAnchor[] portals)
        {
            var list = (List<PortalAnchor>)typeof(WorldSceneBinder)
                .GetField("portals", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(binder);
            list.Clear();
            list.AddRange(portals);
            PropertyInfo version = typeof(WorldSceneBinder).GetProperty(nameof(WorldSceneBinder.PortalsVersion));
            version.SetValue(binder, binder.PortalsVersion + 1);
        }

        private static void SetField(PortalAnchor portal, string field, object value)
        {
            FieldInfo member = typeof(PortalAnchor).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(member, Is.Not.Null, $"PortalAnchor 上没有字段 {field}——测试与实现不同步了");
            member.SetValue(portal, value);
        }

        private sealed class RecordingRegistry : IInteractionRegistry
        {
            public List<IInteractable> Registered { get; } = new List<IInteractable>();

            public List<IInteractable> Unregistered { get; } = new List<IInteractable>();

            public int RegisterCalls => Registered.Count;

            public int UnregisterCalls => Unregistered.Count;

            public IReadOnlyList<IInteractable> Candidates => Registered;

            public InteractionActor Actor => null;

            public event Action<InteractionActor> OnActorChanged
            {
                add { }
                remove { }
            }

            public void Register(IInteractable interactable) => Registered.Add(interactable);

            public void Unregister(IInteractable interactable) => Unregistered.Add(interactable);

            public void ClearCalls()
            {
                Registered.Clear();
                Unregistered.Clear();
            }
        }

        private sealed class UnusedFlow : IGameFlow
        {
            public GameState Current => null;

            public UniTask GoToAsync<TState>(System.Threading.CancellationToken ct = default) where TState : GameState =>
                throw new NotSupportedException("本组用例不触发转场");
        }

        private sealed class UnusedTransition : IWorldTransition
        {
            public bool HasPending => false;

            public WorldTransitionRequest Current => null;

            public void Request(WorldTransitionRequest request) => throw new NotSupportedException("本组用例不触发转场");

            public WorldTransitionResolution Peek() => throw new NotSupportedException("本组用例不触发转场");

            public WorldTransitionResolution TryConsume() => throw new NotSupportedException("本组用例不触发转场");

            public void Clear()
            {
            }
        }

        private sealed class SilentInput : IInputSource
        {
            public void Sample(long tick)
            {
            }

            public InputCommand Current => InputCommand.Empty;
        }
    }
}

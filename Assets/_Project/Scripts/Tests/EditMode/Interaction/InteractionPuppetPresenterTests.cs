// 职责：钉住交互转向表现 InteractionPuppetPresenter（PRP/interaction D11）：焦点触发交互后玩家小人转向目标并进入朝向保持、
//   目标层级里有小人时它转向玩家；找不到小人（世界灰盒玩家、物资箱、纯 C# 对象）就跳过不报错；没有玩家标记时谁都不转；Dispose 后不再响应。
// 为什么新建：表现层组件是第二波新增的，按「一个被测类一个测试类」新建。
//   左右判定要主相机：ChibiPuppet 首次 FaceTowards 才取 Camera.main 并缓存，EditMode 下 Camera.main 取到的是编辑器里开着的场景的相机，
//   结果不可控，所以经反射把门面的缓存相机预先写成本测试建的、右方向 = +X 的相机（同 PortalAnchorTests 写私有字段的做法）。
using System;
using System.Collections.Generic;
using System.Reflection;
using Game.CharacterPuppet;
using Game.Interaction;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode.Interaction
{
    public sealed class InteractionPuppetPresenterTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private Camera testCamera;
        private InteractionRegistry registry;
        private FakeFocus focus;
        private InteractionPuppetPresenter presenter;
        private InteractionActor actor;
        private ChibiPuppet playerPuppet;

        [SetUp]
        public void SetUp()
        {
            testCamera = NewObject("InteractionPuppetPresenterTests_Camera").AddComponent<Camera>();
            testCamera.transform.rotation = Quaternion.identity; // 右方向 = +X
            registry = new InteractionRegistry(() => null);
            focus = new FakeFocus();
            presenter = new InteractionPuppetPresenter(focus, registry);
            presenter.Start();

            // 玩家：根挂 InteractionActor，小人在子物体上（同 SampleScene 的 player/Visual/Chibi_amiya）。
            GameObject player = NewObject("InteractionPuppetPresenterTests_Player");
            actor = player.AddComponent<InteractionActor>();
            playerPuppet = NewPuppet(player.transform, "Chibi_player");
            registry.SetActor(actor);
        }

        [TearDown]
        public void TearDown()
        {
            presenter.Dispose();
            registry.Dispose();
            for (int i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void Interacted_NpcWithPuppet_PlayerAndNpcFaceEachOtherAndHold()
        {
            // 玩家在原点朝左，NPC 在右边 2 米朝右：交互后玩家应转向右、NPC 应转向左（面对玩家）。
            playerPuppet.SetFacing(true, true);
            NpcTarget npc = NewNpc(new Vector3(2f, 0f, 0f), withPuppet: true);
            ChibiPuppet npcPuppet = npc.GetComponentInChildren<ChibiPuppet>();
            npcPuppet.SetFacing(false, true);

            focus.RaiseInteracted(npc);

            Assert.That(playerPuppet.FaceLeft, Is.False, "玩家转向目标（目标在相机右方）");
            Assert.That(playerPuppet.FacingHeld, Is.True, "进入朝向保持：站着不动时驱动层不会把它改回去");
            Assert.That(npcPuppet.FaceLeft, Is.True, "NPC 转向玩家（玩家在它左边）");
            Assert.That(npcPuppet.FacingHeld, Is.True);
        }

        [Test]
        public void Interacted_TargetPuppetOnParent_IsFoundToo()
        {
            // 可交互组件挂在角色根的子物体上时，小人在父链上。
            GameObject root = NewObject("InteractionPuppetPresenterTests_NpcRoot");
            root.transform.position = new Vector3(-2f, 0f, 0f);
            ChibiPuppet rootPuppet = root.AddComponent<ChibiPuppet>();
            SetCamera(rootPuppet);
            var child = new GameObject("Trigger");
            child.transform.SetParent(root.transform, false);
            NpcTarget npc = child.AddComponent<NpcTarget>();

            focus.RaiseInteracted(npc);

            Assert.That(rootPuppet.FacingHeld, Is.True, "父链上的小人也转向玩家");
            Assert.That(rootPuppet.FaceLeft, Is.False, "玩家在它右边");
            Assert.That(playerPuppet.FaceLeft, Is.True, "玩家转向左边的目标");
        }

        [Test]
        public void Interacted_TargetWithoutPuppet_OnlyPlayerTurns()
        {
            // 物资箱 / 传送点：目标层级里没有小人，只转玩家，不报错。
            NpcTarget crate = NewNpc(new Vector3(-1.5f, 0f, 0f), withPuppet: false);

            Assert.DoesNotThrow(() => focus.RaiseInteracted(crate));

            Assert.That(playerPuppet.FacingHeld, Is.True);
            Assert.That(playerPuppet.FaceLeft, Is.True);
        }

        [Test]
        public void Interacted_PlainCSharpTarget_OnlyPlayerTurns()
        {
            var target = new FakeInteractable(new Vector3(3f, 0f, 0f), 5f);
            playerPuppet.SetFacing(true, true);

            Assert.DoesNotThrow(() => focus.RaiseInteracted(target));

            Assert.That(playerPuppet.FaceLeft, Is.False);
            Assert.That(playerPuppet.FacingHeld, Is.True);
        }

        [Test]
        public void Interacted_PlayerWithoutPuppet_NpcStillFacesThePlayer()
        {
            // 世界灰盒场景的玩家只有方块：玩家那半跳过，NPC 照常转向玩家标记的位置。
            Object.DestroyImmediate(playerPuppet.gameObject);
            NpcTarget npc = NewNpc(new Vector3(2f, 0f, 0f), withPuppet: true);
            ChibiPuppet npcPuppet = npc.GetComponentInChildren<ChibiPuppet>();

            Assert.DoesNotThrow(() => focus.RaiseInteracted(npc));

            Assert.That(npcPuppet.FacingHeld, Is.True);
            Assert.That(npcPuppet.FaceLeft, Is.True);
        }

        [Test]
        public void Interacted_NoActor_NobodyTurns()
        {
            // 负对照：没有玩家标记时不知道「玩家在哪」，谁都不转（交互本身在焦点系统那里也不会发生）。
            registry.SetActor(null);
            NpcTarget npc = NewNpc(new Vector3(2f, 0f, 0f), withPuppet: true);
            ChibiPuppet npcPuppet = npc.GetComponentInChildren<ChibiPuppet>();

            Assert.DoesNotThrow(() => focus.RaiseInteracted(npc));

            Assert.That(npcPuppet.FacingHeld, Is.False);
            Assert.That(playerPuppet.FacingHeld, Is.False);
        }

        [Test]
        public void Dispose_Unsubscribes()
        {
            presenter.Dispose();
            NpcTarget npc = NewNpc(new Vector3(2f, 0f, 0f), withPuppet: true);

            focus.RaiseInteracted(npc);

            Assert.That(playerPuppet.FacingHeld, Is.False, "释放后不再响应交互");
            Assert.That(focus.SubscriberCount, Is.Zero);
        }

        // ── 辅助 ────────────────────────────────────────────────

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        private ChibiPuppet NewPuppet(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            ChibiPuppet puppet = go.AddComponent<ChibiPuppet>();
            SetCamera(puppet);
            return puppet;
        }

        private NpcTarget NewNpc(Vector3 position, bool withPuppet)
        {
            GameObject go = NewObject("InteractionPuppetPresenterTests_Npc");
            go.transform.position = position;
            NpcTarget npc = go.AddComponent<NpcTarget>();
            if (withPuppet) NewPuppet(go.transform, "Chibi_npc");
            return npc;
        }

        // 门面首次 FaceTowards 才取 Camera.main 并缓存；预先写入本测试的相机，左右判定才确定。
        private void SetCamera(ChibiPuppet puppet)
        {
            FieldInfo field = typeof(ChibiPuppet).GetField("cachedCamera", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "ChibiPuppet 上没有字段 cachedCamera——测试与实现不同步了");
            field.SetValue(puppet, testCamera);
        }

        /// <summary>挂在物体上的可交互替身（NPC / 箱子）：位置取 transform。</summary>
        private sealed class NpcTarget : MonoBehaviour, IInteractable
        {
            public Vector3 Position => transform.position;
            public float InteractionRadius => 3f;
            public bool CanInteract => true;
            public int InteractionPriority => 0;
            public InteractionPrompt Prompt => new InteractionPrompt("对话", name);
            public void Interact() { }
            public void OnFocusChanged(bool focused) { }
        }

        /// <summary>只读焦点替身：测试直接抛 OnInteracted。</summary>
        private sealed class FakeFocus : IInteractionFocus
        {
            private Action<IInteractable> interacted;

            public IInteractable Current => null;
            public int SubscriberCount => interacted == null ? 0 : interacted.GetInvocationList().Length;

            public event Action<IInteractable> OnFocusChanged
            {
                add { }
                remove { }
            }

            public event Action<IInteractable> OnInteracted
            {
                add => interacted += value;
                remove => interacted -= value;
            }

            public void RaiseInteracted(IInteractable target) => interacted?.Invoke(target);
        }
    }
}

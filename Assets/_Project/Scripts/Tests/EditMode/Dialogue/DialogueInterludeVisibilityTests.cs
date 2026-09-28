// 职责：钉住对白插播时「藏哪些场景角色」的取根与去重规则（DialogueInterludeVisibility）：NPC 取 DialogueInteractable 那层、
//   玩家取 Actor 那层（DialogueInteractionActor / PerformanceTriggerActor 任一）、无标记的小人取场景顶层根、同一根下多个小人只算一次。
// 为什么新建：被测类是新建的纯静态规则，按「被测类 + Tests」单独成文件；隐藏 / 恢复本身由 PerformanceTriggerTests 覆盖，
//   插播期间藏、结束恢复的整条链路由 DialogueControllerTests 覆盖。
using System;
using System.Collections.Generic;
using Game.CharacterPuppet;
using Game.Dialogue;
using Game.Performance;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class DialogueInterludeVisibilityTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        [Test]
        public void ResolveCharacterRoot_NpcPuppet_ReturnsDialogueInteractableLayer()
        {
            // 场景容器 Village / Npc_Elder（DialogueInteractable）/ PuppetVisual / Chibi：取 NPC 根，不往上到场景顶层容器。
            GameObject village = Root("Village");
            GameObject npc = Child("Npc_Elder", village.transform);
            npc.AddComponent<DialogueInteractable>();
            ChibiPuppet puppet = Puppet(Child("PuppetVisual", npc.transform).transform);

            Assert.That(DialogueInterludeVisibility.ResolveCharacterRoot(puppet), Is.SameAs(npc));
        }

        [TestCase(typeof(DialogueInteractionActor))]
        [TestCase(typeof(PerformanceTriggerActor))]
        public void ResolveCharacterRoot_PlayerPuppet_ReturnsActorLayer(Type actorMarker)
        {
            // 场景容器 World / player（Actor 标记）/ Visual / Chibi：两种 Actor 标记任一都算玩家根。
            GameObject world = Root("World");
            GameObject player = Child("player", world.transform);
            player.AddComponent(actorMarker);
            ChibiPuppet puppet = Puppet(Child("Visual", player.transform).transform);

            Assert.That(DialogueInterludeVisibility.ResolveCharacterRoot(puppet), Is.SameAs(player));
        }

        [Test]
        public void ResolveCharacterRoot_UnmarkedPuppet_ReturnsSceneTopLevelRoot()
        {
            // 巡逻怪 enerme / Visual / Chibi：一路没有标记 → 兜底取场景顶层物体（连同名牌、影子、光圈一起藏）。
            GameObject patrol = Root("enerme");
            ChibiPuppet puppet = Puppet(Child("Visual", patrol.transform).transform);

            Assert.That(DialogueInterludeVisibility.ResolveCharacterRoot(puppet), Is.SameAs(patrol));
        }

        [Test]
        public void CollectCharacterRoots_SameRootPuppets_Deduplicated()
        {
            GameObject npc = Root("Npc_Pair");
            npc.AddComponent<DialogueInteractable>();
            ChibiPuppet first = Puppet(Child("PuppetA", npc.transform).transform);
            ChibiPuppet second = Puppet(Child("PuppetB", npc.transform).transform);
            GameObject patrol = Root("enerme");
            ChibiPuppet monster = Puppet(patrol.transform);

            List<GameObject> roots = DialogueInterludeVisibility.CollectCharacterRoots(
                new[] { first, null, second, monster, first });

            Assert.That(roots, Is.EqualTo(new[] { npc, patrol }), "同一角色根只出现一次，按首次出现的顺序；null 跳过");
            Assert.That(DialogueInterludeVisibility.CollectCharacterRoots(null), Is.Empty, "入参 null 返回空列表");
        }

        private GameObject Root(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        private static GameObject Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        // 同 Chibi_<名字>.prefab 的结构：根挂 ChibiPuppet，子物体 Sprite 挂渲染器。
        private static ChibiPuppet Puppet(Transform parent)
        {
            GameObject root = Child("Chibi", parent);
            Child("Sprite", root.transform).AddComponent<SpriteRenderer>();
            return root.AddComponent<ChibiPuppet>();
        }
    }
}

// 职责：守住 SampleScene 里「可对话的角色挡人」的接线——凡挂 DialogueInteractable 的物体，自身必须有启用的非 trigger 碰撞体，
//   且该碰撞体所在层包含在 EncounterSceneView.obstacleMask 里。否则玩家纸片的白盒扫掠（EncounterCollision.Slide）看不见它，
//   玩家会直接走进 NPC 身体里，两张纸片和头顶名字叠在一起（2026-09-30 修过一次：NPC 根节点在 Default 层、mask 只勾 Ground）。
// 为什么新建：复用——EncounterSceneViewTests 守的是投影 / 插值 / 分轴回写这些组件行为，用 new GameObject 搭，不读真实场景；
//   扩展——塞进它职责说不通：这是跨 Monster（obstacleMask）与 Dialogue（NPC 碰撞体）两边的场景接线契约，所以单独成类。
//   刻意读真实场景路径（同 TalkPanelConsistencyTests 刻意读真实预制体）：它守的就是 SampleScene 这份接线本身，
//   正式场景照 SampleScene 的接法抄（unity-assets.md），这里守住了，抄过去的也对。
// 场景按 Additive 打开、测完关掉；开发者已经开着 SampleScene 时直接读内存里的那份、不关，免得关掉别人的未保存改动。
using System.Collections.Generic;
using Game.Dialogue;
using Game.Monster;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Monster
{
    public sealed class SampleSceneObstacleWiringTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        private Scene scene;
        private bool openedHere;
        private bool wasListed;

        [SetUp]
        public void SetUp()
        {
            scene = SceneManager.GetSceneByPath(ScenePath);
            wasListed = scene.IsValid();
            openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, "打不开 " + ScenePath);
        }

        [TearDown]
        public void TearDown()
        {
            // 只关自己打开的；原本就在层级里（未加载）的只卸载、不移除。
            if (openedHere && scene.IsValid()) EditorSceneManager.CloseScene(scene, !wasListed);
        }

        [Test]
        public void DialogueInteractables_InSampleScene_AreBlockedByPlayerSweep()
        {
            List<EncounterSceneView> views = FindAll<EncounterSceneView>();
            List<DialogueInteractable> npcs = FindAll<DialogueInteractable>();
            Assert.That(views, Is.Not.Empty, ScenePath + " 里没有 EncounterSceneView");
            Assert.That(npcs, Is.Not.Empty, ScenePath + " 里没有 DialogueInteractable");

            var problems = new List<string>();
            foreach (EncounterSceneView view in views)
            {
                // obstacleMask 是私有序列化字段，按序列化名读落盘值（同 TalkPanelConsistencyTests 的做法）。
                int mask;
                using (var so = new SerializedObject(view))
                    mask = so.FindProperty("obstacleMask").intValue;

                foreach (DialogueInteractable npc in npcs)
                {
                    int solid = 0;
                    foreach (Collider collider in npc.GetComponents<Collider>())
                    {
                        if (collider.isTrigger || !collider.enabled) continue;
                        solid++;
                        int layer = collider.gameObject.layer;
                        if ((mask & (1 << layer)) == 0)
                            problems.Add($"{npc.name} 的 {collider.GetType().Name} 在「{LayerMask.LayerToName(layer)}」（第 {layer} 层），"
                                         + $"不在 {view.name} 的 obstacleMask（{mask}）里");
                    }

                    if (solid == 0) problems.Add($"{npc.name} 身上没有启用的非 trigger 碰撞体，玩家扫掠挡不住它");
                }
            }

            Assert.That(problems, Is.Empty,
                "玩家能走进可对话角色的身体里（NPC 根节点放 Character 层，并把该层勾进 EncounterSceneView.obstacleMask）：\n"
                + string.Join("\n", problems));
        }

        private List<T> FindAll<T>() where T : Component
        {
            var result = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }
    }
}

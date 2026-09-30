// 职责：守住 SampleScene 里「谁挡人」的接线约定（2026-09-30 用户定：箱子挡人、NPC 不挡人）：
//   1. 凡挂 SupplyCrate 的物体，自身必须有启用的非 trigger 碰撞体，碰撞体所在层包含在 EncounterSceneView.obstacleMask 里，
//      且竖直范围够得着玩家扫掠胶囊（盒顶高于脚底 + obstacleBottomOffset）；否则玩家能走进箱子里（2026-09-30 修过：箱子在 Default 层）。
//      箱子也不许放 Ground 层：OccluderFadePresenter 把 Ground 当遮挡物扫、贴地射线会把人抬上箱顶。
//   2. 可对话角色（DialogueInteractable）的碰撞体都不在 obstacleMask 里：玩家可以穿过 NPC，两张纸片的前后关系靠
//      SpriteDepthClip 的玩家深度偏移（见 CharacterPuppet 的 SpriteDepthBiasWiringTests），不靠碰撞把人隔开。
// 为什么新建：复用——EncounterSceneViewTests 守的是投影 / 插值 / 分轴回写这些组件行为，用 new GameObject 搭，不读真实场景；
//   扩展——塞进它职责说不通：这是跨 Monster（obstacleMask）与 Loot / Dialogue（场景物体碰撞体）的场景接线契约，所以单独成类。
//   刻意读真实场景路径（同 TalkPanelConsistencyTests 刻意读真实预制体）：它守的就是 SampleScene 这份接线本身，
//   正式场景照 SampleScene 的接法抄（unity-assets.md），这里守住了，抄过去的也对。
// 场景按 Additive 打开、测完关掉；开发者已经开着 SampleScene 时直接读内存里的那份、不关，免得关掉别人的未保存改动。
using System.Collections.Generic;
using Game.Dialogue;
using Game.Loot;
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
        private const string GroundLayerName = "Ground";

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
        public void SupplyCrates_InSampleScene_AreBlockedByPlayerSweep()
        {
            List<EncounterSceneView> views = FindAll<EncounterSceneView>();
            List<SupplyCrate> crates = FindAll<SupplyCrate>();
            Assert.That(views, Is.Not.Empty, ScenePath + " 里没有 EncounterSceneView");
            Assert.That(crates, Is.Not.Empty, ScenePath + " 里没有 SupplyCrate");

            int groundLayer = LayerMask.NameToLayer(GroundLayerName);
            var problems = new List<string>();
            foreach (EncounterSceneView view in views)
            {
                // obstacleMask / obstacleBottomOffset 是私有序列化字段，按序列化名读落盘值（同 TalkPanelConsistencyTests 的做法）。
                int mask;
                float bottomOffset;
                using (var so = new SerializedObject(view))
                {
                    mask = so.FindProperty("obstacleMask").intValue;
                    bottomOffset = so.FindProperty("obstacleBottomOffset").floatValue;
                }

                foreach (SupplyCrate crate in crates)
                {
                    int solid = 0;
                    foreach (Collider collider in crate.GetComponents<Collider>())
                    {
                        if (collider.isTrigger || !collider.enabled) continue;
                        solid++;
                        int layer = collider.gameObject.layer;
                        if ((mask & (1 << layer)) == 0)
                            problems.Add($"{crate.name} 的 {collider.GetType().Name} 在「{LayerMask.LayerToName(layer)}」（第 {layer} 层），"
                                         + $"不在 {view.name} 的 obstacleMask（{mask}）里");
                        if (layer == groundLayer)
                            problems.Add($"{crate.name} 在 {GroundLayerName} 层：会被遮挡淡出当遮挡物、被贴地射线打到盒顶，改放 Obstacle 层");

                        // 箱子根节点在地面上（脚底同高），盒顶要高过扫掠胶囊的下沿才挡得住。
                        float top = WorldTop(collider) - crate.transform.position.y;
                        if (top <= bottomOffset)
                            problems.Add($"{crate.name} 的碰撞盒顶只高出根节点 {top:0.###}，不高于胶囊下沿 {bottomOffset}，玩家扫掠碰不到");
                    }

                    if (solid == 0) problems.Add($"{crate.name} 身上没有启用的非 trigger 碰撞体，玩家扫掠挡不住它");
                }
            }

            Assert.That(problems, Is.Empty,
                "玩家能走进物资箱里（箱子根节点放 Obstacle 层并留非 trigger 碰撞体，该层勾进 EncounterSceneView.obstacleMask）：\n"
                + string.Join("\n", problems));
        }

        [Test]
        public void DialogueInteractables_InSampleScene_DoNotBlockPlayer()
        {
            List<EncounterSceneView> views = FindAll<EncounterSceneView>();
            List<DialogueInteractable> npcs = FindAll<DialogueInteractable>();
            Assert.That(views, Is.Not.Empty, ScenePath + " 里没有 EncounterSceneView");
            Assert.That(npcs, Is.Not.Empty, ScenePath + " 里没有 DialogueInteractable");

            var problems = new List<string>();
            foreach (EncounterSceneView view in views)
            {
                int mask;
                using (var so = new SerializedObject(view))
                    mask = so.FindProperty("obstacleMask").intValue;

                foreach (DialogueInteractable npc in npcs)
                {
                    foreach (Collider collider in npc.GetComponentsInChildren<Collider>(true))
                    {
                        if (collider.isTrigger) continue;
                        int layer = collider.gameObject.layer;
                        if ((mask & (1 << layer)) != 0)
                            problems.Add($"{collider.name} 的 {collider.GetType().Name} 在「{LayerMask.LayerToName(layer)}」（第 {layer} 层），"
                                         + $"被 {view.name} 的 obstacleMask（{mask}）挡住");
                    }
                }
            }

            Assert.That(problems, Is.Empty,
                "可对话角色挡人了（用户 2026-09-30 定：NPC 不挡人，碰撞体留在 Default 层；纸片前后靠玩家材质的深度偏移）：\n"
                + string.Join("\n", problems));
        }

        // BoxCollider 按中心与尺寸的 8 个角直接算世界最高点，不依赖 Additive 打开的场景有没有同步进物理场景；其它类型退回 bounds。
        private static float WorldTop(Collider collider)
        {
            var box = collider as BoxCollider;
            if (box == null) return collider.bounds.max.y;

            Vector3 center = box.center;
            Vector3 extents = box.size * 0.5f;
            float top = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    center.x + ((i & 1) == 0 ? -extents.x : extents.x),
                    center.y + ((i & 2) == 0 ? -extents.y : extents.y),
                    center.z + ((i & 4) == 0 ? -extents.z : extents.z));
                top = Mathf.Max(top, box.transform.TransformPoint(corner).y);
            }
            return top;
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

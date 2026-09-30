// 职责：守住「玩家纸片深度偏移」的约定（2026-09-30，修玩家与 NPC 同排重叠时两张纸片交错叠加）：
//   1. 玩家材质 M_SpriteDepthClip_Player 与基础材质 M_SpriteDepthClip 同一着色器、其余属性一致，只有 _DepthBias 不同：
//      基础材质为 0，玩家材质在 (0, 0.05) 内（0.05 是「明显深度差」的上限，超过它会把真实的前后关系颠倒；取值依据见着色器文件头）。
//   2. SampleScene 里玩家根（EncounterSceneView.PlayerBody）下凡用 SpriteDepthClip 着色器的 SpriteRenderer 都用玩家材质，
//      且至少有一个启用的（小人 Sprite）；场景里其它角色的同类渲染器偏移都是 0。
//      玩家小人材质是场景里对 Chibi_amiya 预制体实例的覆写：FramePuppetGenerator 重跑会把预制体里的材质写回基础材质，
//      换角色预制体也会丢掉这条覆写，这里读真实场景兜底。
//   刻意读真实资产路径（同 SampleSceneObstacleWiringTests / TalkPanelConsistencyTests）：守的就是这两份材质与 SampleScene 的接线本身。
// 为什么新建：复用——ChibiPuppetMotionRulesTests / FramePuppetRulesTests 测纯规则，不读资产；RenderPipelineTiersTests 守管线分档，
//   与角色材质无关。扩展——塞进 SampleSceneObstacleWiringTests（守「谁挡人」）职责说不通。按「守的约定 + Tests」单独成文件。
// 场景按 Additive 打开、测完关掉；开发者已经开着 SampleScene 时直接读内存里的那份、不关（同 SampleSceneObstacleWiringTests）。
using System.Collections.Generic;
using Game.Monster;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.CharacterPuppet
{
    public sealed class SpriteDepthBiasWiringTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ShaderName = "21Days/SpriteDepthClip";
        private const string BaseMaterialPath = "Assets/_Project/Art/Materials/Character/M_SpriteDepthClip.mat";
        private const string PlayerMaterialPath = "Assets/_Project/Art/Materials/Character/M_SpriteDepthClip_Player.mat";
        private const string BiasProperty = "_DepthBias";

        /// <summary>深度差超过它就算「明显」，必须按真实深度遮挡；玩家偏移必须小于它。</summary>
        private const float MaxBias = 0.05f;

        private Scene scene;
        private bool openedHere;
        private bool wasListed;

        [TearDown]
        public void TearDown()
        {
            // 只关自己打开的；原本就在层级里（未加载）的只卸载、不移除。
            if (openedHere && scene.IsValid()) EditorSceneManager.CloseScene(scene, !wasListed);
            openedHere = false;
        }

        [Test]
        public void PlayerMaterial_OnlyDiffersFromBaseByPositiveSmallDepthBias()
        {
            var baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
            var playerMaterial = AssetDatabase.LoadAssetAtPath<Material>(PlayerMaterialPath);
            Assert.That(baseMaterial, Is.Not.Null, "缺基础材质 " + BaseMaterialPath);
            Assert.That(playerMaterial, Is.Not.Null, "缺玩家材质 " + PlayerMaterialPath);
            Assert.That(baseMaterial.shader.name, Is.EqualTo(ShaderName));
            Assert.That(playerMaterial.shader, Is.EqualTo(baseMaterial.shader), "玩家材质要和基础材质用同一个着色器");
            Assert.That(baseMaterial.HasProperty(BiasProperty), Is.True, ShaderName + " 没有 " + BiasProperty + " 属性");

            Assert.That(baseMaterial.GetFloat(BiasProperty), Is.EqualTo(0f), "基础材质（NPC、巡逻者都用它）的深度偏移必须是 0");
            float bias = playerMaterial.GetFloat(BiasProperty);
            Assert.That(bias, Is.GreaterThan(0f).And.LessThan(MaxBias),
                $"玩家材质的深度偏移 {bias} 应在 (0, {MaxBias}) 内：0 修不了交错叠加，≥ {MaxBias} 会颠倒真实的前后关系");

            Assert.That(playerMaterial.GetFloat("_Cutoff"), Is.EqualTo(baseMaterial.GetFloat("_Cutoff")), "两份材质的 Alpha Cutoff 应一致");
            Assert.That(playerMaterial.GetColor("_Color"), Is.EqualTo(baseMaterial.GetColor("_Color")), "两份材质的 Tint 应一致");
            Assert.That(playerMaterial.renderQueue, Is.EqualTo(baseMaterial.renderQueue), "不许改渲染队列（透明队列会丢掉与灰盒的深度遮挡）");
        }

        [Test]
        public void SampleScene_PlayerSpritesUseBiasedMaterial_OtherCharactersStayUnbiased()
        {
            OpenScene();
            var views = new List<EncounterSceneView>();
            foreach (GameObject root in scene.GetRootGameObjects())
                views.AddRange(root.GetComponentsInChildren<EncounterSceneView>(true));
            Assert.That(views, Is.Not.Empty, ScenePath + " 里没有 EncounterSceneView");

            var problems = new List<string>();
            var playerRenderers = new HashSet<SpriteRenderer>();
            foreach (EncounterSceneView view in views)
            {
                Transform player = view.PlayerBody;
                if (player == null)
                {
                    problems.Add(view.name + " 没接玩家根（playerBody）");
                    continue;
                }

                int enabledBiased = 0;
                foreach (SpriteRenderer renderer in player.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (!UsesDepthClip(renderer)) continue;
                    playerRenderers.Add(renderer);
                    float bias = renderer.sharedMaterial.GetFloat(BiasProperty);
                    if (bias <= 0f)
                        problems.Add($"玩家的 {PathOf(renderer.transform)} 用的是 {renderer.sharedMaterial.name}（偏移 {bias}），应换成 M_SpriteDepthClip_Player");
                    else if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                        enabledBiased++;
                }

                if (enabledBiased == 0)
                    problems.Add($"{player.name} 下没有启用的、带深度偏移的角色纸片（小人 Sprite 应用 M_SpriteDepthClip_Player）");
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (playerRenderers.Contains(renderer) || !UsesDepthClip(renderer)) continue;
                    float bias = renderer.sharedMaterial.GetFloat(BiasProperty);
                    if (bias != 0f)
                        problems.Add($"{PathOf(renderer.transform)} 不是玩家，却用了偏移 {bias} 的 {renderer.sharedMaterial.name}（其它角色保持 0）");
                }
            }

            Assert.That(problems, Is.Empty,
                "玩家纸片的深度偏移接线不对，玩家与 NPC 同排重叠时会交错叠加或颠倒前后：\n" + string.Join("\n", problems));
        }

        private void OpenScene()
        {
            scene = SceneManager.GetSceneByPath(ScenePath);
            wasListed = scene.IsValid();
            openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, "打不开 " + ScenePath);
        }

        private static bool UsesDepthClip(SpriteRenderer renderer)
        {
            Material material = renderer.sharedMaterial;
            return material != null && material.shader != null && material.shader.name == ShaderName;
        }

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
    }
}

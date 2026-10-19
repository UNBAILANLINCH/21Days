// 复用五店真实进场和移动；既有回放没有同镜头地表前后对照，单独覆盖本次局部资产替换。
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Input;
using Game.Core.Timing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.ShopPlaytest
{
    [Category("Showcase")]
    public sealed class GroundAppearanceShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "ShopPlaytest";

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator LocalGround_BeforeAfterAndWalkingKeepShopInteraction()
        {
            yield return ReadyUnlockedShop(1);
            yield return CompareGround("下层衣肆");
            yield return GoToShop(3);
            yield return CompareGround("中层客栈");
            yield return Command(0);
            yield return Check("客栈原对白结束后恢复操作", () => !DialogueRunning, 5);
            yield return GoToWorldPoint(new Vector3(10.33f, 4.85f, 4.4f));
            var actions = ResolveService<IInputService>().Actions.Gameplay;
            yield return Input.Hold(actions.Sneak);
            yield return MoveTo(new Vector2(10.33f, 10.5f));
            yield return MoveTo(new Vector2(10.33f, 13.5f), .5f);
            yield return Input.Release(actions.Sneak);
            yield return CompareGround("上层平台");
            yield return CheckPreplacedObjects();
        }

        private IEnumerator CompareGround(string label)
        {
#if UNITY_EDITOR
            Input.ReleaseStick();
            yield return Wait(.5f);
            var root = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ShowcaseOptions.DemoScenePath).GetRootGameObjects()
                .Single(item => item.name == "DemonWorld");
            var filters = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(item => UnityEditor.AssetDatabase.GetAssetPath(item.sharedMesh) ==
                    "Assets/_Project/Art/fbx/CourtyardGroundLatest.glb").ToArray();
            Assert.That(filters.Length, Is.EqualTo(6));
            var currentMeshes = filters.Select(item => item.sharedMesh).ToArray();
            var currentMaterials = filters.Select(item => item.GetComponent<MeshRenderer>().sharedMaterials).ToArray();
            var oldAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/fbx/CourtyardLatest.glb");
            var oldMaterials = oldAssets.OfType<Material>().ToDictionary(item => item.name);
            var previousMeshes = new Dictionary<string, string>
            {
                { "立方体", "CourtyardStreetGround_Outward" },
                { "立方体.007", "CourtyardGround_Outward" },
                { "立方体.011", "CourtyardMirrorGround_Outward" },
                { "立方体.014", "CourtyardEastGround_Outward" }
            };
            Vector2 position = State.ControlledPosition;
            using (ResolveService<IWorldPauseService>().Acquire(this))
            {
                try
                {
                    for (int i = 0; i < filters.Length; i++)
                    {
                        string asset;
                        filters[i].sharedMesh = previousMeshes.TryGetValue(filters[i].name, out asset)
                            ? UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Project/Art/Meshes/" + asset + ".asset")
                            : oldAssets.OfType<Mesh>().Single(item => item.name == currentMeshes[i].name);
                        string[] slots = currentMaterials[i].Select(item => item.name).ToArray();
                        if (filters[i].name == "立方体.014") slots = new[] { "草.001", "草", "Lime._st1.002", "Lime._st1.003" };
                        filters[i].GetComponent<MeshRenderer>().sharedMaterials = slots.Select(item => oldMaterials[item]).ToArray();
                        Assert.That(filters[i].sharedMesh, Is.Not.Null);
                    }
                    yield return Snapshot(label + "_旧引用");
                }
                finally
                {
                    for (int i = 0; i < filters.Length; i++)
                    {
                        if (filters[i] == null) continue;
                        filters[i].sharedMesh = currentMeshes[i];
                        filters[i].GetComponent<MeshRenderer>().sharedMaterials = currentMaterials[i];
                    }
                }
                yield return Snapshot(label + "_新版局部地表");
                yield return Check(label + "前后对照没有传送玩家", () => Vector2.Distance(position, State.ControlledPosition) < .01f);
            }
#else
            yield break;
#endif
        }
    }
}

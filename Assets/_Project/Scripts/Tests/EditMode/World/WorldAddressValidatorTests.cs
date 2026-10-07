// 职责：钉住编辑器侧地址校验的**纯判定**——已实装场景的地址不在 Scenes 组里就报，且报出
//   「哪个场景、写了什么地址、组里有什么」；顺带对着真工程配置跑一遍（PRP/world-scenes §3 第 7 条）。
// 为什么新建：Runtime 不能引 UnityEditor，所以「地址真的在 Scenes 组里」这半只能放编辑器，
//   而它恰恰是最容易配错的一步（表里改了 implemented=true 却忘了登记场景）。
using System.Collections.Generic;
using Game.Editor.World;
using NUnit.Framework;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="WorldAddressValidator"/> 的 EditMode 测试。</summary>
    public sealed class WorldAddressValidatorTests
    {
        /// <summary>真工程 Scenes 组的现状：只有 IsometricEncounter 一条（Assets/AddressableAssetsData/AssetGroups/Scenes.asset:19–24）。</summary>
        private static readonly string[] RegisteredScenes = { "IsometricEncounter" };

        // ---------------------------------------------------------------- 纯判定

        [Test]
        public void Compare_ImplementedAddressNotRegistered_ReportsSceneAddressAndGroupContent()
        {
            var scenes = new[] { new WorldAddressValidator.ImplementedScene("human_jingyang", "HumanWorldScene") };

            IReadOnlyList<string> problems = WorldAddressValidator.Compare(
                scenes, RegisteredScenes, WorldAddressValidator.ScenesGroupName);

            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("human_jingyang"), "要点名是哪个场景");
            Assert.That(problems[0], Does.Contain("HumanWorldScene"), "要点名写了什么地址");
            Assert.That(problems[0], Does.Contain("IsometricEncounter"), "要列出组里现在有什么");
            Assert.That(problems[0], Does.Contain(WorldAddressValidator.ScenesGroupName));
            Assert.That(problems[0], Does.Contain("implemented"), "要给出修法");
        }

        [Test]
        public void Compare_AddressIsRegistered_Passes()
        {
            // 负对照：地址真的登记过时一条都不该报（否则这个工具会天天喊狼来了）。
            var scenes = new[] { new WorldAddressValidator.ImplementedScene("human_jingyang", "IsometricEncounter") };

            Assert.That(WorldAddressValidator.Compare(scenes, RegisteredScenes, WorldAddressValidator.ScenesGroupName),
                Is.Empty);
        }

        [Test]
        public void Compare_MultipleImplementedScenes_ReportsOnlyTheMissingOne()
        {
            var scenes = new[]
            {
                new WorldAddressValidator.ImplementedScene("human_jingyang", "Registered"),
                new WorldAddressValidator.ImplementedScene("yao_fangshi", "NotRegistered"),
            };

            IReadOnlyList<string> problems = WorldAddressValidator.Compare(scenes, new[] { "Registered" }, "Scenes");

            Assert.That(problems, Has.Count.EqualTo(1), "对上的那条不报，对不上的那条要报");
            Assert.That(problems[0], Does.Contain("yao_fangshi"));
            Assert.That(problems[0], Does.Not.Contain("human_jingyang"));
        }

        [Test]
        public void Compare_EmptyGroup_ListsNothingInsteadOfCrashing()
        {
            var scenes = new[] { new WorldAddressValidator.ImplementedScene("human_jingyang", "Whatever") };

            IReadOnlyList<string> problems = WorldAddressValidator.Compare(scenes, new string[0], "Scenes");

            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("一条都没有"), "组里空的时候也要说清（而不是留个空白让人猜）");
        }

        [Test]
        public void Compare_NoImplementedScenes_ReportsNothing()
        {
            Assert.That(WorldAddressValidator.Compare(new WorldAddressValidator.ImplementedScene[0], RegisteredScenes, "Scenes"),
                Is.Empty);
            Assert.That(WorldAddressValidator.Compare(null, RegisteredScenes, "Scenes"), Is.Empty, "空输入不该抛");
        }

        // ---------------------------------------------------------------- 对着真实配置跑

        [Test]
        public void Run_AgainstRealProjectConfig_ReportsNoErrors()
        {
            // 用真表字节 + 真 Addressables 设置跑一遍。接线波之后两张主图都 implemented=true，
            // 所以这一条现在的判据是「两个地址都**真的**登记在 Scenes 组里」——
            // 这正是「表改了地址却忘了登记场景」那个最容易配错的形状（PRP/world-scenes §2.4 第 4 条）。
            string report = WorldAddressValidator.Run(out int errors);

            Assert.That(errors, Is.EqualTo(0), "已实装场景的地址都该在 Scenes 组里：\n" + report);
            Assert.That(report, Does.Contain(WorldAddressValidator.ScenesGroupName), "报告要说明 Scenes 组的现状");
            Assert.That(report, Does.Contain("通过：2 个已实装场景"), "两张主图都要被点到名：\n" + report);
        }
    }
}

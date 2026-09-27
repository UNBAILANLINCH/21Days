// 职责：PlaceholderAssetGuardRules 的 EditMode 测试——用内存里的「根 → 依赖」映射验证命中判定、归一化与报告文本，
// 不碰真实资产（Ark 目录以后被替换掉，这些测试照样成立）。
// 为什么新建（复用 → 扩展 → 新建）：占位素材闸门是新写的编辑器规则类，没有现成测试可扩展。
using System.Collections.Generic;
using Game.Editor;
using NUnit.Framework;

namespace Game.Tests.EditMode.Editor.Build
{
    public sealed class PlaceholderAssetGuardRulesTests
    {
        private const string ArkPrefix = "Assets/_Project/Art/Sprites/Characters/Ark/";
        private const string AmiyaFrame = "Assets/_Project/Art/Sprites/Characters/Ark/amiya/chr_amiya_idle_00.png";
        private const string ChenFrame = "Assets/_Project/Art/Sprites/Characters/Ark/chen/chr_chen_walk_03.png";

        private static KeyValuePair<string, IEnumerable<string>> Root(string root, params string[] dependencies)
        {
            return new KeyValuePair<string, IEnumerable<string>>(root, dependencies);
        }

        [Test]
        public void DefaultForbiddenPrefixes_ContainsArkDirectory()
        {
            Assert.That(PlaceholderAssetGuardRules.DefaultForbiddenPrefixes, Does.Contain(ArkPrefix));
        }

        [Test]
        public void FindHits_WhenNoDependencyUnderPrefix_ReturnsEmpty()
        {
            List<PlaceholderAssetGuardRules.Hit> hits = PlaceholderAssetGuardRules.FindHits(
                new[]
                {
                    Root("场景 Assets/_Project/Scenes/Boot.unity",
                        "Assets/_Project/Scenes/Boot.unity",
                        "Assets/_Project/Art/Sprites/Characters/Player/idle.png"),
                },
                null);

            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void FindHits_WhenDependencyUnderPrefix_ReturnsRootAndAsset()
        {
            List<PlaceholderAssetGuardRules.Hit> hits = PlaceholderAssetGuardRules.FindHits(
                new[] { Root("场景 A.unity", "A.unity", "Assets/Prefabs/Chibi.prefab", AmiyaFrame) },
                null);

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0].Root, Is.EqualTo("场景 A.unity"));
            Assert.That(hits[0].AssetPath, Is.EqualTo(AmiyaFrame));
        }

        [Test]
        public void FindHits_WithMultipleRoots_ReportsEveryRootAndHit()
        {
            List<PlaceholderAssetGuardRules.Hit> hits = PlaceholderAssetGuardRules.FindHits(
                new[]
                {
                    Root("场景 A.unity", ChenFrame, AmiyaFrame),
                    Root("场景 Clean.unity", "Assets/Clean.png"),
                    Root("Addressables「UI」X", AmiyaFrame),
                },
                null);

            Assert.That(hits.Count, Is.EqualTo(3));
            // 同一根内按路径排序：amiya 在 chen 前
            Assert.That(hits[0].Root, Is.EqualTo("场景 A.unity"));
            Assert.That(hits[0].AssetPath, Is.EqualTo(AmiyaFrame));
            Assert.That(hits[1].AssetPath, Is.EqualTo(ChenFrame));
            Assert.That(hits[2].Root, Is.EqualTo("Addressables「UI」X"));
        }

        [Test]
        public void FindHits_WithBackslashesAndDifferentCase_NormalizesAndMatches()
        {
            string windowsPath = "assets\\_project\\Art\\Sprites\\Characters\\ARK\\amiya\\chr_amiya_idle_00.png";

            List<PlaceholderAssetGuardRules.Hit> hits = PlaceholderAssetGuardRules.FindHits(
                new[] { Root("场景 A.unity", windowsPath) },
                new[] { "Assets\\_Project\\Art\\Sprites\\Characters\\Ark" });

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0].AssetPath, Is.EqualTo(windowsPath.Replace('\\', '/')));
        }

        [Test]
        public void FindHits_WhenSameAssetListedTwiceInOneRoot_ReportsOnce()
        {
            List<PlaceholderAssetGuardRules.Hit> hits = PlaceholderAssetGuardRules.FindHits(
                new[] { Root("场景 A.unity", AmiyaFrame, AmiyaFrame.Replace('/', '\\')) },
                null);

            Assert.That(hits.Count, Is.EqualTo(1));
        }

        [Test]
        public void IsForbidden_WhenSiblingDirectorySharesPrefixText_DoesNotMatch()
        {
            bool forbidden = PlaceholderAssetGuardRules.IsForbidden(
                "Assets/_Project/Art/Sprites/Characters/Arknights/x.png",
                new[] { "Assets/_Project/Art/Sprites/Characters/Ark" });

            Assert.That(forbidden, Is.False);
        }

        [Test]
        public void BuildReport_WithHits_ContainsRootAndAssetPath()
        {
            List<PlaceholderAssetGuardRules.Hit> hits = PlaceholderAssetGuardRules.FindHits(
                new[] { Root("场景 Assets/Scenes/SampleScene.unity", AmiyaFrame) },
                null);

            string report = PlaceholderAssetGuardRules.BuildReport(hits, true);

            Assert.That(report, Does.Contain("场景 Assets/Scenes/SampleScene.unity → " + AmiyaFrame));
            Assert.That(report, Does.Contain("Release 出包已拦下"));
        }

        [Test]
        public void BuildReport_WithHits_EndsWithPerRootSummary()
        {
            List<PlaceholderAssetGuardRules.Hit> hits = PlaceholderAssetGuardRules.FindHits(
                new[] { Root("场景 A.unity", AmiyaFrame, ChenFrame), Root("场景 B.unity", ChenFrame) },
                null);

            string report = PlaceholderAssetGuardRules.BuildReport(hits, true);

            Assert.That(report, Does.Contain("2 个进包根引用了 2 个"));
            Assert.That(report, Does.Contain("场景 A.unity：2 个占位资产"));
            Assert.That(report, Does.Contain("场景 B.unity：1 个占位资产"));
        }

        [Test]
        public void BuildReport_WhenNotBlocking_SaysDevelopmentPasses()
        {
            List<PlaceholderAssetGuardRules.Hit> hits = new List<PlaceholderAssetGuardRules.Hit>
            {
                new PlaceholderAssetGuardRules.Hit("场景 A.unity", AmiyaFrame),
            };

            string report = PlaceholderAssetGuardRules.BuildReport(hits, false);

            Assert.That(report, Does.Contain("开发版放行"));
        }

        [Test]
        public void BuildReport_WithoutHits_SaysPassed()
        {
            string report = PlaceholderAssetGuardRules.BuildReport(new List<PlaceholderAssetGuardRules.Hit>(), true);

            Assert.That(report, Does.Contain("通过"));
        }
    }
}

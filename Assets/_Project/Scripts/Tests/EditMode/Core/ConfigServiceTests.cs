// 职责：覆盖「Luban 生成代码 + 磁盘上的 .bytes」这条链路，以及 ConfigService 缺表时的报错。
// 为什么新建：波 2 之前没有配置表测试。生成物是跟着 Excel 一起提交的，最容易出的事故是
// 「改了表忘了跑生成」或「代码更新了 .bytes 没跟上」——这两种都只在运行时炸，必须有 EditMode 测试兜住。

using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Config;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// 配置表测试。**不经 Addressables**：直接从磁盘读 <c>Assets/_Project/Data/Config/*.bytes</c>
    /// 喂给 <see cref="ConfigService.BuildTables"/>，所以不依赖 Addressables 的 Play Mode 设置、跑得也快。
    /// Addressables 那一段（标签 → TextAsset）由进 Play 模式时的启动流程覆盖。
    /// </summary>
    public sealed class ConfigServiceTests
    {
        /// <summary>示例表 TbItem 在 Tables/Data/item.xlsx 里的行数。改了示例表要同步改这里。</summary>
        private const int ExpectedItemCount = 4;

        private global::cfg.Tables tables;

        [SetUp]
        public void SetUp()
        {
            tables = ConfigService.BuildTables(ReadAllTableBytes());
        }

        [Test]
        public void BuildTables_WhenFedGeneratedBytes_LoadsEveryRow()
        {
            Assert.That(tables.TbItem, Is.Not.Null);
            Assert.That(tables.TbItem.DataList.Count, Is.EqualTo(ExpectedItemCount),
                "TbItem 行数和 Tables/Data/item.xlsx 对不上——改完表跑一次 scripts/gen-tables.ps1");
        }

        [Test]
        public void TbItem_WhenLookedUpById_ReturnsTheChineseName()
        {
            Assert.That(tables.TbItem.Get(1001).Name, Is.EqualTo("铁剑"));
            Assert.That(tables.TbItem.Get(1004).Name, Is.EqualTo("治疗药水"));
        }

        [Test]
        public void TbItem_WhenLookedUpById_ReturnsEnumAndNumericFields()
        {
            global::cfg.Item ironSword = tables.TbItem.Get(1001);
            Assert.That(ironSword.Quality, Is.EqualTo(global::cfg.EItemQuality.Common));
            Assert.That(ironSword.Price, Is.EqualTo(50));

            global::cfg.Item dragonArmor = tables.TbItem.Get(1003);
            Assert.That(dragonArmor.Quality, Is.EqualTo(global::cfg.EItemQuality.Epic));
            Assert.That(dragonArmor.Price, Is.EqualTo(1200));
        }

        [Test]
        public void TbItem_WhenFieldIsList_SplitsCellBySeparator()
        {
            Assert.That(tables.TbItem.Get(1002).Tags, Is.EqualTo(new[] { "武器", "近战", "锻造" }));
            Assert.That(tables.TbItem.Get(1004).Tags, Is.EqualTo(new[] { "消耗品", "回复" }));
        }

        [Test]
        public void EItemQuality_HasTheValuesDefinedInExcel()
        {
            Assert.That((int)global::cfg.EItemQuality.Common, Is.EqualTo(1));
            Assert.That((int)global::cfg.EItemQuality.Rare, Is.EqualTo(2));
            Assert.That((int)global::cfg.EItemQuality.Epic, Is.EqualTo(3));
        }

        [Test]
        public void BuildTables_WhenATableIsMissing_ThrowsWithTheTableName()
        {
            // 只拿掉 tbitem、其它表都在：不依赖生成代码里各表的加载先后（表一多，空字典报的是第一张表）。
            Dictionary<string, byte[]> withoutItem = ReadAllTableBytes();
            withoutItem.Remove("tbitem");

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() => ConfigService.BuildTables(withoutItem));

            Assert.That(error.Message, Does.Contain("tbitem"), "报错里要点名是哪张表缺了");
            Assert.That(error.Message, Does.Contain("gen-tables.ps1"), "报错里要给出修法");
        }

        /// <summary>
        /// 读 Data/Config 下全部 .bytes，键是不带扩展名的文件名——和 ConfigService 运行时用 TextAsset.name 一致。
        /// 路径从 <c>Application.dataPath</c> 推，不写死本机绝对路径。
        /// <para><see cref="ConfigContentHashTests"/> 也要这份真实数据，所以开成 internal 给它复用，别再抄一份。</para>
        /// </summary>
        internal static Dictionary<string, byte[]> ReadAllTableBytes()
        {
            string configDir = Path.Combine(Application.dataPath, "_Project/Data/Config");
            if (!Directory.Exists(configDir))
            {
                Assert.Fail($"找不到配置表数据目录 {configDir}。先跑一次 scripts/gen-tables.ps1。");
            }

            string[] files = Directory.GetFiles(configDir, "*.bytes", SearchOption.AllDirectories);
            Assert.That(files, Is.Not.Empty, "Data/Config 下一个 .bytes 都没有，先跑 scripts/gen-tables.ps1");

            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>(files.Length, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                result[Path.GetFileNameWithoutExtension(files[i])] = File.ReadAllBytes(files[i]);
            }

            return result;
        }
    }
}

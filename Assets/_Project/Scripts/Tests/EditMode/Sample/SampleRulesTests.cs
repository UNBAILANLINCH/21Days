// 职责：钉住 SampleRules 的定价规则——折后单价、订单总价、以及三类非法输入的报错。
// 为什么新建：SampleRules 是波 4 新增的第一个玩法规则类，Tests/EditMode/ 下原本只有 Core 的测试，
//   没有可扩展的文件；unity-tests.md 要求「一个玩法模块至少一条 EditMode 测试覆盖核心规则」。
//
// 这份文件同时是**玩法模块测试的样板**：规则类是纯 C#，所以这里不进播放模式、不加载场景、
// 不碰 Addressables，只用一个假的 IConfigService 喂真实的配置表数据。

using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Config;
using Game.Sample;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Sample
{
    /// <summary>
    /// <see cref="SampleRules"/> 的测试。
    /// <para>
    /// 配置数据直接从磁盘读 <c>Assets/_Project/Data/Config/*.bytes</c>（和 ConfigServiceTests 一个路子），
    /// 不经 Addressables：跑得快，也不受 Addressables 的 Play Mode 设置影响。
    /// 表里的数值（1001 铁剑 50、1002 精钢长剑 300、1003 龙鳞护甲 1200）改了要同步改这里。
    /// </para>
    /// </summary>
    public sealed class SampleRulesTests
    {
        private SampleRules rules;

        [SetUp]
        public void SetUp()
        {
            rules = new SampleRules(new FakeConfigService(ConfigService.BuildTables(ReadAllTableBytes())));
        }

        [Test]
        public void GetDiscountedPrice_WhenDiscountIsZero_ReturnsTablePrice()
        {
            Assert.That(rules.GetDiscountedPrice(1002, 0f), Is.EqualTo(300));
        }

        [Test]
        public void GetDiscountedPrice_WhenDiscountIsPartial_RoundsHalfAwayFromZero()
        {
            // 50 × (1 - 0.15) = 42.5：逢半进位取 43。
            // 这条同时钉住「钱用 decimal 算」：改回 double 的话 0.15f 的真值是 0.150000005960464…，
            // 算出来 42.4999997 会四舍五入成 42，这条测试就会红。
            Assert.That(rules.GetDiscountedPrice(1001, 0.15f), Is.EqualTo(43));
        }

        [Test]
        public void GetDiscountedPrice_WhenDiscountIsOne_ReturnsZero()
        {
            Assert.That(rules.GetDiscountedPrice(1003, 1f), Is.EqualTo(0), "折扣 1 = 免费，不是原价");
        }

        [Test]
        public void GetDiscountedPrice_WhenItemIdIsUnknown_ThrowsWithTheIdAndTheFix()
        {
            ArgumentOutOfRangeException error =
                Assert.Throws<ArgumentOutOfRangeException>(() => rules.GetDiscountedPrice(9999, 0f));

            Assert.That(error.Message, Does.Contain("9999"), "报错要点名是哪个 id");
            Assert.That(error.Message, Does.Contain("TbItem"), "报错要点名是哪张表");
        }

        [Test]
        public void GetDiscountedPrice_WhenDiscountIsOutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.GetDiscountedPrice(1001, -0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.GetDiscountedPrice(1001, 1.01f));
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.GetDiscountedPrice(1001, float.NaN),
                "NaN 不判就会一路算成 NaN 再转成一个看不懂的整数");
        }

        [Test]
        public void GetOrderTotal_WhenIntentBuysSeveral_MultipliesDiscountedUnitPrice()
        {
            var intent = new BuyItemIntent(1002, 3);

            // 300 × (1 - 0.2) = 240，再 ×3 = 720。
            Assert.That(rules.GetOrderTotal(intent, 0.2f), Is.EqualTo(720));
        }

        [Test]
        public void GetOrderTotal_WhenCountIsNotPositive_ThrowsWithTheIntent()
        {
            // default(BuyItemIntent) 绕过构造函数，Count 是 0——所以合法性由规则类在用的时候判。
            ArgumentOutOfRangeException error =
                Assert.Throws<ArgumentOutOfRangeException>(() => rules.GetOrderTotal(default, 0f));

            Assert.That(error.Message, Does.Contain("正数"));
        }

        /// <summary>
        /// 读 Data/Config 下全部 .bytes，键是不带扩展名的文件名——和运行时 ConfigService 用 TextAsset.name 一致。
        /// 路径从 <see cref="Application.dataPath"/> 推，不写死本机绝对路径。
        /// </summary>
        private static Dictionary<string, byte[]> ReadAllTableBytes()
        {
            string configDir = Path.Combine(Application.dataPath, "_Project/Data/Config");
            string[] files = Directory.GetFiles(configDir, "*.bytes", SearchOption.AllDirectories);
            Assert.That(files, Is.Not.Empty, "Data/Config 下一个 .bytes 都没有，先跑 scripts/gen-tables.ps1");

            Dictionary<string, byte[]> result = new Dictionary<string, byte[]>(files.Length, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                result[Path.GetFileNameWithoutExtension(files[i])] = File.ReadAllBytes(files[i]);
            }

            return result;
        }

        /// <summary>
        /// 假的配置服务：只把一份现成的 <c>cfg.Tables</c> 递出去。
        /// **这就是给框架服务写假实现的标准做法**——接口小、实现几行，测试不必拉起真服务，
        /// 也就不必进播放模式。玩法模块要假 <c>ISaveService</c> / <c>IClock</c> 时照这个写。
        /// </summary>
        private sealed class FakeConfigService : IConfigService
        {
            public FakeConfigService(global::cfg.Tables tables)
            {
                Tables = tables;
            }

            public global::cfg.Tables Tables { get; }

            /// <summary>
            /// 指纹是给回放比对用的，定价规则不该碰它——所以这里抛而不是返回 0：
            /// 哪天 SampleRules 偷偷读了指纹，测试会当场炸，而不是拿着一个假值静默通过。
            /// </summary>
            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }
    }
}

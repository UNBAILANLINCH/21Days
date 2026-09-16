// 职责：钉住 IConfigService.ContentHash 的三条性质——稳定（同数据同指纹）、敏感（改一个字节就变）、
//   与加载顺序无关（按标签批量加载的返回顺序不保证稳定，指纹不能跟着它变）。
// 为什么新建：同目录的 ConfigServiceTests 钉的是「Luban 生成代码 + 磁盘 .bytes」这条读表链路，
//   一个类一个关注点（unity-tests.md：一条测试一个关注点），指纹的判据（golden 值、顺序无关、
//   边界防撞）塞进去会把那份文件的主题冲散；真实表数据的读取则直接复用它的 ReadAllTableBytes，没有另抄一份。
//
// 第三条是本组测试的重点：指纹如果跟着加载顺序变，「配置没改」会被误报成「配置版本不匹配」，
// 而这个误报恰好出现在最需要相信这套机制的时候。所以除了「顺序反过来指纹不变」，
// 还用一个独立算出来的 golden 值钉死「规范顺序就是按表名 Ordinal 升序」——
// 只靠前者的话，万一哪天字典的遍历顺序碰巧不跟插入顺序走，那条断言就变成了永远通过的空断言。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Config;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// 配置内容指纹的 EditMode 测试。**不经 Addressables**：直接把「表名 → 字节」的字典喂给
    /// <see cref="ConfigService.ComputeContentHash"/>，真实数据走
    /// <see cref="ConfigServiceTests.ReadAllTableBytes"/>，合成数据在本文件里手搓。
    /// </summary>
    public sealed class ConfigContentHashTests
    {
        /// <summary>
        /// 固定样例 <c>{ tbalpha: [1,2], tbbeta: [3] }</c> 的指纹，用**独立于被测实现**的一份
        /// FNV-1a 脚本算出来（规范流：按表名 Ordinal 升序 → 表名每个码元拆高低两字节 → 4 字节小端长度 → 内容字节）。
        /// <para>
        /// 它同时钉两件事：算法是 FNV-1a 64 位；规范顺序是「按表名排序」而不是「按喂进来的顺序」——
        /// 样例里两张表的插入顺序和排序后顺序可以不一致，实现少了那步排序，这个值就对不上。
        /// </para>
        /// <para>指纹进了回放文件头就是对外格式，这个常数变了意味着所有旧回放作废，改它要有明确理由。</para>
        /// </summary>
        private const ulong GoldenSampleHash = 14484655062993257612UL;

        /// <summary>空输入的返回值（FNV-1a 的偏移基）。真实数据算出这个值，说明一个字节都没吃进去。</summary>
        private const ulong EmptyInputHash = 14695981039346656037UL;

        [Test]
        public void ComputeContentHash_WhenTheSameDataIsReadTwice_ReturnsTheSameHash()
        {
            ulong first = ConfigService.ComputeContentHash(ConfigServiceTests.ReadAllTableBytes());
            ulong second = ConfigService.ComputeContentHash(ConfigServiceTests.ReadAllTableBytes());

            Assert.That(second, Is.EqualTo(first), "同一份配置读两次必须得到同一个指纹，否则回放每次都会误报版本不匹配");
            Assert.That(first, Is.Not.EqualTo(EmptyInputHash), "指纹等于空输入的值，说明表字节根本没被吃进哈希");
            Assert.That(first, Is.Not.EqualTo(0UL), "指纹恒为 0 的实现也能让上面那条相等断言通过，这里堵掉");
        }

        [Test]
        public void ComputeContentHash_WhenOneByteChanges_ReturnsADifferentHash()
        {
            Dictionary<string, byte[]> baseline = ConfigServiceTests.ReadAllTableBytes();
            string target = FirstTableNameOrdinal(baseline);
            Assert.That(baseline[target], Is.Not.Empty, $"表 {target} 一个字节都没有，翻不动位，这条用例失去意义");

            Dictionary<string, byte[]> mutated = CopyOf(baseline);
            mutated[target][mutated[target].Length - 1] ^= 0x01;

            Assert.That(
                ConfigService.ComputeContentHash(mutated),
                Is.Not.EqualTo(ConfigService.ComputeContentHash(baseline)),
                "只改一个字节指纹就不变的话，改过的配置会被当成同一版，回放对不上却报不出真因");
        }

        [Test]
        public void ComputeContentHash_WhenTablesArriveInADifferentOrder_ReturnsTheSameHash()
        {
            // 字典的插入顺序对应的就是 LoadAllAsync 返回句柄的顺序——而按标签批量加载不保证这个顺序。
            Dictionary<string, byte[]> alphaFirst = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "tbalpha", new byte[] { 1, 2 } },
                { "tbbeta", new byte[] { 3 } },
            };

            Dictionary<string, byte[]> betaFirst = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "tbbeta", new byte[] { 3 } },
                { "tbalpha", new byte[] { 1, 2 } },
            };

            Assert.That(
                ConfigService.ComputeContentHash(betaFirst),
                Is.EqualTo(ConfigService.ComputeContentHash(alphaFirst)),
                "内容相同只是到达顺序不同，指纹必须一样；不然同一份配置在两台机器上会算出两个值");
        }

        [Test]
        public void ComputeContentHash_WhenFedTheFixedSample_MatchesTheGoldenValue()
        {
            Dictionary<string, byte[]> sample = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "tbbeta", new byte[] { 3 } },
                { "tbalpha", new byte[] { 1, 2 } },
            };

            Assert.That(ConfigService.ComputeContentHash(sample), Is.EqualTo(GoldenSampleHash),
                "指纹的规范流变了（算法、排序规则或字段顺序）——这是对外格式，确认是有意改动再更新 GoldenSampleHash");
        }

        [Test]
        public void ComputeContentHash_WhenTwoTablesSwapContents_ReturnsADifferentHash()
        {
            Dictionary<string, byte[]> original = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "tbalpha", new byte[] { 1 } },
                { "tbbeta", new byte[] { 2 } },
            };

            Dictionary<string, byte[]> swapped = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "tbalpha", new byte[] { 2 } },
                { "tbbeta", new byte[] { 1 } },
            };

            Assert.That(
                ConfigService.ComputeContentHash(swapped),
                Is.Not.EqualTo(ConfigService.ComputeContentHash(original)),
                "两张表内容互换是实打实的配置改动；只把内容拼起来算的实现会让它们撞成同一个指纹");
        }

        [Test]
        public void ComputeContentHash_WhenBytesMoveAcrossATableBoundary_ReturnsADifferentHash()
        {
            Dictionary<string, byte[]> original = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "tbalpha", new byte[] { 1, 2 } },
                { "tbbeta", new byte[] { 3 } },
            };

            Dictionary<string, byte[]> shifted = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "tbalpha", new byte[] { 1 } },
                { "tbbeta", new byte[] { 2, 3 } },
            };

            Assert.That(
                ConfigService.ComputeContentHash(shifted),
                Is.Not.EqualTo(ConfigService.ComputeContentHash(original)),
                "两份数据拼起来的字节流一模一样，只有表边界不同——没有长度前缀的实现会漏报这种改动");
        }

        [Test]
        public void ComputeContentHash_WhenATableIsRenamed_ReturnsADifferentHash()
        {
            Dictionary<string, byte[]> original = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "tbalpha", new byte[] { 1, 2 } },
            };

            Dictionary<string, byte[]> renamed = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { "tbgamma", new byte[] { 1, 2 } },
            };

            Assert.That(
                ConfigService.ComputeContentHash(renamed),
                Is.Not.EqualTo(ConfigService.ComputeContentHash(original)),
                "换了张表但内容碰巧一样也是配置改动，表名必须进指纹");
        }

        [Test]
        public void ComputeContentHash_WhenFedNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ConfigService.ComputeContentHash(null));
        }

        [Test]
        public void ContentHash_BeforeInitializeAsync_ThrowsWithTheStartupOrderHint()
        {
            ConfigService service = new ConfigService(new ThrowingAssetService());

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() => _ = service.ContentHash);

            // 这里要的不只是「抛了」：返回 0 之类的哨兵值会被悄悄写进回放文件头，
            // 等到放回放时才炸，那时离真因已经很远了。
            Assert.That(error.Message, Does.Contain("初始化"), "报错要说清是没初始化，不是指纹算错了");
            Assert.That(error.Message, Does.Contain("GameLifetimeScope"), "报错要给出修法：去调注册顺序");
        }

        /// <summary>按 Ordinal 升序取第一张表名，避免依赖字典的遍历顺序，用例每次跑改的都是同一张表。</summary>
        private static string FirstTableNameOrdinal(Dictionary<string, byte[]> tableBytes)
        {
            List<string> names = new List<string>(tableBytes.Keys);
            names.Sort(StringComparer.Ordinal);
            return names[0];
        }

        /// <summary>深拷一份，改动不会污染基准那份的字节数组。</summary>
        private static Dictionary<string, byte[]> CopyOf(Dictionary<string, byte[]> source)
        {
            Dictionary<string, byte[]> copy = new Dictionary<string, byte[]>(source.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, byte[]> pair in source)
            {
                copy[pair.Key] = (byte[])pair.Value.Clone();
            }

            return copy;
        }

        /// <summary>
        /// 只为了把 <see cref="ConfigService"/> new 出来的占位替身（它的构造函数不收 null），
        /// 所有成员一律抛 <see cref="NotSupportedException"/>——本文件的用例都不该真的走到加载。
        /// <para>
        /// 没有复用 UIServiceTests 里那个假资源服务：它是私有嵌套类够不着，
        /// 而为一个「只用来占位」的依赖把它提成共享类型，会把两份用例的需求绑死在一起。
        /// </para>
        /// </summary>
        private sealed class ThrowingAssetService : IAssetService
        {
            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default)
                where T : UnityEngine.Object => throw new NotSupportedException(nameof(LoadAsync));

            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default)
                where T : UnityEngine.Object => throw new NotSupportedException(nameof(LoadAllAsync));

            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default)
                => throw new NotSupportedException(nameof(InstantiateAsync));

            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException(nameof(ReleaseInstance));

            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default)
                => throw new NotSupportedException(nameof(LoadSceneAsync));
        }
    }
}

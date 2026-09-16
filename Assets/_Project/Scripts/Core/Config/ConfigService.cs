// 职责：启动时把 Addressables 里 config 标签下的全部表字节读进内存，构造出 cfg.Tables。
// 为什么新建：IConfigService 是契约，实现分开放；读表逻辑与资源服务职责不同（一个管句柄，一个管表），
// 塞进 AddressablesAssetService 说不通。

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Boot;
using Game.Core.Logging;
using Luban;
using UnityEngine;

namespace Game.Core.Config
{
    /// <summary>
    /// 配置表服务。数据来自 <c>Assets/_Project/Data/Config/*.bytes</c>——那个文件夹整体被打进
    /// Addressables 的 Config 组并打了 <see cref="ConfigLabel"/> 标签，所以加新表不用改这里的代码。
    /// </summary>
    public sealed class ConfigService : IConfigService, IGameService
    {
        /// <summary>Addressables 里给配置表数据打的标签。改这个值要同步改 Addressables 组设置。</summary>
        public const string ConfigLabel = "config";

        /// <summary>
        /// FNV-1a 64 位的两个标准常数。这里写死一份而不是复用 <c>RandomService</c> 里那份：
        /// 那份是它自己的私有实现细节（流名 → 种子），而指纹一旦进了回放文件头就是对外格式，
        /// 算法只能由本文件决定，不能因为别的模块重构自己的哈希而跟着变。
        /// </summary>
        private const ulong FnvOffsetBasis = 14695981039346656037UL;

        /// <summary>见 <see cref="FnvOffsetBasis"/>。</summary>
        private const ulong FnvPrime = 1099511628211UL;

        private readonly IAssetService assets;
        private global::cfg.Tables tables;
        private ulong contentHash;

        public ConfigService(IAssetService assets)
        {
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
        }

        public global::cfg.Tables Tables
        {
            get
            {
                EnsureInitialized();
                return tables;
            }
        }

        /// <inheritdoc />
        public ulong ContentHash
        {
            get
            {
                EnsureInitialized();
                return contentHash;
            }
        }

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            IReadOnlyList<AssetHandle<TextAsset>> handles =
                await assets.LoadAllAsync<TextAsset>(ConfigLabel, ct);

            try
            {
                if (handles.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Addressables 里标签 \"{ConfigLabel}\" 下一个资源都没有。"
                        + "确认 Assets/_Project/Data/Config 已加进 Addressables 的 Config 组并打了这个标签；"
                        + "数据文件本身跑 scripts/gen-tables.ps1 生成。");
                }

                Dictionary<string, byte[]> tableBytes = new Dictionary<string, byte[]>(handles.Count, StringComparer.Ordinal);
                for (int i = 0; i < handles.Count; i++)
                {
                    TextAsset asset = handles[i].Asset;
                    if (asset == null)
                    {
                        continue;
                    }

                    // TextAsset.name 就是不带扩展名的文件名，正好等于 Luban 生成代码里 loader(name) 要的那个 name。
                    // TextAsset.bytes 每次访问都返回新数组，所以下面 finally 里释放句柄不会影响已经拿到的字节。
                    tableBytes[asset.name] = asset.bytes;
                }

                tables = BuildTables(tableBytes);

                // 指纹按表名排好序再算，与上面 handles 的遍历顺序无关（理由见 ComputeContentHash）。
                contentHash = ComputeContentHash(tableBytes);
                Log.Info($"配置表加载完成，共 {tableBytes.Count} 张表，内容指纹 {contentHash:X16}");
            }
            finally
            {
                // 字节已经拷进内存，Addressables 那边的引用可以立刻还掉，不必占着资源常驻。
                for (int i = 0; i < handles.Count; i++)
                {
                    handles[i].Dispose();
                }
            }
        }

        /// <summary>
        /// 用一份「表名 → 字节」的字典构造 <c>cfg.Tables</c>。
        /// 单独抽出来是为了让 EditMode 测试能直接喂磁盘上的 .bytes，不必拉起 Addressables。
        /// </summary>
        /// <param name="tableBytes">键是不带扩展名的表文件名（如 <c>tbitem</c>）。</param>
        /// <exception cref="InvalidOperationException">生成代码要的某张表不在字典里。</exception>
        public static global::cfg.Tables BuildTables(IReadOnlyDictionary<string, byte[]> tableBytes)
        {
            if (tableBytes == null)
            {
                throw new ArgumentNullException(nameof(tableBytes));
            }

            return new global::cfg.Tables(name =>
            {
                if (!tableBytes.TryGetValue(name, out byte[] data))
                {
                    throw new InvalidOperationException(
                        $"配置表 \"{name}\" 的数据文件没找到（已有：{string.Join("、", tableBytes.Keys)}）。"
                        + "多半是生成代码更新了但数据没跟上——跑一次 scripts/gen-tables.ps1，"
                        + "再确认新出来的 .bytes 在 Addressables 的 Config 组里。");
                }

                return new ByteBuf(data);
            });
        }

        /// <summary>
        /// 给一份「表名 → 字节」算内容指纹（FNV-1a 64 位）。和 <see cref="BuildTables"/> 一样抽成静态，
        /// 是为了让 EditMode 测试能直接喂字典，不必拉起 Addressables。
        /// <para>
        /// <b>这里的排序不是多余的，删掉就会引入误报。</b> 字节的来源是
        /// <see cref="IAssetService.LoadAllAsync{T}"/> 的批量加载结果，**按标签批量加载不保证返回顺序**
        /// （顺序取决于 catalog 里的条目排布、打包分组、各自加载完成的先后）。
        /// 直接顺着返回顺序喂哈希的话，同一份配置在两次运行、两台机器上会算出两个不同的指纹，
        /// 于是回放报「配置版本不匹配」，而配置其实一个字节都没改——这个误报恰好出现在最需要相信
        /// 这套机制的时候，还会把人引到配置表上白查一遍。所以先按表名排成固定顺序再喂。
        /// </para>
        /// <para>
        /// 排序用 <see cref="StringComparer.Ordinal"/> 而不是默认的文化相关比较：后者的结果随系统区域设置变，
        /// 等于把「同一份数据在两台机器上算出两个指纹」这个坑换个地方再踩一遍。
        /// </para>
        /// <para>
        /// 喂进去的不只是内容，还有表名和每张表的字节数。只拼内容的话，「两张表内容互换」和
        /// 「一张表末尾少几个字节、下一张表开头多几个字节」这类真实改动会撞出同一个指纹，指纹就漏报了。
        /// </para>
        /// </summary>
        /// <param name="tableBytes">键是不带扩展名的表文件名（如 <c>tbitem</c>），与 <see cref="BuildTables"/> 同一份。</param>
        /// <returns>只由内容决定的 64 位指纹：同样的输入，在任何机器、任何一次运行都得到同一个值。</returns>
        public static ulong ComputeContentHash(IReadOnlyDictionary<string, byte[]> tableBytes)
        {
            if (tableBytes == null)
            {
                throw new ArgumentNullException(nameof(tableBytes));
            }

            List<string> names = new List<string>(tableBytes.Keys);
            names.Sort(StringComparer.Ordinal);

            unchecked
            {
                ulong hash = FnvOffsetBasis;
                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];

                    // 表名按 UTF-16 码元拆成高低两个字节吃进去，指纹就与「字符串在内存里怎么编码」无关。
                    for (int c = 0; c < name.Length; c++)
                    {
                        char ch = name[c];
                        hash = (hash ^ (byte)ch) * FnvPrime;
                        hash = (hash ^ (byte)(ch >> 8)) * FnvPrime;
                    }

                    byte[] data = tableBytes[name];
                    int length = data == null ? 0 : data.Length;

                    // 长度前缀（4 字节小端）划清每张表的边界，防止相邻两张表的字节互相挪位却撞出同一个指纹。
                    for (int b = 0; b < 4; b++)
                    {
                        hash = (hash ^ (byte)(length >> (8 * b))) * FnvPrime;
                    }

                    for (int b = 0; b < length; b++)
                    {
                        hash = (hash ^ data[b]) * FnvPrime;
                    }
                }

                return hash;
            }
        }

        /// <summary>
        /// 初始化完成前访问 <see cref="Tables"/> 或 <see cref="ContentHash"/> 都是用法错误，
        /// 报错直接点名启动顺序，别让调用方去猜。
        /// </summary>
        private void EnsureInitialized()
        {
            // cfg.Tables 是普通 C# 类，不是 UnityEngine.Object，没有伪空问题。
            if (tables == null)
            {
                throw new InvalidOperationException(
                    "配置表还没初始化完。ConfigService 在启动顺序里排在 Assets 之后，"
                    + "要在 InitializeAsync 里读表的服务请注册到它后面（见 GameLifetimeScope）。");
            }
        }
    }
}

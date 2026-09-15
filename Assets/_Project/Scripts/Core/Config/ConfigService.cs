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

        private readonly IAssetService assets;
        private global::cfg.Tables tables;

        public ConfigService(IAssetService assets)
        {
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
        }

        public global::cfg.Tables Tables
        {
            get
            {
                if (tables == null)
                {
                    throw new InvalidOperationException(
                        "配置表还没初始化完。ConfigService 在启动顺序里排在 Assets 之后，"
                        + "要在 InitializeAsync 里读表的服务请注册到它后面（见 GameLifetimeScope）。");
                }

                return tables;
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
                Log.Info($"配置表加载完成，共 {tableBytes.Count} 张表");
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
    }
}

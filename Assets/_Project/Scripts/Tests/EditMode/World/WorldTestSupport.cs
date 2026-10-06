// 职责：World 组测试的公共夹具——真表 / 手写假表的构造、配置服务桩、内存存档服务与空资源服务。
// 为什么新建：本轮几个测试类都要「用手写的最小表造出真表里没有的形状」（先例 WorldRulesTests.WriteSceneTable），
//   各抄一份迟早走偏；而且表字节的字段顺序只在一处维护，生成代码改顺序时只炸这一处。
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Config;
using Game.Core.Save;
using Game.Tests.EditMode.Core;
using Game.World;
using Luban;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.World
{
    /// <summary>World 组测试的公共夹具。</summary>
    internal static class WorldTestSupport
    {
        /// <summary>真表里的场景键（Tables/Data/world/scene/human_jingyang.json）。</summary>
        internal const string HumanScene = "human_jingyang";

        /// <summary>真表里的场景键（Tables/Data/world/scene/yao_fangshi.json）。</summary>
        internal const string YaoScene = "yao_fangshi";

        /// <summary>用当前这份真表建一个目录。</summary>
        internal static WorldCatalog RealCatalog() =>
            new WorldCatalog(new StubConfigService(ConfigService.BuildTables(ConfigServiceTests.ReadAllTableBytes())));

        /// <summary>
        /// 用手写的场景表建目录：区域表与传送点表**必须同时清空**——真表里那些行都引用真场景键，
        /// 换掉场景表却留着它们会先撞上「引用完整性」，那时的失败就不是用例要测的东西了。
        /// </summary>
        internal static WorldCatalog CatalogWithFakeScenes(params FakeScene[] scenes) =>
            new WorldCatalog(new StubConfigService(TablesWithFakeScenes(scenes)));

        /// <summary>
        /// 同上，但只给 <c>cfg.Tables</c>、不建目录。**表级非法**的形状（例如「未实装却写了地址」）没法经
        /// <see cref="CatalogWithFakeScenes"/> 造出来——<c>WorldCatalog</c> 读表那道门会先抛。
        /// 要直接问校验器「这张表报不报」时用这个入口。
        /// </summary>
        internal static global::cfg.Tables TablesWithFakeScenes(params FakeScene[] scenes)
        {
            Dictionary<string, byte[]> bytes = ConfigServiceTests.ReadAllTableBytes();
            bytes["world_tbscene"] = WriteSceneTable(scenes);
            bytes["world_tbregion"] = WriteEmptyTable();
            bytes["world_tbportal"] = WriteEmptyTable();
            return ConfigService.BuildTables(bytes);
        }

        /// <summary>
        /// 手写一张场景表（N 行）。字段顺序照 <c>cfg.world.Scene</c> 的构造函数：生成了新的列要跟着改
        /// （届时会在反序列化处先炸，不会静默误判）。**行数前缀必须自己写一次**——
        /// Luban 的 <c>WriteSize</c> 是变长编码，拼接单行表字节这种写法不成立。
        /// </summary>
        internal static byte[] WriteSceneTable(params FakeScene[] scenes)
        {
            var buf = new ByteBuf();
            buf.WriteSize(scenes.Length);
            for (int i = 0; i < scenes.Length; i++)
            {
                FakeScene scene = scenes[i];
                buf.WriteString(scene.SceneKey);
                buf.WriteString("假场景");
                buf.WriteInt((int)global::cfg.world.WorldId.Human);
                buf.WriteString("fake_world");
                buf.WriteBool(scene.Implemented);
                buf.WriteString(scene.SceneAddress);
                buf.WriteSize(0); // region_ids
                buf.WriteSize(scene.SpawnPoints.Length);
                for (int j = 0; j < scene.SpawnPoints.Length; j++)
                {
                    buf.WriteString(scene.SpawnPoints[j]);
                }

                buf.WriteString(scene.DefaultSpawnId);
                buf.WriteSize(0); // portal_ids
                buf.WriteSize(0); // region_names
                buf.WriteString("测试用假场景");
            }

            return buf.CopyData();
        }

        /// <summary>一行假场景：只保留本组测试用得到的列（其余列写空）。</summary>
        internal readonly struct FakeScene
        {
            public FakeScene(string sceneKey, bool implemented, string sceneAddress, string defaultSpawnId,
                params string[] spawnPoints)
            {
                SceneKey = sceneKey;
                Implemented = implemented;
                SceneAddress = sceneAddress ?? string.Empty;
                DefaultSpawnId = defaultSpawnId ?? string.Empty;
                SpawnPoints = spawnPoints ?? new string[0];
            }

            public string SceneKey { get; }

            public bool Implemented { get; }

            public string SceneAddress { get; }

            public string DefaultSpawnId { get; }

            public string[] SpawnPoints { get; }
        }

        /// <summary>一张零行的表（Luban 的行数前缀写成 0）。</summary>
        internal static byte[] WriteEmptyTable()
        {
            var buf = new ByteBuf();
            buf.WriteSize(0);
            return buf.CopyData();
        }

        /// <summary>只递一份现成的 <c>cfg.Tables</c>。</summary>
        internal sealed class StubConfigService : IConfigService
        {
            public StubConfigService(global::cfg.Tables tables) => Tables = tables;

            public global::cfg.Tables Tables { get; }

            public ulong ContentHash => throw new NotSupportedException("假配置服务不提供内容指纹");
        }

        /// <summary>内存版存档服务：只实现本组测试要用的 <c>Get</c> / 替换分区，其余按契约抛 NotSupported。</summary>
        internal sealed class InMemorySaveService : ISaveService
        {
            private readonly Dictionary<Type, ISaveData> partitions = new Dictionary<Type, ISaveData>();

            public T Get<T>() where T : class, ISaveData, new()
            {
                if (partitions.TryGetValue(typeof(T), out ISaveData existing))
                {
                    return (T)existing;
                }

                var created = new T();
                partitions[typeof(T)] = created;
                return created;
            }

            /// <summary>模拟读档时的整体替换（JsonSaveService.Commit / LoadAsync 的语义）。</summary>
            public void Replace<T>(T data) where T : class, ISaveData => partitions[typeof(T)] = data;

            public UniTask InitializeAsync(System.Threading.CancellationToken ct) => UniTask.CompletedTask;

            public bool Exists(int slot) => throw new NotSupportedException("内存版存档服务不落盘");

            public void Delete(int slot) => throw new NotSupportedException("内存版存档服务不落盘");

            public UniTask<bool> SaveAsync(int slot, System.Threading.CancellationToken ct = default) =>
                throw new NotSupportedException("内存版存档服务不落盘");

            public UniTask<SaveSnapshot> ReadCandidateAsync(int slot, System.Threading.CancellationToken ct = default) =>
                throw new NotSupportedException("内存版存档服务不落盘");

            public UniTask<bool> LoadAsync(int slot, System.Threading.CancellationToken ct = default) =>
                throw new NotSupportedException("内存版存档服务不落盘");

            public SaveSnapshot Capture() => throw new NotSupportedException("内存版存档服务不做整份快照");

            public void Commit(SaveSnapshot snapshot) => throw new NotSupportedException("内存版存档服务不落盘");

            public void ResetAll() => partitions.Clear();

            public UniTask<T> ReadProfileAsync<T>(string name, System.Threading.CancellationToken ct = default) where T : class, new() =>
                throw new NotSupportedException("内存版存档服务不读档案");

            public UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, System.Threading.CancellationToken ct = default) where T : class, new() =>
                throw new NotSupportedException("内存版存档服务不读档案");

            public UniTask WriteProfileAsync<T>(string name, T data, System.Threading.CancellationToken ct = default) where T : class =>
                throw new NotSupportedException("内存版存档服务不写档案");
        }

        /// <summary>用不到的资产服务：本组测试不进场景，任何加载都说明用例走错了路。</summary>
        internal sealed class UnusedAssets : IAssetService
        {
            public UniTask<AssetHandle<T>> LoadAsync<T>(string key, System.Threading.CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException("本组测试不加载资产");

            public UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, System.Threading.CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException("本组测试不加载资产");

            public UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, System.Threading.CancellationToken ct = default) =>
                throw new NotSupportedException("本组测试不实例化资产");

            public void ReleaseInstance(GameObject instance) => throw new NotSupportedException("本组测试不实例化资产");

            public UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, System.Threading.CancellationToken ct = default) =>
                throw new NotSupportedException("本组测试不进场景");
        }
    }
}

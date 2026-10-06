// 职责：钉住 NarrativeInstaller 里新加的那段构建回调——装上 NarrativeInstaller + IdentityInstaller 之后，
//   `NarrativeConditionSource` 真的拿到了身份四件套：借着一个身份时，条件源能读到 `identity.*` 事实键；
//   没有身份模块时这一层不生效（与接线前一致）。
// 为什么新建：`NarrativeConditionSourceIdentityTests` 是**手工调** `BindIdentity` 的；本波把它挪进了装配回调，
//   而「回调里参数传错、TryResolve 拿到的是另一个实例、回调根本没跑」这几种失效，手工测试一条都看不出来。
// 真容器（VContainer）只补两样：PlayerModel 与 ISaveService（后者用最小假实现，同那份文件的 FakeSaves）。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Narrative;
using Game.Player;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace Game.Tests.EditMode.Narrative
{
    public sealed class NarrativeInstallerIdentityTests
    {
        private const string Target = "sample_target";
        private const string StewardId = "duzhi";

        private GameObject host;
        private IObjectResolver container;
        private IdentityConfig identityConfig;

        [TearDown]
        public void TearDown()
        {
            container?.Dispose();
            container = null;
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            host = null;
            if (identityConfig != null)
            {
                UnityEngine.Object.DestroyImmediate(identityConfig);
            }

            identityConfig = null;
        }

        /// <summary>正向：容器装出来的条件源带着身份状态——借到身份之后 `identity.borrowed` 立刻可读。</summary>
        [Test]
        public void Install_WithIdentityInstaller_BindsIdentityIntoConditionSource()
        {
            Build(withIdentity: true);
            IdentityState state = container.Resolve<IdentityState>();
            IdentityEnterResult entered = container.Resolve<IdentityRules>()
                .TryEnter(state, container.Resolve<IdentityLedger>(), IdentityId.From(StewardId));
            Assert.That(entered, Is.EqualTo(IdentityEnterResult.Entered), "配置里那条身份要借得到，否则这条用例测不到东西");

            EncounterContext context = container.Resolve<NarrativeConditionSource>().Snapshot(Target);

            Assert.That(Read(context, IdentityFacts.Borrowed), Is.True, "回调没跑到的话这个键恒为假（条件永远不触发）");
            Assert.That(Read(context, IdentityFacts.Prefix + StewardId), Is.True);
        }

        /// <summary>负对照：没装 IdentityInstaller 时条件源照常建得出来，身份键一个都不写。</summary>
        [Test]
        public void Install_WithoutIdentityInstaller_WritesNoIdentityFact()
        {
            Build(withIdentity: false);

            EncounterContext context = container.Resolve<NarrativeConditionSource>().Snapshot(Target);

            Assert.That(Read(context, IdentityFacts.Borrowed), Is.False);
            Assert.That(context.PlayerAlive, Is.True, "玩家事实照常可读");
        }

        // ──────────────────────────── 夹具 ────────────────────────────

        /// <summary>故意先装 NarrativeInstaller：GameLifetimeScope 按组件顺序调注册器，用最坏顺序证明与次序无关。</summary>
        private void Build(bool withIdentity)
        {
            host = new GameObject("GameBootstrap(测试)");
            var narrative = host.AddComponent<NarrativeInstaller>();

            var builder = new ContainerBuilder();
            // 玩家事实要可读：PlayerModel 的默认生命是 0，先按存档恢复一份（同 NarrativeConditionSourceIdentityTests）。
            var player = new PlayerModel();
            player.Restore(new PlayerSaveData { Health = 3 });
            builder.RegisterInstance(player);
            builder.RegisterInstance<ISaveService>(new FakeSaves());
            builder.RegisterInstance(TelemetryOptions.Default);
            builder.RegisterInstance(new FakeTelemetryClock()).As<ITelemetryClock>();
            builder.RegisterInstance(new ITelemetrySink[] { new RecordingTelemetrySink() });
            builder.Register<TelemetryService>(Lifetime.Singleton).As<ITelemetryService>();

            narrative.Install(builder);
            if (withIdentity)
            {
                identityConfig = NewIdentityConfigWithOneDefinition();
                IdentityInstaller identity = host.AddComponent<IdentityInstaller>();
                AttachConfig(identity, identityConfig);
                identity.Install(builder);
            }

            container = builder.Build();
        }

        private static bool Read(EncounterContext context, string key) =>
            context.Read(EncounterContext.Fact.StoryFlag, key);

        /// <summary>造一份「有一个可借身份」的配置（真资产的 definitions 还是空的：身份内容待策划拍板）。</summary>
        private static IdentityConfig NewIdentityConfigWithOneDefinition()
        {
            var config = ScriptableObject.CreateInstance<IdentityConfig>();
            var serialized = new SerializedObject(config);
            SerializedProperty definitions = serialized.FindProperty("definitions");
            definitions.arraySize = 1;
            SerializedProperty entry = definitions.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("id").stringValue = StewardId;
            entry.FindPropertyRelative("displayName").stringValue = "都知";
            entry.FindPropertyRelative("sourceCharacterId").stringValue = "duzhi_npc";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private static void AttachConfig(GameplayInstaller installer, UnityEngine.Object config)
        {
            var serialized = new SerializedObject(installer);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>最小存档服务：只要能让 NarrativeSaveData 分区读写就够（本文件不落盘，同 NarrativeConditionSourceIdentityTests）。</summary>
        private sealed class FakeSaves : ISaveService
        {
            private readonly Dictionary<Type, ISaveData> partitions = new Dictionary<Type, ISaveData>();

            public T Get<T>() where T : class, ISaveData, new()
            {
                if (!partitions.TryGetValue(typeof(T), out ISaveData data))
                {
                    data = new T();
                    partitions[typeof(T)] = data;
                }

                return (T)data;
            }

            public UniTask<bool> SaveAsync(int slot, CancellationToken ct = default) => UniTask.FromResult(false);
            public UniTask<bool> LoadAsync(int slot, CancellationToken ct = default) => UniTask.FromResult(false);
            public UniTask<SaveSnapshot> ReadCandidateAsync(int slot, CancellationToken ct = default) => UniTask.FromResult<SaveSnapshot>(null);
            public SaveSnapshot Capture() => null;
            public void Commit(SaveSnapshot snapshot) { }
            public void ResetAll() => partitions.Clear();
            public UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new() => UniTask.FromResult<T>(null);
            public UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default) where T : class, new() => UniTask.FromResult<T>(null);
            public UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class => UniTask.CompletedTask;
            public bool Exists(int slot) => false;
            public void Delete(int slot) { }
        }
    }
}

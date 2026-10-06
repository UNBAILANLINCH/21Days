// 职责：钉住「身份 → 剧情事实」的接线（S1/S2）——投影合并、未接线时零影响、档位键互斥、瞬时事实源并入。
// 为什么新建：`IdentityFactSnapshotTests` 只测内核自己的投影；本文件测的是**条件求值路径上的那一次合并**
//   （NarrativeConditionSource.Snapshot），也就是「键名拼错静默失效」这条链路上唯一会被真正读到的地方。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Save;
using Game.Identity;
using Game.Narrative;
using Game.Player;
using NUnit.Framework;

namespace Game.Tests.EditMode.Narrative
{
    public sealed class NarrativeConditionSourceIdentityTests
    {
        private const string Target = "sample_target";
        private const string StewardPelt = "zhishi_pi";

        private FakeSaves saves;
        private PlayerModel player;
        private NarrativeConditionSource source;
        private IdentitySettings settings;
        private IdentityState state;
        private IdentityLedger ledger;
        private SuspicionState suspicion;

        [SetUp]
        public void SetUp()
        {
            saves = new FakeSaves();
            player = new PlayerModel();
            player.Restore(new PlayerSaveData { Health = 3 });
            source = new NarrativeConditionSource(player, saves);
            settings = new IdentitySettings();
            state = new IdentityState();
            ledger = new IdentityLedger();
            suspicion = SuspicionState.FromSettings(settings);
        }

        /// <summary>接线后的正向：借着一个「皮」身份时，投影出来的键全部可读。</summary>
        [Test]
        public void Snapshot_WhileBorrowingPelt_ExposesIdentityFacts()
        {
            BindIdentity();
            Enter(StewardPelt);

            EncounterContext context = source.Snapshot(Target);

            Assert.That(Read(context, IdentityFacts.Borrowed), Is.True);
            Assert.That(Read(context, IdentityFacts.Prefix + StewardPelt), Is.True);
            Assert.That(Read(context, IdentityFacts.Skin), Is.True);
            Assert.That(Read(context, IdentityFacts.Memory), Is.True);
            Assert.That(Read(context, IdentityFacts.Mask), Is.False, "皮不是面具");
            Assert.That(Read(context, IdentityFacts.Exposed), Is.False);
        }

        /// <summary>负对照：不调 BindIdentity 时身份键一个都不写——条件行为与接线前一致。</summary>
        [Test]
        public void Snapshot_WithoutIdentityBinding_WritesNoIdentityFact()
        {
            Enter(StewardPelt);

            EncounterContext context = source.Snapshot(Target);

            Assert.That(Read(context, IdentityFacts.Borrowed), Is.False);
            Assert.That(Read(context, IdentityFacts.Prefix + StewardPelt), Is.False);
            Assert.That(Read(context, IdentityFacts.Skin), Is.False);
            Assert.That(context.PlayerAlive, Is.True, "玩家事实不受影响");
        }

        /// <summary>退出身份后 `identity.borrowed` 与 `identity.&lt;id&gt;` 消失，记忆（跨阶段不清）还在。</summary>
        [Test]
        public void Snapshot_AfterExit_DropsBorrowedKeysKeepsMemory()
        {
            BindIdentity();
            Enter(StewardPelt);
            new IdentityRules(settings, Catalog()).TryExit(state);

            EncounterContext context = source.Snapshot(Target);

            Assert.That(Read(context, IdentityFacts.Borrowed), Is.False);
            Assert.That(Read(context, IdentityFacts.Prefix + StewardPelt), Is.False);
            Assert.That(Read(context, IdentityFacts.Memory), Is.True);
        }

        /// <summary>
        /// 档位键互斥（字典 §4.1）：存档里遗留的低档键必须被清掉，同一时刻只有当前档为真。
        /// 这条同时是「清档位键」那一步的负对照——不清理的话低档会与高档同时为真。
        /// </summary>
        [Test]
        public void Snapshot_WithStaleTierFlagInSave_KeepsExactlyOneTierTrue()
        {
            BindIdentity();
            saves.Get<NarrativeSaveData>().StoryFlags.Add(IdentityFacts.Suspicion(FactTier.Low));
            suspicion.Add(settings.SuspicionHighThreshold);

            EncounterContext context = source.Snapshot(Target);

            Assert.That(Read(context, IdentityFacts.Suspicion(FactTier.High)), Is.True);
            Assert.That(Read(context, IdentityFacts.Suspicion(FactTier.Mid)), Is.False);
            Assert.That(Read(context, IdentityFacts.Suspicion(FactTier.Low)), Is.False, "存档里的旧档位必须被清掉");
        }

        /// <summary>存档里的普通事实键不受档位清理影响（只清那六个身份档位键）。</summary>
        [Test]
        public void Snapshot_KeepsOrdinaryStoryFlags()
        {
            BindIdentity();
            saves.Get<NarrativeSaveData>().StoryFlags.Add("stage.p1.passed");

            EncounterContext context = source.Snapshot(Target);

            Assert.That(Read(context, "stage.p1.passed"), Is.True);
        }

        /// <summary>瞬时事实源（S3/S4）：接上之后 `stealth.*` 能在条件求值路径上读到。</summary>
        [Test]
        public void Snapshot_WithEncounterFacts_MergesTransientKeys()
        {
            var facts = new FakeEncounterFacts("stealth.hidden", "chase.active");
            source.BindEncounterFacts(facts);

            EncounterContext context = source.Snapshot(Target);

            Assert.That(Read(context, "stealth.hidden"), Is.True);
            Assert.That(Read(context, "chase.active"), Is.True);
            Assert.That(Read(context, "stealth.behind"), Is.False);
        }

        /// <summary>负对照：不接瞬时事实源时这些键恒为假。</summary>
        [Test]
        public void Snapshot_WithoutEncounterFacts_TransientKeysAreFalse()
        {
            EncounterContext context = source.Snapshot(Target);

            Assert.That(Read(context, "stealth.hidden"), Is.False);
            Assert.That(Read(context, "chase.active"), Is.False);
        }

        /// <summary>Snapshot 会被反复调用（遭遇仲裁 + 每次自动推进），所以必须复用缓冲、不每次分配新集合。</summary>
        [Test]
        public void Snapshot_CalledRepeatedly_ProducesIndependentResults()
        {
            BindIdentity();
            EncounterContext before = source.Snapshot(Target);
            Enter(StewardPelt);
            EncounterContext after = source.Snapshot(Target);

            Assert.That(Read(before, IdentityFacts.Borrowed), Is.False, "先前的快照不受后续调用影响");
            Assert.That(Read(after, IdentityFacts.Borrowed), Is.True);
        }

        private static bool Read(EncounterContext context, string key) =>
            context.Read(EncounterContext.Fact.StoryFlag, key);

        private void BindIdentity() => source.BindIdentity(state, ledger, suspicion, settings);

        private void Enter(string identityId)
        {
            IdentityEnterResult result = new IdentityRules(settings, Catalog()).TryEnter(state, ledger, IdentityId.From(identityId));
            Assert.That(result, Is.EqualTo(IdentityEnterResult.Entered));
        }

        private static IdentityCatalog Catalog() => IdentityCatalog.From(new[]
        {
            IdentityDefinition.Create(StewardPelt, "执事皮", IdentityOrigin.Pelt, "zhishi_npc"),
            IdentityDefinition.Create("duzhi", "都知", IdentityOrigin.Possession, "duzhi_npc"),
        });

        /// <summary>测试用的瞬时事实源：固定几个键为真，其余为假。</summary>
        private sealed class FakeEncounterFacts : IEncounterFacts
        {
            private readonly List<string> trueKeys = new List<string>();

            public FakeEncounterFacts(params string[] keys) => trueKeys.AddRange(keys);

            public bool IsTrue(string key) => trueKeys.Contains(key);

            public void CollectFacts(List<string> into) => into.AddRange(trueKeys);
        }

        /// <summary>最小存档服务：只要能让 NarrativeSaveData 分区读写就够（本文件不落盘）。</summary>
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

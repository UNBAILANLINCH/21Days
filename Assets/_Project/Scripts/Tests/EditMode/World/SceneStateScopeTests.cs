// 职责：钉住跨场景状态暂存——按场景键分区读写已开箱 / 已死怪，两份地图互不串味，
//   幂等、往返、以及「分区被整体换掉（读档）之后能重新灌回来」。
// 为什么新建：这是 roadmap A4「跨场景任务点与 NPC 状态」里唯一新增的存档分区与其适配层，
//   而它最容易出的错是「两份地图的同一个键互相顶掉」，所以测试重点在场景作用域与往返。
using System;
using System.Collections.Generic;
using Game.Core.Save;
using Game.World;
using NUnit.Framework;

namespace Game.Tests.EditMode.World
{
    /// <summary><see cref="SceneStateScope"/> 与 <see cref="WorldSaveData"/> 的 EditMode 测试。</summary>
    public sealed class SceneStateScopeTests
    {
        private const string HumanScene = "human_jingyang";
        private const string YaoScene = "yao_fangshi";

        private InMemorySaveService saves;
        private SceneStateScope human;
        private SceneStateScope yao;

        [SetUp]
        public void SetUp()
        {
            saves = new InMemorySaveService();
            human = new SceneStateScope(saves, HumanScene);
            yao = new SceneStateScope(saves, YaoScene);
        }

        // ---------------------------------------------------------------- 基础往返

        [Test]
        public void Crate_RoundTrip_RemembersAcrossScopes()
        {
            Assert.That(human.IsCrateOpened("crate_inn_01"), Is.False, "还没开过");
            Assert.That(human.MarkCrateOpened("crate_inn_01"), Is.True, "第一次开，记下来");

            // 换一个视图读同一份存档（模拟「离开这张图又回来」）。
            var again = new SceneStateScope(saves, HumanScene);
            Assert.That(again.IsCrateOpened("crate_inn_01"), Is.True, "换回这张图要记得箱子开过");
            Assert.That(again.IsCrateOpened("crate_inn_02"), Is.False, "别的箱子不受影响");
        }

        [Test]
        public void Crate_MarkIsIdempotent()
        {
            Assert.That(human.MarkCrateOpened("crate_inn_01"), Is.True);
            Assert.That(human.MarkCrateOpened("crate_inn_01"), Is.False, "第二次开同一箱子不该再记一次（也不该再弹奖励）");
            Assert.That(saves.Get<WorldSaveData>().CollectedCrates.Count, Is.EqualTo(1));
        }

        [Test]
        public void Monster_RoundTrip_RemembersDeath()
        {
            Assert.That(human.IsMonsterDefeated("1"), Is.False);
            Assert.That(human.MarkMonsterDefeated("1"), Is.True);
            Assert.That(human.IsMonsterDefeated("1"), Is.True);
            Assert.That(human.MarkMonsterDefeated("1"), Is.False, "同一只怪只记一次");

            var again = new SceneStateScope(saves, HumanScene);
            Assert.That(again.IsMonsterDefeated("1"), Is.True);
            Assert.That(again.IsMonsterDefeated("2"), Is.False);
        }

        // ---------------------------------------------------------------- 场景作用域（负对照）

        [Test]
        public void SameIdInTwoScenes_DoesNotCollide()
        {
            // 两张同构地图会有同名实体（同一个箱子键、同一只怪的 id）；
            // 一个场景里的记录不能把另一个场景的判断顶成 true。
            human.MarkCrateOpened("crate_inn_01");
            human.MarkMonsterDefeated("1");

            Assert.That(yao.IsCrateOpened("crate_inn_01"), Is.False, "妖界那个同名箱子还没开过");
            Assert.That(yao.IsMonsterDefeated("1"), Is.False, "妖界那只同 id 的怪还没死");

            yao.MarkCrateOpened("crate_inn_01");
            yao.MarkMonsterDefeated("1");

            // 两边各记一份，谁的记录都不覆盖谁。
            List<string> stored = saves.Get<WorldSaveData>().CollectedCrates;
            Assert.That(stored, Is.EquivalentTo(new[]
            {
                SceneStateKey.Scoped(HumanScene, "crate_inn_01"),
                SceneStateKey.Scoped(YaoScene, "crate_inn_01"),
            }));
        }

        [Test]
        public void StoredKeys_AreScopedToTheirScene()
        {
            human.MarkCrateOpened("crate_inn_01");
            human.MarkMonsterDefeated("1");

            Assert.That(saves.Get<WorldSaveData>().CollectedCrates,
                Is.EqualTo(new[] { "human_jingyang::crate_inn_01" }), "键就是「场景键::实体标识」");
            Assert.That(saves.Get<WorldSaveData>().DefeatedMonsters, Does.Contain("human_jingyang::1"));
        }

        [Test]
        public void Partition_MissingSets_DoesNotThrow()
        {
            // 负对照：老存档里这个分区没有这两个集合（JSON 里是 null），读的时候不能炸。
            saves.Get<WorldSaveData>().CollectedCrates = null;
            saves.Get<WorldSaveData>().DefeatedMonsters = null;

            var scope = new SceneStateScope(saves, HumanScene);
            Assert.That(scope.IsCrateOpened("crate_inn_01"), Is.False);
            Assert.That(scope.IsMonsterDefeated("1"), Is.False);

            // 写的时候要能补上被清空的集合（不能因为分区里是 null 就抛空引用）。
            Assert.That(() => scope.MarkCrateOpened("crate_inn_01"), Throws.Nothing);
            Assert.That(() => scope.MarkMonsterDefeated("1"), Throws.Nothing);
            Assert.That(scope.IsCrateOpened("crate_inn_01"), Is.True);
            Assert.That(scope.IsMonsterDefeated("1"), Is.True);
        }

        // ---------------------------------------------------------------- 读档之后重新灌

        [Test]
        public void RestoreFromPartition_PicksUpRecordsWrittenByAnotherScope()
        {
            // 模拟读档：分区实例被整体换掉（JsonSaveService.Commit 就是这么做的），
            // 旧的对象还拿着旧分区；调用方要 RestoreFromPartition 重新灌一遍。
            human.MarkCrateOpened("crate_inn_01");

            var replacement = new WorldSaveData();
            replacement.CollectedCrates.Add(SceneStateKey.Scoped(HumanScene, "crate_altar_01"));
            replacement.DefeatedMonsters.Add(SceneStateKey.Scoped(HumanScene, "7"));
            saves.Replace<WorldSaveData>(replacement);

            // 进程内缓存**不会被自动刷新**（读档后没调 RestoreFromPartition 就是这种状态）：
            // 它只加速、不做准；准的是分区。所以旧缓存不改变任何查询结果。
            Assert.That(human.RecordedCrates, Is.EqualTo(new[] { "crate_inn_01" }), "缓存还是换分区之前那一份");
            Assert.That(human.IsCrateOpened("crate_altar_01"), Is.True, "分区是真相：新分区里的记录立刻查得到");
            Assert.That(human.IsCrateOpened("crate_inn_01"), Is.True,
                "旧缓存里还有它——这是「缓存不做准」的直接体现；调 RestoreFromPartition 之后才消失");
            Assert.That(human.MarkCrateOpened("crate_inn_01"), Is.False, "幂等：缓存里记过就不再写分区");

            human.RestoreFromPartition();

            Assert.That(human.IsCrateOpened("crate_altar_01"), Is.True);
            Assert.That(human.IsCrateOpened("crate_inn_01"), Is.False, "重新灌过之后旧记录不在缓存里了");
            Assert.That(human.IsMonsterDefeated("7"), Is.True);
            Assert.That(human.RecordedCrates, Does.Contain("crate_altar_01"));
            Assert.That(human.RecordedCrates, Does.Not.Contain("crate_inn_01"), "缓存被重新灌过，只留分区里那一份");
        }

        [Test]
        public void RestoreFromPartition_IgnoresOtherScenesKeys()
        {
            // 负对照：分区里混着别的场景的键，本场景的视图不能把它们当成自己的。
            saves.Get<WorldSaveData>().CollectedCrates.Add(SceneStateKey.Scoped(YaoScene, "crate_inn_01"));

            human.RestoreFromPartition();

            Assert.That(human.IsCrateOpened("crate_inn_01"), Is.False);
            Assert.That(human.RecordedCrates, Is.Empty);
        }

        // ---------------------------------------------------------------- 参数校验

        [Test]
        public void Constructor_EmptySceneKey_Throws()
        {
            // 空场景键会让两份地图共用一个作用域（键退化成「::实体」），必须当场拦掉。
            Assert.That(() => new SceneStateScope(saves, string.Empty), Throws.TypeOf<ArgumentException>());
            Assert.That(() => new SceneStateScope(saves, null), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Constructor_NullSaveService_Throws()
        {
            Assert.That(() => new SceneStateScope(null, HumanScene), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void EntryId_Empty_Throws()
        {
            Assert.That(() => human.IsCrateOpened(string.Empty), Throws.TypeOf<ArgumentException>());
            Assert.That(() => human.MarkMonsterDefeated(null), Throws.TypeOf<ArgumentException>());
        }

        // ---------------------------------------------------------------- 键拼法

        [Test]
        public void SceneStateKey_Scoped_JoinsWithSeparator()
        {
            Assert.That(SceneStateKey.Scoped("human_jingyang", "crate_1"), Is.EqualTo("human_jingyang::crate_1"));
            Assert.That(SceneStateKey.PrefixOf("human_jingyang"), Is.EqualTo("human_jingyang::"));
        }

        [Test]
        public void SceneStateKey_RejectsEmptyPartsAndNestedSeparator()
        {
            // 负对照：任何一半为空、或者实体标识里再带一次分隔符，都会拼出两义键。
            Assert.That(() => SceneStateKey.Scoped(string.Empty, "crate_1"), Throws.TypeOf<ArgumentException>());
            Assert.That(() => SceneStateKey.Scoped("human_jingyang", string.Empty), Throws.TypeOf<ArgumentException>());
            Assert.That(() => SceneStateKey.Scoped("human_jingyang", "a::b"), Throws.TypeOf<ArgumentException>());
            Assert.That(() => SceneStateKey.PrefixOf(null), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void SceneStateKey_PrefixOf_DoesNotMatchASiblingScene()
        {
            // 负对照：前缀筛选不能用「开头的字符串」——'human_jingyang_x' 不是 'human_jingyang' 的场景。
            saves.Get<WorldSaveData>().CollectedCrates.Add(SceneStateKey.Scoped("human_jingyang_x", "crate_1"));
            human.RestoreFromPartition();

            Assert.That(human.IsCrateOpened("crate_1"), Is.False);
        }

        // ---------------------------------------------------------------- 分区形态

        [Test]
        public void WorldSaveData_IsVersionOne_AndMigrationIsANoOp()
        {
            // 分区版本从 1 起（Core/Save/ISaveData.cs:18）；第 1 版没有历史版本要迁。
            var data = new WorldSaveData();
            Assert.That(data.Version, Is.EqualTo(1));
            Assert.That(() => data.Migrate(1), Throws.Nothing);

            // 默认分区必须是「空但可用」的，不能是 null 集合。
            Assert.That(data.CollectedCrates, Is.Not.Null.And.Empty);
            Assert.That(data.DefeatedMonsters, Is.Not.Null.And.Empty);
        }

        /// <summary>内存版存档服务：只实现本测试要用的 <c>Get</c> / 替换分区，其余按契约抛 NotSupported。</summary>
        private sealed class InMemorySaveService : ISaveService
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

            public Cysharp.Threading.Tasks.UniTask InitializeAsync(System.Threading.CancellationToken ct) =>
                Cysharp.Threading.Tasks.UniTask.CompletedTask;

            public bool Exists(int slot) => throw new NotSupportedException("内存版存档服务不落盘");

            public void Delete(int slot) => throw new NotSupportedException("内存版存档服务不落盘");

            public Cysharp.Threading.Tasks.UniTask<bool> SaveAsync(int slot, System.Threading.CancellationToken ct = default) =>
                throw new NotSupportedException("内存版存档服务不落盘");

            public Cysharp.Threading.Tasks.UniTask<SaveSnapshot> ReadCandidateAsync(int slot, System.Threading.CancellationToken ct = default) =>
                throw new NotSupportedException("内存版存档服务不落盘");

            public Cysharp.Threading.Tasks.UniTask<bool> LoadAsync(int slot, System.Threading.CancellationToken ct = default) =>
                throw new NotSupportedException("内存版存档服务不落盘");

            // SaveSnapshot 的构造函数是 internal（Core/Save/SaveSnapshot.cs:11），Game.Runtime 没对测试程序集开
            // InternalsVisibleTo，测试里造不出来；本测试也用不到它（SceneStateScope 只经 ISaveService.Get<T>() 读分区）。
            public SaveSnapshot Capture() => throw new NotSupportedException("内存版存档服务不做整份快照");

            public void Commit(SaveSnapshot snapshot) => throw new NotSupportedException("内存版存档服务不落盘");

            public void ResetAll() => partitions.Clear();

            public Cysharp.Threading.Tasks.UniTask<T> ReadProfileAsync<T>(string name, System.Threading.CancellationToken ct = default) where T : class, new() =>
                throw new NotSupportedException("内存版存档服务不读档案");

            public Cysharp.Threading.Tasks.UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, System.Threading.CancellationToken ct = default) where T : class, new() =>
                throw new NotSupportedException("内存版存档服务不读档案");

            public Cysharp.Threading.Tasks.UniTask WriteProfileAsync<T>(string name, T data, System.Threading.CancellationToken ct = default) where T : class =>
                throw new NotSupportedException("内存版存档服务不写档案");
        }
    }
}

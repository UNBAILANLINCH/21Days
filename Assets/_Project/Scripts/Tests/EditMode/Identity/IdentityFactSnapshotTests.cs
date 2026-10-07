// 职责：钉住字典对齐——投影出来的每个键都必须是 `ai-docs/docs/story-facts.md` §4.1 登记过的键，
//   档位键同一时刻只有一个为真，退出身份后 identity.borrowed 与 identity.<id> 必须消失。
// 为什么新建：字典 §1 第 2 条记着现状——键名拼错完全静默，条件永远为假、表现为「这段剧情就是不触发」；
//   这份测试就是那个静默失效的守门人。

using System.Collections.Generic;
using Game.Identity;
using NUnit.Framework;
using static Game.Tests.EditMode.Identity.IdentityTestContent;

namespace Game.Tests.EditMode.Identity
{
    public sealed class IdentityFactSnapshotTests
    {
        /// <summary>
        /// 字典 §4.1 里「写入方 = Identity」的全部固定键（一字不差照抄）。
        /// 改字典与改这里必须是同一次改动——本文件是字典在代码里的对照物。
        /// </summary>
        private static readonly string[] RegisteredKeys =
        {
            "identity.borrowed",
            "identity.skin",
            "identity.mask",
            "identity.memory",
            "identity.exposed",
            "identity.exposedCount.low",
            "identity.exposedCount.mid",
            "identity.exposedCount.high",
            "identity.suspicion.low",
            "identity.suspicion.mid",
            "identity.suspicion.high",
            "identity.ledger.over",
            "identity.dead",
        };

        private IdentitySettings settings;
        private IdentityRules rules;
        private IdentityState state;
        private IdentityLedger ledger;
        private SuspicionState suspicion;

        [SetUp]
        public void SetUp()
        {
            settings = NewSettings();
            rules = new IdentityRules(settings, NewCatalog());
            state = new IdentityState();
            ledger = new IdentityLedger();
            suspicion = SuspicionState.FromSettings(settings);
        }

        [Test]
        public void CollectTrueKeys_WhileBorrowingPelt_WritesOnlyRegisteredKeys()
        {
            rules.TryEnter(state, ledger, StewardPeltId);

            List<string> keys = Collect();

            Assert.That(keys, Contains.Item("identity.borrowed"));
            Assert.That(keys, Contains.Item("identity." + StewardPelt));
            Assert.That(keys, Contains.Item("identity.skin"));
            Assert.That(keys, Contains.Item("identity.memory"));
            Assert.That(keys, Does.Not.Contain("identity.mask"), "皮不是面具");
            Assert.That(keys, Does.Not.Contain("identity.exposed"));
            foreach (string key in keys)
            {
                Assert.That(IsRegisteredOrIdentityKey(key), Is.True, $"未登记的键：{key}");
            }
        }

        [Test]
        public void CollectTrueKeys_WhileBorrowingMask_WritesMaskKeyNotSkinKey()
        {
            rules.TryEnter(state, ledger, InspectorMaskId);

            List<string> keys = Collect();

            Assert.That(keys, Contains.Item("identity.mask"));
            Assert.That(keys, Does.Not.Contain("identity.skin"));
        }

        [Test]
        public void CollectTrueKeys_AfterExit_DropsBorrowedAndIdentityKeys()
        {
            rules.TryEnter(state, ledger, MusicianId);
            Assert.That(Collect(), Contains.Item("identity." + Musician));

            rules.TryExit(state);

            List<string> keys = Collect();
            Assert.That(keys, Does.Not.Contain("identity.borrowed"));
            Assert.That(keys, Does.Not.Contain("identity." + Musician));
            Assert.That(keys, Contains.Item("identity.memory"), "记忆跨阶段不清（字典 §6 第 3 条）");
        }

        /// <summary>档位键互斥：升到哪一档就只有那一档为真，退出该档后三档都不为真。</summary>
        [Test]
        public void IsTrue_TierKeys_ExactlyOneTierTrueAtOnce()
        {
            var snapshot = new IdentityFactSnapshot(state, ledger, suspicion, settings);

            Assert.That(snapshot.IsTrue("identity.suspicion.low"), Is.False);
            Assert.That(snapshot.IsTrue("identity.suspicion.mid"), Is.False);
            Assert.That(snapshot.IsTrue("identity.suspicion.high"), Is.False);

            suspicion.Add(settings.SuspicionHighThreshold);
            snapshot = new IdentityFactSnapshot(state, ledger, suspicion, settings);

            Assert.That(snapshot.IsTrue("identity.suspicion.high"), Is.True);
            Assert.That(snapshot.IsTrue("identity.suspicion.mid"), Is.False, "高档为真时中档必须为假");
            Assert.That(snapshot.IsTrue("identity.suspicion.low"), Is.False, "高档为真时低档必须为假");

            List<string> keys = Collect();
            int tierKeyCount = 0;
            foreach (string key in keys)
            {
                if (key.StartsWith("identity.suspicion.", System.StringComparison.Ordinal))
                {
                    tierKeyCount++;
                }
            }

            Assert.That(tierKeyCount, Is.EqualTo(1));
        }

        [Test]
        public void IsTrue_LedgerOverAndDead_ReflectTheirOwners()
        {
            settings.LedgerMode = LedgerCountingMode.DistinctIdentities;
            settings.LedgerLimit = 1;
            ledger.Record(IdentityId.From(Musician));
            ledger.Record(IdentityId.From(Steward));

            var snapshot = new IdentityFactSnapshot(state, ledger, suspicion, settings);
            Assert.That(snapshot.IsTrue("identity.ledger.over"), Is.True);
            Assert.That(snapshot.IsTrue("identity.dead"), Is.False);
        }

        [Test]
        public void IsTrue_ForeignOrUnknownKey_IsFalse()
        {
            rules.TryEnter(state, ledger, MusicianId);
            var snapshot = new IdentityFactSnapshot(state, ledger, suspicion, settings);

            Assert.That(snapshot.IsTrue("identity.suspision.high"), Is.False, "拼错的档位键不能命中");
            Assert.That(snapshot.IsTrue("identity.not_an_identity"), Is.False);
            Assert.That(snapshot.IsTrue("stealth.hidden"), Is.False, "别的命名空间不归本模块管");
            Assert.That(snapshot.IsTrue(string.Empty), Is.False);
            Assert.That(snapshot.IsTrue(null), Is.False);
        }

        /// <summary>身份 id 与固定键撞名会写出同一个键，注册处直接拒收（字典 §3.2 唯一性）。</summary>
        [Test]
        public void Catalog_RejectsReservedIdentityId()
        {
            var catalog = new IdentityCatalog();

            Assert.Throws<System.ArgumentException>(() => catalog.Register(IdentityDefinition.Create("borrowed", "撞名身份")));
            Assert.Throws<System.ArgumentException>(() => catalog.Register(IdentityDefinition.Create("suspicion", "撞名身份")));
            Assert.DoesNotThrow(() => catalog.Register(IdentityDefinition.Create(Musician, "乐正")));
        }

        [Test]
        public void CollectTierKeys_ReturnsSixKeys_ForClearingBeforeWrite()
        {
            var buffer = new List<string>();

            IdentityFacts.CollectTierKeys(buffer);

            Assert.That(buffer.Count, Is.EqualTo(6));
            Assert.That(buffer, Contains.Item("identity.exposedCount.high"));
            Assert.That(buffer, Contains.Item("identity.suspicion.high"));
            foreach (string key in buffer)
            {
                Assert.That(IsRegisteredOrIdentityKey(key), Is.True, $"未登记的档位键：{key}");
            }
        }

        private List<string> Collect()
        {
            var keys = new List<string>();
            new IdentityFactSnapshot(state, ledger, suspicion, settings).CollectTrueKeys(keys);
            return keys;
        }

        private static bool IsRegisteredOrIdentityKey(string key)
        {
            for (int i = 0; i < RegisteredKeys.Length; i++)
            {
                if (RegisteredKeys[i] == key)
                {
                    return true;
                }
            }

            // 形态 2：identity.<id>（一身份一键）。id 段里不允许再有点号。
            if (!key.StartsWith(IdentityFacts.Prefix, System.StringComparison.Ordinal))
            {
                return false;
            }

            string id = key.Substring(IdentityFacts.Prefix.Length);
            return id.Length > 0 && id.IndexOf('.') < 0 && !IdentityFacts.IsReservedName(id);
        }
    }
}

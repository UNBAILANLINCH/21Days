// 职责：从当前玩家与槽位分区取事实；目标由场景显式登记，缺失目标按不可用处理。
// S 组接线（2026-10-07）——把已验收的三个内核接进条件求值路径，三处都不动 Fact 枚举（字典 §3.1「A 类不要扩」）：
//   1. 身份 → 事实（S1/S2）：IdentityFactSnapshot 的投影并进 EncounterContext，走现成的 WithStoryFlags；
//   2. 瞬时态事实源（S3/S4）：stealth.* / chase.* 由遭遇结算写进内存事实集，本类只读合并；
//   3. 档位键卫生（字典 §4.1）：identity.suspicion.* / identity.exposedCount.* 六个档位键先清后写，
//      保证快照里「同一时刻只有一个档位为真」——不清的话，存档里遗留的旧档位会与新档位同时为真。
// 落盘纪律：以上三类派生键**都不写进 NarrativeSaveData.StoryFlags**。两种身份状态各有真源
//   （IdentityState/账簿/怀疑度在 IdentitySaveData 分区里），投影键每次查询重算；写进存档就是
//   「瞬时态入档」（字典 §6.2 明令禁止）——读档会把玩家恢复成「正被击倒 / 正借着一个早就失效的身份」。
// 内存与分配：Snapshot 在条件求值路径上被反复调用，因此三个缓冲全部复用，稳态零分配。
using System;
using System.Collections.Generic;
using Game.Core.Save;
using Game.Dialogue;
using Game.Identity;
using Game.Player;

namespace Game.Narrative
{
    /// <summary>
    /// 一次遭遇的瞬时事实源（`stealth.*` / `chase.*`）。只活在内存里，不落盘（`ai-docs/docs/story-facts.md` §6.2）。
    /// <para>
    /// 实现方是遭遇结算（<c>Game.Monster.EncounterFactLog</c>）；契约放在读方这一侧（Narrative），
    /// 这样生产者只依赖一个「只写集合」的小接口，不会反向认识剧情服务。
    /// </para>
    /// </summary>
    public interface IEncounterFacts
    {
        /// <summary>这个键此刻是否成立。</summary>
        bool IsTrue(string key);

        /// <summary>把当前为真的键追加进 <paramref name="into"/>（不清空它，调用方自己决定）。</summary>
        void CollectFacts(List<string> into);
    }

    public sealed class NarrativeConditionSource : IDialogueConditionSource
    {
        private readonly PlayerModel player;
        private readonly ISaveService saves;
        private readonly Dictionary<string, NarrativeTrigger> targets = new Dictionary<string, NarrativeTrigger>(StringComparer.Ordinal);

        // 复用缓冲：Snapshot 在条件求值路径上（遭遇仲裁、每次自动推进）都会被调一次，每次 new 就是一处 GC 热点。
        private readonly List<string> persistedFlags = new List<string>();
        private readonly List<string> tierKeys = new List<string>();
        private readonly List<string> extraFlags = new List<string>();

        private IdentityState identityState;
        private IdentityLedger identityLedger;
        private SuspicionState identitySuspicion;
        private IdentitySettings identitySettings;
        private IEncounterFacts encounterFacts;

        public NarrativeConditionSource(PlayerModel player, ISaveService saves)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            IdentityFacts.CollectTierKeys(tierKeys);
        }

        /// <summary>
        /// 接入身份内核（S1/S2）：之后每次 <see cref="Snapshot"/> 都会把身份状态投影成事实键并进快照。
        /// <b>不接线时身份键恒不写</b>，条件行为与接线前一致——接线点在组装侧（容器 / 场景），不是本类。
        /// </summary>
        public void BindIdentity(IdentityState state, IdentityLedger ledger, SuspicionState suspicion, IdentitySettings settings)
        {
            identityState = state ?? throw new ArgumentNullException(nameof(state));
            identityLedger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            identitySuspicion = suspicion ?? throw new ArgumentNullException(nameof(suspicion));
            identitySettings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>接入遭遇侧的瞬时事实源（S3/S4）。不接线时 `stealth.*` / `chase.*` 条件恒为假。</summary>
        public void BindEncounterFacts(IEncounterFacts facts) => encounterFacts = facts;

        public void Register(NarrativeTrigger target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.TargetId)) throw new ArgumentException("叙事目标身份不可为空");
            if (targets.TryGetValue(target.TargetId, out NarrativeTrigger previous) && previous != null && previous != target)
                throw new ArgumentException("重复叙事目标：" + target.TargetId);
            targets[target.TargetId] = target;
        }

        public void Unregister(NarrativeTrigger target)
        {
            if (target != null && targets.TryGetValue(target.TargetId, out NarrativeTrigger current) && current == target)
                targets.Remove(target.TargetId);
        }

        public NarrativeTrigger Find(string id) => targets.TryGetValue(id, out NarrativeTrigger target) ? target : null;

        public EncounterContext Snapshot(string targetId)
        {
            NarrativeTrigger target = Find(targetId);
            PlayerSnapshot snapshot = player.Snapshot;
            var context = new EncounterContext(targetId, target == null ? string.Empty : target.TargetKind,
                snapshot.IsAlive, snapshot.IsSneaking, snapshot.IsDisguised,
                target != null && target.isActiveAndEnabled, false, false, CollectPersistedFlags());

            extraFlags.Clear();
            if (identityState != null)
            {
                new IdentityFactSnapshot(identityState, identityLedger, identitySuspicion, identitySettings)
                    .CollectTrueKeys(extraFlags);
            }

            if (encounterFacts != null)
            {
                encounterFacts.CollectFacts(extraFlags);
            }

            return extraFlags.Count == 0 ? context : context.WithStoryFlags(extraFlags);
        }

        /// <summary>
        /// 存档里的事实集合，**先去掉六个档位键**：它们由身份状态现算（每次查询重投影），存档里留下的
        /// 只会是旧快照的残影；不清就会与新算出来的档位同时为真，而字典 §4.1 要求同一时刻只有一个档位成立。
        /// </summary>
        private List<string> CollectPersistedFlags()
        {
            persistedFlags.Clear();
            foreach (string flag in saves.Get<NarrativeSaveData>().StoryFlags)
            {
                if (!tierKeys.Contains(flag))
                {
                    persistedFlags.Add(flag);
                }
            }

            return persistedFlags;
        }
    }
}

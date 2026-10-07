// 职责：一次露馅判定要看的「世界事实」——由调用方（关卡 / 接线方）填，规则层只读不猜。
// 为什么新建：六种露馅的原始事实来自四个不同模块（区域、目击、关口、龙族、怪物视野），
//   塞进方法参数会变成一长串 bool，读不出哪一条喂哪一条；做成一个结构体，
//   字段名就是出处，判定与喂值分开。

namespace Game.Identity
{
    /// <summary>
    /// 露馅判定的输入。全部字段都是<b>原始事实</b>，「合取」由
    /// <see cref="IdentityRules.EvaluateExposure"/> 做——调用方不要把三条条件自己先合起来，
    /// 否则「为什么没触发」在日志里查不出来。
    /// <para>
    /// 逐字段对应：<see cref="PersonaRestrictedIdentity"/> / <see cref="InPersonaRestrictedArea"/> /
    /// <see cref="SeenByOther"/> 是人物特性三条件（`02_身份暴露与怀疑.md:96-97` R4/R5，
    /// 真源是乐正在音乐解谜区被看见）；<see cref="CheckpointRequiresIdentity"/> /
    /// <see cref="CheckpointAccepted"/> 是核验关口（`02:107-110`）；
    /// <see cref="RevealedByDragon"/> / <see cref="RevealBlocked"/> 是揭露与蜃师面具抵挡一次
    /// （`02:131-132` R27/R28）；<see cref="InRedZone"/> / <see cref="InOrangeZone"/> 是视线层
    /// （`02:142-144` R32/R34）。账簿与怀疑度不在这里——它们的阈值在
    /// <see cref="IdentitySettings"/> 里，由规则层自己判。
    /// </para>
    /// <para>
    /// 用命名构造器（<see cref="Persona"/> / <see cref="Checkpoint"/> / <see cref="Reveal"/> /
    /// <see cref="Alert"/>）一次只喂一条；要同时喂多条就调全参构造，别把两个构造器的结果相加。
    /// </para>
    /// </summary>
    public readonly struct ExposureSignals
    {
        /// <summary>一条都不喂：判定结果恒为 <see cref="ExposureCause.None"/>（账簿与怀疑度仍按状态判）。</summary>
        public static readonly ExposureSignals None = default;

        public ExposureSignals(
            IdentityId personaRestrictedIdentity,
            bool inPersonaRestrictedArea,
            bool seenByOther,
            bool checkpointRequiresIdentity,
            bool checkpointAccepted,
            bool revealedByDragon,
            bool revealBlocked,
            bool inRedZone,
            bool inOrangeZone)
        {
            PersonaRestrictedIdentity = personaRestrictedIdentity;
            InPersonaRestrictedArea = inPersonaRestrictedArea;
            SeenByOther = seenByOther;
            CheckpointRequiresIdentity = checkpointRequiresIdentity;
            CheckpointAccepted = checkpointAccepted;
            RevealedByDragon = revealedByDragon;
            RevealBlocked = revealBlocked;
            InRedZone = inRedZone;
            InOrangeZone = inOrangeZone;
        }

        /// <summary>该区域限制的是哪个身份；<see cref="IdentityId.None"/> 表示这个区域不限制任何身份。</summary>
        public IdentityId PersonaRestrictedIdentity { get; }

        /// <summary>玩家当前是否在该身份的禁区里（如乐正的音乐解谜区）。</summary>
        public bool InPersonaRestrictedArea { get; }

        /// <summary>是否被其他人看见（目击者还在场）。</summary>
        public bool SeenByOther { get; }

        /// <summary>当前是否正在过一道会核验身份的关口。</summary>
        public bool CheckpointRequiresIdentity { get; }

        /// <summary>关口的核验结果：身份对得上、或持有正确的钥匙 / 皮 / 面具。</summary>
        public bool CheckpointAccepted { get; }

        /// <summary>龙族这一次的「揭露」是否发生。</summary>
        public bool RevealedByDragon { get; }

        /// <summary>这一次揭露是否被挡下（蜃师面具，一次性，`02:132` R28）。</summary>
        public bool RevealBlocked { get; }

        /// <summary>玩家是否在怪物的红区（前方 75° 扇区，`02:142` R32）。</summary>
        public bool InRedZone { get; }

        /// <summary>玩家是否在怪物的橙区（`02:142` R32）。</summary>
        public bool InOrangeZone { get; }

        /// <summary>人物特性三条件：禁区限制的身份 + 是否在禁区 + 是否被看见。</summary>
        public static ExposureSignals Persona(IdentityId restrictedIdentity, bool inRestrictedArea, bool seenByOther)
            => new ExposureSignals(restrictedIdentity, inRestrictedArea, seenByOther,
                false, false, false, false, false, false);

        /// <summary>身份核验关口：<paramref name="accepted"/> 为核验通过。</summary>
        public static ExposureSignals Checkpoint(bool accepted)
            => new ExposureSignals(IdentityId.None, false, false, true, accepted, false, false, false, false);

        /// <summary>揭露：<paramref name="blocked"/> 为被蜃师面具挡下（挡下即不露馅）。</summary>
        public static ExposureSignals Reveal(bool blocked)
            => new ExposureSignals(IdentityId.None, false, false, false, false, true, blocked, false, false);

        /// <summary>视线层：红区 / 橙区各一个开关。</summary>
        public static ExposureSignals Alert(bool inRedZone, bool inOrangeZone)
            => new ExposureSignals(IdentityId.None, false, false, false, false, false, false, inRedZone, inOrangeZone);
    }
}

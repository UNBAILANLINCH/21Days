// 职责：一次露馅判定的完整结果——命中哪几条、策略给出的后果、身份有没有掉、惩罚时长多少。
// 为什么新建：结果要同时喂三个地方（表现、追逐 / 死亡流程、埋点），
//   只回一个枚举会让调用方重新算一遍已经算过的东西。

namespace Game.Identity
{
    /// <summary>
    /// <see cref="IdentityRules.ResolveExposure"/> 的返回值。一次判定可能同时命中多条露馅，
    /// <see cref="Cause"/> 是位掩码，不是「第一条」。
    /// <para>
    /// <see cref="IdentityLost"/> 表示这次判定已经让身份失效（<see cref="IdentityState"/> 已回到本体）；
    /// <see cref="PunishmentSeconds"/> 是这次后果对应的时长，取自
    /// <see cref="IdentitySettings.ExposureChaseDurationSeconds"/> 或
    /// <see cref="IdentitySettings.ExposureDeathDelaySeconds"/>——<b>两个数都是占位</b>，出处见那两个字段。
    /// </para>
    /// </summary>
    public readonly struct ExposureResolution
    {
        /// <summary>什么都没发生。</summary>
        public static readonly ExposureResolution None =
            new ExposureResolution(ExposureCause.None, ExposureOutcome.None, false, 0f);

        public ExposureResolution(ExposureCause cause, ExposureOutcome outcome, bool identityLost, float punishmentSeconds)
        {
            Cause = cause;
            Outcome = outcome;
            IdentityLost = identityLost;
            PunishmentSeconds = punishmentSeconds;
        }

        /// <summary>命中的露馅方式（位掩码，可能多条）。</summary>
        public ExposureCause Cause { get; }

        /// <summary>注入策略给出的后果；没有注入策略时恒为 <see cref="ExposureOutcome.None"/>。</summary>
        public ExposureOutcome Outcome { get; }

        /// <summary>这次判定是否已让所借身份失效。</summary>
        public bool IdentityLost { get; }

        /// <summary>后果对应时长（秒）；无后果时为 0。</summary>
        public float PunishmentSeconds { get; }

        /// <summary>是否命中了至少一条露馅。</summary>
        public bool Triggered => Cause != ExposureCause.None;

        public override string ToString()
            => $"身份露馅：{Cause} → {Outcome}（身份失效={IdentityLost}，时长={PunishmentSeconds}）";
    }
}

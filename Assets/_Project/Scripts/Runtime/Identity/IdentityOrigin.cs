// 职责：身份是从哪儿借来的——附身、皮、面具、丹四类来源，决定剧情事实键写 identity.skin 还是 identity.mask。
// 为什么新建：来源不是布尔，散在各调用点会让「用皮」和「用面具」写出两种判断；
//   `Disguise` 模块只有开关语义，装不下来源，且本波不改它。

namespace Game.Identity
{
    /// <summary>
    /// 身份借用的来源。取值口径照 <c>docs/design/features-spotlight/01_换皮与附身.md:22</c>
    /// （术语表「伪装」行：不论来源是附身、皮、面具还是丹）与
    /// <c>docs/design/features-spotlight/00_功能总览.md:225</c>（「穿皮」禁用，来源要分开记）。
    /// <para>
    /// 来源决定写哪个剧情事实键：<see cref="Pelt"/> → <c>identity.skin</c>、<see cref="Mask"/> → <c>identity.mask</c>
    /// （<c>ai-docs/docs/story-facts.md</c> §4.1）。<see cref="Elixir"/>（如泾龙丹）与
    /// <see cref="Possession"/> 目前没有专属键，只写 `identity.borrowed` 与 `identity.&lt;id&gt;`。
    /// </para>
    /// </summary>
    public enum IdentityOrigin
    {
        /// <summary>本体：没有借用任何身份。</summary>
        None = 0,

        /// <summary>附身：对一个活着的角色发起，不经过道具（`01_换皮与附身.md:15`）。</summary>
        Possession = 1,

        /// <summary>皮：击杀 / 暗杀掉落、使用后生效（`01_换皮与附身.md:19`、`:166` 的执事皮「生效中」）。</summary>
        Pelt = 2,

        /// <summary>面具：剥脸 / 击杀所得（`01_换皮与附身.md:20`）。</summary>
        Mask = 3,

        /// <summary>丹：如「使用泾龙丹进行伪装」（`01_换皮与附身.md:86`）。</summary>
        Elixir = 4,
    }
}

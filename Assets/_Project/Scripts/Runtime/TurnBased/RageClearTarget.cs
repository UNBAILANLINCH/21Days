// 职责：形态切换时「怒气清空」清的是谁的怒气——原文没写，做成配置项。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:94`「且在形态切换后，BOSS生命值恢复为100%，怒气清空」。
// 疑点：sp07 通篇只有玩家有怒气（招式 1 攒、招式 2 / 3 花，:50-54），BOSS 从没写过怒气，
// 所以这句清的可能是玩家的、也可能是笔误。转写把这条列成 **C94**（09_BOSS战.md Q22）。
// **本枚举是占位口径，不是结论**：拍板后改 `BossProfile.RageClearOnPhaseChange` 一个值即可。

namespace Game.TurnBased
{
    /// <summary>形态切换时清谁的怒气（原文没写，占位 None；见 C94）。</summary>
    public enum RageClearTarget
    {
        /// <summary>不清（占位默认：按「通篇只有玩家有怒气」这句存疑处理，先不动任何人的怒气）。</summary>
        None = 0,

        /// <summary>清玩家的怒气（字面读法一）。</summary>
        Player = 1,

        /// <summary>清 BOSS 自己的怒气（字面读法二；本内核 BOSS 侧目前没有怒气，选了等于没效果）。</summary>
        Boss = 2,
    }
}

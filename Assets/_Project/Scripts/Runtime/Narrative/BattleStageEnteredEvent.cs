// 职责：剧情停到 Battle 阶段的事实通知——战斗侧（Game.Battle）据此开仗，打完经
//   NarrativeService.CompleteBattleAsync 用同一组身份回写（PRP/turnbased-battle D2 / D9）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：NarrativeChangedEvent 的语义是「分区已写回」，订阅者是 Session 的保存合并，只带阶段 ID；
//      拿它当开战信号，战斗侧得自己再读 Current 拼身份，而且每次 Flush 都会被叫醒。
//   2. 扩展不行：给 NarrativeChangedEvent 加战斗字段会让保存合并的订阅者也背上战斗语义（EventConventions 第 3 条：一个事件一个文件）。
// 发布点（全在 NarrativeService）：DriveAsync 停到 Battle 阶段、读档恢复到 Battle 阶段、Battle 阶段上同目标主动交互重试。
namespace Game.Narrative
{
    /// <summary>
    /// 剧情进入（或读档恢复到）一个 Battle 阶段。身份三项（<see cref="Generation"/> / <see cref="ActivationId"/> /
    /// <see cref="TargetId"/>）就是回写 <c>CompleteBattleAsync</c> 时要原样带回的值——不要换成遭遇步骤的 EncounterId。
    /// </summary>
    public readonly struct BattleStageEnteredEvent
    {
        public BattleStageEnteredEvent(long generation, long activationId, string targetId, string stageId, string payload)
        {
            Generation = generation;
            ActivationId = activationId;
            TargetId = targetId ?? string.Empty;
            StageId = stageId ?? string.Empty;
            Payload = payload ?? string.Empty;
        }

        /// <summary>剧情代数（读档 / 换主线会 +1，旧代数的回写会被拒）。</summary>
        public long Generation { get; }

        /// <summary>这一次阶段进入的激活号。</summary>
        public long ActivationId { get; }

        /// <summary>剧情目标的稳定 ID（场景里 NarrativeTrigger 的 targetId）。</summary>
        public string TargetId { get; }

        /// <summary>战斗阶段 ID（埋点与日志用）。</summary>
        public string StageId { get; }

        /// <summary>阶段表的 payload 列：战斗侧按它查 BOSS 定义（PRP/turnbased-battle D3）。</summary>
        public string Payload { get; }
    }
}

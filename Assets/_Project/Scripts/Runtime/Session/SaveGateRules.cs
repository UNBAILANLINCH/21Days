// 职责：自动保存的纯规则——什么时候允许落盘（稳定边界），以及多次请求如何合并成一次。
// 为什么新建：规则要能在 EditMode 里不起容器直接测（unity-tests 规则：规则先抽纯逻辑）；
//   放进 GameSession 会和槽位 IO、事件发布搅在一起，测一条闸门条件就得搭一整套假服务。

namespace Game.Session
{
    /// <summary>
    /// 自动保存闸门与待处理合并。全部是静态纯函数，无状态、零分配。
    /// </summary>
    public static class SaveGateRules
    {
        /// <summary>
        /// 能否现在落盘：必须处于玩法状态，且没有对白、没有面板 / 弹窗、没有待消费的战斗终局。
        /// </summary>
        public static bool CanSave(bool isGameplay, bool dialogueRunning, bool anyPanelOpen, bool battlePending)
        {
            return isGameplay && !dialogueRunning && !anyPanelOpen && !battlePending;
        }

        /// <summary>记一次保存请求：已有待处理时合并成一次，原因改记最新这次的（空原因保留原来的）。</summary>
        public static PendingSave Request(PendingSave pending, string reason)
        {
            string merged = string.IsNullOrEmpty(reason) ? pending.Reason : reason;
            return new PendingSave(true, string.IsNullOrEmpty(merged) ? "unknown" : merged);
        }

        /// <summary>取走待处理请求：返回清空后的值，<paramref name="reason"/> 为被取走请求的原因（没有请求时为 null）。</summary>
        public static PendingSave Take(PendingSave pending, out string reason)
        {
            reason = pending.HasRequest ? pending.Reason : null;
            return default;
        }

        /// <summary>待处理的保存请求。默认值 = 没有请求。</summary>
        public readonly struct PendingSave
        {
            public PendingSave(bool hasRequest, string reason)
            {
                HasRequest = hasRequest;
                Reason = reason;
            }

            public bool HasRequest { get; }

            /// <summary>最近一次请求的原因（埋点用）。</summary>
            public string Reason { get; }
        }
    }
}

// 职责：携带结果身份，拒绝旧场景／旧读档回调；通用事件没有剧情请求身份。
namespace Game.Narrative
{
    public readonly struct NarrativeIntent
    {
        public NarrativeIntent(long generation, long activationId, string targetId, string requestId, string result, string part = null)
        {
            Generation = generation;
            ActivationId = activationId;
            TargetId = targetId;
            RequestId = requestId;
            Result = result;
            Part = part;
        }
        public long Generation { get; }
        public long ActivationId { get; }
        public string TargetId { get; }
        public string RequestId { get; }
        public string Result { get; }
        public string Part { get; }
    }
}

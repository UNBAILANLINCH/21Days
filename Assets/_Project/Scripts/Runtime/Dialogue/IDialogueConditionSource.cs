// 职责：对白选项条件的事实来源——按目标 id 给出当前的 EncounterContext 快照。
// 为什么新建：Controller 旧签名用 Func<EncounterContext> 由调用方临时拼，没法由容器注入、也没法在 Narrative 接线后整体替换；
//   Narrative 的 EncounterContext 只是值，没有「谁来产生它」的接口可复用，所以单立一个小接口。
using Game.Narrative;

namespace Game.Dialogue
{
    /// <summary>对白条件快照来源。选项可用性刷新与选择提交时各取一次，实现须廉价、无副作用。</summary>
    public interface IDialogueConditionSource
    {
        /// <summary>取 <paramref name="targetId"/>（如 <c>"dialogue:1001"</c>）当前的遭遇事实快照。</summary>
        EncounterContext Snapshot(string targetId);
    }
}

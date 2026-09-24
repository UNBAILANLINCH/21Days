// 职责：对白条件的占位来源——所有正向事实为真、无剧情标记，让条件选项在 Narrative 未接线前也能走通。
// 为什么新建：IDialogueConditionSource 是接口，需要一个默认实现供容器注册；Narrative 目前没有可直接产出
//   EncounterContext 的服务可复用。Narrative 接线后用真实来源替换本类的注册（DialogueInstaller），本文件随之删除。
using Game.Narrative;

namespace Game.Dialogue
{
    public sealed class DefaultDialogueConditionSource : IDialogueConditionSource
    {
        public EncounterContext Snapshot(string targetId) =>
            new EncounterContext(targetId, "", true, false, false, true, false, true);
    }
}

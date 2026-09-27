// 职责：GameSession 看到的「玩法现场」——闸门四个条件、遭遇捕获 / 恢复准备、进度描述。
// 为什么新建：GameSession 要在 EditMode 里测，而 Quest / Dialogue / Monster / UIService 的具体类型都要容器与场景；
//   把这些具体依赖包进一个接口（实现是 SessionStateAdapter），测试给一个假实现即可。

namespace Game.Session
{
    /// <summary>存档会话的现场来源。实现只读玩法模块的公开 API，不改玩法状态（遭遇恢复准备除外）。</summary>
    public interface ISessionStateSource
    {
        /// <summary>当前是否处于玩法状态（能计时、能存档）。</summary>
        bool IsGameplayState { get; }

        /// <summary>对白进行中。</summary>
        bool DialogueRunning { get; }

        /// <summary>有 Panel / Popup 开着（UI 栈顶非空）。</summary>
        bool AnyPanelOpen { get; }

        /// <summary>战斗终局已出、还没被消费。</summary>
        bool BattleResultPending { get; }

        /// <summary>把遭遇现场捕获进 <c>saves.Get&lt;EncounterSaveData&gt;()</c>（原地写，不换分区实例）。</summary>
        void CaptureEncounter(long tick);

        /// <summary>
        /// true：用 <c>saves.Get&lt;EncounterSaveData&gt;()</c> 准备下次进场景的恢复，校验失败则不准备并记警告；
        /// false：清掉已准备的恢复（新游戏落出生点）。
        /// </summary>
        void PrepareRestore(bool fromSave);

        /// <summary>进度描述：追踪中的任务标题 → 进行中的第一条主线标题 → 配置占位文案。</summary>
        string BuildProgressText();
    }
}

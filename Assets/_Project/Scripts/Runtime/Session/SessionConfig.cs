// 职责：存档会话的可调参数——槽位数、保存提示文案与停留秒数、读档失败提示、进度占位文案、退出前保存的超时。
// 为什么新建：Session 是新模块，没有可复用的配置资产；数值与文案按规范进 ScriptableObject，不写死在代码里。

using UnityEngine;

namespace Game.Session
{
    /// <summary>存档会话配置。资产放 <c>Data/Session/SessionConfig.asset</c>，拖到 Boot 的 <c>SessionInstaller</c> 上。</summary>
    [CreateAssetMenu(menuName = "21Days/Session/Session Config")]
    public sealed class SessionConfig : ScriptableObject
    {
        [Tooltip("存档槽数量，槽号从 1 起。")]
        [SerializeField, Min(1)] private int slotCount = 3;

        [Tooltip("自动保存落盘成功后弹出的通知标题。")]
        [SerializeField] private string saveNoticeTitle = "已保存";

        [Tooltip("保存通知停留秒数（不受时间缩放影响）。")]
        [SerializeField, Min(0f)] private float saveNoticeSeconds = 1f;

        [Tooltip("读档失败（损坏 / 版本过高 / 缺元数据）时的通知标题。")]
        [SerializeField] private string loadFailedTitle = "存档不可用";

        [Tooltip("既没有追踪任务也没有进行中主线时的进度描述。")]
        [SerializeField] private string progressPlaceholder = "旅程开始";

        [Tooltip("退出游戏前保存最多等多少秒（真实时间）；≤ 0 表示不单独限时，只受 GameQuit 的总超时约束。")]
        [SerializeField, Min(0f)] private float quitHookTimeoutSeconds = 2f;

        public int SlotCount => slotCount;
        public string SaveNoticeTitle => saveNoticeTitle;
        public float SaveNoticeSeconds => saveNoticeSeconds;
        public string LoadFailedTitle => loadFailedTitle;
        public string ProgressPlaceholder => progressPlaceholder;
        public float QuitHookTimeoutSeconds => quitHookTimeoutSeconds;
    }
}

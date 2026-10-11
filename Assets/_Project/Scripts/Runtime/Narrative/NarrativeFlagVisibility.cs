// 职责：按剧情标记显隐所挂物体——配一个标记键，标记成立时隐藏（SetActive(false)），不成立时显示；
//   绑定时、读档后、每次剧情分区写回后都按当前槽位的标记重算（PRP/turnbased-battle D11「胜利后 BOSS NPC 读该标记退场」）。
// 为什么新建（project-root「加能力的顺序」）：
//   1. 复用不行：Runtime 里没有「按剧情标记显隐」的组件——SupplyCrate / QuestSceneBinder 各管自己的标记物，读的是开箱 / 任务分区，
//      不认识剧情标记；NarrativeConditionSource 只给条件求值出快照，不驱动场景物体。
//   2. 扩展不行：塞进 NarrativeTrigger 会让「剧情入口」兼管「显隐」，而且入口所在物体一隐藏它自己就停了（OnDisable 取消生命周期）；
//      显隐是独立职责，不带入口的物体（门、摆件）也要用。
// 物体隐藏后仍收得到通知：订阅的是 NarrativeService 的 C# 事件，不依赖 Update / OnEnable，物体关着照样被调到，
//   所以读档回到「标记没写」的进度时能重新显示。退订在 OnDestroy——场景里初始激活的物体卸载时 Unity 必调它。
// 绑定：NarrativeService 在场景加载时扫描并调 Bind（与 NarrativeTrigger 同一处）；运行时生成的物体自己调 Bind。
using System;
using UnityEngine;

namespace Game.Narrative
{
    /// <summary>剧情标记成立时隐藏所挂物体。只读标记、不写标记。</summary>
    public sealed class NarrativeFlagVisibility : MonoBehaviour
    {
        [SerializeField, Tooltip("剧情标记键（NarrativeSaveData.StoryFlags）。成立时隐藏本物体，例：world.sampleboss.defeated。")]
        private string flagKey = string.Empty;

        private NarrativeService service;

        /// <summary>配置的标记键。</summary>
        public string FlagKey => flagKey;

        /// <summary>已绑定剧情服务。</summary>
        public bool IsBound => service != null;

        /// <summary>运行时配置（运行时生成的物体 / 测试用）；已绑定时按新键立即重算。</summary>
        public void Configure(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("剧情标记键不可为空", nameof(key));
            flagKey = key;
            Refresh();
        }

        /// <summary>绑定剧情服务并立即按当前标记重算；重复绑定先解旧的。</summary>
        public void Bind(NarrativeService narrative)
        {
            Unbind();
            service = narrative ?? throw new ArgumentNullException(nameof(narrative));
            service.OnChanged += Refresh;
            Refresh();
        }

        /// <summary>解绑（不改显隐）。</summary>
        public void Unbind()
        {
            if (service != null) service.OnChanged -= Refresh;
            service = null;
        }

        /// <summary>按当前标记重算显隐；没绑定、键为空或服务未就绪时不动物体。</summary>
        public void Refresh()
        {
            if (service == null || !service.IsReady || string.IsNullOrWhiteSpace(flagKey)) return;
            bool visible = ShouldBeVisible(service.HasStoryFlag(flagKey));
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        /// <summary>纯判定：标记成立 → 隐藏。</summary>
        public static bool ShouldBeVisible(bool flagSet) => !flagSet;

        private void OnDestroy() => Unbind();
    }
}

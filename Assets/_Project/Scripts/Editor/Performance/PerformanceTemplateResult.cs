// 职责：PerformanceTemplateFactory.Create 的返回值——建出来的预制体与时间轴路径、是否已登记 Addressables。
// 为什么新建（复用 → 扩展 → 新建）：调用方（编辑器窗口、测试、波 3 的示例生成）要同时拿到三项结果，
//   工程规范一个文件一个类，只能单独成文件。
namespace Game.Editor.Performance
{
    /// <summary>新建演出模板的结果。</summary>
    public sealed class PerformanceTemplateResult
    {
        public PerformanceTemplateResult(string prefabPath, string timelinePath, bool registered)
        {
            PrefabPath = prefabPath;
            TimelinePath = timelinePath;
            Registered = registered;
        }

        /// <summary>预制体资产路径，如 Assets/_Project/Prefabs/Performance/perf_x.prefab。</summary>
        public string PrefabPath { get; }

        /// <summary>时间轴资产路径，如 Assets/_Project/Data/Performance/Timelines/perf_x.playable。</summary>
        public string TimelinePath { get; }

        /// <summary>是否已登记进 Addressables（地址 = id）。</summary>
        public bool Registered { get; }
    }
}

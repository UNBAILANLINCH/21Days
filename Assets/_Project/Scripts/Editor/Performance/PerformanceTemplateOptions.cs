// 职责：PerformanceTemplateFactory.Create 的可选参数——预制体 / 时间轴放哪、是否登记 Addressables、占位演员的默认表情。
// 为什么新建（复用 → 扩展 → 新建）：参数有四项且都有默认值，写成方法重载会爆炸；工程规范一个文件一个类，
//   不能嵌在工厂文件里，只能单独成文件。
using System.Collections.Generic;
using Game.Performance;

namespace Game.Editor.Performance
{
    /// <summary>新建演出模板的选项。不传（null）等价于全部默认。</summary>
    public sealed class PerformanceTemplateOptions
    {
        public const string DefaultPrefabFolder = "Assets/_Project/Prefabs/Performance";
        public const string DefaultTimelineFolder = "Assets/_Project/Data/Performance/Timelines";

        /// <summary>预制体目录（Assets/ 开头，不存在会逐级建）。</summary>
        public string PrefabFolder { get; set; } = DefaultPrefabFolder;

        /// <summary>时间轴资产目录（Assets/ 开头，不存在会逐级建）。</summary>
        public string TimelineFolder { get; set; } = DefaultTimelineFolder;

        /// <summary>是否把预制体登记进 Addressables 的 Performance 组（地址 = id）。测试里关掉。</summary>
        public bool RegisterAddressable { get; set; } = true;

        /// <summary>占位演员的默认表情表（名字 → Sprite）；null 或空表示演员先不带表情。</summary>
        public IReadOnlyList<SpritePerformanceActor.ExpressionEntry> ActorSprites { get; set; }
    }
}

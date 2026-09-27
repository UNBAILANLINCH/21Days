// 职责：演出演员的抽象——表情轨道只认它：列出可用表情名（供编辑器校验）、按名字切表情、显隐。
// 为什么新建（复用 → 扩展 → 新建）：占位演员是 SpriteRenderer，正式演员是 Live2D 模型（独立可选程序集），
//   时间轴轨道需要一个与两者都无关的绑定类型；DialogueCharacter 是对白立绘数据不是场景演员，且 Performance 不得依赖 Dialogue。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace Game.Performance
{
    /// <summary>
    /// 演出演员基类。挂在演出预制体的演员子物体上，由 <see cref="Timeline.ExpressionTrack"/> 绑定。
    /// 动作不经这里：Animation 轨道直接绑模型的 Animator。
    /// </summary>
    public abstract class PerformanceActor : MonoBehaviour
    {
        /// <summary>本演员支持的表情名（编辑器校验表情片段用）。</summary>
        public abstract IReadOnlyList<string> ExpressionNames { get; }

        /// <summary>切到指定表情。名字不存在时由实现记一次 Warn，不抛。</summary>
        public abstract void SetExpression(string expressionName);

        /// <summary>显隐演员。默认切整个物体的激活状态；有更轻量做法的实现（只关渲染器）应重写。</summary>
        public virtual void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        /// <summary>
        /// 登记编辑器预览时会被表情轨道改动的属性，退出预览时 Timeline 会把它们还原。默认不登记；
        /// 改了序列化属性（如 SpriteRenderer.sprite）的实现要重写，否则拖时间线会弄脏预制体。
        /// </summary>
        public virtual void GatherPreviewProperties(IPropertyCollector driver)
        {
        }
    }
}

// 职责：示例模块的数值配置——展示哪个道具、买几个、打几折。资产在 Assets/_Project/Data/Sample/。
// 为什么新建：csharp-code.md 要求「数值配置进 ScriptableObject，代码里不写死魔法数字」。
//   1. 复用不行：UIConfig / AudioConfig 是框架自己的配置，往里塞玩法数值就把 Core 和玩法焊死了。
//   2. 扩展不行：本模块此前没有任何配置类；把三个数值写成 SampleRules 里的 const 也不行——
//      那样策划改个折扣就得改代码重编译，正是这条规则要避免的。

using UnityEngine;

namespace Game.Sample
{
    /// <summary>
    /// 示例模块配置。资产：<c>Assets/_Project/Data/Sample/SampleConfig.asset</c>，
    /// 由 <see cref="SampleInstaller"/> 在 Inspector 上拖赋并注册进根作用域，
    /// 需要它的类（<see cref="SampleState"/>）构造注入即可。
    /// <para>**运行时只读**：改 ScriptableObject 的字段会写回资产文件，编辑器里会留下莫名其妙的 diff。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "21Days/Sample/Sample Config", fileName = "SampleConfig")]
    public sealed class SampleConfig : ScriptableObject
    {
        [Header("展示用的一笔订单")]
        [Tooltip("道具 id，要能在配置表 TbItem（Tables/Data/item.xlsx）里查到。")]
        [SerializeField] private int itemId = 1002;

        [Tooltip("买几个。必须是正数，否则 SampleRules.GetOrderTotal 会抛异常。")]
        [Min(1)]
        [SerializeField] private int count = 3;

        [Tooltip("减免比例：0 = 原价，1 = 免费。")]
        [Range(0f, 1f)]
        [SerializeField] private float discount = 0.2f;

        /// <summary>展示用的道具 id。</summary>
        public int ItemId => itemId;

        /// <summary>展示用的购买数量。</summary>
        public int Count => count;

        /// <summary>展示用的减免比例，0～1。</summary>
        public float Discount => discount;
    }
}

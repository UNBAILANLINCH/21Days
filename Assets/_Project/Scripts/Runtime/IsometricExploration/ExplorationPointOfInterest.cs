// 职责：场景里的探索兴趣点标签——万向标指向的对象（NPC / 物资箱 / 地点），声明名字、种类与是否仍需指引。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：QuestLocation 只登记任务地点、DialogueInteractable 只登记可对话物体、SupplyCrate 只管开合，
//      万向标要把三类统一成一张清单，任何一个都覆盖不全。
//   2. 扩展不行：往三者各加「万向标标签」字段会让 Quest / Dialogue / Loot 都认识探索 HUD，依赖方向反了。
//   IsometricExploration → Loot 的引用（读 SupplyCrate.IsOpened）符合 PRP/exploration-whitebox 3.1 的依赖方向。
using Game.Loot;
using UnityEngine;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 探索兴趣点。挂在 NPC / 物资箱 / 地点物体上，由 <see cref="ExplorationCompassPresenter"/> 在场景加载时登记（含未激活的）。
    /// 不注入服务、不每帧运行。
    /// </summary>
    public sealed class ExplorationPointOfInterest : MonoBehaviour
    {
        [Tooltip("万向标上显示的名字（如 NPC 名、「物资箱」、「营地」）。")]
        [SerializeField] private string label;

        [Tooltip("种类：Crate 类型在同物体的 SupplyCrate 打开后不再指引。")]
        [SerializeField] private PoiKind kind = PoiKind.Location;

        [Tooltip("可选：指引锚点（为空时用本物体位置）。")]
        [SerializeField] private Transform anchor;

        private SupplyCrate crate;
        private bool crateLookedUp;

        public string Label => label;
        public PoiKind Kind => kind;

        /// <summary>万向标指向的世界坐标。</summary>
        public Vector3 Position => anchor != null ? anchor.position : transform.position;

        /// <summary>是否仍需指引：Crate 类型取同物体 SupplyCrate 未开；其余恒 true。</summary>
        public bool IsVisible
        {
            get
            {
                if (kind != PoiKind.Crate) return true;
                // 未激活物体上 Awake 还没跑过：第一次访问时补取一次，之后不再 GetComponent。
                if (!crateLookedUp) CacheCrate();
                return crate == null || !crate.IsOpened;
            }
        }

        private void Awake()
        {
            CacheCrate();
        }

        private void CacheCrate()
        {
            crateLookedUp = true;
            crate = GetComponent<SupplyCrate>();
        }
    }
}

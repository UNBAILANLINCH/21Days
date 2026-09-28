// 职责：场景里「可被照镜的对象」标记——声明是人 / 物 / 妖、妖的表 id、人与物的显示名与形象、影子提示挂点、是否跟随巡逻怪模型。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：DialogueInteractable 是可对话物体、SupplyCrate 是物资箱、ExplorationPointOfInterest 是万向标兴趣点，都不表达「身份」。
//   2. 扩展不行：往它们身上加身份字段会让对白 / 拾取 / 探索模块认识妖物表，且给共享组件加序列化字段会静默改掉别的场景（pitfalls）。
//   所以 Mirror 模块新建独立标记组件，挂到对象上即可，不改原有组件。
using UnityEngine;

namespace Game.Mirror
{
    /// <summary>
    /// 照镜对象标记。由 <see cref="MirrorSceneBinder"/> 在场景加载时登记（含未激活的），运行时 Instantiate 的不登记。
    /// 逻辑位置由绑定器换算：默认取 <see cref="WorldPosition"/> 经 EncounterSceneView 的投影约定；
    /// 勾了 <see cref="FollowsMonster"/> 时改读 MonsterModel.Position（巡逻怪的场景位置是插值后的表现，逻辑以模型为准）。
    /// 组件自己不读输入、不做判定。
    /// </summary>
    public sealed class MirrorSubject : MonoBehaviour
    {
        [Tooltip("对象种类：人 / 物 / 妖。")]
        [SerializeField] private MirrorSubjectKind kind = MirrorSubjectKind.Human;

        [Tooltip("妖物表主键（tbyao id），种类为妖时必填。")]
        [SerializeField, Min(0)] private int yaoId;

        [Tooltip("人 / 物照镜时显示的名字；妖的名字取妖物表，这里不填。")]
        [SerializeField] private string displayName;

        [Tooltip("人 / 物照镜时显示的形象；妖的真形图取妖物表。")]
        [SerializeField] private Sprite portrait;

        [Tooltip("通灵视影子提示的挂点；为空时挂在本物体上。")]
        [SerializeField] private Transform hintAnchor;

        [Tooltip("挂在巡逻怪上时勾选：逻辑位置改读 MonsterModel.Position，而不是本物体的场景坐标。")]
        [SerializeField] private bool followsMonster;

        public MirrorSubjectKind Kind => kind;
        public int YaoId => yaoId;
        public string DisplayName => displayName;
        public Sprite Portrait => portrait;
        public bool FollowsMonster => followsMonster;

        /// <summary>影子提示挂点；没拖时返回本物体。</summary>
        public Transform HintAnchor => hintAnchor != null ? hintAnchor : transform;

        /// <summary>场景坐标（未投影）。</summary>
        public Vector3 WorldPosition => transform.position;
    }
}

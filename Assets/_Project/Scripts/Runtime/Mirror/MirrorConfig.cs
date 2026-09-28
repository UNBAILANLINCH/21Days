// 职责：镜模块的规则与表现参数（作用距离、裂痕缩减、扇形半角、冷却、结果停留、通灵视半径、可见范围、刻痕与各结果文案）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：PlayerConfig / LootConfig 各管本模块数值，照镜距离与刻痕文案不属于它们。
//   2. 扩展不行：塞进 PlayerConfig 会让玩家模块认识镜、妖与 UI 文案（PRP/mirror-core 2.1 依赖方向是 Mirror → Player）。
//   所以 Mirror 模块首次落地新建自己的 SO。
using UnityEngine;

namespace Game.Mirror
{
    /// <summary>镜配置。资产放 <c>Data/Mirror/MirrorConfig.asset</c>，拖到 Boot 场景 <c>GameBootstrap</c> 的 MirrorInstaller 上。运行时只读。</summary>
    [CreateAssetMenu(menuName = "21Days/Mirror/MirrorConfig", fileName = "MirrorConfig")]
    public sealed class MirrorConfig : ScriptableObject
    {
        [Header("照镜")]
        [Tooltip("无裂痕时的照镜作用距离（逻辑单位，米）。")]
        [SerializeField, Min(0f)] private float baseRange = 5f;

        [Tooltip("每道裂痕（击中裂痕 + 剧情裂痕）缩减的作用距离；缩到 0 为止。")]
        [SerializeField, Min(0f)] private float rangeLossPerCrack = 1.2f;

        [Tooltip("照镜扇形的半角（度）：以玩家朝向为轴，左右各这么多度。")]
        [SerializeField, Range(0f, 180f)] private float fanHalfAngle = 35f;

        [Tooltip("两次照镜之间的最短间隔（秒），冷却内按键忽略。")]
        [SerializeField, Min(0f)] private float castCooldown = 0.4f;

        [Tooltip("结果画面停留秒数；确认键可提前关。")]
        [SerializeField, Min(0f)] private float resultSeconds = 2.5f;

        [Header("通灵视")]
        [Tooltip("通灵视生效时，玩家多远内的妖会出现影子提示（逻辑单位，米）。")]
        [SerializeField, Min(0f)] private float sightRadius = 8f;

        [Tooltip("影子提示图：拖 Art/Sprites/Fx/Fx_SpiritShadow.png（专属图，不复用其它模块的标记图）。为空时不显示提示。")]
        [SerializeField] private Sprite spiritShadowSprite;

        [Tooltip("影子提示在挂点（MirrorSubject.HintAnchor）之上抬高的距离（米）。")]
        [SerializeField] private float spiritHintLift = 1.2f;

        [Tooltip("影子提示的统一缩放。")]
        [SerializeField, Min(0.01f)] private float spiritHintScale = 1f;

        [Tooltip("影子提示 SpriteRenderer 的 sortingOrder。")]
        [SerializeField] private int spiritHintSortingOrder = 10;

        [Header("可见范围（镜裂视野）")]
        [Tooltip("无裂痕时屏幕可见范围的半径（画布短边的比例，1 = 不遮）。")]
        [SerializeField, Min(0f)] private float visionBaseRadius = 1f;

        [Tooltip("每道裂痕（击中裂痕 + 剧情裂痕）缩减的可见范围比例；缩到 0 为止。")]
        [SerializeField, Min(0f)] private float visionLossPerCrack = 0.18f;

        [Header("文案（占位）")]
        [Tooltip("镜缘刻痕：每个结果画面都显示的律条文字，按顺序排在镜框边缘。")]
        [SerializeField] private string[] engraving =
        {
            "照形不照心",
            "一照一问，不问二回",
            "镜中无我",
        };

        [Tooltip("照人正常的标题。")]
        [SerializeField] private string humanTitle = "是人";

        [Tooltip("照物正常的标题。")]
        [SerializeField] private string objectTitle = "是物";

        [Tooltip("模糊轮廓的标题（不含名字）。")]
        [SerializeField] private string blurryTitle = "看不真切";

        [Tooltip("模糊轮廓的说明。")]
        [SerializeField] private string blurryBody = "镜里只有一团影子。也许还缺点什么线索。";

        [Tooltip("照见真形的标题；真形名与描述另取妖物表。")]
        [SerializeField] private string trueFormTitle = "照见真形";

        [Tooltip("照不到的标题。")]
        [SerializeField] private string nothingTitle = "照不到";

        [Tooltip("照不到的说明。")]
        [SerializeField] private string nothingBody = "镜面前空无一物，或者太远了。";

        [Tooltip("自照的标题。")]
        [SerializeField] private string selfTitle = "镜中无我";

        [Tooltip("自照的说明。")]
        [SerializeField] private string selfBody = "镜里只有身后的景物。";

        [Tooltip("镜碎页上唯一的文字。")]
        [SerializeField] private string shatterText = "镜碎";

        [Tooltip("镜碎页出现后多少秒（真实时间）内不响应按键，防止被击中时连按的键把页面直接跳过。")]
        [SerializeField, Min(0f)] private float shatterInputDelay = 0.6f;

        public float BaseRange => baseRange;
        public float RangeLossPerCrack => rangeLossPerCrack;
        public float FanHalfAngle => fanHalfAngle;
        public float CastCooldown => castCooldown;
        public float ResultSeconds => resultSeconds;
        public float SightRadius => sightRadius;
        public Sprite SpiritShadowSprite => spiritShadowSprite;
        public float SpiritHintLift => spiritHintLift;
        public float SpiritHintScale => spiritHintScale;
        public int SpiritHintSortingOrder => spiritHintSortingOrder;
        public float ShatterInputDelay => shatterInputDelay;
        public float VisionBaseRadius => visionBaseRadius;
        public float VisionLossPerCrack => visionLossPerCrack;

        /// <summary>镜缘刻痕文案；未配置时为空数组。</summary>
        public System.Collections.Generic.IReadOnlyList<string> Engraving => engraving ?? System.Array.Empty<string>();

        public string HumanTitle => humanTitle;
        public string ObjectTitle => objectTitle;
        public string BlurryTitle => blurryTitle;
        public string BlurryBody => blurryBody;
        public string TrueFormTitle => trueFormTitle;
        public string NothingTitle => nothingTitle;
        public string NothingBody => nothingBody;
        public string SelfTitle => selfTitle;
        public string SelfBody => selfBody;
        public string ShatterText => shatterText;
    }
}

// 职责：视线遮挡体的纯数据形状（轴对齐矩形 / 圆），只存几何，不认识场景、碰撞体与层。
//
// 为什么新建（不并进 Monster 模块，加能力的顺序：复用 → 扩展 → 新建）：
// - 复用：Monster 现有的遮挡能力在表现层（`EncounterSceneView.obstacleMask` 走 `Physics.CapsuleCast` 挡人、 // lint-ok: 注释里对比现有表现层遮挡能力，正文不含任何物理调用
//   `EncounterCollision` 做扫掠），是「挡人」不是「挡视线」，而且进不了确定性内核；工程里没有纯数据遮挡体，
//   也没有可借用的几何类型（Core 只有 `GameMath`，不含形状）。
// - 扩展：`MonsterConfig` / `MonsterRules` 正在被并行任务修改（本次任务明令不动），且遮挡体列表属于
//   「场景几何」，不属于某只怪物的调参；塞进 Monster 会让每只怪各带一份场景数据。
// - 新建：以上两条都不成立，故新建独立的 `Game.Stealth` 模块，只放纯规则与纯数据，
//   由 Monster / Player 将来以「注入策略」的方式调用（见 Files 头注释与交付报告的建议补丁）。
//
// 出处：`docs/design/features-spotlight/03_潜行与暗杀.md:129-136`（R24–R28 掩体与地形）、
// `03:132`（R25「掩体怎么起作用，原文没写」= 未定，形状如何参与判定由本内核给出可注入的实现）。
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>遮挡体形状。首版只有轴对齐矩形与圆两种，都是「简单形状」，便于纯数学求交。</summary>
    public enum StealthOccluderKind : byte
    {
        /// <summary>轴对齐矩形（掩体、柜台、箱子一类方方正正的东西）。</summary>
        Rectangle = 1,

        /// <summary>圆（柱子、树、石鼓一类圆形的东西）。</summary>
        Circle = 2,
    }

    /// <summary>
    /// 一个遮挡体的纯几何描述。语义是「这块区域挡视线」，不表达挡不挡人
    /// （挡人在工程里另有表现层白盒方案，见 `ai-docs/docs/modules/monster/monster-module-guide.md`）。
    /// <para>
    /// 只带几何、不带场景引用：这样它既能进确定性内核，也能在 EditMode 里用纯逻辑测。
    /// </para>
    /// </summary>
    public readonly struct StealthOccluder
    {
        private readonly Vector2 center;
        private readonly Vector2 halfSize;
        private readonly float radius;
        private readonly StealthOccluderKind kind;

        private StealthOccluder(StealthOccluderKind kind, Vector2 center, Vector2 halfSize, float radius, short id)
        {
            this.kind = kind;
            this.center = center;
            this.halfSize = halfSize;
            this.radius = radius;
            Id = id;
        }

        /// <summary>调用方给的标识（场景物体 id、关卡表行号都行）；只用于回传「被谁挡住」，0 表示未指定。</summary>
        public short Id { get; }

        /// <summary>形状。</summary>
        public StealthOccluderKind Kind => kind;

        /// <summary>矩形中心（圆也复用这个字段）。</summary>
        public Vector2 Center => center;

        /// <summary>矩形半宽半高（圆为 <see cref="Vector2.zero"/>）。</summary>
        public Vector2 HalfSize => halfSize;

        /// <summary>圆半径（矩形为 0）。</summary>
        public float Radius => radius;

        /// <summary>
        /// 轴对齐矩形遮挡体。<paramref name="size"/> 是全长全宽，内部折半存；
        /// 非正数会被拒（构造期就炸，别让一块零面积掩体静默失效）。
        /// </summary>
        public static StealthOccluder MakeRectangle(Vector2 center, Vector2 size, short id = 0)
        {
            if (size.x <= 0f || size.y <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(size), "遮挡矩形的长宽必须为正数");
            }

            return new StealthOccluder(StealthOccluderKind.Rectangle, center, size * 0.5f, 0f, id);
        }

        /// <summary>圆遮挡体；半径必须为正数。</summary>
        public static StealthOccluder MakeCircle(Vector2 center, float radius, short id = 0)
        {
            if (radius <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(radius), "遮挡圆的半径必须为正数");
            }

            return new StealthOccluder(StealthOccluderKind.Circle, center, Vector2.zero, radius, id);
        }

        /// <summary>轴对齐矩形的左 / 右 / 下 / 上边界，供求交直接取用。</summary>
        public void Bounds(out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = center.x - halfSize.x;
            maxX = center.x + halfSize.x;
            minY = center.y - halfSize.y;
            maxY = center.y + halfSize.y;
        }

        /// <summary>是不是合法形状（枚举值只可能是构造出来的两种，这里守的是默认值 <c>default</c>）。</summary>
        public bool IsValid => kind == StealthOccluderKind.Rectangle || kind == StealthOccluderKind.Circle;
    }
}

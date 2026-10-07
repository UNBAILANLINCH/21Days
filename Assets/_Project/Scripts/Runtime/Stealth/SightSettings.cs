// 职责：遮挡查询的可调旋钮。查询本身是纯几何（`StealthGeometry`），这里只放「怎么算」和「写不写键」。
//
// 为什么新建：Monster 的感知配置（`MonsterConfig`）只有半径与角度，没有任何遮挡概念；
// 遮挡是场景几何的属性，不该塞进每种怪物的数值里（并行任务正在改 `MonsterConfig`，本次明令不动）。
//
// 出处：`docs/design/features-spotlight/03_潜行与暗杀.md:129-136`（R24–R28）、
// `00_功能总览.md:384`（§8.1 #6「掩体与视线遮挡做不做」未定 → 本内核把规则做出来、数值可注入，
// 做不做由接线侧决定，未接线时 `stealth.cover` 恒不写）。
namespace Game.Stealth
{
    /// <summary>遮挡查询的可调项。</summary>
    public readonly struct SightSettings
    {
        /// <summary>
        /// 造成遮挡（视线被挡）时写不写 `stealth.cover`。
        /// <para>
        /// [待拍板] 占位 true，等 `00_功能总览.md:384` §8.1 #6（掩体与视线遮挡做不做）。
        /// 拍板「不做」时把它改 false——规则照样能跑、测试照样能测，只是不对外写键。
        /// </para>
        /// </summary>
        public bool WritesCoverFact { get; }

        /// <summary>
        /// 视线被挡时是否额外写 `stealth.hidden`（掩体即隐身）。
        /// <para>
        /// [待拍板] 占位 false，等 `03_潜行与暗杀.md:132` R25「掩体怎么起作用，原文没写」。
        /// false 表示掩体只挡视线、不直接等于隐身；感知是否归零由调用方的 `StealthDecisionGate` 汇总裁决。
        /// </para>
        /// </summary>
        public bool CoverImpliesHidden { get; }

        public SightSettings(bool writesCoverFact, bool coverImpliesHidden)
        {
            WritesCoverFact = writesCoverFact;
            CoverImpliesHidden = coverImpliesHidden;
        }

        /// <summary>
        /// 本工程的占位默认值（不是策划定稿）：掩体写 `stealth.cover`，但不等于隐身。
        /// </summary>
        public static SightSettings PlaceholderDefault => new SightSettings(true, false);
    }
}

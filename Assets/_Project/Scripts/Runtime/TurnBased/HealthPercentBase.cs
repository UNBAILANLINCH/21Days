// 职责：「生命值百分比」作用在什么基数上。
//
// 为什么要有这个枚举：原文给了百分比却没给基数——07:18 的「减少20%」、07:80 的「回复10%」
// 都没说按生命上限还是按当前生命算，09_BOSS战.md:239 把这条列成待定（C91）。
// 做成配置项，拍板后改一个字段即可，规则代码不动。
//
// 出处：`docs/design/features-spotlight/09_BOSS战.md:239`；待拍板 `待策划拍板问题.md:1256` C91。

namespace Game.TurnBased
{
    /// <summary>百分比以哪个血量为基数。</summary>
    public enum HealthPercentBase
    {
        /// <summary>按生命上限算（占位口径）。</summary>
        MaxHealth = 0,

        /// <summary>按当前生命算。</summary>
        CurrentHealth = 1,
    }
}

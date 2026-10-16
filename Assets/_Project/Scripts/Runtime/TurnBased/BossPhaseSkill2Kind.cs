// 职责：BOSS 招式 2 是哪一种——三个阶段各不同，原文都没在招式名以外的地方标出来，所以按形态判。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:80`（阶段一「招式2：饮酒，增加30醉酒值，回复10%生命值」）、
// :176（阶段九人形态「招式2：使用金锁禁锢住玩家，同时造成少量伤害」）、
// :192（阶段九龙形态「招式2：BOSS进入自我禁锢」）。

namespace Game.TurnBased
{
    /// <summary>BOSS 招式 2 的效果种类（由形态配置推导，见 <see cref="BossPhaseSettings.Skill2Kind"/>）。</summary>
    public enum BossPhaseSkill2Kind
    {
        /// <summary>饮酒：加醉酒值、回血、下次招式 1 增伤（阶段一，07:80）。</summary>
        Drink = 0,

        /// <summary>金锁禁锢玩家若干回合（阶段九人形态，07:176）。</summary>
        SkillLock = 1,

        /// <summary>BOSS 自我禁锢若干回合（阶段九龙形态，07:192）。</summary>
        SelfImprison = 2,
    }
}

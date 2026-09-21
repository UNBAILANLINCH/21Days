// 职责：伪装对敌人攻击的独立规则；状态与按键边缘复用 Player，不复制一套状态。
// 为什么新建：Monster 的感知不等于攻击许可；独立规则供所有敌人复用，不依赖驯服。
namespace Game.Disguise
{
    public static class DisguiseRules
    {
        public static bool AllowsEnemyAttack(bool isDisguised) => !isDisguised;
    }
}

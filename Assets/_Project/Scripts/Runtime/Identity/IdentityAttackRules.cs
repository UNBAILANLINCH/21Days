// 职责：给 Monster 的「这个敌人是否允许攻击玩家」再加一层——身份生效中也算伪装。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`DisguiseRules.AllowsEnemyAttack(bool)`（DisguiseRules.cs:7）只有一个布尔入参，
//      装不下「身份」这一层，而它是现有测试与 `MonsterRules.cs:238` 的调用点依赖的签名——
//      本波**不改 `Disguise` 模块**（一个字都不改），改为在它之上叠一层。
//   2. 扩展不行：不能改 `MonsterRules` 的调用（`Runtime/Monster/` 是并行任务 A 的目录），
//      也不能把身份判断塞进 `PlayerSnapshot`（`Runtime/Player/` 本波不许改）。
//   **只定义、不接线**：接线方式见交付报告的「建议补丁」。

namespace Game.Identity
{
    /// <summary>
    /// 敌人攻击许可的层叠判定。<b>现有语义原样保留</b>：
    /// <c>DisguiseRules.AllowsEnemyAttack(isDisguised) =&gt; !isDisguised</c>（DisguiseRules.cs:7，
    /// 模块文档 disguise-module-guide.md:19-31 写明「伪装期间敌人不攻击玩家」）。
    /// 本类只在其上<b>增加</b>一层：<paramref name="isIdentityInEffect"/> 为 true（身份生效中，
    /// 见 <see cref="IdentityState.IsInEffect"/>）时同样不允许攻击。
    /// <para>
    /// <paramref name="isIdentityInEffect"/> 传 false 时结果与旧行为<b>逐字相同</b>，
    /// 因此接线可以分两步走：先把现有调用换成这个重载并传 false（行为不变），
    /// 再把身份状态接上——不会出现「接身份的那一次提交顺手改了伪装语义」。
    /// </para>
    /// <para>
    /// 口径依据：`docs/design/features-spotlight/01_换皮与附身.md:160` R24（借着身份时按 mai 的「伪装」处理）
    /// 与 `PRP/monster-ai/prd.md`「验收 4」（伪装免于橙区警戒、不免红区敌对）。
    /// 「附身 / 用皮 / 用面具在怪物感知上是否就等于现有伪装」原文待定，见 `01_换皮与附身.md:263` Q16。
    /// </para>
    /// </summary>
    public static class IdentityAttackRules
    {
        /// <summary>
        /// 敌人能否攻击玩家。两层任意一层成立（旧伪装布尔，或身份生效中）都不允许攻击。
        /// </summary>
        public static bool AllowsEnemyAttack(bool isDisguised, bool isIdentityInEffect)
            => Game.Disguise.DisguiseRules.AllowsEnemyAttack(isDisguised) && !isIdentityInEffect;
    }
}

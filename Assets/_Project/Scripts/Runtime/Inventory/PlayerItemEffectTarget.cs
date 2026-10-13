// 职责：把玩家的生命口适配成道具效果要的 IItemEffectTarget——持有 PlayerRules，转发读写。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：PlayerRules 只有 ApplyDamage（治疗会被它的「伤害必须为正数」校验拒掉），PlayerModel.Health 的 setter 是
//      internal（虽然同属 Game.Runtime 程序集、Inventory 里写得到，但绕过 PlayerRules 直接改字段等于在规则类背后开洞：
//      上限、死亡、埋点都归它）。所以 Player 侧需要一个口——2026-10-07 主窗口裁决批准在 PlayerRules 上加了 Heal(int)。
//   2. 扩展不行：让 PlayerRules 直接实现 IItemEffectTarget 会新开一条 **Player → Inventory** 的依赖边，方向是反的
//      （Player 是更底层的玩法规则，Inventory 是消费方）。契约声明在使用方这一侧（Game.Inventory），
//      实现方**经本适配器**反向认识它——同 LootMonsterDropSink 之于 IMonsterDropSink 的接法。
// 为什么放在 Game.Inventory 而不是 Game.Player：一句话——**依赖方向是 Inventory → Player**，
//   适配器属于消费方，放在这里 Player 一个 using 都不用加；放进 Game.Player 就变成 Player 认识 Inventory 了。
using Game.Player;

namespace Game.Inventory
{
    /// <summary>
    /// <see cref="IItemEffectTarget"/> 的玩家实现：读 <c>PlayerRules.Model.Health</c>、写经 <c>PlayerRules.Heal</c>。
    /// 在 <see cref="InventoryInstaller"/> 里注册/传入。
    /// </summary>
    public sealed class PlayerItemEffectTarget : IItemEffectTarget
    {
        private readonly PlayerRules rules;

        public PlayerItemEffectTarget(PlayerRules rules)
        {
            // PlayerRules 是普通 C# 类（非 UnityEngine.Object），判空用 ?? 即可。
            this.rules = rules ?? throw new System.ArgumentNullException(nameof(rules));
        }

        /// <inheritdoc />
        public int CurrentHealth => rules.Model.Health;

        /// <inheritdoc />
        /// <remarks>转发给 <see cref="PlayerRules.Heal"/>：封顶、已满血、已倒下的口径都在那里，本类不重复判断。</remarks>
        public int TryHeal(int amount) => rules.Heal(amount);
    }
}

// 职责：效果执行者能吃到的目标集合——目前只有「玩家生命」这一项（占位治疗要的那个口）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：Game.Player 的 PlayerRules 是玩法规则类（它管移动、攻击、挨打），把它整个塞给道具效果
//      等于让道具能推玩家移动；「道具只该改它需要改的那一项」这件事需要一个更窄的形状。
//   2. 扩展不行：往 PlayerRules 上直接加 Heal 会改到 Game.Player 的文件——本波次文件所有权不含 Player，
//      而且那是另一路的目录。契约声明在**使用方**这一侧（Game.Inventory），实现方按需反向认识它。
// 当前状态（2026-10-07 主窗口裁决后）：
//   Player 侧的治疗口**已经补上**：`PlayerRules.Heal(int)`（与 ApplyDamage 对称，封顶 PlayerConfig.MaxHealth）。
//   本接口的实现方是 **`Game.Inventory.PlayerItemEffectTarget`**（本模块的薄适配器，持 PlayerRules 转发），
//   在 `InventoryInstaller` 里构造并传给 `InventoryPanelController`。
//   为什么不实现本接口的那一方是 PlayerRules 自己：那会新开一条 **Player → Inventory** 的依赖边，方向是反的
//   （Player 是更底层的玩法规则，Inventory 是消费方）。契约声明在使用方这一侧，实现方经适配器反向认识它。
//   —— 顺带更正一处曾经的误判：`PlayerModel.Health` 的 setter 是 `internal`，但全 Runtime 是单一程序集 `Game.Runtime`，
//   Inventory 里其实写得动它；**真正的理由不是权限，而是绕过 PlayerRules 直接改字段会在规则类背后开洞**
//   （上限、死亡、埋点都归它）。所以补的是 PlayerRules 上的口，不是字段权限。
namespace Game.Inventory
{
    /// <summary>
    /// 道具效果的目标口。实现方是 <see cref="PlayerItemEffectTarget"/>（本模块的薄适配器，底层转发 Game.Player 的规则）；
    /// 没接（为 null）时，需要它的效果返回 <see cref="ItemEffectStatus.BadTarget"/> 并带上中文原因，**不静默失败**。
    /// </summary>
    public interface IItemEffectTarget
    {
        /// <summary>当前生命值。</summary>
        int CurrentHealth { get; }

        /// <summary>
        /// 回复 <paramref name="amount"/> 点生命，封顶到生命上限。
        /// </summary>
        /// <returns>实际回复的点数；<paramref name="amount"/> 非正、已经满血、或已经倒下时返回 0（不改状态）。</returns>
        int TryHeal(int amount);
    }
}

// 职责：一次「实时遭遇升级为回合制」的请求快照——哪只怪、什么种类、怎么触发的、玩家此刻的实时生命与上限；
//   外加两侧（进战斗 / 出战斗）共用的血量换算口径 ScaleHealth（PRP/combat-iteration D5「血量按比例互通」）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`IBattleEntryContext` 是「主怪的局势快照」，回答不了「这一下是谁打了谁、打的是哪一只」，
//      也不带玩家的实时血量；`Game.TurnBased.BattleEntryRequest` 是纯规则内核的输入，不认识怪物 id 与种类。
//   2. 扩展不行：往 `IBattleEntryContext` 加字段会把「只读局势」与「发起开战」混成一个契约；
//      而 Monster 不能引用 Game.Battle（battle-module-guide :49、:59），契约只能声明在 Monster 侧。
//   所以新建这个只读值类型，由 `EncounterStep` 填、经 `IEncounterEscalationPort` 交给战斗侧。
using System;

namespace Game.Monster
{
    /// <summary>
    /// 升级回合制的请求（只读值）。由 <see cref="EncounterStep"/> 在拦截点当场构造，交给
    /// <see cref="IEncounterEscalationPort.TryEscalate"/>；字段全是值，不持有任何规则对象。
    /// </summary>
    public readonly struct EncounterEscalationRequest
    {
        /// <param name="targetId">这一下涉及的那只怪的场景稳定 id（<c>Taming.TargetIds</c> 里的一项）。</param>
        /// <param name="kindId">它的种类 id（<c>monster_species</c> 主键）；没接种类表时为 0。</param>
        /// <param name="trigger">触发情形，不能是 <see cref="EncounterEscalationTrigger.None"/>。</param>
        /// <param name="playerHealth">玩家此刻的实时生命，落在 0..<paramref name="playerMaxHealth"/>。</param>
        /// <param name="playerMaxHealth">玩家实时生命上限，必须大于 0。</param>
        public EncounterEscalationRequest(string targetId, int kindId, EncounterEscalationTrigger trigger,
            int playerHealth, int playerMaxHealth)
        {
            if (string.IsNullOrEmpty(targetId)) throw new ArgumentException("升级请求缺目标 id", nameof(targetId));
            if (kindId < 0) throw new ArgumentOutOfRangeException(nameof(kindId), kindId, "种类 id 不能为负数");
            if (trigger == EncounterEscalationTrigger.None) throw new ArgumentException("升级请求必须写明触发情形", nameof(trigger));
            if (playerMaxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(playerMaxHealth), playerMaxHealth, "玩家生命上限必须大于 0");
            if (playerHealth < 0 || playerHealth > playerMaxHealth)
                throw new ArgumentOutOfRangeException(nameof(playerHealth), playerHealth, "玩家实时生命必须落在 0..上限 内");

            TargetId = targetId;
            KindId = kindId;
            Trigger = trigger;
            PlayerHealth = playerHealth;
            PlayerMaxHealth = playerMaxHealth;
        }

        /// <summary>那只怪的场景稳定 id。</summary>
        public string TargetId { get; }

        /// <summary>那只怪的种类 id（0 = 没接种类表）。</summary>
        public int KindId { get; }

        /// <summary>触发情形。</summary>
        public EncounterEscalationTrigger Trigger { get; }

        /// <summary>玩家此刻的实时生命。</summary>
        public int PlayerHealth { get; }

        /// <summary>玩家实时生命上限。</summary>
        public int PlayerMaxHealth { get; }

        /// <summary>
        /// 血量按比例换算**进场**方向（用户 2026-10-14 拍板「回合制与实时血量按比例互通」）：
        /// <c>ceil(health / fromMax × toMax)</c>，至少 1、至多 <paramref name="toMax"/>。
        /// 调用方只有「实时 → 战斗上限」这一处（<c>BattleSetup.TryCreateCore</c>）；出场方向见 <see cref="ScaleHealthBack"/>。
        /// <para>
        /// <b>用整数算，不用浮点比例</b>：1/3 这类比例在 float 里乘回去会落在 1.0000001，向上取整就多出 1 点
        /// （「取整边界」测试钉住的正是这一格）。整数向上取整 = <c>(health × toMax + fromMax − 1) / fromMax</c>，精确无误差。
        /// </para>
        /// <para>「至少 1」：换算只用于「活着进战斗」与「打赢出战斗」两处，结果为 0 就等于凭空判死，所以兜底为 1；
        /// 打输置 0 不走这里（见 <see cref="EncounterStep.CompleteEscalation"/>）。</para>
        /// </summary>
        public static int ScaleHealth(int health, int fromMax, int toMax)
        {
            if (fromMax <= 0) throw new ArgumentOutOfRangeException(nameof(fromMax), fromMax, "换算前的生命上限必须大于 0");
            if (toMax <= 0) throw new ArgumentOutOfRangeException(nameof(toMax), toMax, "换算后的生命上限必须大于 0");

            long clamped = health < 0 ? 0 : health > fromMax ? fromMax : health;
            long scaled = (clamped * toMax + fromMax - 1) / fromMax;
            if (scaled < 1) return 1;
            return scaled > toMax ? toMax : (int)scaled;
        }

        /// <summary>
        /// 血量按比例换算**出场**方向（战斗剩余 → 实时上限）：<c>round(health / fromMax × toMax)</c>，四舍五入
        /// （<c>.5</c> 向上），至少 1、至多 <paramref name="toMax"/>。调用方只有
        /// <see cref="EncounterStep.CompleteEscalation"/> 打赢那一支。
        /// <para>
        /// <b>为什么进场向上取整、出场四舍五入</b>（2026-10-16 代码审查指出、当场修）：两个方向都用 `ceil` 会凭空回血——
        /// 实时 1/3 进战斗 <c>ceil(1/3 × 10) = 4</c>，一下没挨打地打完出来 <c>ceil(4/10 × 3) = 2</c>，1/3 变 2/3。
        /// 出场改四舍五入后 <c>round(0.4 × 3) = 1</c>，1/3 原样出来还是 1/3；1/3、2/3、3/3 原样往返都不变。
        /// 进场仍向上取整：那一步的意义是「残血进战斗不能比 0 更惨」，宁可多给一点也不让玩家半血进去变空血。
        /// </para>
        /// <para>
        /// 整数算法同 <see cref="ScaleHealth"/>：<c>(health × toMax × 2 + fromMax) / (fromMax × 2)</c> 截断即四舍五入，
        /// 不引浮点。（<c>+ fromMax</c> 是半个分母：<c>(2·h·toMax + fromMax) / (2·fromMax)</c>。）
        /// </para>
        /// </summary>
        public static int ScaleHealthBack(int health, int fromMax, int toMax)
        {
            if (fromMax <= 0) throw new ArgumentOutOfRangeException(nameof(fromMax), fromMax, "换算前的生命上限必须大于 0");
            if (toMax <= 0) throw new ArgumentOutOfRangeException(nameof(toMax), toMax, "换算后的生命上限必须大于 0");

            long clamped = health < 0 ? 0 : health > fromMax ? fromMax : health;
            long scaled = (clamped * toMax * 2 + fromMax) / (fromMax * 2);
            if (scaled < 1) return 1;
            return scaled > toMax ? toMax : (int)scaled;
        }
    }
}

// 职责：怪物每个逻辑 tick 的只读感知意图；不让表现层直接更改状态字段。
using Game.Player;

namespace Game.Monster
{
    public readonly struct MonsterIntent
    {
        /// <param name="isIdentityInEffect">
        /// 玩家当前借的身份是否**生效中**（`IdentityState.IsInEffect`，见 `Runtime/Identity/`）。
        /// <b>可选、默认 false</b>：老调用点（Disguise / Taming / Standalone / Showcase 与既有测试）
        /// 一行都不用改，传 false 时攻击许可与接线前逐字相同。
        /// <para>
        /// 为什么放在意图里而不是查全局：本结构是「这一 tick 的只读输入」，身份状态由遭遇流程
        /// （<c>EncounterStep.BindIdentity</c>）持有并逐 tick 填进来，规则类不认识 Identity 模块的状态对象。
        /// </para>
        /// </param>
        public MonsterIntent(PlayerSnapshot target, float deltaTime, bool isIdentityInEffect = false, float awarenessScale = 1f, float movementScale = 1f)
        {
            if (float.IsNaN(awarenessScale) || float.IsInfinity(awarenessScale) || awarenessScale <= 0f || awarenessScale > 1f ||
                float.IsNaN(movementScale) || float.IsInfinity(movementScale) || movementScale <= 0f || movementScale > 1f)
                throw new System.ArgumentOutOfRangeException(nameof(awarenessScale));
            Target = target;
            DeltaTime = deltaTime;
            IsIdentityInEffect = isIdentityInEffect;
            AwarenessScale = awarenessScale;
            MovementScale = movementScale;
        }
        public float AwarenessScale { get; }
        public float MovementScale { get; }

        public PlayerSnapshot Target { get; }
        public float DeltaTime { get; }

        /// <summary>
        /// 玩家正以「借来的身份」行动（附身 / 皮 / 面具生效中）。敌人攻击许可的第二层，
        /// 判据见 <c>Game.Identity.IdentityAttackRules.AllowsEnemyAttack</c>。
        /// </summary>
        public bool IsIdentityInEffect { get; }
    }
}

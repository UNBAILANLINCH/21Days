// 职责：独立验证驯服归属、控制切换和移动路由；不依赖伪装或接入正式遭遇/回放。
// 为什么新建：Player/Monster 只控制单一实体；控制归属不应混进个体移动或敌人感知。
using System;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using UnityEngine;

namespace Game.Taming
{
    public sealed class TamingRules
    {
        private readonly PlayerRules player;
        private readonly MonsterRules enemy;
        private readonly ITelemetryScope telemetry;
        private bool previousToggle;

        public TamingRules(PlayerRules player, MonsterRules enemy, ITelemetryScope telemetry)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.enemy = enemy ?? throw new ArgumentNullException(nameof(enemy));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public bool IsTamed { get; private set; }
        public bool IsControllingEnemy { get; private set; }

        public void Step(in TamingIntent intent, float deltaTime)
        {
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            bool canControl = player.Model.Health > 0 && enemy.Model.Health > 0;
            if (!canControl && IsControllingEnemy)
            {
                IsControllingEnemy = false;
                telemetry.Track("control_returned", ("reason", "dead"));
            }

            if (intent.ToggleControl && !previousToggle)
            {
                if (canControl)
                {
                    IsTamed = true;
                    IsControllingEnemy = !IsControllingEnemy;
                    telemetry.Track("control_changed", ("enemy", IsControllingEnemy));
                }
                else
                {
                    telemetry.Track("control_rejected", ("reason", "dead"));
                }
            }
            previousToggle = intent.ToggleControl;

            var playerIntent = new PlayerIntent(IsControllingEnemy ? Vector2.zero : intent.Movement,
                false, false, false, intent.Run);
            player.Step(in playerIntent, deltaTime);
            if (IsTamed)
            {
                // 返回玩家后仍保持友善；未控制时原地待命，不自动跟随。
                enemy.MoveControlled(IsControllingEnemy ? intent.Movement : Vector2.zero, deltaTime);
                return;
            }

            var enemyIntent = new MonsterIntent(player.Model.Snapshot, deltaTime);
            if (enemy.Step(in enemyIntent)) player.ApplyDamage(new DamageIntent(enemy.AttackDamage));
        }
    }
}

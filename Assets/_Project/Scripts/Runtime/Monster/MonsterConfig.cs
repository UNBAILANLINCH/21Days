// 职责：怪物巡逻、感知、战斗的可调原型数值；现有配置只服务各自框架职责。
using UnityEngine;

namespace Game.Monster
{
    [CreateAssetMenu(menuName = "21Days/Monster/Monster Config")]
    public sealed class MonsterConfig : ScriptableObject
    {
        [SerializeField] private float patrolSpeed = 2f;
        [SerializeField] private float alertSpeedMultiplier = 1.1f;
        [SerializeField] private float hostileSpeedMultiplier = 1.25f;
        [SerializeField] private float visionAngle = 75f;
        [SerializeField] private float alertRadius = 6f;
        [SerializeField] private float hostileRadius = 2f;
        [SerializeField] private float nearSenseRadius = 1.5f;
        [SerializeField] private float attackRange = 0.8f;
        [SerializeField] private float alertFillSeconds = 4f;
        [SerializeField] private float alertFallSeconds = 6f;
        [SerializeField] private float hostileLoseSeconds = 2f;
        [SerializeField] private float patrolPauseSeconds = 2f;
        [SerializeField] private int patrolMinSeconds = 7;
        [SerializeField] private int patrolMaxSeconds = 10;
        [SerializeField] private float attackCooldown = 1f;
        [SerializeField] private int maxHealth = 3;
        [SerializeField] private int attackDamage = 1;

        public float PatrolSpeed => patrolSpeed;
        public float AlertSpeedMultiplier => alertSpeedMultiplier;
        public float HostileSpeedMultiplier => hostileSpeedMultiplier;
        public float VisionAngle => visionAngle;
        public float AlertRadius => alertRadius;
        public float HostileRadius => hostileRadius;
        public float NearSenseRadius => nearSenseRadius;
        public float AttackRange => attackRange;
        public float AlertFillSeconds => alertFillSeconds;
        public float AlertFallSeconds => alertFallSeconds;
        public float HostileLoseSeconds => hostileLoseSeconds;
        public float PatrolPauseSeconds => patrolPauseSeconds;
        public int PatrolMinSeconds => patrolMinSeconds;
        public int PatrolMaxSeconds => patrolMaxSeconds;
        public float AttackCooldown => attackCooldown;
        public int MaxHealth => maxHealth;
        public int AttackDamage => attackDamage;
    }
}

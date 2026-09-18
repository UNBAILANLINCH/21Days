// 职责：玩家移动与战斗的可调数值。Core 无玩法参数，SampleConfig 也不负责玩家。
using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(menuName = "21Days/Player/Player Config")]
    public sealed class PlayerConfig : ScriptableObject
    {
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float sneakSpeed = 1.5f;
        [SerializeField] private float attackRange = 1f;
        [SerializeField] private float attackCooldown = 0.6f;
        [SerializeField] private int maxHealth = 3;
        [SerializeField] private int attackDamage = 1;

        public float MoveSpeed => moveSpeed;
        public float SneakSpeed => sneakSpeed;
        public float AttackRange => attackRange;
        public float AttackCooldown => attackCooldown;
        public int MaxHealth => maxHealth;
        public int AttackDamage => attackDamage;
    }
}

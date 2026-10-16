// 职责：原稿20/100暂作为上限的可修改试玩配置；正式含义仍待文策确认。
using UnityEngine;
namespace Game.Tavern
{
    [CreateAssetMenu(menuName = "21Days/Tavern/Config")]
    public sealed class TavernConfig : ScriptableObject
    {
        [SerializeField] private int perDrink = 10;
        [SerializeField] private int playerMaximum = 100;
        [SerializeField] private int bossMaximum = 100;
        [SerializeField] private int customerMaximum = 20;
        [SerializeField] private int playerEffectThreshold = 10;
        [SerializeField] private float drunkSpeedMultiplier = 0.5f;
        [SerializeField] private float minimumAwarenessMultiplier = 0.5f;
        public TavernDrunkRules CreateRules() => new TavernDrunkRules(perDrink, playerMaximum, bossMaximum, customerMaximum,
            playerEffectThreshold, drunkSpeedMultiplier, minimumAwarenessMultiplier);
    }
}

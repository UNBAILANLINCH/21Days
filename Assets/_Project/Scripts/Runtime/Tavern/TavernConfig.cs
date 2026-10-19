// 职责：酒肆饮酒、消息门槛与划拳动态强度配置；运行时只读。
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
        [SerializeField, TextArea] private string customerAClue = "今晚有贵客设宴，二楼已清场。要上去，得先过主人这一关。";
        [SerializeField, TextArea] private string customerBClue = "二楼窗边有个红衣人影，方才还向街上看。";
        [SerializeField, Range(0, 1000)] private int customerAStrength = 200;
        [SerializeField, Range(0, 1000)] private int customerBStrength = 400;
        [SerializeField, Range(0, 1000)] private int bossStrength = 650;
        [SerializeField, Range(0, 1000)] private int habitChance = 600;
        [SerializeField, Range(0, 1000)] private int maximumStrengthChange = 35;
        [SerializeField] private AnimationCurve difficultyAdjustment = new AnimationCurve(new Keyframe(0f, -.25f), new Keyframe(.5f, 0f), new Keyframe(1f, .25f));
        public int HabitChance => habitChance;
        public int MaximumStrengthChange => maximumStrengthChange;
        public int InitialStrength(string id) => id == "shop_customer_a" ? customerAStrength : id == "shop_customer_b" ? customerBStrength : bossStrength;
        public int FavoriteFinger(string id) => id == "shop_customer_a" ? 1 : id == "shop_customer_b" ? 3 : 5;
        public int FavoriteGuess(string id) => id == "shop_customer_a" ? 2 : id == "shop_customer_b" ? 4 : 3;
        public int DesiredStrength(string id, int performance)
        {
            float adjustment = difficultyAdjustment == null ? 0f : difficultyAdjustment.Evaluate(performance / 1000f);
            if (float.IsNaN(adjustment) || float.IsInfinity(adjustment)) throw new System.InvalidOperationException("划拳强度曲线含非法数值");
            int value = InitialStrength(id) + (int)(adjustment * 1000f);
            return value < 0 ? 0 : value > 1000 ? 1000 : value;
        }
        public int CustomerClueDrunk => customerMaximum;
        public int ChallengeDrunk => perDrink;
        public string CustomerAClue => customerAClue;
        public string CustomerBClue => customerBClue;
        public TavernDrunkRules CreateRules() => new TavernDrunkRules(perDrink, playerMaximum, bossMaximum, customerMaximum,
            playerEffectThreshold, drunkSpeedMultiplier, minimumAwarenessMultiplier);
    }
}

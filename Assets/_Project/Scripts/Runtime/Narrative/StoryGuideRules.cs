// 职责：依据当前节点、范围和真实完成事实选择软提示；不读取 Unity 或改变玩法。
// Quest 的屏幕投影不判断取皮、身份与店铺进度，不能复用它来选择教程。
namespace Game.Narrative
{
    public static class StoryGuideRules
    {
        // 保留旧等待 ID 供存档恢复；这些叙事衔接不要求玩家跑点。
        public static bool IsAutomaticEntry(string stage) => stage == "enter_r0083" || stage == "enter_r0129";
        public static string Objective(string stage, bool human)
        {
            if (human && stage == "action_r0070") return "与李诏阳切磋，学习回合制战斗";
            if (human) return stage == "enter_r0171" ? "前往戏班右侧平台，靠近镜面穿越" : "与李诏阳交谈，听取旧案线索";
            if (stage == "enter_r0171") return "前往轮回堂，向门前值守问询";
            if (stage == "enter_r0188") return "离开轮回堂，前往坊市入口查看酒肆";
            return "";
        }
        public enum Action { None, Interact, Sneak, Equip, Verify, Redirect, Perform, Clothes, Quota, Forge, Guess, Challenge, Meet, Complete }
        public static Action Select(bool eligibleNode, bool inRange, bool completed, bool blocked, Action action) =>
            eligibleNode && inRange && !completed && !blocked ? action : Action.None;
        public static string RequiredRole(int shop, bool entered, bool redirected, bool quotaReady)
        {
            switch (shop)
            {
                case 0: return !entered ? "apprentice" : !redirected ? "director" : "musician";
                case 1: return "customer1";
                case 2: return quotaReady ? "smith" : "officer";
                case 4: return "guest";
                default: return "";
            }
        }
        public static Action ShopAction(int shop, string role, bool ownsRequiredSkin, bool entered, bool redirected,
            bool completed, bool quotaReady, bool bossDrunk)
        {
            if (completed) return Action.None;
            string required = RequiredRole(shop, entered, redirected, quotaReady);
            if (required.Length > 0 && role != required) return ownsRequiredSkin ? Action.Equip : Action.Sneak;
            switch (shop)
            {
                case 0: return !entered ? Action.Verify : !redirected ? Action.Redirect : Action.Perform;
                case 1: return Action.Clothes;
                case 2: return quotaReady ? Action.Forge : Action.Quota;
                case 3: return Action.Interact;
                case 4: return bossDrunk ? Action.Challenge : Action.Guess;
                default: return Action.None;
            }
        }
    }
}

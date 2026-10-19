// 职责：只读正式剧情快照，选择固定光照与局部天气，不推进剧情或另存时间。
// 复用 NarrativeSaveData；StoryGuideRules 只选操作提示，环境映射独立以免混入引导职责。
namespace Game.Narrative
{
    public static class SpotlightEnvironmentRules
    {
        public enum Preset { None, Dawn, Day, MoonNight }
        // 每次 await 返回后都重新核对，不让旧会话揭开主流程黑幕或恢复它的输入。
        public static bool OwnsTransition(long operation, long currentOperation, long generation, long currentGeneration,
            bool flowChanging, bool sameOwner)
            => operation == currentOperation && generation == currentGeneration && !flowChanging && sameOwner;

        public readonly struct State
        {
            public Preset Lighting { get; }
            public bool Snow { get; }
            public bool AltarFire { get; }
            public bool Candles { get; }
            public bool WaterMist { get; }

            public State(Preset lighting, bool snow = false, bool altarFire = false,
                bool candles = false, bool waterMist = false)
            {
                Lighting = lighting;
                Snow = snow;
                AltarFire = altarFire;
                Candles = candles;
                WaterMist = waterMist;
            }
        }

        public static State Resolve(NarrativeSaveData snapshot)
        {
            if (snapshot == null) return default;
            // 局部遭遇继承正式父帧；未知主线不能借历史标记猜成夜晚。
            NarrativeSaveData.Frame frame = snapshot.Current;
            if (frame == null || frame.StoryId != "spotlight_current") frame = snapshot.Parent;
            if (frame == null || frame.StoryId != "spotlight_current") return default;

            // 明确列出 Tables/Data/narrative/spotlight_current.json 的现有键。
            // 时段与局部效果来自 docs/design/spotlight/writer-import.json；不按编号范围外推。
            switch (frame.StageId)
            {
                case "talk_21000":
                    return new State(Preset.MoonNight, snow: true, altarFire: true);
                case "talk_21001":
                case "talk_21002":
                    return new State(Preset.MoonNight, snow: true);
                case "talk_21003":
                case "enter_r0026":
                case "talk_21004":
                case "action_r0070":
                case "talk_21005":
                    return new State(Preset.Dawn);
                case "enter_r0083":
                case "talk_21006":
                    return new State(Preset.Day, candles: true);
                case "enter_r0129":
                case "talk_21007":
                case "enter_r0339":
                case "talk_21014":
                case "enter_r0469":
                case "talk_21020":
                case "action_r0478":
                    return new State(Preset.MoonNight, candles: true);
                case "enter_r0384":
                case "talk_21015":
                case "enter_r0397":
                case "talk_21016":
                case "enter_r0427":
                case "talk_21017":
                case "action_r0434":
                    return new State(Preset.MoonNight, waterMist: true);
                case "enter_r0171":
                case "talk_21008":
                case "enter_r0188":
                case "talk_21009":
                case "action_r0197":
                case "enter_r0198":
                case "talk_21010":
                case "enter_r0215":
                case "talk_21011":
                case "enter_r0293":
                case "talk_21012":
                case "action_r0304":
                case "talk_21013":
                case "enter_r0435":
                case "talk_21018":
                case "action_r0445":
                case "talk_21019":
                    return new State(Preset.MoonNight);
                default:
                    // None 让表现层恢复原场景；结束、旧示例档和未接入内容不抢光照。
                    return default;
            }
        }
    }
}

// 职责：ShowcaseScenario 的「回合制战斗」部分（partial）——几份回放都要进回合制战斗场景打一仗，共用这几样：
//   读战斗界面头顶的生命条（ReadBattleHealthBar）；轮到玩家就出招、直到某条件成立（FightUntil）；
//   看这一仗**怎么开的局**（WatchBattleOpening：开场那一刻左上角的回合提示、第一次轮到你出招时双方血量、
//   你出招之前有没有先挨打）；回放内换一份 BOSS 招式数值（WithBossOnlySkill1，经 BattleSetup.OverrideSettings，不改资产）。
//   全部只读界面 / 流程的公开状态，出招走真实按键（Dialogue/Choice1–3，战斗期间借用的键盘 1/2/3）。
//
// 用法（先进世界；战斗由剧情或实时遭遇升级开起来）：
//   var opening = new BattleOpeningRecord();
//   yield return WatchBattleOpening(opening, 20f);                    // 等到第一次轮到你出招（或在你出招前就分出胜负）
//   yield return Check("玩家先手", () => opening.CommandAwaited && !opening.PlayerHitBeforeCommand);
//   yield return FightUntil(() => BattleResultShown(BattleCueRules.VictoryText), 90f, false);
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— BattleShowcase 私有的 FightUntil / ReadPlayerBar / WithBossOnlySkill1 只它自己能用；
//           Monster 回放（实时遭遇升级回合制，PRP/combat-iteration T6.2）要同一套，再抄一份就是两份会各自过期的出招逻辑。
//   扩展 —— 要用 ResolveService / Input / WaitUntil 等 protected 成员，只能落在基类；与 DemoScene（场景内容）、
//           PlayerDrive（走路）、BootFlow（进退场）职责都不同，按职责单独一个 partial。BattleShowcase 改用这里的版本，行为逐字不变。
using System;
using System.Collections;
using System.Globalization;
using Game.Battle;
using Game.Core.Input;
using Game.TurnBased;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Tests.Showcase
{
    public abstract partial class ShowcaseScenario
    {
        /// <summary>战斗界面里玩家头顶块的物体名（BattleView 预制体）。</summary>
        protected const string PlayerHudName = "PlayerHud";

        /// <summary>战斗界面里 BOSS 头顶块的物体名（BattleView 预制体）。</summary>
        protected const string BossHudName = "BossHud";

        /// <summary>容器里的战斗表现（登记的是接口 <see cref="IBattlePresenter"/>，同 BattleShowcase 的取法）；取不到为 null。</summary>
        protected BattleScenePresenter BattlePresenter => ResolveService<IBattlePresenter>() as BattleScenePresenter;

        /// <summary>
        /// 战斗界面里名为 <paramref name="hudName"/> 的头顶块上「生命 当前 / 上限」的数字：<paramref name="part"/> 0 = 当前、1 = 上限；
        /// 取不到（界面没开 / 文字不是这个格式）返回 -1。
        /// </summary>
        protected static int ReadBattleHealthBar(BattleView view, string hudName, int part)
        {
            Transform hud = view == null ? null : FindDeep<Transform>(view.transform, hudName);
            Transform bar = hud == null ? null : FindDeep<Transform>(hud, "HealthBar");
            TMP_Text label = bar == null ? null : FindDeep<TMP_Text>(bar, "Label");
            if (label == null) return -1;
            string text = label.text;
            int space = text.IndexOf(' ');
            string[] parts = (space >= 0 ? text.Substring(space + 1) : text).Split('/');
            return parts.Length == 2 && int.TryParse(parts[part].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : -1;
        }

        /// <summary>战斗界面中央深色横条上的胜负大字完全显出、且正是 <paramref name="text"/>（只看「可见」会截到淡入第一帧）。</summary>
        protected bool BattleResultShown(string text)
        {
            BattleScenePresenter presenter = BattlePresenter;
            BattleView view = presenter == null ? null : presenter.View;
            return view != null && view.HintOpaque && view.HintText == text;
        }

        /// <summary>
        /// 轮到玩家就出招，直到 <paramref name="done"/> 成立或超时：<paramref name="skill1Only"/> 时只按 1；
        /// 否则怒气满（招式 3 格亮）按 3，不然按 1（招式 1 攒怒气）。按下后等这一手被接住再进下一轮。
        /// 不记报告条目（调用方随后自己 Check 结果）。
        /// </summary>
        protected IEnumerator FightUntil(Func<bool> done, float timeout, bool skill1Only)
        {
            IInputService inputService = ResolveService<IInputService>();
            BattleScenePresenter presenter = BattlePresenter;
            if (inputService == null || inputService.Actions == null || presenter == null)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] FightUntil 取不到输入服务 / 战斗表现，没法出招");
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                if (!presenter.IsAwaitingCommand || presenter.View == null)
                {
                    yield return null;
                    continue;
                }

                InputAction key = !skill1Only && presenter.View.IsSkillLit(2)
                    ? inputService.Actions.Dialogue.Choice3
                    : inputService.Actions.Dialogue.Choice1;
                yield return Input.Press(key);
                float accepted = Time.realtimeSinceStartup + 2f;
                while (presenter.IsAwaitingCommand && !done() && Time.realtimeSinceStartup < accepted)
                {
                    yield return null;
                }
            }
        }

        /// <summary>
        /// 记一条等待：从现在起盯着战斗界面，直到**第一次轮到你出招**（或这一仗在你出招之前就分出了胜负 / 收场）。
        /// 逐帧记下 <paramref name="record"/>：界面第一次出现时的回合提示与双方血量、第一次等你出招时的回合提示与双方血量、
        /// 这之前你的血条有没有掉过（= 对方先出了手）。调用时战斗可以还没开（剧情对白 / 升级在途都行）。
        /// </summary>
        protected IEnumerator WatchBattleOpening(BattleOpeningRecord record, float timeout)
        {
            BattleScenePresenter presenter = BattlePresenter;
            yield return WaitUntil("等到第一次轮到你出招（或这一仗在你出招之前就分出了胜负）", () =>
            {
                BattleView view = presenter == null ? null : presenter.View;
                if (view == null)
                {
                    // 界面开过又没了 = 这一仗已经收场（在你出招之前）。
                    if (record.FirstTurnText != null)
                    {
                        record.EndedBeforeCommand = true;
                        return true;
                    }

                    return false;
                }

                int playerHealth = ReadBattleHealthBar(view, PlayerHudName, 0);
                if (record.FirstTurnText == null)
                {
                    record.FirstTurnText = view.TurnText;
                    record.FirstPlayerHealth = playerHealth;
                    record.FirstPlayerMax = ReadBattleHealthBar(view, PlayerHudName, 1);
                    record.FirstBossHealth = ReadBattleHealthBar(view, BossHudName, 0);
                    record.FirstBossMax = ReadBattleHealthBar(view, BossHudName, 1);
                }

                if (playerHealth >= 0 && record.FirstPlayerHealth >= 0 && playerHealth < record.FirstPlayerHealth)
                {
                    record.PlayerHitBeforeCommand = true;
                }

                if (presenter.IsAwaitingCommand)
                {
                    record.CommandAwaited = true;
                    record.TurnTextAtCommand = view.TurnText;
                    record.PlayerHealthAtCommand = playerHealth;
                    record.PlayerMaxAtCommand = ReadBattleHealthBar(view, PlayerHudName, 1);
                    record.BossHealthAtCommand = ReadBattleHealthBar(view, BossHudName, 0);
                    record.BossMaxAtCommand = ReadBattleHealthBar(view, BossHudName, 1);
                    return true;
                }

                if (view.HintOpaque && (view.HintText == BattleCueRules.DefeatText || view.HintText == BattleCueRules.VictoryText))
                {
                    record.EndedBeforeCommand = true;
                    return true;
                }

                return false;
            }, timeout);
        }

        /// <summary>只换 BOSS 招式：只出招式 1（权重 1:0:0）、伤害 <paramref name="skill1Damage"/>；其余数值照装配时的那份。</summary>
        protected static BattleSettings WithBossOnlySkill1(in BattleSettings source, int skill1Damage)
        {
            BossSkillSettings boss = source.BossSkills;
            var replaced = new BossSkillSettings(skill1Damage, boss.Skill2DrinkAddDrunk, boss.Skill2HealPercent, boss.Skill2HealBase,
                boss.Skill2NextSkill1DamageBonusPercent, boss.Skill3Damage, boss.Skill3StunRounds, 1, 0, 0);
            return new BattleSettings(source.Entry, source.PlayerSkills, replaced, source.Drunk, source.Flow,
                source.ItemOncePerBattle, source.InheritsDrunkValue, source.Items);
        }

        /// <summary>
        /// <see cref="WatchBattleOpening"/> 记下的开局现场（只读界面上看得见的东西：回合提示与两根生命条的数字）。
        /// 数字取不到为 -1；文字没看到为 null。
        /// </summary>
        protected sealed class BattleOpeningRecord
        {
            /// <summary>战斗界面第一次出现时左上角的回合提示（如「第 1 回合 · 敌方回合」）。</summary>
            public string FirstTurnText { get; set; }

            /// <summary>界面第一次出现时玩家的生命 / 上限。</summary>
            public int FirstPlayerHealth { get; set; } = -1;

            public int FirstPlayerMax { get; set; } = -1;

            /// <summary>界面第一次出现时 BOSS 的生命 / 上限（偷袭开局在这一刻已经扣过 20%）。</summary>
            public int FirstBossHealth { get; set; } = -1;

            public int FirstBossMax { get; set; } = -1;

            /// <summary>等到过「轮到你出招」。</summary>
            public bool CommandAwaited { get; set; }

            /// <summary>第一次轮到你出招时的回合提示与双方生命 / 上限。</summary>
            public string TurnTextAtCommand { get; set; }

            public int PlayerHealthAtCommand { get; set; } = -1;

            public int PlayerMaxAtCommand { get; set; } = -1;

            public int BossHealthAtCommand { get; set; } = -1;

            public int BossMaxAtCommand { get; set; } = -1;

            /// <summary>在你第一次出招之前，你的血条掉过（= 对方先出了手）。</summary>
            public bool PlayerHitBeforeCommand { get; set; }

            /// <summary>在你第一次出招之前这一仗就分出了胜负 / 收场了。</summary>
            public bool EndedBeforeCommand { get; set; }

            /// <summary>写进检查点文字的一行现场。</summary>
            public string Describe() =>
                $"开场提示「{FirstTurnText}」、开场你 {FirstPlayerHealth}/{FirstPlayerMax}、BOSS {FirstBossHealth}/{FirstBossMax}；"
                + (CommandAwaited
                    ? $"第一次轮到你出招时「{TurnTextAtCommand}」、你 {PlayerHealthAtCommand}/{PlayerMaxAtCommand}、BOSS {BossHealthAtCommand}/{BossMaxAtCommand}"
                    : "没等到轮到你出招")
                + $"；出招前挨过打 {PlayerHitBeforeCommand}、出招前已分胜负 {EndedBeforeCommand}";
        }
    }
}

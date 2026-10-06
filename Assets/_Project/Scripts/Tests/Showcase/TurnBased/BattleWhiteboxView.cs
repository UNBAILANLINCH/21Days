// 职责：TurnBased 的**白盒状态板**——回放期间贴在 Game 视图左侧的那块自绘面板，
//   把回合制作战内核当前的状态原样显示出来：先手方、阶段、玩家的怒气 / 生命、BOSS 的生命 / 醉酒值与档位、
//   剩余回合、每个招式现在能不能施放、最近一次动作的事件，以及跳过回合时**屏幕中央闪现的提示文案**。
//
// 为什么是白盒（而不是正式战斗界面）：现在是白盒阶段，正式界面（招式格高亮 / 怒气槽 / 怪物血条上下方的
//   状态与醉酒值）属于美术范围且等 **C92 / Q19** 拍板（`docs/design/features-spotlight/09_BOSS战.md` 的开放问题；
//   模块 guide「接线清单」最后一行也写着界面卡在 C92 / Q19）。所以这里只做「能看清数值与状态变化」的最小可见形式：
//   一块 IMGUI 文本面板 + 一条居中的提示条，零美术资产、零预制体。**它不是生产表现层**，故放在
//   `Scripts/Tests/Showcase/TurnBased/` 而不是 `Runtime/`。
//
// 每一项显示什么、对着文档哪一行（正文里也带出处，方便对着策划原件核）：
//   · 先手方 / 进入方式 —— `07_回合制作战文档.md:18`（偷袭：玩家先手 + BOSS 生命 -20%）、
//     `:23`（正面攻击：玩家先手）、`:28`（被打：BOSS 先手）；值取自 `BattleSession.Entry`。
//   · 玩家怒气 —— `:50`（招式 1 使用后 +1 怒气）、`:52`（招式 2 消耗 1）、`:54`（招式 3 消耗 3）；
//     怒气上限原文没给，是配置项 `PlayerSkillSettings.RageMax`（占位 3，等 C91），所以面板写「怒气 x/上限」。
//   · 玩家生命 —— 原文没写玩家血量（`09_BOSS战.md:239`），由接线侧注入 `PlayerBattleSnapshot`，面板如实显示。
//   · 招式可用性 —— `:40`「在招式有足够怒气可以施放时，招式格显示高亮，否则置暗」的**白盒版**：
//     正式界面用高亮 / 置暗，这里用「✓ 可施放 / ✗ 原因」表达同一件事，判定取 `PlayerBattleRules.CanCast` 与
//     `PlayerSkillRules.RageCost`。
//   · 额外伤害次数 —— `:54`「玩家接下来的 3 次攻击附带额外伤害」，取 `PlayerBattleRules.ExtraDamageCharges`。
//   · BOSS 生命 / 醉酒值 / 档位 —— `:66`（进战斗继承战斗外醉酒值）、`:70-73`（正常 0-49 / 微醺 50-79（20%）/
//     薄醉 80-99（40%）/ 酩酊 100（100%、-50、持续 2 回合））；值取自 `BossBattleRules.Health` / `.Drunk`。
//   · 减疗 / 下次招式 1 +30% —— `:52`（减少对方 50% 治疗效果）、`:80`（饮酒后下次招式 1 伤害 +30%）。
//   · 剩余回合 —— `:43`「玩家状态右下角显示剩余回合」；回合上限是配置项 `BattleFlowSettings.RoundLimit`
//     （占位 0 = 不限，等 C91），所以面板照实写「不限」而不编一个数。
//   · 中央闪现提示 —— `:71` / `:72` / `:73` 三句原文（由 `BattleEvent.HintText` 带出来，本组件只负责画）。
//
// 组件只读不写：它**不**调 `TryCastSkill` / `RunBossTurn`（那是界面输入层与战斗流程的职责），
//   只按 `Bind` 给的那场 `BattleSession` 显示状态、按 `Present()` 播最近一次动作的事件。
//   正式接线的接法就是这两步：战斗流程 new 出会话 → 表现层 `Bind` + 每次动作后 `Present`。
//
// 命中 project-lint 的两条每帧规则：OnGUI 里不查找（GetComponent / Find）、不打日志（Debug.Log），
//   样式与贴图懒建一次并缓存（同 `ShowcaseOverlay` 的做法）。

using System.Collections.Generic;
using System.Text;
using Game.TurnBased;
using UnityEngine;

namespace Game.Tests.Showcase.TurnBased
{
    /// <summary>回合制作战内核的白盒状态板（回放期间贴在 Game 视图上，给人看数值与状态变化）。</summary>
    public sealed class BattleWhiteboxView : MonoBehaviour
    {
        /// <summary>面板左上角坐标：右侧让开顶部 78px 的回放信息条（ShowcaseOverlay）。</summary>
        private const float PanelLeft = 12f;
        private const float PanelTop = 90f;
        private const float PanelWidth = 720f;
        private const float LineHeight = 21f;

        /// <summary>屏幕中央提示闪现的真实秒数（07:71-73「闪现」：够看清，又不至于糊在屏幕上不走）。</summary>
        private const float HintSeconds = 3f;

        /// <summary>面板里最多留几条事件（只留最近的，够回看「刚才发生了什么」）。</summary>
        private const int MaxLogLines = 6;

        private const int PanelFontSize = 17;
        private const int HintFontSize = 30;

        private readonly StringBuilder builder = new StringBuilder(1200);
        private readonly List<string> log = new List<string>(MaxLogLines);

        private BattleSession session;
        private BattleSettings settings;
        private string battleTitle = string.Empty;
        private string hintText;
        private float hintUntil;
        private string renderedText = string.Empty;
        private int renderedLineCount = 1;

        private GUIStyle panelStyle;
        private GUIStyle hintStyle;
        private Texture2D backdrop;
        private Texture2D hintBackdrop;

        /// <summary>上一帧真正画在屏幕上的一份文本。批处理（无图形设备）下 OnGUI 可能不跑，那时按当前状态现算。</summary>
        public string ScreenText => renderedText.Length > 0 ? renderedText : BuildPanelText();

        /// <summary>面板有没有真的画过（至少跑过一次 OnGUI）。</summary>
        public bool HasRendered => renderedText.Length > 0;

        /// <summary>正在闪现的提示文案；没在闪现时是 null。回放用它核对「屏幕上的字 = 策划原文」。</summary>
        public string HintText => IsHintVisible ? hintText : null;

        /// <summary>提示是否还在闪现期。</summary>
        public bool IsHintVisible => !string.IsNullOrEmpty(hintText) && Time.realtimeSinceStartup < hintUntil;

        /// <summary>绑定要显示的这一场战斗与它用的那份数值设置（换一场就再调一次）。</summary>
        public void Bind(BattleSession battle, in BattleSettings battleSettings, string title)
        {
            session = battle;
            settings = battleSettings;
            battleTitle = string.IsNullOrEmpty(title) ? "(未命名)" : title;
            log.Clear();
            ClearHint();
            renderedText = string.Empty;
            Note("面板绑定到这一场战斗：" + battleTitle);
        }

        /// <summary>
        /// 播最近一次动作的表现：把 <see cref="BattleSession.Events"/> 逐条记进事件行；
        /// 遇到「怪物跳过回合」就按 07:71-73 把文案打到屏幕中央。每次调用动作之后调一次。
        /// </summary>
        public void Present()
        {
            if (session == null)
            {
                return;
            }

            // 新的动作一到，上一次的中央提示就该消失（正式界面同理：闪现只在本次动作的窗口里）。
            ClearHint();

            IReadOnlyList<BattleEvent> events = session.Events;
            if (events.Count > 0)
            {
                // 分隔线之下是「刚刚这次动作」的事件，之上是历史；否则回看时分不清哪条是新的。
                Note("— 本回合 —");
            }

            for (int i = 0; i < events.Count; i++)
            {
                BattleEvent battleEvent = events[i];
                Note(battleEvent.Describe());
                if (battleEvent.Kind == BattleEventKind.BossTurnSkipped && !string.IsNullOrEmpty(battleEvent.HintText))
                {
                    ShowHint(battleEvent.HintText);
                }
            }
        }

        /// <summary>面板的事件行追加一句（回放侧也可以用它记「这一步做了什么」）。</summary>
        public void Note(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            log.Add(line);
            if (log.Count > MaxLogLines)
            {
                log.RemoveAt(0);
            }
        }

        private void ShowHint(string text)
        {
            hintText = text;
            hintUntil = Time.realtimeSinceStartup + HintSeconds;
        }

        /// <summary>撤掉正在闪现的提示（下一次动作到来、或换一场战斗时）。</summary>
        private void ClearHint()
        {
            hintText = null;
            hintUntil = 0f;
        }

        private void OnGUI()
        {
            EnsureStyles();

            renderedText = BuildPanelText();
            float height = renderedLineCount * LineHeight + 14f;
            GUI.DrawTexture(new Rect(PanelLeft, PanelTop, PanelWidth, height), backdrop);
            GUI.Label(new Rect(PanelLeft + 10f, PanelTop + 7f, PanelWidth - 20f, height - 14f), renderedText, panelStyle);

            if (!IsHintVisible)
            {
                return;
            }

            // 07:71-73 的「屏幕中央闪现提示」：正式界面怎么做等 C92 / Q19，这里先把字原样摆到画面中央。
            float width = Mathf.Min(Screen.width - 80f, 960f);
            Rect hintRect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.40f, width, 72f);
            GUI.DrawTexture(hintRect, hintBackdrop);
            GUI.Label(hintRect, hintText, hintStyle);
        }

        /// <summary>
        /// 拼出面板文本（同一份文本既用于画，也用于检查点核对「屏幕上写的 = 内核里的」）。
        /// 每次都现拼：面板是白盒调试件，状态每秒都在变，缓存反而容易显示旧值。
        /// </summary>
        private string BuildPanelText()
        {
            builder.Clear();
            builder.AppendLine("回合制作战 · 白盒状态板（白盒阶段；正式界面等 C92 / Q19）");
            renderedLineCount = 1;

            if (session == null)
            {
                AppendLine("还没开战：等战斗流程 Bind 一场 BattleSession");
                return builder.ToString();
            }

            string initiative = session.Entry.Initiative == BattleInitiative.Boss ? "BOSS" : "玩家";
            AppendLine("战斗：" + battleTitle);
            AppendLine("进入方式：" + BattleEntryRules.Describe(session.Entry.Kind) + " · 先手方：" + initiative);
            AppendLine("阶段：" + DescribePhase(session.Phase) + " · 已打回合 " + session.CompletedRounds
                       + " · 剩余回合：" + DescribeRoundsLeft());
            AppendLine("玩家：生命 " + session.Player.Health + "/" + session.Player.MaxHealth
                       + " · 怒气 " + session.Player.Rage + "/" + session.Player.RageMax
                       + " · 额外伤害 " + session.Player.ExtraDamageCharges + " 次（07:54）");
            AppendLine("招式1（耗 " + RageCost(PlayerSkill.Skill1) + "）：" + DescribeSkill(PlayerSkill.Skill1));
            AppendLine("招式2（耗 " + RageCost(PlayerSkill.Skill2) + "）：" + DescribeSkill(PlayerSkill.Skill2));
            AppendLine("招式3（耗 " + RageCost(PlayerSkill.Skill3) + "）：" + DescribeSkill(PlayerSkill.Skill3));
            AppendLine("BOSS：生命 " + session.Boss.Health + "/" + session.Boss.MaxHealth
                       + " · 醉酒 " + session.Boss.DrunkValue + "/" + settings.Drunk.MaxDrunkValue
                       + " = " + DrunkTierRules.Name(session.Boss.Tier)
                       + "（跳过 " + session.Boss.Drunk.SkipChancePercent + "%）");
            AppendLine("BOSS 状态：减疗 " + session.Boss.HealReductionPercent + "%"
                       + " · 下次招式1 +30%：" + (session.Boss.NextSkill1DamageBonusPending ? "是" : "否"));
            AppendLine("最近事件：");
            for (int i = 0; i < log.Count; i++)
            {
                AppendLine("  · " + log[i]);
            }

            if (IsHintVisible)
            {
                AppendLine("中央闪现：" + hintText);
            }

            AppendLine("出处：07:18/:23/:28 先手 · 07:43 剩余回合 · 07:50-54 招式");
            AppendLine("　　　07:66-73 醉酒（含三句闪现提示）· 07:79-84 BOSS 三招与 6:3:1");
            return builder.ToString();
        }

        private void AppendLine(string line)
        {
            builder.Append(line).Append('\n');
            renderedLineCount++;
        }

        /// <summary>剩余回合：配置 0 = 不限（原文只说「显示剩余回合」，上限多少等 C91），照实显示，不编数。</summary>
        private string DescribeRoundsLeft()
        {
            int limit = settings.Flow.RoundLimit;
            if (limit <= 0)
            {
                return "不限（RoundLimit=0，等 C91）";
            }

            int left = limit - session.CompletedRounds;
            return left > 0 ? left.ToString() : "0（已到上限）";
        }

        /// <summary>招式现在能不能放：正式界面是「高亮 / 置暗」（07:40），白盒版写清楚能不能放与为什么。</summary>
        private string DescribeSkill(PlayerSkill skill)
        {
            if (session.Phase == BattlePhase.Ended)
            {
                return "✗ 战斗已结束";
            }

            if (session.Phase != BattlePhase.PlayerTurn)
            {
                return "✗ 不是玩家回合（07:48 招式只能在自己回合用）";
            }

            if (!session.Player.CanCast)
            {
                return "✗ 本回合不可行动（被打倒 / 晕眩）";
            }

            int cost = RageCost(skill);
            return session.Player.Rage >= cost ? "✓ 可施放" : "✗ 怒气不足";
        }

        private int RageCost(PlayerSkill skill) => PlayerSkillRules.RageCost(skill, settings.PlayerSkills);

        private static string DescribePhase(BattlePhase phase)
        {
            switch (phase)
            {
                case BattlePhase.PlayerTurn:
                    return "玩家回合";
                case BattlePhase.BossTurn:
                    return "BOSS 回合";
                case BattlePhase.Ended:
                    return "战斗结束";
                default:
                    return "未开始";
            }
        }

        /// <summary>样式与贴图只在第一次 OnGUI 时建（GUI.skin 只在 GUI 事件里有效，Awake 里读不到）。</summary>
        private void EnsureStyles()
        {
            if (backdrop != null)
            {
                return;
            }

            backdrop = MakeTexture(new Color(0f, 0f, 0f, 0.78f));
            hintBackdrop = MakeTexture(new Color(0.10f, 0.06f, 0f, 0.88f));

            panelStyle = new GUIStyle(GUI.skin.label);
            panelStyle.fontSize = PanelFontSize;
            panelStyle.alignment = TextAnchor.UpperLeft;
            panelStyle.wordWrap = false;
            panelStyle.richText = false;
            panelStyle.normal.textColor = new Color(0.95f, 0.96f, 1f, 0.96f);

            hintStyle = new GUIStyle(GUI.skin.label);
            hintStyle.fontSize = HintFontSize;
            hintStyle.fontStyle = FontStyle.Bold;
            hintStyle.alignment = TextAnchor.MiddleCenter;
            hintStyle.normal.textColor = new Color(1f, 0.86f, 0.42f);
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            if (backdrop != null)
            {
                Destroy(backdrop);
                backdrop = null;
            }

            if (hintBackdrop != null)
            {
                Destroy(hintBackdrop);
                hintBackdrop = null;
            }
        }
    }
}

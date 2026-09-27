// 职责：主线编排的纯函数——从前置关系推出主线顺序链、▲▼ 调序时重写主线前置、给支线生成「解锁时机」分组名。
// 为什么新建：运行时 QuestRules 只判定「能不能接」，不关心编排顺序；这些逻辑放进 IMGUI 窗口就没法单测，故从窗口拆出成静态类。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.Quest;

namespace Game.Editor.Quest
{
    public static class QuestMainChain
    {
        /// <summary>
        /// 只看主线；主线前置 = prerequisites 里同为主线的那些。
        /// 线性（每条 ≤1 个主线前置、每条被 ≤1 条主线当前置、唯一链首能走完全部主线）时返回 true 并按顺序填 chain；
        /// 否则返回 false，chain 为空，reason 为中文原因。没有主线时返回 true、chain 为空。
        /// </summary>
        public static bool TryBuildChain(IReadOnlyList<QuestDraft> drafts, List<QuestDraft> chain, out string reason)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            chain.Clear();
            reason = string.Empty;
            if (drafts == null) return true;

            var mains = new Dictionary<int, QuestDraft>();
            for (int i = 0; i < drafts.Count; i++)
            {
                QuestDraft draft = drafts[i];
                if (draft == null || draft.Kind != QuestKind.Main) continue;
                if (mains.ContainsKey(draft.Id))
                {
                    reason = $"主线编号 {Id(draft.Id)} 重复";
                    return false;
                }

                mains.Add(draft.Id, draft);
            }

            if (mains.Count == 0) return true;

            var successor = new Dictionary<int, int>();
            var heads = new List<int>();
            foreach (QuestDraft main in mains.Values.OrderBy(draft => draft.Id))
            {
                List<int> mainPrerequisites = MainPrerequisites(main, mains);
                if (mainPrerequisites.Count > 1)
                {
                    reason = $"主线 {Id(main.Id)} 有 {mainPrerequisites.Count} 条主线前置（{string.Join("、", mainPrerequisites.Select(Id))}）";
                    return false;
                }

                if (mainPrerequisites.Count == 0)
                {
                    heads.Add(main.Id);
                    continue;
                }

                int previous = mainPrerequisites[0];
                if (successor.TryGetValue(previous, out int other))
                {
                    reason = $"主线 {Id(previous)} 同时是 {Id(other)} 和 {Id(main.Id)} 的前置（分叉）";
                    return false;
                }

                successor.Add(previous, main.Id);
            }

            if (heads.Count == 0)
            {
                reason = "每条主线都有主线前置，找不到链首（前置成环）";
                return false;
            }

            if (heads.Count > 1)
            {
                reason = $"有 {heads.Count} 条主线没有主线前置（{string.Join("、", heads.Select(Id))}），不是一条链";
                return false;
            }

            var visited = new HashSet<int>();
            int current = heads[0];
            while (visited.Add(current))
            {
                chain.Add(mains[current]);
                if (!successor.TryGetValue(current, out current)) break;
            }

            if (chain.Count != mains.Count)
            {
                chain.Clear();
                reason = "有主线不在从链首出发的链上（前置成环或引用自己）";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 把 chain[index] 与 chain[index + delta] 交换，然后重写链上每条主线的前置 =
        /// 原有非主线前置（保序）+ 上一条主线编号（链首没有）。越界返回 false 且不改任何东西。
        /// </summary>
        public static bool Move(List<QuestDraft> chain, int index, int delta)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));

            int target = index + delta;
            if (delta == 0 || index < 0 || index >= chain.Count || target < 0 || target >= chain.Count) return false;

            QuestDraft moving = chain[index];
            chain[index] = chain[target];
            chain[target] = moving;

            var mainIds = new HashSet<int>(chain.Select(draft => draft.Id));
            for (int i = 0; i < chain.Count; i++)
            {
                QuestDraft draft = chain[i];
                var rewritten = new List<int>();
                if (draft.Prerequisites != null)
                {
                    for (int p = 0; p < draft.Prerequisites.Count; p++)
                    {
                        int prerequisite = draft.Prerequisites[p];
                        if (!mainIds.Contains(prerequisite)) rewritten.Add(prerequisite);
                    }
                }

                if (i > 0) rewritten.Add(chain[i - 1].Id);
                draft.Prerequisites = rewritten;
            }

            return true;
        }

        /// <summary>支线的解锁时机分组名：没有前置 →「开局」；否则「在 1001 找到落脚处 之后」，多条前置用「、」连接。</summary>
        public static string SideUnlockLabel(QuestDraft side, IReadOnlyList<QuestDraft> all)
        {
            if (side == null) throw new ArgumentNullException(nameof(side));
            if (side.Prerequisites == null || side.Prerequisites.Count == 0) return "开局";

            var parts = new List<string>(side.Prerequisites.Count);
            for (int i = 0; i < side.Prerequisites.Count; i++)
            {
                int id = side.Prerequisites[i];
                QuestDraft prerequisite = null;
                if (all != null)
                {
                    for (int a = 0; a < all.Count; a++)
                    {
                        if (all[a] != null && all[a].Id == id)
                        {
                            prerequisite = all[a];
                            break;
                        }
                    }
                }

                parts.Add(prerequisite == null || string.IsNullOrWhiteSpace(prerequisite.Title)
                    ? Id(id)
                    : $"{Id(id)} {prerequisite.Title}");
            }

            return $"在 {string.Join("、", parts)} 之后";
        }

        private static List<int> MainPrerequisites(QuestDraft draft, Dictionary<int, QuestDraft> mains)
        {
            var result = new List<int>();
            if (draft.Prerequisites == null) return result;

            for (int i = 0; i < draft.Prerequisites.Count; i++)
            {
                int id = draft.Prerequisites[i];
                if (mains.ContainsKey(id) && !result.Contains(id)) result.Add(id);
            }

            return result;
        }

        private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);
    }
}

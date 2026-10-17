// 职责：`BossProfile` 的可序列化镜像——BOSS 名册资产里配「这只 BOSS 分不分形态」用。
//
// 为什么单独一个类型：同 BossSkillData。转换在 `ToProfile()`。
// 空 = 单形态、用全局开战效果（与加多形态这一层之前逐字一致）。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Battle
{
    /// <summary>一个 BOSS 的多形态档案的 Inspector 形状。</summary>
    [Serializable]
    public sealed class BossProfileData
    {
        [Tooltip("按顺序排列的形态；第一个是开战时的形态。**空 = 不分形态**（阶段一那类 BOSS），行为与加这一层之前一致。")]
        [SerializeField] private BossPhaseData[] phases = Array.Empty<BossPhaseData>();

        [Tooltip("按 BOSS 覆盖「偷袭开战扣 BOSS 生命百分比」；**-1 = 用全局那份**。"
                 + "龙族写 0：sp07:90「龙族感知敏锐，玩家通过偷袭攻击进入对战的效果和正面战斗进入对战的效果相同」。")]
        [SerializeField] private int sneakHealthLossPercentOverride = -1;

        [Tooltip("形态切换时「怒气清空」清谁的怒气。原文没写（sp07:94 只说「怒气清空」，可通篇只有玩家有怒气），"
                 + "占位 None；转写把这条列成 C94。")]
        [SerializeField] private TurnBased.RageClearTarget rageClearOnPhaseChange = TurnBased.RageClearTarget.None;

        /// <summary>序列化用。</summary>
        public BossProfileData()
        {
        }

        /// <summary>形态条数（0 = 不分形态）。</summary>
        public int PhaseCount => phases != null ? phases.Length : 0;

        /// <summary>转成纯值档案。</summary>
        public TurnBased.BossProfile ToProfile()
        {
            if (phases == null || phases.Length == 0)
            {
                return default;
            }

            var list = new List<TurnBased.BossPhaseSettings>(phases.Length);
            for (int i = 0; i < phases.Length; i++)
            {
                if (phases[i] != null)
                {
                    list.Add(phases[i].ToPhase());
                }
            }

            return list.Count == 0
                ? default
                : new TurnBased.BossProfile(list, sneakHealthLossPercentOverride, rageClearOnPhaseChange);
        }
    }
}

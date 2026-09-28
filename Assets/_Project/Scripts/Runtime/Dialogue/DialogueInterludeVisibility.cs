// 职责：对白节点前插播演出期间藏起场景里的全部角色——找出场景中的小人（ChibiPuppet），每个取一个「角色根」并去重，
//   交给 PerformanceTriggerRules.HideVisuals 关掉根下全部 Renderer 与 Canvas（头顶名牌、NPC 标记、脚下光圈随根一起藏），
//   由 DialogueController 在插播结束时（完成 / 跳过 / 取消 / 异常同一个 finally）用 RestoreVisuals 按原值恢复。
//   取根与去重是纯静态方法，可直接喂手搭物体树做 EditMode 测试；场景查找只在插播开始时做一次，不进每帧路径。
// 取根规则：从小人自身向上找最近的、带 DialogueInteractable / DialogueInteractionActor / PerformanceTriggerActor 的物体；
//   都没有（如巡逻怪）取小人所在的场景顶层物体 transform.root。
//   2026-09-28 核对 SampleScene：六个小人（player / enerme / Npc_Elder / Npc_Traveler / Npc_Villager / Yao_WellWoman 下）
//   的 transform.root 都是角色自己的根，没有把角色收进公共容器，兜底不会连带藏地形或道具组。
//   以后若有场景把无标记角色收进公共容器（如 Monsters/xxx），兜底会把容器下的兄弟一起藏：那时给该类角色根挂标记或改兜底。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用：隐藏 / 恢复直接复用 Performance 的 HideVisuals / RestoreVisuals（成对、组件去重、只切 enabled 不 SetActive）；
//      但「谁算场景角色、取哪一层当根」要认 Dialogue 自己的两个组件，放进 Performance 就成了 Performance → Dialogue 反向依赖。
//   2. 扩展：DialogueController 是表现驱动（面板、立绘、打字），再塞场景扫描与取根规则会混职责，且单测取根得搭整套假服务；
//      DialogueSceneBinder 管的是场景加载时绑定 NPC，生命周期与「插播那一刻扫一次」不同。所以单独成一个静态类。
// 跨模块引用：Game.CharacterPuppet（ChibiPuppet，只当「这是个角色」的标记来查找，不调它任何方法）与 Game.Performance
//   （PerformanceTriggerActor、PerformanceTriggerRules）。三者同在 Game.Runtime 程序集；CharacterPuppet 不引用任何玩法模块、
//   Performance 不引用 Dialogue，所以 Dialogue → CharacterPuppet / Performance 都是单向的，不成环；没有 Runtime → Editor / Tests。
using System.Collections.Generic;
using Game.CharacterPuppet;
using Game.Performance;
using UnityEngine;

namespace Game.Dialogue
{
    /// <summary>对白插播期间场景角色的显隐：查找场景小人 → 取角色根 → 隐藏。全部静态。</summary>
    public static class DialogueInterludeVisibility
    {
        /// <summary>
        /// 查找场景里全部激活的小人（不含未激活的），取角色根去重后隐藏根下全部 Renderer 与 Canvas，返回快照
        /// （交给 <see cref="PerformanceTriggerRules.RestoreVisuals"/> 恢复）。要在拉起演出之前调：
        /// 演出舞台在 <c>IPerformanceService.PlayAsync</c> 内异步生成，此刻场景里的小人天然不含舞台替身。
        /// </summary>
        public static PerformanceTriggerRules.HiddenVisuals HideSceneCharacters()
        {
            ChibiPuppet[] puppets = Object.FindObjectsByType<ChibiPuppet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            return PerformanceTriggerRules.HideVisuals(CollectCharacterRoots(puppets));
        }

        /// <summary>
        /// 每个小人取 <see cref="ResolveCharacterRoot"/>，按首次出现的顺序去重（同一角色根下多个小人只算一次）；
        /// 入参为 null 或含 null / 已销毁项都安全（跳过）。
        /// </summary>
        public static List<GameObject> CollectCharacterRoots(IReadOnlyList<ChibiPuppet> puppets)
        {
            var roots = new List<GameObject>();
            if (puppets == null) return roots;
            for (int i = 0; i < puppets.Count; i++)
            {
                GameObject root = ResolveCharacterRoot(puppets[i]);
                if (root != null && !roots.Contains(root)) roots.Add(root);
            }
            return roots;
        }

        /// <summary>
        /// 小人的角色根：从小人自身向上，最近的带 <see cref="DialogueInteractable"/>、<see cref="DialogueInteractionActor"/>
        /// 或 <see cref="PerformanceTriggerActor"/> 的物体（NPC 根 / 玩家根）；都没有时取场景顶层物体 <c>transform.root</c>。
        /// 小人为 null / 已销毁返回 null。
        /// </summary>
        public static GameObject ResolveCharacterRoot(ChibiPuppet puppet)
        {
            if (puppet == null) return null;
            for (Transform node = puppet.transform; node != null; node = node.parent)
            {
                if (node.TryGetComponent<DialogueInteractable>(out _) || node.TryGetComponent<DialogueInteractionActor>(out _) ||
                    node.TryGetComponent<PerformanceTriggerActor>(out _))
                    return node.gameObject;
            }
            return puppet.transform.root.gameObject;
        }
    }
}

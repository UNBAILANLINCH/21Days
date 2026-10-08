// 职责：交互时的小人表现（PRP/interaction D11）——统一焦点刚触发一次交互（OnInteracted）后：
//   玩家小人转向目标（FaceTowards，带朝向保持）并播一次程序化交互动作（PlayInteractPulse）；
//   目标身上（自身或父子层级里）有小人时，让它转向玩家（NPC 转过来面对说话的人）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：CharacterPuppet 只是「看位移演动画」的皮，guide 写明「什么时候转向谁」由外部触发方决定，
//      它自己不该认识交互；没有现成的组件在交互那一刻驱动小人。
//   2. 扩展不行：塞进 InteractionFocus 会让焦点系统依赖小人表现，焦点的纯判定测试就得带上小人；
//      塞进 Dialogue / Loot / World 各自的实现方又回到「每个模块写一套」。
//   3. 所以在 Interaction 里新建一个只订阅 OnInteracted 的表现层入口点。只调 ChibiPuppet 的公开门面，不写 Animator 参数，
//      「同一小人只有一个驱动者写参数」的约束不变；CharacterPuppet 不反向认识 Interaction。
using System;
using Game.CharacterPuppet;
using Game.Core.Telemetry;
using UnityEngine;
using VContainer.Unity;

namespace Game.Interaction
{
    /// <summary>
    /// 交互转向表现。玩家小人从 <see cref="IInteractionRegistry.Actor"/> 所在层级里找（含子物体），目标小人在目标组件的自身 / 子物体 / 父链上找；
    /// 找不到就跳过，不报错（灰盒世界场景的玩家只有方块、物资箱与传送点没有小人）。
    /// <para>
    /// 朝向保持由小人自己在「真正移动」时解除：玩家走开就转回纸片朝向；NPC 不走动，会一直面向上次交互的玩家位置，直到场景重载。
    /// 只转表现层，逻辑朝向（隐藏纸片 flipX、PlayerModel / MonsterModel 的 Facing）不变，潜行 / 处决判定不受影响。
    /// </para>
    /// <para>
    /// 战斗舞台不受影响：BOSS 被交互后进的战斗场景（BattleArena）里，BattleActor 挂的是另一份小人实例
    /// （BOSS 按 StagePrefab 现实例化、玩家是战斗场景里摆好的），不是 SampleScene 里被转向的那一只。
    /// </para>
    /// </summary>
    public sealed class InteractionPuppetPresenter : IStartable, IDisposable
    {
        private readonly IInteractionFocus focus;
        private readonly IInteractionRegistry registry;
        private readonly ITelemetryScope telemetry;
        private bool started;
        private bool disposed;

        public InteractionPuppetPresenter(IInteractionFocus focus, IInteractionRegistry registry, ITelemetryScope telemetry = null)
        {
            this.focus = focus ?? throw new ArgumentNullException(nameof(focus));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        public void Start()
        {
            if (disposed || started) return;
            started = true;
            focus.OnInteracted += Present;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (started) focus.OnInteracted -= Present;
        }

        /// <summary>
        /// 对一次交互做小人表现。<see cref="Start"/> 后由 <see cref="IInteractionFocus.OnInteracted"/> 调；公开是为了测试可直接驱动。
        /// 只在交互那一刻跑（按键 / 点击），不在每帧路径上，所以按事件现找小人组件、不缓存。
        /// </summary>
        public void Present(IInteractable target)
        {
            if (disposed || target == null) return;
            // 目标若是已销毁的组件（伪空）就不演：它的位置已不可信。
            if (target is UnityEngine.Object unityTarget && unityTarget == null) return;

            InteractionActor actor = registry.Actor;
            ChibiPuppet playerPuppet = actor == null ? null : actor.GetComponentInChildren<ChibiPuppet>();
            ChibiPuppet targetPuppet = TargetPuppetOf(target);
            // 目标层级里找到的就是玩家自己那只（不该发生的摆法）：只当作没有目标小人，免得自己转向自己。
            if (targetPuppet != null && targetPuppet == playerPuppet) targetPuppet = null;

            if (playerPuppet != null)
            {
                playerPuppet.FaceTowards(target.Position);
                playerPuppet.PlayInteractPulse();
            }

            if (targetPuppet != null && actor != null)
            {
                targetPuppet.FaceTowards(actor.Anchor.position);
            }

            // 只在交互那一刻埋一次：小人没转时能分清是「没找到小人」还是「根本没触发」。
            telemetry.Track("puppets_turned", ("target", Describe(target)), ("player", playerPuppet != null), ("npc", targetPuppet != null));
        }

        /// <summary>目标的小人：先找自身与子物体，再找父链（可交互组件挂在角色根的子物体上时）；纯 C# 实现没有小人。</summary>
        private static ChibiPuppet TargetPuppetOf(IInteractable target)
        {
            if (!(target is Component component)) return null;
            ChibiPuppet puppet = component.GetComponentInChildren<ChibiPuppet>();
            if (puppet != null) return puppet;
            return component.GetComponentInParent<ChibiPuppet>();
        }

        private static string Describe(IInteractable target) =>
            target is Component component ? component.name : target.GetType().Name;
    }
}

// 职责：物资箱交互焦点——每帧在已登记的箱子里选离玩家最近、半径内且未开的一个为 Current，交互键（Gameplay/Interact，E / F / 手柄 A）时开箱；
//   对白焦点 / 对白进行中 / 世界暂停 / 沉浸模式时让位（Current = null、不响应）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：DialogueInteractionFocus 只认 DialogueInteractable 并驱动对白 HUD；泛化成 IInteractable 属 roadmap A3 正式版，本 PRP 不做。
//   2. 扩展不行：LootSceneBinder 是事件驱动的场景登记，塞进逐帧选择会让它变成 ITickable 并依赖输入与对白（同 Dialogue 的分法）。
using System;
using System.Collections.Generic;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using UnityEngine;
using VContainer.Unity;

namespace Game.Loot
{
    /// <summary>
    /// 物资箱焦点入口点。玩家锚点取 <see cref="DialogueSceneBinder.Actor"/>；没有玩家标记时无焦点。
    /// 提示文案显隐由探索 HUD 订阅 <see cref="OnFocusChanged"/> 自己做，本类不开面板。
    /// 开箱走交互键（Gameplay/Interact，E / F / 手柄 A），与 <see cref="Game.Dialogue.DialogueInteractionFocus"/> 同一路。
    /// </summary>
    public sealed class SupplyCrateFocus : ITickable, IDisposable
    {
        private readonly LootService service;
        private readonly LootSceneBinder binder;
        private readonly LootConfig config;
        private readonly DialogueSceneBinder dialogueBinder;
        private readonly DialogueInteractionFocus dialogueFocus;
        private readonly DialogueService dialogue;
        private readonly IWorldPauseService worldPause;
        private readonly IHudVisibility hudVisibility;
        private readonly IInputService input;
        private bool disposed;

        public SupplyCrateFocus(LootService service, LootSceneBinder binder, LootConfig config,
            DialogueSceneBinder dialogueBinder, DialogueInteractionFocus dialogueFocus, DialogueService dialogue,
            IWorldPauseService worldPause, IHudVisibility hudVisibility, IInputService input)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            // LootConfig 是 ScriptableObject，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.dialogueBinder = dialogueBinder ?? throw new ArgumentNullException(nameof(dialogueBinder));
            this.dialogueFocus = dialogueFocus ?? throw new ArgumentNullException(nameof(dialogueFocus));
            this.dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            this.worldPause = worldPause ?? throw new ArgumentNullException(nameof(worldPause));
            this.hudVisibility = hudVisibility ?? throw new ArgumentNullException(nameof(hudVisibility));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
        }

        /// <summary>当前焦点箱子；没有时为 null（用 == null 判）。</summary>
        public SupplyCrate Current { get; private set; }

        /// <summary>焦点变化（含变为 null）。只在变化时触发，不每帧触发。</summary>
        public event Action<SupplyCrate> OnFocusChanged;

        public void Tick()
        {
            if (disposed) return;

            SupplyCrate next = null;
            if (!ShouldYield())
            {
                DialogueInteractionActor actor = dialogueBinder.Actor;
                if (actor != null)
                {
                    next = SelectNearest(actor.Anchor.position, config.CrateInteractRadius, binder.Crates);
                }
            }

            // 用引用比较：旧焦点随场景销毁成伪空时，Unity 的 == 会把它和 null 判成相等，订阅者就收不到「焦点清空」。
            if (!ReferenceEquals(next, Current))
            {
                // 焦点本帧才变：不响应本帧的确认键，避免关对白的那一下确认顺手把刚进范围的箱子开掉。
                SetFocus(next);
                return;
            }

            // 开箱不属于确定性模拟（不进逻辑帧、不影响回放），同 DialogueInteractionFocus 读 Interact 动作。
            // 动作集可能晚于入口点就绪，每帧判空容错。
            if (Current != null && input.Actions != null && input.Actions.Gameplay.Interact.WasPressedThisFrame()) // lint-ok: 开箱不属于确定性模拟，同 DialogueInteractionFocus 读 Interact 动作
            {
                service.TryCollect(Current);
            }
        }

        /// <summary>
        /// 在候选里选离 <paramref name="origin"/> 最近、半径内、未开且激活的一个；没有返回 null。纯选择逻辑，无分配，供 Tick 与测试共用。
        /// </summary>
        public static SupplyCrate SelectNearest(Vector3 origin, float radius, IReadOnlyList<SupplyCrate> candidates)
        {
            if (candidates == null || radius <= 0f) return null;
            SupplyCrate best = null;
            float bestSqr = radius * radius;
            for (int i = 0; i < candidates.Count; i++)
            {
                SupplyCrate candidate = candidates[i];
                if (candidate == null || candidate.IsOpened || !candidate.isActiveAndEnabled) continue;
                float sqr = (candidate.Position - origin).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }
            return best;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Current = null;
            OnFocusChanged = null;
        }

        // 让位条件：对白焦点在、对白进行中、世界暂停、沉浸模式。
        private bool ShouldYield()
        {
            return dialogueFocus.Current != null || dialogue.IsRunning || worldPause.IsPaused || hudVisibility.IsHudHidden;
        }

        private void SetFocus(SupplyCrate next)
        {
            Current = next;
            OnFocusChanged?.Invoke(next);
        }
    }
}

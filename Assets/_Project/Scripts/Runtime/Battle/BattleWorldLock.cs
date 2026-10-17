// 职责：战斗期间把世界「按住」——世界暂停令牌、关 Gameplay 输入图、藏 HUD 层；收尾只恢复进来之前的状态。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：同样的三件事在 InventoryPanelController（暂停 + 输入图，:84-88）与 DialogueService（+ HUD 层，:131-158）里
//      各写了一份，都是各自会话的私有收尾，没有可调用的公共件。
//   2. 扩展不行：把战斗的收尾塞进它们任何一个都会让对方认识战斗。这里照它们的写法单写一份，
//      并抽成独立类，让「正常 / 异常 / 取消都恢复到进来前」能单测。
// 自动存档不在这里压：那一层走剧情的「战斗在途」登记（IBattleNarrative.TryBeginBattle → NarrativeService.CanSave = false），
//   Session 的现成闸门（NarrativeStable）就会挡住自动保存与离场保存，不用改 Session。
// 宿主销毁时的撤锁（2026-10-15，PRP/combat-iteration W6a 补修）：根作用域释放时 UI / 输入服务与 BattleFlow 一起销毁，
//   收场（异步，可能晚于它们的 Dispose）再调 SetLayerVisible 会抛 ObjectDisposedException。所以按住世界的令牌多一条
//   Engagement.Abandon：只交还暂停令牌、不还原 HUD 层与输入图（世界本身也在销毁，没有可还原的对象），之后的 Dispose 是空操作。
//   由 BattleFlow.Dispose 同步调用；正常 / 出错收场仍走 Dispose，逐项还原。
using System;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;

namespace Game.Battle
{
    /// <summary>战斗期间的世界锁（根作用域单例）。</summary>
    public sealed class BattleWorldLock
    {
        private readonly IWorldPauseService pause;
        private readonly IInputService input;
        private readonly IUIService ui;

        public BattleWorldLock(IWorldPauseService pause, IInputService input, IUIService ui)
        {
            this.pause = pause ?? throw new ArgumentNullException(nameof(pause));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        /// <summary>
        /// 按住世界：取暂停令牌（timeScale = 0、逻辑 tick 停）、关 Gameplay 图、藏 HUD 层。
        /// 返回的令牌 Dispose 即恢复，且只恢复进来之前的状态（进来前就关着的图 / 藏着的层不擅自打开）；Dispose 幂等。
        /// 宿主销毁时改调 <see cref="Engagement.Abandon"/>（只交还暂停令牌）。
        /// </summary>
        public Engagement Engage(object owner)
        {
            IDisposable pauseToken = pause.Acquire(owner);
            try
            {
                // Actions 为 null（输入服务没初始化 / EditMode 测试）时没有输入图可管，记录 / 禁用 / 恢复一并跳过。
                bool hasInput = input.Actions != null; // lint-ok: 只判动作集是否已创建，不读设备输入、不影响回放
                bool gameplayWasEnabled = hasInput && input.Actions.Gameplay.enabled; // lint-ok: 只读动作图启用状态用于收尾恢复，不读设备输入、不影响回放
                if (hasInput) input.DisableMap(InputService.GameplayMap);
                bool hudWasVisible = ui.IsLayerVisible(UILayer.Hud);
                ui.SetLayerVisible(UILayer.Hud, false);
                return new Engagement(this, pauseToken, gameplayWasEnabled, hudWasVisible);
            }
            catch
            {
                pauseToken.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 一次按住；Dispose 按「HUD → 输入图 → 暂停」逆序恢复，<see cref="Abandon"/> 只交还暂停。两者合起来只生效一次（先到先得）。
        /// </summary>
        public sealed class Engagement : IDisposable
        {
            private readonly BattleWorldLock owner;
            private readonly IDisposable pauseToken;
            private readonly bool gameplayWasEnabled;
            private readonly bool hudWasVisible;
            private bool released;

            internal Engagement(BattleWorldLock owner, IDisposable pauseToken, bool gameplayWasEnabled, bool hudWasVisible)
            {
                this.owner = owner;
                this.pauseToken = pauseToken;
                this.gameplayWasEnabled = gameplayWasEnabled;
                this.hudWasVisible = hudWasVisible;
            }

            public void Dispose()
            {
                if (released) return;
                released = true;
                try
                {
                    owner.ui.SetLayerVisible(UILayer.Hud, hudWasVisible);
                    if (gameplayWasEnabled) owner.input.EnableMap(InputService.GameplayMap);
                }
                finally
                {
                    pauseToken.Dispose();
                }
            }

            /// <summary>
            /// 宿主（根作用域）销毁时的撤锁：只交还暂停令牌，<b>不还原 HUD 层与输入图</b>——UI / 输入服务随同一作用域销毁
            /// （可能已先一步释放，再调会抛 ObjectDisposedException），世界本身也在销毁，没有可还原的对象。
            /// 交还暂停令牌在暂停服务已释放时是空操作（WorldPauseService.Token），没释放时恰好让 timeScale 回来。
            /// 之后的 <see cref="Dispose"/> 是空操作；已经还原过再调也是空操作。
            /// </summary>
            public void Abandon()
            {
                if (released) return;
                released = true;
                pauseToken.Dispose();
            }
        }
    }
}

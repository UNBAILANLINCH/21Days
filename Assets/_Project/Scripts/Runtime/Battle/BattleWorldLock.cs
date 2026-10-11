// 职责：战斗期间把世界「按住」——世界暂停令牌、关 Gameplay 输入图、藏 HUD 层；收尾只恢复进来之前的状态。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：同样的三件事在 InventoryPanelController（暂停 + 输入图，:84-88）与 DialogueService（+ HUD 层，:131-158）里
//      各写了一份，都是各自会话的私有收尾，没有可调用的公共件。
//   2. 扩展不行：把战斗的收尾塞进它们任何一个都会让对方认识战斗。这里照它们的写法单写一份，
//      并抽成独立类，让「正常 / 异常 / 取消都恢复到进来前」能单测。
// 自动存档不在这里压：那一层走剧情的「战斗在途」登记（IBattleNarrative.TryBeginBattle → NarrativeService.CanSave = false），
//   Session 的现成闸门（NarrativeStable）就会挡住自动保存与离场保存，不用改 Session。
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
        /// </summary>
        public IDisposable Engage(object owner)
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

        /// <summary>一次按住；Dispose 按「HUD → 输入图 → 暂停」逆序恢复。</summary>
        private sealed class Engagement : IDisposable
        {
            private readonly BattleWorldLock owner;
            private readonly IDisposable pauseToken;
            private readonly bool gameplayWasEnabled;
            private readonly bool hudWasVisible;
            private bool released;

            public Engagement(BattleWorldLock owner, IDisposable pauseToken, bool gameplayWasEnabled, bool hudWasVisible)
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
        }
    }
}

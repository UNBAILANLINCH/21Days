// 职责：标题路由的纯判定——新游戏落哪个槽、「继续」按钮是否显示。
// 为什么新建：SessionTitleRouter 依赖 MessagePipe 订阅与 UI 服务，EditMode 里不起容器测不了；
//   两条判定抽成静态纯函数单独可测，路由只负责「事件 → 判定 → 调 GameSession」。

using System.Collections.Generic;

namespace Game.Session
{
    /// <summary>标题路由的纯判定。</summary>
    public static class SessionTitleRules
    {
        /// <summary>新游戏用的槽：按列表顺序第一个 <see cref="SlotState.Empty"/> 槽；没有空槽（或列表为空）返回 0，表示要开选槽面板让玩家选覆盖哪个。</summary>
        public static int PickNewGameSlot(IReadOnlyList<SlotInfo> infos)
        {
            if (infos == null) return 0;
            for (int i = 0; i < infos.Count; i++)
            {
                if (infos[i].State == SlotState.Empty) return infos[i].Slot;
            }

            return 0;
        }

        /// <summary>「继续」是否显示：有最近可用槽（槽号从 1 起，0 = 没有可用存档，此时隐藏按钮）。</summary>
        public static bool ShouldShowContinue(int latestSlot) => latestSlot > 0;
    }
}

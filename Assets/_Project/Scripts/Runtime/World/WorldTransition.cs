// 职责：IWorldTransition 的唯一实现——挂一条待处理转场、取用时校验目标场景可加载并立刻清空。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：见 IWorldTransition 的类注释（工程里没有这个交接点）。
//   2. 扩展不行：不塞进 WorldCatalog——那是只读表适配层，挂一个可写字段会让「只读查询」这个契约失效，
//      而且表适配层要能在没有转场概念的地方复用（校验器、编辑器工具都在用）。
//   3. 所以单独一个纯状态类，只依赖 WorldCatalog（查表）与埋点门面。
using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Logging;
using Game.Core.Telemetry;

namespace Game.World
{
    /// <summary>
    /// 待处理转场（根作用域单例）。语义见 <see cref="IWorldTransition"/>。
    /// <para>
    /// 校验口径：**存在 + 可加载**（<see cref="WorldCatalog.CanLoadScene"/>：<c>implemented=true</c> 且地址非空）。
    /// 三种失败分开报：不在表里 / 未实装 / 实装但没写地址——它们的修法完全不同（加行 / 做场景 / 补地址）。
    /// </para>
    /// </summary>
    public sealed class WorldTransition : IWorldTransition
    {
        private readonly WorldCatalog catalog;
        private readonly ITelemetryScope telemetry;
        private WorldTransitionRequest pending;

        /// <param name="catalog">世界表只读查询（校验目标场景）。</param>
        /// <param name="telemetry">埋点门面；不传时用空实现（EditMode 与纯逻辑场景不必造埋点服务）。</param>
        public WorldTransition(WorldCatalog catalog, ITelemetryScope telemetry = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
        }

        /// <summary>被后到请求覆盖掉的次数（本进程累计）。连点传送点的现场证据，只读给调试与测试用。</summary>
        public int OverwrittenCount { get; private set; }

        public bool HasPending => pending != null;

        public WorldTransitionRequest Current => pending;

        public void Request(WorldTransitionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            WorldTransitionRequest previous = pending;
            if (previous != null)
            {
                // 后到覆盖先到：不抛。玩家在传送点上连点两下是最常见的触发方式，
                // 崩在这里等于把「多点了一下」变成事故。留一条 Warn 是为了回查「为什么落点不是我以为的那个」。
                OverwrittenCount++;
                Log.Warn($"转场请求被覆盖（第 {OverwrittenCount} 次）：{previous.Describe()} 已被 {request.Describe()} 顶掉。"
                         + "一次只挂一个待处理转场（PRP/world-scenes §2.1）。");
                telemetry.TrackWarn("transition_overwritten", TelemetryProps.Of(
                    ("previous", previous.SceneKey), ("next", request.SceneKey), ("count", OverwrittenCount)));
            }

            pending = request;
        }

        public WorldTransitionResolution TryConsume()
        {
            // 校验与 Peek 同一份（不多写第二遍规则：两处判定早晚会走偏）。
            WorldTransitionResolution resolution = Peek();

            // 清空与校验成败无关：失败的请求也不留在待处理位上（见接口注释里的理由）。
            pending = null;
            return resolution;
        }

        public WorldTransitionResolution Peek()
        {
            WorldTransitionRequest request = pending;

            if (request == null)
            {
                return WorldTransitionResolution.Rejected(null, WorldTransitionFailure.NoPending,
                    "没有待处理转场：进世界场景之前必须先写一条目标（IWorldTransition.Request），"
                    + "否则不知道要去哪张图、站哪个出生点。新开局那条路请先写默认场景再 GoToAsync<WorldSceneState>()。");
            }

            // 表没就绪与「表里没有这个场景」要分开报：前者是启动顺序问题，后者是数据问题。
            if (!catalog.IsReady)
            {
                return WorldTransitionResolution.Rejected(request, WorldTransitionFailure.TableNotReady,
                    $"配置表还没就绪，校验不了目标场景「{request.SceneKey}」（{request.Describe()}）。"
                    + "ConfigService 初始化完成之前不要进世界场景。");
            }

            if (!catalog.TryGetScene(request.SceneKey, out global::cfg.world.Scene scene))
            {
                return WorldTransitionResolution.Rejected(request, WorldTransitionFailure.SceneUnknown,
                    $"目标场景「{request.SceneKey}」不在 TbScene 里（{request.Describe()}）。表里现有：{JoinSceneKeys()}。"
                    + "表定义见 Tables/Defines/world.xml，数据在 Tables/Data/world/scene/ 下。");
            }

            if (!scene.Implemented)
            {
                return WorldTransitionResolution.Rejected(request, WorldTransitionFailure.SceneNotImplemented,
                    $"目标场景「{request.SceneKey}」在表里标着 implemented=false（还没实装），切不过去（{request.Describe()}）。"
                    + "先建场景、登记进 Addressables 的 Scenes 组，再把 Tables/Data/world/scene/ 里这一行的 "
                    + "implemented 改 true 并填上 scene_address。");
            }

            if (string.IsNullOrEmpty(scene.SceneAddress))
            {
                return WorldTransitionResolution.Rejected(request, WorldTransitionFailure.SceneAddressMissing,
                    $"目标场景「{request.SceneKey}」标了 implemented=true 却没写 scene_address（{request.Describe()}）。"
                    + "地址为空就没法加载，补 Tables/Data/world/scene/ 里这一行的 scene_address。");
            }

            return WorldTransitionResolution.Consumed(request);
        }

        public void Clear()
        {
            pending = null;
        }

        // 报错时把表里现有的场景键列出来：写错一个字母是最常见的情形，这一句能省一轮翻表。
        private string JoinSceneKeys()
        {
            IReadOnlyList<global::cfg.world.Scene> scenes = catalog.AllScenes;
            if (scenes == null || scenes.Count == 0)
            {
                return "一条都没有";
            }

            var text = new StringBuilder();
            for (int i = 0; i < scenes.Count; i++)
            {
                if (i > 0)
                {
                    text.Append('、');
                }

                text.Append(scenes[i].SceneKey);
            }

            return text.ToString();
        }
    }
}

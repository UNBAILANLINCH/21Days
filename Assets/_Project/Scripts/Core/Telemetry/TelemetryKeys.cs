// 职责：框架层的模块名、事件名与属性键常量，外加一份给玩法层看的命名规矩。
// 为什么新建：模块名 / 事件名是**跨工程跨脚本的关联键**（analyze.py 按键名做跨模块聚合），
//   散成字面量就必然出现 "core.ui" 与 "core.UI" 并存、拼错了还照样编译通过。
//   工程内没有同类常量表可复用；也不能塞进 TelemetryService.cs——那样每个埋点调用方都要引用实现类。

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点常量表（对应 docs/telemetry.md 第 2.1 节那张表）。
    /// <para>
    /// **玩法层不受这个类约束**，但要照同一套规矩起名，否则分析脚本关联不上：
    /// 模块名用模块目录的小写（<c>sample</c> <c>player</c> <c>inventory</c>），只能出现小写字母、数字、
    /// 下划线和点；事件名用 snake_case 且描述**已经发生的事实**（<c>buy_item</c> 而不是 <c>do_buy</c>）；
    /// 属性键优先复用 <see cref="Props"/> 里已有的——同一个语义全工程一个键名，新键随便加，改名不行。
    /// </para>
    /// </summary>
    public static class TelemetryKeys
    {
        /// <summary>会话级事件所属的「模块」，也是框架层模块名的公共前缀。</summary>
        public const string Core = "core";

        /// <summary>启动流程：GameBootstrap 逐个初始化服务。</summary>
        public const string Boot = "core.boot";

        /// <summary>状态流：GameFlow 的状态进出。</summary>
        public const string Flow = "core.flow";

        /// <summary>UI：面板开关与栈深度。</summary>
        public const string Ui = "core.ui";

        /// <summary>资源：Addressables 加载、场景切换、释放。</summary>
        public const string Asset = "core.asset";

        /// <summary>存档：写盘、读档、损坏、迁移。</summary>
        public const string Save = "core.save";

        /// <summary>音频：BGM 切换与音效被拒。</summary>
        public const string Audio = "core.audio";

        /// <summary>确定性内核：固定步长推进，目前只埋追帧超上限丢 tick。</summary>
        public const string Sim = "core.sim";

        /// <summary>性能：周期采样与帧尖峰。</summary>
        public const string Perf = "core.perf";

        /// <summary>日志桥：Unity 侧的报错与未捕获异常。</summary>
        public const string Log = "core.log";

        /// <summary>会话级事件名。</summary>
        public static class CoreEvents
        {
            /// <summary>会话头，每次启动的第一条，序号固定 0。</summary>
            public const string SessionStart = "session_start";

            /// <summary>会话尾。没有这一条就说明这次运行是崩了或被截断。</summary>
            public const string SessionEnd = "session_end";

            /// <summary>限流补报：上一秒被丢掉了多少条（属性 <c>n</c>）。</summary>
            public const string Throttled = "throttled";
        }

        /// <summary>启动流程事件名。</summary>
        public static class BootEvents
        {
            /// <summary>一个服务初始化完成，属性 <c>name</c> <c>ms</c>。</summary>
            public const string Step = "step";

            /// <summary>全部服务就绪。</summary>
            public const string Ready = "ready";

            /// <summary>启动中止。</summary>
            public const string Failed = "failed";
        }

        /// <summary>状态流事件名。</summary>
        public static class FlowEvents
        {
            /// <summary>进入状态，属性 <c>from</c> <c>to</c> <c>ms</c>。</summary>
            public const string StateEnter = "state_enter";

            /// <summary>离开状态。</summary>
            public const string StateExit = "state_exit";

            /// <summary>状态切换失败。</summary>
            public const string StateFailed = "state_failed";
        }

        /// <summary>资源事件名。</summary>
        public static class AssetEvents
        {
            /// <summary>资源加载完成，属性 <c>key</c> <c>ms</c>。</summary>
            public const string Load = "load";

            /// <summary>资源加载失败，带 <c>err</c> / <c>st</c>。</summary>
            public const string LoadFailed = "load_failed";

            /// <summary>场景加载完成。</summary>
            public const string SceneLoad = "scene_load";

            /// <summary>句柄释放。</summary>
            public const string Release = "release";
        }

        /// <summary>UI 事件名。</summary>
        public static class UiEvents
        {
            /// <summary>面板打开，属性 <c>panel</c> <c>ms</c> <c>depth</c>。</summary>
            public const string Open = "open";

            /// <summary>面板关闭。</summary>
            public const string Close = "close";

            /// <summary>整层显隐切换（沉浸模式 / 过场），属性 <c>layer</c> <c>visible</c>。只在状态真的变了时记。</summary>
            public const string LayerVisible = "layer_visible";
        }

        /// <summary>存档事件名。</summary>
        public static class SaveEvents
        {
            /// <summary>写盘完成，属性 <c>slot</c> <c>ms</c> <c>bytes</c>。</summary>
            public const string Write = "write";

            /// <summary>读档完成。</summary>
            public const string Load = "load";

            /// <summary>存档损坏，已按空档继续。</summary>
            public const string Corrupt = "corrupt";

            /// <summary>分区版本迁移。</summary>
            public const string Migrate = "migrate";
        }

        /// <summary>音频事件名。</summary>
        public static class AudioEvents
        {
            /// <summary>BGM 切换，属性 <c>key</c>。</summary>
            public const string Bgm = "bgm";

            /// <summary>音效被拒（声部用满或资源缺失）。</summary>
            public const string SfxDenied = "sfx_denied";
        }

        /// <summary>性能事件名。</summary>
        public static class PerfEvents
        {
            /// <summary>周期采样，属性 <c>fps</c> <c>frame_ms</c> <c>gc_mb</c> <c>mem_mb</c>。</summary>
            public const string Sample = "sample";

            /// <summary>帧尖峰：单帧耗时超过阈值。</summary>
            public const string Spike = "spike";
        }

        /// <summary>日志桥事件名。</summary>
        public static class LogEvents
        {
            /// <summary>Unity 侧的 Error / Exception / Assert，根因分析的主线索。</summary>
            public const string UnityError = "unity_error";
        }

        /// <summary>约定俗成的属性键。同一个语义全工程用同一个键名，脚本按键名做跨模块关联。</summary>
        public static class Props
        {
            /// <summary>耗时毫秒。</summary>
            public const string Ms = "ms";

            /// <summary>资源 / 配置地址。</summary>
            public const string Key = "key";

            /// <summary>业务 id。</summary>
            public const string Id = "id";

            /// <summary>转移的起点。</summary>
            public const string From = "from";

            /// <summary>转移的终点。</summary>
            public const string To = "to";

            /// <summary>数量。</summary>
            public const string N = "n";

            /// <summary>布尔结果。</summary>
            public const string Ok = "ok";

            /// <summary>名字（服务名、面板名这类）。</summary>
            public const string Name = "name";

            /// <summary>面板名（<c>core.ui</c> 的 open / close 用）。</summary>
            public const string Panel = "panel";

            /// <summary>层数：<c>core.ui</c> 里是这条事件发生之后还开着几个面板。</summary>
            public const string Depth = "depth";

            /// <summary>UI 层名（<c>core.ui</c> 的 layer_visible 用，取值 Hud / Panel / Popup / Top）。</summary>
            public const string Layer = "layer";

            /// <summary>显隐结果（<c>core.ui</c> 的 layer_visible 用）。</summary>
            public const string Visible = "visible";

            /// <summary>存档槽位号。</summary>
            public const string Slot = "slot";

            /// <summary>字节数（写盘大小这类）。</summary>
            public const string Bytes = "bytes";

            /// <summary>
            /// 失败原因的短代码（snake_case，取值有限，例如 <c>not_initialized</c> / <c>superseded</c>）。
            /// 同一个事件名下有多条失败分支时靠它分开聚合——把原因写进事件名会让事件名爆炸。
            /// </summary>
            public const string Reason = "reason";

            /// <summary>每秒帧数。</summary>
            public const string Fps = "fps";

            /// <summary>单帧耗时毫秒。</summary>
            public const string FrameMs = "frame_ms";

            /// <summary>托管堆占用 MB。</summary>
            public const string GcMb = "gc_mb";

            /// <summary>进程总分配 MB。</summary>
            public const string MemMb = "mem_mb";

            /// <summary>错误条数（会话尾用）。</summary>
            public const string Errs = "errs";
        }
    }
}

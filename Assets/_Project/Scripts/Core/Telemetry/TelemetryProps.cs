// 职责：一条埋点的属性表——固定 4 个槽位的值类型容器，零堆分配。
// 为什么新建：Dictionary 与 params 数组每次调用都分配（params 哪怕传 0 个参数也会拿到一个空数组实例），
//   而埋点会出现在意图入口、状态迁移这些每秒十几次的路径上。工程内没有同类定长容器可复用；
//   也不能扩展进 PropValue.cs：那里是「一个值」，这里是「一组键值对」，槽位数是这个文件独有的取舍。

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 埋点属性表。**定长 4 槽**，键是字符串常量、值是 <see cref="PropValue"/>，整体是值类型，
    /// 从调用点传到格式化器全程走栈，不产生任何堆对象。
    /// <para>
    /// 为什么是 4 个而不是「想加多少加多少」：<c>params</c> 数组每次调用都分配，Dictionary 更贵；
    /// 定长就必须定一个长度，4 个覆盖了契约里约定俗成的键（<c>ms</c> <c>key</c> <c>id</c> <c>n</c> <c>ok</c>）
    /// 的绝大多数组合。**属性超过 4 个说明这一条事件混了两件事，拆成两条事件**——
    /// 这样脚本做列式聚合时每一列的语义也更干净。会话头那种字段固定又超长的行由服务内部直接拼 JSON，
    /// 不走这个容器（见 TelemetryEvent.RawProps）。
    /// </para>
    /// </summary>
    public readonly struct TelemetryProps
    {
        /// <summary>槽位数。调用方要判断「还塞不塞得下」时读它，别写死 4。</summary>
        public const int Capacity = 4;

        private readonly string key0;
        private readonly string key1;
        private readonly string key2;
        private readonly string key3;
        private readonly PropValue value0;
        private readonly PropValue value1;
        private readonly PropValue value2;
        private readonly PropValue value3;
        private readonly int count;

        private TelemetryProps(
            int count,
            string key0, PropValue value0,
            string key1, PropValue value1,
            string key2, PropValue value2,
            string key3, PropValue value3)
        {
            this.count = count;
            this.key0 = key0;
            this.key1 = key1;
            this.key2 = key2;
            this.key3 = key3;
            this.value0 = value0;
            this.value1 = value1;
            this.value2 = value2;
            this.value3 = value3;
        }

        /// <summary>已占用的槽位数，0～<see cref="Capacity"/>。</summary>
        public int Count => count;

        /// <summary>空属性表。等价于 <c>default</c>。</summary>
        public static TelemetryProps Empty => default;

        /// <summary>一个属性。</summary>
        public static TelemetryProps Of((string Key, PropValue Value) p0)
            => new TelemetryProps(1, p0.Key, p0.Value, null, default, null, default, null, default);

        /// <summary>两个属性。</summary>
        public static TelemetryProps Of((string Key, PropValue Value) p0, (string Key, PropValue Value) p1)
            => new TelemetryProps(2, p0.Key, p0.Value, p1.Key, p1.Value, null, default, null, default);

        /// <summary>三个属性。</summary>
        public static TelemetryProps Of(
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2)
            => new TelemetryProps(3, p0.Key, p0.Value, p1.Key, p1.Value, p2.Key, p2.Value, null, default);

        /// <summary>四个属性（槽位上限）。</summary>
        public static TelemetryProps Of(
            (string Key, PropValue Value) p0,
            (string Key, PropValue Value) p1,
            (string Key, PropValue Value) p2,
            (string Key, PropValue Value) p3)
            => new TelemetryProps(4, p0.Key, p0.Value, p1.Key, p1.Value, p2.Key, p2.Value, p3.Key, p3.Value);

        /// <summary>取第 index 个键；越界返回 null。</summary>
        public string KeyAt(int index)
        {
            switch (index)
            {
                case 0: return key0;
                case 1: return key1;
                case 2: return key2;
                case 3: return key3;
                default: return null;
            }
        }

        /// <summary>取第 index 个值；越界返回空值（<see cref="PropKind.None"/>）。</summary>
        public PropValue ValueAt(int index)
        {
            switch (index)
            {
                case 0: return value0;
                case 1: return value1;
                case 2: return value2;
                case 3: return value3;
                default: return default;
            }
        }
    }
}

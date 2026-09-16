// 职责：一个埋点属性的值——不装箱的判别联合，承载 整数 / 浮点 / 字符串 / 布尔 四类 JSON 值。
// 为什么新建：埋点跑在游戏主循环里，属性容器不能用 Dictionary<string, object>——每塞一个 int / bool
//   都要装箱，一次 Track 就是几次堆分配，GC 尖峰最后会反过来污染性能埋点本身。C# 没有内置判别联合，
//   只能自己写一个 readonly struct；工程内没有同类值类型可复用（AssetHandle / TimerHandle 是句柄，不是值）。
//   也没有塞进 TelemetryProps.cs：那个文件管「固定槽位容器」，值怎么表示是另一件事，混在一起两边都难改。

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 一个属性值。**不装箱**：整数、浮点、布尔共用一个 <c>double</c> 字段，字符串单独一个引用字段，
    /// 具体读哪个由 <see cref="Kind"/> 决定。
    /// <para>
    /// 对 <c>int</c> / <c>long</c> / <c>float</c> / <c>double</c> / <c>string</c> / <c>bool</c> 提供隐式转换，
    /// 所以调用点写 <c>Track("buy_item", ("id", 1001), ("n", 3))</c> 就够了——元组是
    /// <c>(string, PropValue)</c>，隐式转换在编译期生效，运行期一个堆对象都不产生。
    /// </para>
    /// <para>
    /// 精度取舍：整数也存在 double 里，超过 2^53（约 9.0e15）会丢精度。业务 id、数量、毫秒数都远在这个
    /// 量级之下；真要埋满 64 位的哈希值，先自己转成十六进制字符串再埋。
    /// </para>
    /// <para>
    /// 枚举值请转成 <c>int</c> 或用常量字符串——<c>ToString()</c> 每次都分配一个字符串。
    /// </para>
    /// </summary>
    public readonly struct PropValue
    {
        private readonly double number;
        private readonly string text;
        private readonly PropKind kind;

        private PropValue(PropKind kind, double number, string text)
        {
            this.kind = kind;
            this.number = number;
            this.text = text;
        }

        /// <summary>当前装的是哪一类值。<see cref="PropKind.None"/> 表示空槽位。</summary>
        public PropKind Kind => kind;

        /// <summary>浮点值。只有 <see cref="PropKind.Float"/> 时有意义。</summary>
        public double Number => number;

        /// <summary>整数值。只有 <see cref="PropKind.Integer"/> 时有意义。</summary>
        public long Integer => (long)number;

        /// <summary>布尔值。只有 <see cref="PropKind.Bool"/> 时有意义。</summary>
        public bool Bool => number != 0d;

        /// <summary>字符串值。只有 <see cref="PropKind.String"/> 时有意义，可能为 null。</summary>
        public string Text => text;

        public static implicit operator PropValue(int value) => new PropValue(PropKind.Integer, value, null);

        public static implicit operator PropValue(long value) => new PropValue(PropKind.Integer, value, null);

        public static implicit operator PropValue(float value) => new PropValue(PropKind.Float, value, null);

        public static implicit operator PropValue(double value) => new PropValue(PropKind.Float, value, null);

        public static implicit operator PropValue(bool value) => new PropValue(PropKind.Bool, value ? 1d : 0d, null);

        public static implicit operator PropValue(string value) => new PropValue(PropKind.String, 0d, value);
    }
}

// 职责：标记一个 PropValue 当前装的是哪一类值（判别联合的「判别子」）。
// 为什么新建：PropValue 是 readonly struct，判别子必须是它的一个字段，而 C# 的枚举只能是顶层或嵌套类型；
// 嵌套进 PropValue 会和它的 Kind 属性重名，所以单独成文件。工程内没有同类枚举可复用。

namespace Game.Core.Telemetry
{
    /// <summary>
    /// 属性值的类别。契约规定 <c>p</c> 里的值只能是 number / string / bool（不放数组、不放嵌套对象），
    /// 这里把 number 再拆成整数与浮点两类——输出时整数不带小数点，脚本做列式聚合更省事。
    /// </summary>
    public enum PropKind
    {
        /// <summary>空槽位。格式化时整个键值对被跳过。</summary>
        None = 0,

        /// <summary>整数（int / long）。输出形如 <c>1001</c>。</summary>
        Integer = 1,

        /// <summary>浮点（float / double）。输出最多三位小数，形如 <c>16.7</c>。</summary>
        Float = 2,

        /// <summary>字符串。输出时按 JSON 规则转义。</summary>
        String = 3,

        /// <summary>布尔。输出 <c>true</c> / <c>false</c>。</summary>
        Bool = 4,
    }
}

// 职责：把玩法用到的数学运算（三角函数、插值、钳制、取整、Vector2 运算）统一转发到
// UnityEngine.Mathf / UnityEngine.Vector2，玩法代码只准调用本类，不直接写 Mathf.Xxx()
// 或 vector.magnitude 之类的写法（配套 lint 规则会拦 Runtime/ 下直接用 Mathf. 的代码）。
//
// 为什么要有这层「看起来多余」的转发（不要因为它现在只是转发就把它当无用层删掉）：
// 我们在做「输入录制 + 逻辑重放」的 bug 复现系统。将来如果要支持跨设备重放
//（Android 录、PC 放），浮点运算在不同平台/硬件上的舍入差异会导致重放结果漂移，
// 届时可能要把玩法数值整体换成定点数。GameMath 就是那次升级唯一的改动点：
// 玩法代码只准调它，到时候改这一个文件、把内部实现换成定点运算即可，而不必
// 满工程 grep Mathf. 逐处替换。它今天的价值是零，明天的价值是省掉一次全工程重构——
// 所以现在就要让玩法代码养成只调它的习惯。
//
// 加能力的顺序（复用 → 扩展 → 新建）：
// - 复用：工程内目前没有任何数学封装层，没有可以直接拿来用的既有类。
// - 扩展：Mathf 是 UnityEngine 提供的静态类，C# 静态类不能被继承，塞不进已有的类；
//   也不该把数学转发硬塞进别的职责类（比如 IClock、TimerService）——数学运算和
//   时间、生命周期管理是两回事，硬塞会让那些类的职责名不副实。
// - 新建：以上两条都不成立，所以新建本文件，一个纯静态转发层。
//
// 本期只做纯转发，不改变任何数值行为：不加精度处理、不加定点数、不加缓存。
// 这些留到「真的要支持跨设备重放」那一天，到时候只改这一个文件。

using System.Runtime.CompilerServices;
using UnityEngine;

namespace Game.Core.Simulation
{
    /// <summary>
    /// 数学运算薄封装层：玩法代码只准调用本类，不直接用 Mathf / Vector2 的运算方法。
    /// 本期纯转发，行为与 Mathf / Vector2 完全一致；将来要支持跨设备重放时，
    /// 定点数改造只改这一个文件，玩法代码不用动。
    /// </summary>
    public static class GameMath
    {
        /// <summary>平方根，转发 <see cref="Mathf.Sqrt(float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sqrt(float value) => Mathf.Sqrt(value);

        /// <summary>正弦（参数为弧度），转发 <see cref="Mathf.Sin(float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sin(float radians) => Mathf.Sin(radians);

        /// <summary>余弦（参数为弧度），转发 <see cref="Mathf.Cos(float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cos(float radians) => Mathf.Cos(radians);

        /// <summary>反正切（返回弧度），转发 <see cref="Mathf.Atan2(float, float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Atan2(float y, float x) => Mathf.Atan2(y, x);

        /// <summary>线性插值（t 会被钳制到 [0, 1]），转发 <see cref="Mathf.Lerp(float, float, float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float a, float b, float t) => Mathf.Lerp(a, b, t);

        /// <summary>绝对值，转发 <see cref="Mathf.Abs(float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float value) => Mathf.Abs(value);

        /// <summary>绝对值（整数重载），转发 <see cref="Mathf.Abs(int)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Abs(int value) => Mathf.Abs(value);

        /// <summary>取较小值，转发 <see cref="Mathf.Min(float, float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float a, float b) => Mathf.Min(a, b);

        /// <summary>取较小值（整数重载），转发 <see cref="Mathf.Min(int, int)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Min(int a, int b) => Mathf.Min(a, b);

        /// <summary>取较大值，转发 <see cref="Mathf.Max(float, float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float a, float b) => Mathf.Max(a, b);

        /// <summary>取较大值（整数重载），转发 <see cref="Mathf.Max(int, int)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Max(int a, int b) => Mathf.Max(a, b);

        /// <summary>钳制到 [min, max]，转发 <see cref="Mathf.Clamp(float, float, float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float value, float min, float max) => Mathf.Clamp(value, min, max);

        /// <summary>钳制到 [min, max]（整数重载），转发 <see cref="Mathf.Clamp(int, int, int)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int min, int max) => Mathf.Clamp(value, min, max);

        /// <summary>钳制到 [0, 1]，转发 <see cref="Mathf.Clamp01(float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp01(float value) => Mathf.Clamp01(value);

        /// <summary>
        /// 符号，转发 <see cref="Mathf.Sign(float)"/>。
        /// 注意 Unity 的实现里 0 会返回 1（不是 0），这是 Mathf 本来的行为，本类不改。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sign(float value) => Mathf.Sign(value);

        /// <summary>向下取整，转发 <see cref="Mathf.Floor(float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Floor(float value) => Mathf.Floor(value);

        /// <summary>向上取整，转发 <see cref="Mathf.Ceil(float)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Ceil(float value) => Mathf.Ceil(value);

        /// <summary>
        /// 四舍五入，转发 <see cref="Mathf.Round(float)"/>。
        /// 注意 Mathf.Round 用的是银行家舍入（.5 舍到最近的偶数），这是 Mathf 本来的行为，本类不改。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Round(float value) => Mathf.Round(value);

        /// <summary>归一化后的向量副本（不修改入参，零向量返回零向量），转发 <see cref="Vector2.normalized"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Normalize(Vector2 value) => value.normalized;

        /// <summary>两点距离，转发 <see cref="Vector2.Distance(Vector2, Vector2)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

        /// <summary>点积，转发 <see cref="Vector2.Dot(Vector2, Vector2)"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vector2 a, Vector2 b) => Vector2.Dot(a, b);

        /// <summary>向量长度，转发 <see cref="Vector2.magnitude"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Magnitude(Vector2 value) => value.magnitude;

        /// <summary>向量长度的平方（免开方，只比较大小时优先用这个），转发 <see cref="Vector2.sqrMagnitude"/>。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrMagnitude(Vector2 value) => value.sqrMagnitude;
    }
}

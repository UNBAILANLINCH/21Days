// 职责：测试用的确定性随机源——按剧本吐数，不多不少。
//
// 为什么要它：概率规则的边界（「掷出 19 会跳过、掷出 20 不会」）必须能精确构造，
// 用真随机源只能统计近似。它同时把「多抽了一个随机数」变成硬失败——序列用光就抛异常，
// 这样「正常态不该消耗随机数」这类约定才验得出来。
//
// 取值口径与 `Game.Core.Simulation.XorShiftRandomStream` 完全一致（乘法缩放），
// 否则测试里构造的「掷出 19」到了真实现里不是同一个数。

using System;
using System.Collections.Generic;
using Game.Core.Simulation;

namespace Game.Tests.EditMode.TurnBased
{
    /// <summary>按剧本吐数的 IRandomStream（测试专用）。</summary>
    public sealed class FixedRandomStream : IRandomStream
    {
        private readonly Queue<uint> scripted;

        /// <summary>用一串「原始 32 位输出」构造，按顺序消耗。</summary>
        public FixedRandomStream(params uint[] values)
        {
            scripted = new Queue<uint>(values ?? Array.Empty<uint>());
        }

        /// <summary>已经吐完（用来断言「没有多抽」）。</summary>
        public bool IsDrained => scripted.Count == 0;

        /// <summary>剩余个数。</summary>
        public int Remaining => scripted.Count;

        /// <summary>状态：本实现不支持存取（测试里不需要）。</summary>
        public ulong State
        {
            get => 0UL;
            set => throw new NotSupportedException("FixedRandomStream 不支持状态存取");
        }

        /// <summary>调用方不该直接用：脚本用光就抛，暴露「多抽了」。</summary>
        public uint NextUInt()
        {
            if (scripted.Count == 0)
            {
                throw new InvalidOperationException("剧本里的随机数已经用完：这段逻辑多抽了一个数");
            }

            return scripted.Dequeue();
        }

        /// <summary>与 XorShiftRandomStream.Range 同口径的乘法缩放。</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range 要求 minInclusive < maxExclusive");
            }

            uint width = (uint)((long)maxExclusive - minInclusive);
            ulong scaled = (ulong)NextUInt() * width;
            return (int)(minInclusive + (long)(scaled >> 32));
        }

        /// <summary>[0,1) 浮点，粒度 1/2^24（与 XorShiftRandomStream.Value01 同口径）。</summary>
        public float Value01() => (NextUInt() >> 8) * (1.0f / 16777216.0f);

        /// <summary>
        /// 构造一段剧本，使 <c>Range(0, 100)</c> 依次掷出给定的点数（每个点数消耗一个随机数）。
        /// </summary>
        public static FixedRandomStream ForRolls(params int[] rolls)
        {
            if (rolls == null)
            {
                throw new ArgumentNullException(nameof(rolls));
            }

            var values = new List<uint>(rolls.Length);
            foreach (int roll in rolls)
            {
                if (roll < 0 || roll > 99)
                {
                    throw new ArgumentOutOfRangeException(nameof(rolls), "点数必须落在 0..99（Range(0,100) 的取值域）");
                }

                // 找第一个使 floor(v * 100 / 2^32) == roll 的 v。起点取精确商的下取整，几步内必然命中。
                uint candidate = (uint)((ulong)roll * 4294967296UL / 100UL);
                while (((ulong)candidate * 100UL >> 32) != (ulong)roll)
                {
                    candidate++;
                }

                values.Add(candidate);
            }

            return new FixedRandomStream(values.ToArray());
        }
    }
}

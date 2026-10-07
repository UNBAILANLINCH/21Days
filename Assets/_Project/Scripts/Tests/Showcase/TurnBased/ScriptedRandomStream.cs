// 职责：回放专用的确定性随机源——按事先写好的剧本逐次吐数，多抽一个就抛异常。
//
// 为什么回放要自带随机源：TurnBased 的一切概率（醉酒四档「跳过 / 不跳过」、BOSS 六三一选招）
//   都只走注入的 IRandomStream（`turnbased-module-guide.md`「随机数与回放」）。
//   换成 UnityEngine.Random 或真随机的种子，同一档位这一轮跳过、下一轮不跳过，检查点就成了掷骰子；
//   剧本流的办法是把「这一次掷出几点」写死：微醺这一次掷 19（<20 → 跳过），下一次掷 20（≥20 → 不跳过），
//   于是概率的两侧都能**确定性地**演给人看。**没有种子**——它不是伪随机序列，是逐次指定的点数。
//
// 为什么在 Showcase 里另写一份、不直接用 EditMode 的 `FixedRandomStream`：
//   那个类型在 `Game.Tests.EditMode.TurnBased` 程序集里，Showcase 程序集引用不到（也不该引用测试程序集）；
//   本文件按**同一取值口径**（乘法缩放，与 `Game.Core.Simulation.XorShiftRandomStream` 一致）重写一份，
//   保证「回放里掷出的点数」与真实现里同一个数的含义相同。**没有改那个文件**（它属于另一波）。
//
// 与 IRandomStream 的契约（`Assets/_Project/Scripts/Core/Simulation/IRandomStream.cs`，只读引用 Core）：
//   NextUInt 是唯一的原始出口，Range / Value01 由它派生；每次 Range 恰好消耗一个 NextUInt；
//   State 要能存取——剧本流不支持存取（回放不需要快照，用了就抛，别静默给假状态）。

using System;
using System.Collections.Generic;
using Game.Core.Simulation;

namespace Game.Tests.Showcase.TurnBased
{
    /// <summary>按剧本吐数的随机源（回放专用）。用法：<c>new ScriptedRandomStream().Roll(100, 19)</c>。</summary>
    public sealed class ScriptedRandomStream : IRandomStream
    {
        private readonly List<uint> scripted = new List<uint>();
        private int cursor;

        /// <summary>剧本里还剩几个数没吐（用来断言「这一段逻辑一个随机数都没抽」）。</summary>
        public int Remaining => scripted.Count - cursor;

        /// <summary>一共抽了几次（含已吐完的），用来钉「100% 跳过不消耗随机数」这条约定。</summary>
        public int DrawCount { get; private set; }

        /// <summary>剧本已经吐完。</summary>
        public bool IsDrained => Remaining == 0;

        /// <summary>
        /// 追加一段剧本：下一次 <c>Range(0, width)</c> 恰好掷出 <paramref name="roll"/>（含下界）。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">点数不在 [0, width) 内。</exception>
        public ScriptedRandomStream Roll(int width, int roll)
        {
            scripted.Add(RawFor(width, roll));
            return this;
        }

        /// <summary>
        /// 求一个原始 32 位输出，使 <c>Range(0, width)</c> 的乘法缩放结果正好是 <paramref name="roll"/>。
        /// 与 `XorShiftRandomStream.Range` / `FixedRandomStream.Range` 同一算式：<c>(v * width) &gt;&gt; 32</c>。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">width 非正，或点数越界。</exception>
        public static uint RawFor(int width, int roll)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "宽度必须为正");
            }

            if (roll < 0 || roll >= width)
            {
                throw new ArgumentOutOfRangeException(nameof(roll), "点数必须落在 0.." + (width - 1) + " 内");
            }

            // 从精确商的下取整起步，几步之内必然命中（区间宽度是 2^32 / width >> 1）。
            uint candidate = (uint)((ulong)roll * 4294967296UL / (ulong)width);
            while ((ulong)candidate * (ulong)width >> 32 != (ulong)roll)
            {
                candidate++;
            }

            return candidate;
        }

        /// <summary>取下一个原始输出；剧本用光就抛——「多抽了一个」必须是硬失败，不能静默补数。</summary>
        /// <exception cref="InvalidOperationException">剧本已用完。</exception>
        public uint NextUInt()
        {
            if (cursor >= scripted.Count)
            {
                throw new InvalidOperationException(
                    "剧本里的随机数已经用完（这是第 " + (cursor + 1) + " 次抽取）：这段逻辑多抽了一个数，"
                    + "概率序列会从这里开始漂移");
            }

            DrawCount++;
            return scripted[cursor++];
        }

        /// <summary>[minInclusive, maxExclusive) 上的整数，每次调用恰好消耗一个原始输出。</summary>
        /// <exception cref="ArgumentOutOfRangeException">区间非法。</exception>
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

        /// <summary>[0,1) 浮点，粒度 1/2^24（与 XorShiftRandomStream.Value01 同口径）。回放里用不到，实现保持一致。</summary>
        public float Value01() => (NextUInt() >> 8) * (1.0f / 16777216.0f);

        /// <summary>剧本流的状态不是单个整数，不支持存取（回放不需要快照）。</summary>
        /// <exception cref="NotSupportedException">取值或赋值都抛。</exception>
        public ulong State
        {
            get => throw new NotSupportedException("ScriptedRandomStream 不支持状态存取（剧本流不是单个整数的状态机）");
            set => throw new NotSupportedException("ScriptedRandomStream 不支持状态存取（剧本流不是单个整数的状态机）");
        }
    }
}

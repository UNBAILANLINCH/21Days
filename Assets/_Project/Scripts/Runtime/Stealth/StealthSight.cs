// 职责：把一组遮挡体套在「两点之间通不通视」这件事上；几何求交全在 `StealthGeometry`。
// 输入只有几何（起点、终点）与遮挡体列表，不碰场景 API、不做物理查询、不读时间与随机数。
//
// 为什么新建：Monster 的感知是 `MonsterRules.Sense` 里「距离 + 夹角」的数学判定，
// 没有任何遮挡概念（`ai-docs/docs/modules/monster/monster-module-guide.md` 第 54 行写明
// 「感知是位置和朝向的数学判定，没有 Physics 查询或遮挡物判断」）。遮挡体是场景几何，
// 塞进 `MonsterRules` 会让规则类认识关卡数据，也让并行任务改 `MonsterConfig` 时冲突。
//
// 出处：`docs/design/features-spotlight/03_潜行与暗杀.md:129-136`（R24–R28）、
// `04_追逐.md:104`（R23 摆脱条件之一「躲进掩体」的判定基础）。
using System;
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>
    /// 视线遮挡查询器。持有一份遮挡体列表（按场景换一份即可），对任意两点做通视判定。
    /// <para>
    /// 用法：关卡加载后把掩体一次性喂进来（<see cref="SetOccluders"/>），
    /// 怪物感知 / 玩家潜行判定每 tick 调 <see cref="TrySight"/>。
    /// </para>
    /// </summary>
    public sealed class StealthSight
    {
        private StealthOccluder[] occluders = Array.Empty<StealthOccluder>();
        private int count;

        /// <summary>当前遮挡体数量。</summary>
        public int Count => count;

        /// <summary>
        /// 换一份遮挡体列表（关卡加载 / 场景切换时调）。传 null 或空数组等于清空。
        /// 内部拷贝一份，调用方之后改自己的数组不影响这里。
        /// </summary>
        public void SetOccluders(StealthOccluder[] source)
        {
            if (source == null || source.Length == 0)
            {
                occluders = Array.Empty<StealthOccluder>();
                count = 0;
                return;
            }

            var copy = new StealthOccluder[source.Length];
            int valid = 0;
            for (int i = 0; i < source.Length; i++)
            {
                if (!source[i].IsValid)
                {
                    throw new ArgumentException($"第 {i} 个遮挡体不是合法形状（是不是用了 default？）", nameof(source));
                }

                copy[valid] = source[i];
                valid++;
            }

            occluders = copy;
            count = valid;
        }

        /// <summary>清空遮挡体（离开关卡时调，避免上一关的掩体挡住下一关）。</summary>
        public void Clear() => SetOccluders(null);

        /// <summary>取第 <paramref name="index"/> 个遮挡体；越界抛异常。</summary>
        public StealthOccluder GetOccluder(int index)
        {
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return occluders[index];
        }

        /// <summary>
        /// 两点之间通不通视。零长度线段（同一位置）永远通视——站在掩体里不动不算「被自己挡住」，
        /// 而且线段退化成点时「线段是否穿过矩形」没有定义，不能靠几何层的退化分支兜底。
        /// </summary>
        public SightResult TrySight(Vector2 from, Vector2 to)
        {
            if (GameMath.SqrMagnitude(to - from) <= 0f)
            {
                return new SightResult(true, null, 0);
            }

            if (count == 0)
            {
                return new SightResult(true, null, 0);
            }

            int[] hits = null;
            int hitCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (!StealthGeometry.SegmentIntersectsOccluder(from, to, occluders[i]))
                {
                    continue;
                }

                hits = hits ?? new int[count];
                hits[hitCount] = occluders[i].Id;
                hitCount++;
            }

            return new SightResult(hitCount == 0, hits, hitCount);
        }

        /// <summary>
        /// 零分配版本：把造成遮挡的遮挡体 id 依次写进 <paramref name="blockers"/>（先 Clear），
        /// 返回是否通视。每帧路径上用这个，别用 <see cref="TrySight"/>。
        /// </summary>
        public bool TrySightNonAlloc(Vector2 from, Vector2 to, System.Collections.Generic.List<int> blockers)
        {
            if (blockers == null)
            {
                throw new ArgumentNullException(nameof(blockers));
            }

            blockers.Clear();
            for (int i = 0; i < count; i++)
            {
                if (StealthGeometry.SegmentIntersectsOccluder(from, to, occluders[i]))
                {
                    blockers.Add(occluders[i].Id);
                }
            }

            return blockers.Count == 0;
        }

        /// <summary>是否被任一遮挡体挡住（<see cref="TrySight"/> 的简写）。</summary>
        public bool IsBlocked(Vector2 from, Vector2 to) => !TrySight(from, to).HasSight;

        /// <summary>两点之间是否通视（<see cref="TrySight"/> 的简写）。</summary>
        public bool HasSight(Vector2 from, Vector2 to) => TrySight(from, to).HasSight;
    }
}

// 职责：身份定义表——按 id 查定义，接线期注册、运行期只读。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有「按 id 查内容」的通用表可复用；Quest 的表是 Luban 生成物（不许手改），
//      Narrative 的表是 NodeCatalog 形态（节点、不是身份）。
//   2. 扩展不行：不能把查找塞进 `IdentityState`——状态是逐 tick 变的东西，内容是启动期固定的东西，
//      混在一起后「状态进存档」会连带把定义也序列化一遍。

using System;
using System.Collections.Generic;

namespace Game.Identity
{
    /// <summary>
    /// 身份定义表。查找一律 <see cref="StringComparer.Ordinal"/>（id 大小写敏感，见 <see cref="IdentityId"/>）。
    /// <para>
    /// <b>无效 id 的明确行为</b>：<see cref="IdentityId.None"/>（本体）与格式不合法的 id 都<b>查不到</b>、
    /// <see cref="TryGet"/> 返回 false；<see cref="Require"/> 抛异常（只给接线期与测试用）。
    /// 规则层借身份走 <see cref="IdentityRules.TryEnter"/>，未知 id 得到
    /// <see cref="IdentityEnterResult.UnknownIdentity"/>，不抛异常、不改状态。
    /// </para>
    /// </summary>
    public sealed class IdentityCatalog
    {
        private readonly Dictionary<IdentityId, IdentityDefinition> byId =
            new Dictionary<IdentityId, IdentityDefinition>();

        private readonly List<IdentityDefinition> ordered = new List<IdentityDefinition>();

        /// <summary>表里有多少条定义。</summary>
        public int Count => ordered.Count;

        /// <summary>按注册顺序排列的定义（只读；顺序固定，便于内容对比与测试断言）。</summary>
        public IReadOnlyList<IdentityDefinition> Definitions => ordered;

        /// <summary>
        /// 注册一条定义。id 为空或格式不合法、重复注册同一 id 都会抛异常——
        /// 内容表写错是接线期问题，越早炸越便宜。
        /// </summary>
        public void Register(IdentityDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            IdentityId id = definition.Id;
            if (!id.IsValid)
            {
                throw new ArgumentException("身份定义的 id 不能为空。", nameof(definition));
            }

            if (byId.ContainsKey(id))
            {
                throw new ArgumentException($"身份 id 重复注册：{id.Value}。", nameof(definition));
            }

            // id 会拼成 `identity.<id>`，撞上固定键名（identity.borrowed / skin / mask / …）就会
            // 出现「同一个键两处写入」，字典 §3.2 的唯一性要求禁止这种情况，所以在注册处直接拒收。
            if (IdentityFacts.IsReservedName(id.Value))
            {
                throw new ArgumentException(
                    $"身份 id \"{id.Value}\" 与固定剧情事实键撞名（identity.{id.Value}）。改名，"
                    + "不要动 identity.borrowed / skin / mask / memory / exposed / exposedCount / suspicion / ledger / dead 这几个登记键。",
                    nameof(definition));
            }

            byId.Add(id, definition);
            ordered.Add(definition);
        }

        /// <summary>按 id 查定义。本体与格式不合法的 id 返回 false（不抛异常）。</summary>
        public bool TryGet(IdentityId id, out IdentityDefinition definition)
        {
            if (!id.IsValid)
            {
                definition = null;
                return false;
            }

            return byId.TryGetValue(id, out definition);
        }

        /// <summary>按 id 取定义，查不到抛异常。用于接线期与测试，不用于每帧路径。</summary>
        public IdentityDefinition Require(IdentityId id)
        {
            if (!TryGet(id, out IdentityDefinition definition))
            {
                throw new KeyNotFoundException(
                    $"身份定义表里没有 \"{id.Value}\"。要么内容没配，要么 id 拼错了（大小写敏感）。");
            }

            return definition;
        }

        /// <summary>清空重来（只在构建期用）。</summary>
        public void Clear()
        {
            byId.Clear();
            ordered.Clear();
        }

        /// <summary>由一批定义建表（空数组得到空表，合法：此时任何身份都借不到）。</summary>
        public static IdentityCatalog From(IEnumerable<IdentityDefinition> definitions)
        {
            var catalog = new IdentityCatalog();
            if (definitions == null)
            {
                return catalog;
            }

            foreach (IdentityDefinition definition in definitions)
            {
                if (definition == null)
                {
                    continue;
                }

                catalog.Register(definition);
            }

            return catalog;
        }
    }
}

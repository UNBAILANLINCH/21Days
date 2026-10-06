// 职责：一次转场请求的内容——去哪（目标场景键 + 目标出生点 id）、怎么到（到达方式）、谁触发的（传送点 id）。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：PortalAnchor 只有场景侧的两个字段，没有「从哪个传送点来」这一维（回查与埋点要用它）；
//      WorldSpawnTarget 是**解析之后**的落点，到达方式与来源在那一步已经被消化掉了。
//   2. 扩展不行：给 WorldSpawnTarget 加「来源」会让解析结果承担请求的语义——解析结果会被回退逻辑改写
//      （指名的出生点不可用 → 回退到默认），而请求描述的是「玩家想怎么走」，两者不是一回事。
//   3. 所以请求单独一份不可变纯数据，在传送点（场景侧）与世界状态之间传递。
using System;

namespace Game.World
{
    /// <summary>
    /// 一次待处理的转场请求（不可变）。由触发方构造（接线后是 <see cref="PortalAnchor"/> 的持有者），
    /// 交给 <see cref="IWorldTransition.Request"/> 挂起，由 <see cref="WorldSceneState"/> 取用。
    /// </summary>
    public sealed class WorldTransitionRequest
    {
        /// <param name="sceneKey">目标场景键（<c>TbScene.scene_key</c>）。</param>
        /// <param name="spawnId">目标出生点 id；空串/null = 由 <see cref="WorldRules"/> 回退到该场景的默认出生点。</param>
        /// <param name="arrivalMethod">到达方式原文（<c>TbPortal.arrival_method</c>）；空 = <see cref="WorldRules.ArrivalDefault"/>。</param>
        /// <param name="portalId">触发来源（<c>TbPortal.portal_id</c>）；空 = 不是经由传送点来的（新开局、读档等）。</param>
        /// <exception cref="ArgumentException">目标场景键为空——那等于「没接线」，不该悄悄传送到某处。</exception>
        public WorldTransitionRequest(string sceneKey, string spawnId = null, string arrivalMethod = null, string portalId = null)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                throw new ArgumentException(
                    "目标场景键不能为空：没填目标就是没接线（同 PortalAnchor.CanTrigger 的口径），"
                    + "空目标要去哪张图没有依据。表见 Tables/Defines/world.xml 的 TbScene。", nameof(sceneKey));
            }

            SceneKey = sceneKey;
            SpawnId = spawnId ?? string.Empty;
            ArrivalMethod = string.IsNullOrEmpty(arrivalMethod) ? WorldRules.ArrivalDefault : arrivalMethod;
            PortalId = portalId ?? string.Empty;
        }

        /// <summary>目标场景键（<c>TbScene.scene_key</c>）。</summary>
        public string SceneKey { get; }

        /// <summary>目标出生点 id；空串 = 用目标场景的默认出生点。</summary>
        public string SpawnId { get; }

        /// <summary>到达方式（<c>TbPortal.arrival_method</c>）；不会是空串，没写就是 <see cref="WorldRules.ArrivalDefault"/>。</summary>
        public string ArrivalMethod { get; }

        /// <summary>触发来源的传送点 id；空串 = 不是经由传送点来的。</summary>
        public string PortalId { get; }

        /// <summary>是否经由传送点来（<see cref="PortalId"/> 非空）。</summary>
        public bool FromPortal => !string.IsNullOrEmpty(PortalId);

        /// <summary>一句话描述，给日志与失败原因用（「从哪来、去哪、站哪」）。</summary>
        public string Describe()
        {
            string spawn = string.IsNullOrEmpty(SpawnId) ? "默认出生点" : SpawnId;
            string from = FromPortal ? $"传送点「{PortalId}」" : "非传送点来源";
            return $"{from} → 场景「{SceneKey}」的出生点「{spawn}」（到达方式「{ArrivalMethod}」）";
        }
    }
}

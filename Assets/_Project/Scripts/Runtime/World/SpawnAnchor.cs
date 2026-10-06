// 职责：出生点锚点——挂在场景里的空物体上，只带一个出生点 id，供 WorldSpawnPlacement 按 id 找到落点。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有任何「按 id 查落点」的场景组件。EncounterSceneView.playerSpawn 是遭遇场景专用的**单个**
//      起点（不是按 id 查的），DialogueInteractionActor 是对话测距用的玩家标记（不带出生点 id），
//      SupplyCrate 带 id 但语义是「物资箱」不是「落点」。
//   2. 扩展不行：往 WorldRules / WorldCatalog 里加场景引用，会让纯规则与表适配层认识 UnityEngine 的场景对象。
//   3. 所以单独一个只带 id 的组件。
//
// **id 放在组件字段里，不靠物体名**：物体名会在层级里被随手改（多人协作下尤其常见），改名之后按名字查找会**静默找不到**
//   （表现是「玩家站在上一张图的落点上」而不是报错）。组件字段漏填 / 填错在 Inspector 上看得见，
//   而且 WorldSpawnPlacement 找不到锚点时会**点名是哪个 id、场景里现有的锚点有哪些**。
//
// 命名约定（本波定，写进 `ai-docs/docs/modules/world/world-module-guide.md`）：
//   物体名 `Spawn_<spawnId>`，组件字段 `spawnId` 是唯一真源；`<spawnId>` 必须逐字等于 `TbScene.spawn_points` 里的某一项。
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// 出生点锚点。一个场景里按 <c>TbScene.spawn_points</c> 逐个摆，id 与表里那一项逐字相同。
    /// <para>
    /// 锚点是**纯数据**：不带任何逻辑、不订阅事件、不读输入。摆人由 <see cref="WorldSpawnPlacement"/> 做。
    /// </para>
    /// </summary>
    [AddComponentMenu("21Days/World/SpawnAnchor")]
    public sealed class SpawnAnchor : MonoBehaviour
    {
        [Tooltip("出生点 id，必须与 TbScene.spawn_points 里的某一项逐字相同（大小写敏感）。"
                 + "物体名建议写成 Spawn_<spawnId>，但真源是本字段，不是物体名。")]
        [SerializeField] private string spawnId = string.Empty;

        /// <summary>出生点 id（<c>TbScene.spawn_points</c> 里的原文）。</summary>
        public string SpawnId => spawnId;

        /// <summary>
        /// 锚点的世界坐标。逻辑平面是 **XZ**（与 SampleScene 的等距场景同一套约定）：
        /// 玩家逻辑坐标 = (世界 x, 世界 z)，见 <c>EncounterSceneView.ToLogicPosition</c>。
        /// </summary>
        public Vector3 WorldPosition => transform.position;
    }
}

// 职责：出生点放置策略的**真实现**——按出生点 id 找到场景里的 SpawnAnchor，把玩家摆过去（逻辑位置 + 场景根物体）、
//   把相机对准，并让相机继续跟着这个根物体。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：UnwiredSpawnPlacement **故意**只返回 false（机制波的默认实现，防「找不到锚点却假装摆成功」），
//      它不做任何摆放，不能当实现用。
//   2. 扩展不行：给 UnwiredSpawnPlacement 加摆放能力，就等于把「未接线」这个语义抹掉——那条「一定 false + 说清缺什么」
//      的行为是接线前的安全网，改掉它会让「接线漏了」再也报不出来。
//   3. 所以新建一个真实现，在 WorldInstaller 里把那一行注册换掉（换实现即可，WorldSceneState 与它的测试一行不改）。
//
// 为什么构造只收 Func<PlayerRules>：本类在容器构建期被 new 出来，而 PlayerRules 由 PlayerInstaller 注册
//   （WorldInstaller 的装配测试只装框架服务，没有 Player 模块）。构造期不 Resolve，装配 WorldInstaller 就不依赖别的玩法模块；
//   第一次真正摆放时再取（同 GameFlow 持有 resolver 的思路，但这里只暴露「我需要的这一个」）。
using System;
using Game.Core.Logging;
using Game.IsometricExploration;
using Game.Player;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// 世界场景的出生点放置策略。判定顺序：有没有落点 → 有没有世界场景 → 找不找得到锚点 → 相机与玩家根物体齐不齐。
    /// <b>任何一步不成立都返回 false 并点名缺什么</b>，绝不「摆了一半返回成功」。
    /// <para>
    /// 逻辑平面固定 **XZ**：玩家逻辑坐标 = (锚点世界 x, 锚点世界 z)，与 <c>EncounterSceneView.ToLogicPosition</c> 同一套约定。
    /// </para>
    /// </summary>
    public sealed class WorldSpawnPlacement : ISpawnPlacement
    {
        private readonly WorldSceneBinder binder;
        private readonly Func<PlayerRules> playerRules;

        /// <param name="binder">世界场景登记器（锚点 / 相机 / 玩家根物体都从它读）。</param>
        /// <param name="playerRules">玩家规则工厂：摆放时要走 <see cref="PlayerRules.Reset"/> 挪**逻辑**位置
        /// （不能只挪场景根物体，否则下一帧投影会把根物体拉回旧逻辑坐标）。</param>
        public WorldSpawnPlacement(WorldSceneBinder binder, Func<PlayerRules> playerRules)
        {
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            this.playerRules = playerRules ?? throw new ArgumentNullException(nameof(playerRules));
        }

        /// <inheritdoc />
        public bool TryPlace(WorldSpawnTarget spawn, WorldTransitionRequest request, out string reason)
        {
            if (spawn == null)
            {
                reason = "没有落点：WorldRules 没解析出出生点，没地方摆人。";
                return false;
            }

            if (!binder.HasWorldScene)
            {
                reason = $"当前没有可用的世界场景，摆不了「{spawn.SceneKey}::{spawn.SpawnId}」。"
                         + "世界场景的判据是「场景里至少有一个 SpawnAnchor」，并且要有挂着 SmoothCameraFollow"
                         + "、设了 Target（玩家根物体）的相机；场景没加载成、或锚点没摆进场景，都会走到这一支。";
                Log.Warn($"出生点放置失败：{reason}");
                return false;
            }

            if (!binder.TryGetSpawn(spawn.SpawnId, out SpawnAnchor anchor) || anchor == null)
            {
                reason = $"场景「{spawn.SceneKey}」里找不到出生点「{spawn.SpawnId}」的锚点，"
                         + $"现有锚点是：{binder.DescribeSpawns()}。"
                         + "约定：物体名 Spawn_<spawnId>，且物体上的 SpawnAnchor.spawnId 逐字等于表里 TbScene.spawn_points 的那一项"
                         + "（真源是组件字段，不是物体名；见 ai-docs/docs/modules/world/world-module-guide.md）。";
                Log.Warn($"出生点放置失败：{reason}");
                return false;
            }

            Transform playerRoot = binder.PlayerRoot;
            if (playerRoot == null)
            {
                reason = $"场景「{spawn.SceneKey}」里有相机但相机没设 Target，找不到玩家根物体，摆不了人"
                         + "（相机跟随目标就是玩家根物体，把玩家根物体拖到相机的 Target 上）。";
                Log.Warn($"出生点放置失败：{reason}");
                return false;
            }

            Vector3 position = anchor.WorldPosition;

            // ① 逻辑位置先摆：玩家在确定性内核里的坐标是 (x, z)。漏了这一步，下一帧场景投影会把根物体拉回旧坐标。
            PlayerRules rules = playerRules();
            rules.Reset(new Vector2(position.x, position.z));

            // ② 场景根物体摆到锚点上。
            playerRoot.position = position;

            // ③ 相机对准：目标换成这个根物体，立刻移到「根物体 + 场景里摆好的构图偏移」，
            //    不让 SmoothCameraFollow 在黑幕底下自己补间横穿整张图（那会让揭幕后的第一帧构图是错的）。
            SmoothCameraFollow camera = binder.Camera;
            if (camera != null)
            {
                camera.SetTarget(playerRoot);
                camera.SetOffset(binder.CameraOffset);
                camera.transform.position = camera.ResolveDesiredPosition();
            }

            reason = string.Empty;
            return true;
        }
    }
}

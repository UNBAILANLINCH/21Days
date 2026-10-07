// 职责：解析出来的落点——目标场景键与真正要用的出生点 id。
// 为什么新建：一个文件一个类（csharp-code.md「一个文件一个类，文件名等于类名」）；
//   出生点选择的结果要在调用方之间传递（谁真正把玩家摆到场上谁就用它），所以不能塞成私有嵌套类型。
namespace Game.World
{
    /// <summary>出生点选择的成功结果。</summary>
    public sealed class WorldSpawnTarget
    {
        public WorldSpawnTarget(string sceneKey, string spawnId, bool usedFallback)
        {
            SceneKey = sceneKey;
            SpawnId = spawnId;
            UsedFallback = usedFallback;
        }

        /// <summary>目标场景键（TbScene.scene_key）。</summary>
        public string SceneKey { get; }

        /// <summary>真正要用的出生点 id：场景里摆一个同名锚点，把玩家摆到那里。</summary>
        public string SpawnId { get; }

        /// <summary>是否走了回退：true = 指名的出生点不可用，用的是本场景的默认出生点。</summary>
        public bool UsedFallback { get; }
    }
}

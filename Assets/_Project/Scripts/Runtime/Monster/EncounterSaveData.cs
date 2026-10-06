// 职责：战斗持久快照与待消费结果；不复用会随回放版本作废的二进制布局。
using System;
using Game.Core.Save;
using Game.Player;

namespace Game.Monster
{
    public sealed class EncounterSaveData : ISaveData
    {
        public int Version => 1;
        public string SceneKey { get; set; } = "IsometricEncounter";
        public long Tick { get; set; }
        public bool Active { get; set; }
        public long EncounterId { get; set; }
        public long ActivationId { get; set; }
        public EncounterStep.Result Result { get; set; }
        public bool ResultConsumed { get; set; }
        public PlayerSaveData Player { get; set; }
        public MonsterSaveData Monster { get; set; }
        public void Migrate(int fromVersion) { }
        public void Validate()
        {
            // 场景键的判据（PRP/world-scenes §2.2：去写死）：**非空 + 格式合法**，不再是「必须等于某个字面量」。
            //   - 旧档里的 "IsometricEncounter" 照样通过：那是遗留原型路径的地址（MonsterEncounterState.cs:46
            //     仍在写常量，本波禁改，两条并存），它不是 snake_case —— 所以格式判据**不能**要求命名风格；
            //   - 「地址在不在 TbScene / Addressables 里」**不在这里判**：本方法必须是纯数据方法
            //     （不许依赖 IConfigService）。那两件事分别归世界表校验（WorldCatalogValidator）与调用方
            //     （世界场景入口：WorldTransition.TryConsume 取用待处理转场时按 WorldCatalog 校验）；
            //   - 分区 Version 不升：字段没改、Migrate 没有迁移动作，改的只是「合法值域」这一层判据。
            if (!IsValidSceneKey(SceneKey) || Tick < 0 || EncounterId < 0 || ActivationId < 0 ||
                (EncounterId == 0) != (ActivationId == 0) || Player == null || Monster == null ||
                !Enum.IsDefined(typeof(EncounterStep.Result), Result) ||
                (Result != EncounterStep.Result.None && EncounterId == 0) ||
                (ResultConsumed && Result == EncounterStep.Result.None))
                throw new ArgumentException("遭遇快照身份或结果非法");
            Player.Validate();
            Monster.Validate();
            if (Result == EncounterStep.Result.Victory && (Monster.Health != 0 || Player.Health == 0))
                throw new ArgumentException("胜利结果与生命不符");
            if (Result == EncounterStep.Result.Defeat && Player.Health != 0)
                throw new ArgumentException("失败结果与生命不符");
        }

        /// <summary>
        /// 场景键的**格式**判据（纯数据，不查表）：
        /// ① 非空；② 不含任何空白（Addressables 地址与 <c>TbScene.scene_key</c> 都不含空白，出现了就是配错）；
        /// ③ 不含场景作用域分隔符 <c>::</c>（<c>Game.World.SceneStateKey.Separator</c> 用「场景键::实体标识」拼键，
        ///    场景键里再带一次分隔符会拼出两义键）。
        /// <para>
        /// 都通过之后这个键仍可能指向一张不存在的图 —— 那是**表校验（WorldCatalogValidator）与调用方**的事，
        /// 本类不认识表（理由见 <see cref="Validate"/> 的注释）。
        /// </para>
        /// </summary>
        private static bool IsValidSceneKey(string sceneKey)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                return false;
            }

            for (int i = 0; i < sceneKey.Length; i++)
            {
                if (char.IsWhiteSpace(sceneKey[i]))
                {
                    return false;
                }
            }

            return sceneKey.IndexOf("::", StringComparison.Ordinal) < 0;
        }
    }
}

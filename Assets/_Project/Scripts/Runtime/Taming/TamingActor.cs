// 职责：场景实例的稳定标识、显示名和既有巡逻/纸片接线，不负责推进玩法。
// 为什么新建：现有角色仅靠视图引用；模型与小人组件没有可序列化的实例身份，不能用名字或 InstanceID 替代。
// 一场遭遇多种怪（PRP/combat-iteration D2，2026-10-14）：加两个作者数据字段 kindId / fullParticipant。
//   扩展而不新建：它们就是「场景里这一只巡逻者是什么种类、算不算完整参与者」的作者数据，与稳定标识同一归属。
//   ⚠️ **两个字段的代码默认值必须等于旧行为**（ai-docs/pitfalls.md:243）：SampleScene 的巡逻者乙（Patrol_B，
//   协作者的线）两个字段都不填，靠默认值保持「沿用主怪种类、只巡逻 / 感知 / 攻击」逐字不变。改默认值 = 改它的行为。
using System;
using Game.Monster;
using TMPro;
using UnityEngine;

namespace Game.Taming
{
    [DisallowMultipleComponent]
    public sealed class TamingActor : MonoBehaviour
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private SpriteRenderer stateIndicator;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Transform[] patrolPoints;

        [Tooltip("这只巡逻者的种类 Id（monster_species 表主键）。0 = 沿用主怪的种类（旧行为，巡逻者乙靠它）。"
            + "首位巡逻者（主怪）忽略本字段，它的种类由 GameBootstrap 上 MonsterInstaller 的 Kind Id 决定")]
        [SerializeField, Min(0)] private int kindId;

        [Tooltip("勾选 = 完整参与者：可被背后处决、死亡掉落（后续波次还有露馅、被抓、升级回合制）。"
            + "不勾 = 只巡逻 / 感知 / 攻击的附加巡逻者（旧行为，巡逻者乙靠它）。首位巡逻者（主怪）恒为完整参与者，忽略本字段。"
            + "整场遭遇的胜负只认主怪，附加怪死亡不结算整场")]
        [SerializeField] private bool fullParticipant;

        public string StableId => stableId;
        public string DisplayName => displayName;

        /// <summary>种类覆盖；<c>0</c> = 沿用主怪种类（默认，旧行为）。首位巡逻者忽略它。</summary>
        public int KindId => kindId;

        /// <summary>是否完整参与者；默认 <c>false</c>（旧行为：只巡逻 / 感知 / 攻击）。首位巡逻者忽略它（恒为真）。</summary>
        public bool IsFullParticipant => fullParticipant;
        public SpriteRenderer Visual => visual;
        public SpriteRenderer StateIndicator => stateIndicator;
        public event Action<TamingActor, bool> OnAvailabilityChanged;

        // Reset 由编辑器添加组件时调用，保存后沿用；运行时和复制时都不偷偷重造标识。
        private void Reset() => stableId = Guid.NewGuid().ToString("N");
        private void Awake() => RefreshLabel();
        private void OnEnable() => OnAvailabilityChanged?.Invoke(this, true);
        private void OnDisable() => OnAvailabilityChanged?.Invoke(this, false);

        public void RefreshLabel()
        {
            if (nameLabel != null) nameLabel.text = displayName + " [" + stableId + "]";
        }

        public Vector2[] PatrolPositions(EncounterSceneView view)
        {
            if (patrolPoints == null || patrolPoints.Length == 0) throw new InvalidOperationException("巡逻者缺少路线：" + stableId);
            var result = new Vector2[patrolPoints.Length];
            for (int i = 0; i < result.Length; i++)
            {
                if (patrolPoints[i] == null) throw new InvalidOperationException("巡逻点引用为空：" + stableId);
                result[i] = view.ToLogicPosition(patrolPoints[i].position);
            }
            return result;
        }
    }
}

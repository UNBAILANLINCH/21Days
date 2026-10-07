// 职责：按场景稳定标识管理驯服归属、控制切换和移动路由；独立演示与正式遭遇共用。
// 为什么新建：Player/Monster 只控制单一实体；控制归属不应混进个体移动或敌人感知。
using System;
using System.Collections.Generic;
using Game.Core.Telemetry;
using Game.Core.Simulation;
using Game.Monster;
using Game.Player;
using UnityEngine;

namespace Game.Taming
{
    public sealed class TamingRules
    {
        private readonly PlayerRules player;
        private readonly ITelemetryScope telemetry;
        private readonly List<string> targetIds = new List<string>();
        private readonly Dictionary<string, MonsterRules> targets = new Dictionary<string, MonsterRules>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> tamed = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> unavailable = new HashSet<string>(StringComparer.Ordinal);
        private bool previousToggle;
        private readonly IReadOnlyList<string> readOnlyIds;

        public TamingRules(PlayerRules player, MonsterRules enemy, ITelemetryScope telemetry)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            readOnlyIds = targetIds.AsReadOnly();
            RegisterTarget("enemy", "巡逻者", enemy ?? throw new ArgumentNullException(nameof(enemy)));
        }

        public string PlayerId { get; private set; } = "player";
        public string PlayerName { get; private set; } = "玩家";
        public string CurrentControlId { get; private set; } = "player";
        public bool IsTamed => targetIds.Count > 0 && IsTargetTamed(targetIds[0]);
        public bool IsControllingEnemy => CurrentControlId != PlayerId;
        public bool PreviousToggle => previousToggle;
        public IReadOnlyList<string> TargetIds => readOnlyIds;
        public event Action<string> OnControlChanged;

        public void Configure(string playerId, string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > 128 || string.IsNullOrWhiteSpace(playerName))
                throw new ArgumentException("玩家必须有稳定标识与显示名");
            targetIds.Clear();
            targets.Clear();
            names.Clear();
            tamed.Clear();
            unavailable.Clear();
            PlayerId = playerId;
            PlayerName = playerName;
            CurrentControlId = playerId;
            previousToggle = false;
        }

        public void RegisterTarget(string id, string displayName, MonsterRules rules)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || targetIds.Count >= 128 || string.IsNullOrWhiteSpace(displayName) || id == PlayerId || targets.ContainsKey(id))
                throw new ArgumentException("巡逻者标识为空或重复：" + id);
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            targets.Add(id, rules);
            names.Add(id, displayName);
            targetIds.Add(id);
        }

        public MonsterRules GetTarget(string id) => id != null && targets.TryGetValue(id, out MonsterRules value) ? value : null;
        public string GetDisplayName(string id) => id == PlayerId ? PlayerName : id != null && names.TryGetValue(id, out string value) ? value : string.Empty;
        public bool IsTargetTamed(string id) => id != null && tamed.Contains(id);
        public bool IsAvailable(string id) => id != null && !unavailable.Contains(id) && GetTarget(id) != null;
        public bool CanControl(string id) => id == PlayerId || (IsAvailable(id) && IsTargetTamed(id) && GetTarget(id).Model.Health > 0 && player.Model.Health > 0);

        public void SetAvailable(string id, bool available)
        {
            if (id == null || !targets.ContainsKey(id)) return;
            if (available) unavailable.Remove(id); else unavailable.Add(id);
            ValidateControl();
        }

        public bool TryTame(string id)
        {
            MonsterRules target = GetTarget(id);
            if (!IsAvailable(id) || target.Model.Health <= 0 || player.Model.Health <= 0) return false;
            if (tamed.Add(id)) telemetry.Track("tamed", ("id", id));
            return true;
        }

        public bool TryControl(string id)
        {
            if (!CanControl(id)) return false;
            if (CurrentControlId == id) return true;
            CurrentControlId = id;
            telemetry.Track("control_changed", ("id", id));
            OnControlChanged?.Invoke(id);
            return true;
        }

        public void ValidateControl()
        {
            if (!CanControl(CurrentControlId)) TryControl(PlayerId);
        }

        public void ProcessToggle(bool toggle, string targetId = null)
        {
            ValidateControl();
            if (toggle && !previousToggle)
            {
                if (targetId == null && IsControllingEnemy) TryControl(PlayerId);
                else
                {
                    string id = targetId ?? NearestTarget();
                    if (TryTame(id)) TryControl(id);
                    else telemetry.Track("control_rejected", ("reason", "invalid_or_dead"));
                }
            }
            previousToggle = toggle;
        }

        private string NearestTarget()
        {
            string nearest = null;
            float distance = float.MaxValue;
            foreach (string id in targetIds)
            {
                if (!IsAvailable(id) || targets[id].Model.Health <= 0) continue;
                float candidate = GameMath.SqrMagnitude(targets[id].Model.Position - player.Model.Position);
                if (candidate < distance) { nearest = id; distance = candidate; }
            }
            return nearest;
        }

        public void Reset()
        {
            tamed.Clear();
            unavailable.Clear();
            previousToggle = false;
            TryControl(PlayerId);
        }

        public TamingTargetSaveData[] Capture()
        {
            var result = new TamingTargetSaveData[targetIds.Count];
            for (int i = 0; i < targetIds.Count; i++)
            {
                string id = targetIds[i];
                result[i] = new TamingTargetSaveData { Id = id, IsTamed = IsTargetTamed(id), Monster = targets[id].Capture() };
            }
            return result;
        }

        public void ValidateRestore(TamingTargetSaveData[] saved)
        {
            if (saved == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TamingTargetSaveData entry in saved)
            {
                if (entry == null || !seen.Add(entry.Id) || GetTarget(entry.Id) == null)
                    throw new ArgumentException("存档巡逻者标识与场景不匹配");
                if (entry.Monster == null) throw new ArgumentException("巡逻者快照缺失");
                entry.Monster.Validate();
            }
            if (seen.Count != targetIds.Count) throw new ArgumentException("存档巡逻者数量与场景不匹配");
        }

        public void Restore(TamingTargetSaveData[] saved, string controlId, bool held)
        {
            ValidateRestore(saved);
            if (saved == null) { Reset(); return; }
            tamed.Clear();
            foreach (TamingTargetSaveData entry in saved)
            {
                targets[entry.Id].Restore(entry.Monster);
                if (entry.IsTamed) tamed.Add(entry.Id);
            }
            previousToggle = held;
            TryControl(PlayerId);
            TryControl(controlId);
        }

        // 回放也包含未 Begin 的状态，不能用要求已初始化路线的持久存档校验。
        public void RestoreReplayFlags(bool[] flags, string controlId, bool held)
        {
            if (flags == null || flags.Length != targetIds.Count) throw new ArgumentException("回放巡逻者数量不匹配");
            tamed.Clear();
            for (int i = 0; i < flags.Length; i++) if (flags[i]) tamed.Add(targetIds[i]);
            previousToggle = held;
            TryControl(PlayerId);
            TryControl(controlId);
        }

        public bool AdvanceTargets(Vector2 movement, float deltaTime, bool isIdentityInEffect = false)
        {
            bool primaryAttacked = false;
            foreach (string id in targetIds)
            {
                MonsterRules target = targets[id];
                if (!IsAvailable(id)) { target.Model.SyncPreviousPosition(); continue; }
                if (IsTargetTamed(id)) target.MoveControlled(CurrentControlId == id ? movement : Vector2.zero, deltaTime);
                else
                {
                    var intent = new MonsterIntent(player.Model.Snapshot, deltaTime, isIdentityInEffect);
                    if (target.Step(in intent))
                    {
                        if (id == targetIds[0]) primaryAttacked = true;
                        player.ApplyDamage(new DamageIntent(target.AttackDamage));
                    }
                }
            }
            ValidateControl();
            return primaryAttacked;
        }

        public void Step(in TamingIntent intent, float deltaTime)
        {
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            ProcessToggle(intent.ToggleControl);
            var playerIntent = new PlayerIntent(IsControllingEnemy ? Vector2.zero : intent.Movement,
                false, false, false, intent.Run);
            player.Step(in playerIntent, deltaTime);
            AdvanceTargets(intent.Movement, deltaTime);
        }
    }
}

// 职责：保护外部运行与结果消费；现有自由演奏进度规则不负责战斗上下文。
using System;
using System.Collections.Generic;

namespace Game.Rhythm
{
    public sealed class RhythmExternalSession
    {
        private readonly IRhythmEntryPermission permission;
        private readonly IRhythmCombatPolicy combatPolicy;
        private readonly Action<RhythmRunResult> onSuccess;
        private readonly Action<RhythmRunResult> onFailure;
        private readonly Action<RhythmRunResult> onAborted;
        private readonly Action<RhythmRunResult> onTechnicalError;
        private readonly HashSet<string> usedRunIds = new HashSet<string>(StringComparer.Ordinal);
        private RhythmPlayRequest active;

        public RhythmExternalSession(IRhythmEntryPermission permission, IRhythmCombatPolicy combatPolicy = null,
            Action<RhythmRunResult> onSuccess = null, Action<RhythmRunResult> onFailure = null,
            Action<RhythmRunResult> onAborted = null, Action<RhythmRunResult> onTechnicalError = null)
        {
            this.permission = permission ?? throw new ArgumentNullException(nameof(permission));
            this.combatPolicy = combatPolicy;
            this.onSuccess = onSuccess;
            this.onFailure = onFailure;
            this.onAborted = onAborted;
            this.onTechnicalError = onTechnicalError;
        }

        public RhythmPlayRequest Active => active;

        public bool CanAccessLibrary(string contextId)
        {
            if (string.IsNullOrWhiteSpace(contextId)) return false;
            RhythmEntryAccess access = permission.Capture(contextId);
            return access.IsContextValid && access.CanAccessLibrary;
        }

        public bool TryBegin(RhythmPlayRequest request, out string reason)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            reason = null;
            if (active != null) reason = "active_run";
            else if (usedRunIds.Contains(request.RunId)) reason = "used_run_id";
            else if (request.Mode == RhythmPlayMode.Combat && combatPolicy == null) reason = "missing_combat_policy";
            else
            {
                RhythmEntryAccess access = permission.Capture(request.ContextId);
                if (!access.IsContextValid) reason = "expired_context";
                else if (!access.CanAccessLibrary) reason = "not_tamed";
                else if (!access.CanPerform) reason = "not_controlling_performer";
            }
            if (reason != null) return false;
            usedRunIds.Add(request.RunId);
            active = request;
            return true;
        }

        // 场景卸载或调用方取消后，迟到结果永远不能重新激活旧运行。
        public void Invalidate() => active = null;

        public bool Consume(RhythmRunResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (active == null || result.RunId != active.RunId || result.ContextId != active.ContextId ||
                result.SongId != active.SongId || result.Mode != active.Mode) return false;
            // 身份匹配后立即封口；权限适配器、策略或回调异常都不能重放这份结果。
            RhythmPlayRequest request = active;
            active = null;
            RhythmEntryAccess access = permission.Capture(request.ContextId);
            if (!access.IsContextValid || !access.CanAccessLibrary || !access.CanPerform)
            {
                return false;
            }
            RhythmPlayMode mode = request.Mode;
            if (result.Completion == RhythmRunCompletion.Aborted) onAborted?.Invoke(result);
            else if (result.Completion == RhythmRunCompletion.TechnicalError) onTechnicalError?.Invoke(result);
            else if (result.Completion == RhythmRunCompletion.Completed && mode == RhythmPlayMode.Combat)
            {
                if (combatPolicy.IsSuccess(result)) onSuccess?.Invoke(result);
                else onFailure?.Invoke(result);
            }
            return true;
        }
    }
}

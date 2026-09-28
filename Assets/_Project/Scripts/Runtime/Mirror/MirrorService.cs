// 职责：镜模块对外门面（根作用域单例）——照镜 / 自照时组装判定输入、写辨认记录、首次照见请求保存、发布事件与埋点；
//   对外给出裂痕、作用距离、可见范围、辨认状态与妖物表行。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：MirrorRules 是纯规则，不认识存档服务、玩家模型、背包、配置表与 MessagePipe。
//   2. 扩展不行：塞进 LootService / PlayerRules 会让拾取或玩家模块认识妖与镜（PRP/mirror-core 2.1 依赖方向是 Mirror → 它们）。
//   同 LootService / LootRules 的分法：接线放门面，规则保持可单测。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Config;
using Game.Core.Logging;
using Game.Core.Save;
using Game.Core.Telemetry;
using Game.Loot;
using Game.Player;
using MessagePipe;

namespace Game.Mirror
{
    /// <summary>
    /// 镜服务。存档分区每次操作都重新 <c>saves.Get&lt;MirrorSaveData&gt;()</c>——读档 Commit / 新游戏 ResetAll 会整体替换分区实例，
    /// 缓存旧实例会把辨认记录写进一份没人读的对象（同 LootService）。所以也不需要订阅 SessionStartedEvent 重载。
    /// <para>
    /// 保存请求经构造参数 <c>requestSave</c> 递进来（安装器接 <c>GameSession.RequestSave</c>）：
    /// 本类因此不引用 Game.Session，EditMode 里也能直接观察「首次照见才请求保存」。
    /// </para>
    /// <para>
    /// 击中裂痕由 <see cref="PlayerModel.Health"/> 推出：遭遇开始前 Health 为 0，会被算成 3 道裂痕，
    /// 所以只在玩法状态里读 <see cref="HitCracks"/> / <see cref="EffectiveRange"/> / <see cref="VisionRadius"/>。
    /// </para>
    /// </summary>
    public sealed class MirrorService : IGameService, IDisposable
    {
        private const string IdentifiedSaveReason = "mirror_identified";

        private readonly MirrorConfig config;
        private readonly ISaveService saves;
        private readonly IConfigService configService;
        private readonly LootService loot;
        private readonly PlayerModel player;
        private readonly PlayerConfig playerConfig;
        private readonly MirrorSceneBinder binder;
        private readonly IPublisher<MirrorCastEvent> castPublisher;
        private readonly Action<string> requestSave;
        private readonly ITelemetryScope telemetry;
        private readonly Func<int, bool> hasClues;
        private readonly List<MirrorCandidate> candidateBuffer = new List<MirrorCandidate>();
        private readonly List<MirrorSubject> ownerBuffer = new List<MirrorSubject>();

        public MirrorService(MirrorConfig config, ISaveService saves, IConfigService configService, LootService loot,
            PlayerModel player, PlayerConfig playerConfig, MirrorSceneBinder binder, IPublisher<MirrorCastEvent> cast,
            Action<string> requestSave, ITelemetryScope telemetry)
        {
            // MirrorConfig / PlayerConfig 是 ScriptableObject，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (playerConfig == null) throw new ArgumentNullException(nameof(playerConfig));
            this.config = config;
            this.playerConfig = playerConfig;
            this.saves = saves ?? throw new ArgumentNullException(nameof(saves));
            this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
            this.loot = loot ?? throw new ArgumentNullException(nameof(loot));
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            castPublisher = cast ?? throw new ArgumentNullException(nameof(cast));
            this.requestSave = requestSave ?? throw new ArgumentNullException(nameof(requestSave));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            hasClues = HasClues;
        }

        /// <summary>击中裂痕（0..3），由玩家生命推出。</summary>
        public int HitCracks => MirrorCrackRules.HitCracks(playerConfig.MaxHealth, player.Health);

        /// <summary>剧情裂痕数（存档里）。</summary>
        public int StoryCracks => Data.StoryCracks;

        /// <summary>当前照镜作用距离（已计入两种裂痕）。</summary>
        public float EffectiveRange => MirrorCrackRules.EffectiveRange(config, HitCracks, StoryCracks);

        /// <summary>当前屏幕可见范围（画布比例，已计入两种裂痕）。</summary>
        public float VisionRadius => MirrorCrackRules.VisionRadius(config, HitCracks, StoryCracks);

        /// <summary>最近一次照镜命中的场景标记；照不到 / 自照 / <see cref="CastAt"/> 时为 null（用 == null 判）。</summary>
        public MirrorSubject LastSubject { get; private set; }

        /// <summary>
        /// 最近一次照镜 / 自照（含 <see cref="CastAt"/>）的判定结果；还没照过时为 <see cref="MirrorResult.Nothing"/>。
        /// 只读快照，不进存档；给回放与后续模块按种类判断，不用去比结果画面的文案。
        /// </summary>
        public MirrorResult LastResult { get; private set; } = MirrorResult.Nothing;

        private MirrorSaveData Data => saves.Get<MirrorSaveData>();

        /// <summary>只确保分区存在（首次 Get 会建默认分区），不做别的。</summary>
        public UniTask InitializeAsync(CancellationToken ct)
        {
            MirrorSaveData data = Data;
            telemetry.Track("initialized", ("identified", data.Identified == null ? 0 : data.Identified.Count),
                ("story_cracks", data.StoryCracks));
            return UniTask.CompletedTask;
        }

        /// <summary>该妖是否已照见真形。</summary>
        public bool IsIdentified(int yaoId) => MirrorRules.Contains(Data.Identified, yaoId);

        /// <summary>该妖是否见过模糊轮廓。</summary>
        public bool HasGlimpsed(int yaoId) => MirrorRules.Contains(Data.GlimpsedBlurry, yaoId);

        /// <summary>查妖物表一行；表未就绪或查不到返回 false（结果画面据此退回占位文案）。</summary>
        public bool TryGetYao(int yaoId, out global::cfg.yao.Yao yao)
        {
            yao = FindYao(yaoId);
            return yao != null;
        }

        /// <summary>
        /// 照镜：取玩家逻辑位置与朝向、当前作用距离、场景里登记的候选，判定后写辨认记录；
        /// 首次照见真形时请求保存；最后发布 <see cref="MirrorCastEvent"/> 并埋点。让位 / 冷却由调用方（输入呈现器）判断。
        /// </summary>
        public MirrorResult Cast()
        {
            binder.CollectCandidates(candidateBuffer, ownerBuffer);
            return Resolve(candidateBuffer, ownerBuffer);
        }

        /// <summary>用给定候选照镜（不经场景登记）：测试与调试用，其余流程同 <see cref="Cast"/>；事件里的 Subject 为 null。</summary>
        public MirrorResult CastAt(IReadOnlyList<MirrorCandidate> candidates) => Resolve(candidates, null);

        /// <summary>自照：结果恒为空白；自照次数 +1，发布事件并埋点。</summary>
        public MirrorResult LookSelf()
        {
            MirrorResult result = MirrorRules.ResolveSelf();
            MirrorSaveData data = Data;
            MirrorRules.Record(data, result);
            LastSubject = null;
            LastResult = result;
            castPublisher.Publish(new MirrorCastEvent(result, null, EffectiveRange));
            telemetry.Track("mirror_self", ("self_looks", data.SelfLooks));
            return result;
        }

        /// <summary>剧情裂痕 +1：只缩作用距离与可见范围，不计入三裂、不致镜碎。本波只提供接口，不接剧情。</summary>
        public void AddStoryCrack()
        {
            MirrorSaveData data = Data;
            data.StoryCracks++;
            telemetry.Track("mirror_cracked", ("hit_cracks", HitCracks), ("story_cracks", data.StoryCracks),
                ("range", EffectiveRange), ("source", "story"));
        }

        public void Dispose()
        {
            // 无订阅、无非托管资源；实现 IDisposable 是为与其它门面服务一致，容器托管释放。
            candidateBuffer.Clear();
            ownerBuffer.Clear();
            LastSubject = null;
            LastResult = MirrorResult.Nothing;
        }

        private MirrorResult Resolve(IReadOnlyList<MirrorCandidate> candidates, IReadOnlyList<MirrorSubject> owners)
        {
            float range = EffectiveRange;
            var query = new MirrorQuery(player.Position, player.Facing, range, config.FanHalfAngle, candidates, hasClues);
            MirrorResult result = MirrorRules.Resolve(query);

            bool firstIdentified = MirrorRules.Record(Data, result);
            if (firstIdentified)
            {
                requestSave(IdentifiedSaveReason);
                telemetry.Track("mirror_identified", ("yao_id", result.YaoId));
            }

            MirrorSubject subject = null;
            if (owners != null && result.HasTarget && result.CandidateIndex < owners.Count)
            {
                subject = owners[result.CandidateIndex];
            }
            LastSubject = subject;
            LastResult = result;

            castPublisher.Publish(new MirrorCastEvent(result, subject, range));
            telemetry.Track("mirror_cast", ("result", ResultKey(result.Kind)), ("yao_id", result.YaoId),
                ("distance", result.Distance), ("range", range));
            return result;
        }

        // 线索是否齐：查妖物表的 clue_items，对照背包（LootService.Items）。表里没有该妖时视为不齐（只给模糊轮廓）。
        private bool HasClues(int yaoId)
        {
            global::cfg.yao.Yao yao = FindYao(yaoId);
            if (yao == null) return false;
            return MirrorRules.CluesSatisfied(yao.ClueItems, loot.Items);
        }

        // 查妖物表；表未就绪（抛 InvalidOperationException）或查不到时返回 null（同 LootService.ItemName 的容错）。
        private global::cfg.yao.Yao FindYao(int yaoId)
        {
            try
            {
                global::cfg.yao.Yao yao = configService.Tables.TbYao.GetOrDefault(yaoId);
                if (yao == null) Log.Warn($"MirrorService：妖物表里没有 id {yaoId}，按线索不齐处理。");
                return yao;
            }
            catch (InvalidOperationException e)
            {
                Log.Warn($"MirrorService：配置表未就绪，妖 {yaoId} 按线索不齐处理：{e.Message}");
                return null;
            }
        }

        // 埋点用的结果键：固定字符串，不走 enum.ToString() 分配。
        private static string ResultKey(MirrorResultKind kind)
        {
            switch (kind)
            {
                case MirrorResultKind.Human: return "human";
                case MirrorResultKind.Object: return "object";
                case MirrorResultKind.Blurry: return "blurry";
                case MirrorResultKind.TrueForm: return "true_form";
                case MirrorResultKind.Self: return "self";
                default: return "nothing";
            }
        }
    }
}

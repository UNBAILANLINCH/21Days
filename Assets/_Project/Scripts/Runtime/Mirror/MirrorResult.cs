// 职责：一次照镜的结果值——种类、命中的候选下标、妖 id、距离。
// 为什么新建：PRP/mirror-core 2.3 约定 MirrorRules.Resolve 返回 MirrorResult；结果要同时给服务写存档、
//   给事件 / 结果画面取对象，既有模块没有可复用的结果类型。
namespace Game.Mirror
{
    /// <summary>照镜结果。<see cref="CandidateIndex"/> 是输入候选列表里的下标，照不到 / 自照时为 -1。</summary>
    public readonly struct MirrorResult
    {
        public MirrorResult(MirrorResultKind kind, int candidateIndex, int yaoId, float distance)
        {
            Kind = kind;
            CandidateIndex = candidateIndex;
            YaoId = yaoId;
            Distance = distance;
        }

        /// <summary>照不到。</summary>
        public static MirrorResult Nothing => new MirrorResult(MirrorResultKind.Nothing, -1, 0, 0f);

        /// <summary>自照：空白镜面。</summary>
        public static MirrorResult Self => new MirrorResult(MirrorResultKind.Self, -1, 0, 0f);

        public MirrorResultKind Kind { get; }

        /// <summary>命中候选在输入列表里的下标；没有命中为 -1。</summary>
        public int CandidateIndex { get; }

        /// <summary>命中妖的妖物表主键；结果不是 Blurry / TrueForm 时为 0。</summary>
        public int YaoId { get; }

        /// <summary>玩家到命中对象的逻辑距离；没有命中为 0。</summary>
        public float Distance { get; }

        /// <summary>是否命中了某个候选。</summary>
        public bool HasTarget => CandidateIndex >= 0;
    }
}

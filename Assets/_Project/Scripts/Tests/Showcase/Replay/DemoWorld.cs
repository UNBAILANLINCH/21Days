// DemoWorld —— 回放 Showcase 专用的最小演示世界：若干实体随 tick 移动、用 logic.* 流的随机数决定转向、
//   响应 InputCommand 的轴与按钮，并把「变了就会影响后续逻辑」的全部状态暴露成 IReplayState。
//
// 【定位，先说清楚它不是什么】**这是验机制、不验玩法的最小世界。**
//   它存在的唯一目的，是让回放 Showcase 在真实运行环境里证明三件事——「录得下、放得准、漂移报得出」。
//   它不是玩法示范，也不是玩法模块的雏形：这里的实体数、转向规则、速度倍率没有任何玩法含义，
//   谁都不要把它当成约定往外抄。工程玩法未定，此时建一个假玩法模块注定变成技术债，
//   所以刻意把它留在测试程序集里。玩法模块落地时照它的**接线方式**接即可：
//   实现 ISimulationStep（每 tick 只从 SimulationContext 取输入/随机/时间）+ IReplayState
//   （把影响后续逻辑的字段按固定顺序读写），再注册进推进器与状态注册表，回放就自动管用了。
//
// 【刻意埋的两个形态】
//   ① 晚出场的随机流：logic.demo.pulse 要到第 PulseFirstTick 个 tick 才第一次被取用，
//      起始快照落在它出场之前。这是回放里最容易静默分叉的形态——它出场之前一切都对得上，
//      出场之后才开始偏，而排查的人往往还在盯着开头几帧。Showcase 必须覆盖到它。
//   ② 可开关的不确定来源：DriftSourceEnabled 打开后，逻辑里会去读墙上时间（见 Step 开头）。
//      它不进快照也不进哈希，专供 Showcase 第 6 步验「漂移报得出」。默认是关的。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：工程里没有任何 ISimulationStep + IReplayState 的实现。Core/ 是零玩法框架层，
//      Runtime/ 下还没有玩法模块（玩法未定）。没有被测世界，回放就只能空跑，证明不了「放得准」。
//   2. 扩展不行：不能塞进 Core/Replay 或 Core/Simulation——框架层不许出现玩法名词与演示代码；
//      也不能塞进 ShowcaseSelfTest——那条用例专门自检回放框架本身、刻意不依赖任何 Core 能力，
//      掺进确定性内核之后，第一次出问题就分不清是「回放框架坏了」还是「确定性内核坏了」。

using Game.Core.Replay;
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Tests.Showcase.Replay
{
    /// <summary>
    /// 最小演示世界。每 tick 只从 <see cref="SimulationContext"/> 取输入、随机与步长，不读
    /// <c>UnityEngine.Time</c> / <c>Input</c> / <c>UnityEngine.Random</c>——
    /// 唯一的例外是 <see cref="EnableDriftSource"/> 打开后那段**故意写坏**的代码。
    /// </summary>
    internal sealed class DemoWorld : ISimulationStep, IReplayState
    {
        /// <summary>实体数。够看出「一群东西各走各的」，又不至于让快照字节多到看不清。</summary>
        internal const int EntityCount = 6;

        /// <summary>
        /// 「晚出场的随机流」第一次被取用的 tick。起始快照在 tick 0，所以它出场时快照早就写完了。
        /// </summary>
        internal const long PulseFirstTick = 400L;

        /// <summary>不确定来源从第几个 tick 起生效。见 <see cref="EnableDriftSource"/>。</summary>
        internal const long DriftFirstTick = 100L;

        /// <summary>逻辑流：决定转向与下一次转向的间隔。从第 0 个 tick 起就在用。</summary>
        private const string TurnStreamName = "logic.demo.turn";

        /// <summary>逻辑流：决定给谁加速。**到 <see cref="PulseFirstTick"/> 才第一次被取用**。</summary>
        private const string PulseStreamName = "logic.demo.pulse";

        /// <summary>活动范围半边长（世界单位）。取 2.5 是为了在竖屏 Game 视图里也整个看得见。</summary>
        private const float ArenaHalfSize = 2.5f;

        /// <summary>活动范围边长，越界时按它平移一次绕回来。</summary>
        private const float ArenaSize = ArenaHalfSize * 2f;

        /// <summary>基础速度（世界单位/秒）。</summary>
        private const float BaseSpeed = 1.6f;

        /// <summary>按住 Confirm 时的加速倍率。</summary>
        private const float BoostScale = 2f;

        /// <summary>两次转向之间的最短 / 最长 tick 数（含下界不含上界）。</summary>
        private const int TurnMinTicks = 12;
        private const int TurnMaxTicks = 48;

        /// <summary>每隔多少 tick 让 pulse 流挑一个实体改速度倍率。</summary>
        private const int PulseIntervalTicks = 30;

        /// <summary>速度倍率的取值区间 [Min, Min + Range)。</summary>
        private const float PulseScaleMin = 0.5f;
        private const float PulseScaleRange = 1.5f;

        /// <summary>不确定来源每 tick 给 0 号实体加的最大位移（世界单位）。</summary>
        private const float DriftNudgeUnits = 0.01f;

        /// <summary>
        /// 八个方向的单位向量（正交四向 + 四个对角）。用查表而不是三角函数：
        /// libm 的 sin/cos 各平台实现有差异，逻辑里用它就等于给自己埋一个跨端对不上的雷。
        /// </summary>
        private static readonly Vector2[] Directions =
        {
            new Vector2(1f, 0f),
            new Vector2(0.70710678f, 0.70710678f),
            new Vector2(0f, 1f),
            new Vector2(-0.70710678f, 0.70710678f),
            new Vector2(-1f, 0f),
            new Vector2(-0.70710678f, -0.70710678f),
            new Vector2(0f, -1f),
            new Vector2(0.70710678f, -0.70710678f),
        };

        private readonly IRandomService random;

        // 以下四组就是这个世界的全部状态，Serialize / Deserialize 按同样的顺序逐行镜像。
        private readonly Vector2[] positions = new Vector2[EntityCount];
        private readonly byte[] directionIndices = new byte[EntityCount];
        private readonly short[] turnCountdowns = new short[EntityCount];
        private readonly float[] speedScales = new float[EntityCount];

        // 刻意**不进**状态：它是「这次运行有没有开着那个坏毛病」，不是世界的一部分。
        // 进了快照的话，从快照续跑会把坏毛病一起恢复，漂移检测就变成了自证清白。
        private bool driftSourceEnabled;

        /// <param name="random">确定性随机服务，用来取两条 logic.* 流；不可为空。</param>
        internal DemoWorld(IRandomService random)
        {
            this.random = random;
            ResetWorld();
        }

        /// <summary>实体数量，给画面同步用。</summary>
        internal int Count
        {
            get { return positions.Length; }
        }

        /// <summary>第 <paramref name="index"/> 个实体此刻的位置，给画面同步用（只读）。</summary>
        internal Vector2 PositionOf(int index)
        {
            return positions[index];
        }

        /// <summary>
        /// 回到一个写死的初始布局：实体沿 x 轴等距排开，方向各不相同，速度倍率都是 1。
        /// 写死而不是随机，是为了让「录的那一局」从一个肉眼可辨的形状开始。
        /// <para>不动两条随机流的状态——流的状态属于世界状态，该由快照负责恢复，不该由这里悄悄重置。</para>
        /// </summary>
        internal void ResetWorld()
        {
            for (int i = 0; i < EntityCount; i++)
            {
                float t = EntityCount <= 1 ? 0f : i / (float)(EntityCount - 1);
                positions[i] = new Vector2(Mathf.Lerp(-ArenaHalfSize, ArenaHalfSize, t), 0f);
                directionIndices[i] = (byte)(i % Directions.Length);
                turnCountdowns[i] = (short)(TurnMinTicks + i);
                speedScales[i] = 1f;
            }

            driftSourceEnabled = false;
        }

        /// <summary>
        /// 开 / 关那个**故意埋的不确定来源**（逻辑里读墙上时间）。只给 Showcase 的「验漂移」那一步用。
        /// <para>
        /// 打开之后，从第 <see cref="DriftFirstTick"/> 个 tick 起，0 号实体每 tick 会多挪一点点，
        /// 挪多少取决于 <c>Time.realtimeSinceStartup</c>——一个既不在输入里、也不在快照里的量。
        /// 录制那一遍是关着它跑的，所以重放时必然对不上，且**从这个 tick 起**才开始对不上：
        /// 这正是「首次漂移 tick 要指向真凶、而不是最后一个校验点」要验的东西。
        /// </para>
        /// </summary>
        internal void EnableDriftSource(bool enabled)
        {
            driftSourceEnabled = enabled;
        }

        /// <summary>推进一个 tick：（可选的坏毛病）→ 转向 → 移动 → 晚出场的随机流。</summary>
        public void Step(in SimulationContext context)
        {
            ApplyDriftSource(context.Tick);
            MoveEntities(context);
            ApplyLateStream(context.Tick, context.Random);
        }

        /// <summary>
        /// 按固定顺序写出全部状态。**两条 logic.* 流的状态无条件都写**——
        /// pulse 流要到 <see cref="PulseFirstTick"/> 才第一次被取用，起始快照落在它出场之前；
        /// 要是写成「用过才写」，字节布局就会随运行过程变化，同一份快照在另一时刻读回来整体错位，
        /// 而且多半不抛异常，只会读出一堆看起来合理的垃圾值。
        /// <para><c>random.Stream(name)</c> 是幂等的：流不存在就按主种子建一条，建出来不消耗任何数。</para>
        /// </summary>
        public void Serialize(IStateWriter writer)
        {
            for (int i = 0; i < EntityCount; i++)
            {
                writer.WriteVector2(positions[i]);
                writer.WriteByte(directionIndices[i]);
                writer.WriteShort(turnCountdowns[i]);
                writer.WriteFloat(speedScales[i]);
            }

            writer.WriteULong(random.Stream(TurnStreamName).State);
            writer.WriteULong(random.Stream(PulseStreamName).State);
        }

        /// <summary>逐行镜像 <see cref="Serialize"/>，顺序一个字段都不能错开。</summary>
        public void Deserialize(IStateReader reader)
        {
            for (int i = 0; i < EntityCount; i++)
            {
                positions[i] = reader.ReadVector2();
                directionIndices[i] = reader.ReadByte();
                turnCountdowns[i] = reader.ReadShort();
                speedScales[i] = reader.ReadFloat();
            }

            random.Stream(TurnStreamName).State = reader.ReadULong();
            random.Stream(PulseStreamName).State = reader.ReadULong();
        }

        /// <summary>
        /// 【故意写坏的一段】逻辑里读墙上时间。正常玩法代码里出现这种东西就是 bug，
        /// 这里是标本：它不在输入里、不在快照里、不在哈希里，所以重放时对不上，而且查不到来由。
        /// </summary>
        private void ApplyDriftSource(long tick)
        {
            if (!driftSourceEnabled || tick < DriftFirstTick)
            {
                return;
            }

            // 取 [0.5, 1) 的一个系数，保证每 tick 的偏移恒不为零——为零就漂不出来，
            // 那这一步的 Showcase 会时灵时不灵，比不验还糟。
            // lint-ok: 读墙上时间是本方法的全部目的（见方法注释），不是疏忽
            float wall = 0.5f + (0.5f * Mathf.Repeat(Time.realtimeSinceStartup, 1f));
            positions[0] = new Vector2(Wrap(positions[0].x + (DriftNudgeUnits * wall)), positions[0].y);
        }

        /// <summary>转向 + 移动。0 号实体额外听输入的轴与按钮。</summary>
        private void MoveEntities(in SimulationContext context)
        {
            IRandomStream turn = context.Random.Stream(TurnStreamName);
            InputCommand input = context.Input;
            bool boost = input.HasButton(InputCommand.ButtonConfirm);
            bool halt = input.HasButton(InputCommand.ButtonCancel);
            float delta = context.DeltaTime;

            for (int i = 0; i < EntityCount; i++)
            {
                turnCountdowns[i]--;
                if (turnCountdowns[i] <= 0)
                {
                    // 每次转向恰好消耗两个随机数，顺序固定：先方向后间隔。
                    // 顺序一变，同一条流后面的每一个数都跟着挪位，老录像整份作废。
                    directionIndices[i] = (byte)turn.Range(0, Directions.Length);
                    turnCountdowns[i] = (short)turn.Range(TurnMinTicks, TurnMaxTicks);
                }

                Vector2 direction = Directions[directionIndices[i]];

                // 0 号实体是「玩家」：轴一推就按轴走。**不归一化**——录进去的是什么就照什么算，
                // 这样输入的每一位都真的参与了逻辑，输入错位一格立刻表现成位置对不上。
                if (i == 0 && (input.Axis0.x != 0f || input.Axis0.y != 0f))
                {
                    direction = input.Axis0;
                }

                float speed = halt ? 0f : BaseSpeed * speedScales[i] * (boost ? BoostScale : 1f);
                Vector2 moved = positions[i] + (direction * (speed * delta));
                positions[i] = new Vector2(Wrap(moved.x), Wrap(moved.y));
            }
        }

        /// <summary>
        /// 晚出场的那条流：到 <see cref="PulseFirstTick"/> 之后才第一次被取用，之后每
        /// <see cref="PulseIntervalTicks"/> 个 tick 挑一个实体改速度倍率。
        /// </summary>
        private void ApplyLateStream(long tick, IRandomService source)
        {
            if (tick < PulseFirstTick || (tick - PulseFirstTick) % PulseIntervalTicks != 0L)
            {
                return;
            }

            IRandomStream pulse = source.Stream(PulseStreamName);
            int target = pulse.Range(0, EntityCount);
            speedScales[target] = PulseScaleMin + (pulse.Value01() * PulseScaleRange);
        }

        /// <summary>出界就平移一个边长绕回来。每 tick 的位移远小于边长，平移一次就够。</summary>
        private static float Wrap(float value)
        {
            if (value > ArenaHalfSize)
            {
                return value - ArenaSize;
            }

            return value < -ArenaHalfSize ? value + ArenaSize : value;
        }
    }
}

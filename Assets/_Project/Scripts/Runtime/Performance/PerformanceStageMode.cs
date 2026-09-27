// 职责：演出舞台的渲染模式——叠加在游戏画面上的独立小舞台，还是演员直接站在世界里、由舞台相机接管整个画面。
// 为什么新建（复用 → 扩展 → 新建）：PerformanceStage 原来只有 bool 开关，没有「舞台怎么渲染」的概念；
//   两种模式会继续分叉（相机、摆放、校验规则都不同），用 bool 塞进 PerformanceStage 读不出语义，枚举单独成文件（一个文件一个类型）。

namespace Game.Performance
{
    /// <summary>演出舞台的渲染模式。</summary>
    public enum PerformanceStageMode
    {
        /// <summary>
        /// 叠加模式（默认，旧演出全是它）：舞台相机为 URP Overlay、正交、只渲染 Performance 层，
        /// 叠到主相机的相机栈上；舞台内容是一块画在游戏画面之上的独立小舞台，与世界位置无关。
        /// </summary>
        Overlay = 0,

        /// <summary>
        /// 世界模式：舞台相机为透视 Base 相机，演出期间接管整个画面（渲染主相机能看到的全部图层 + Performance 层）；
        /// 预制体子物体就是站在世界里的演员，实例按调用方给的 <see cref="PerformancePlacement"/> 摆到世界位姿上。
        /// </summary>
        World = 1,
    }
}

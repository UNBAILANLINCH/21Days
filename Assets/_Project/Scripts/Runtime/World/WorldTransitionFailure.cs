// 职责：待处理转场取用失败的原因（机器可判的档位），与 WorldTransitionResolution 一起用。
// 为什么新建（一个文件一个类；枚举同 PortalTriggerKind 的先例）：失败原因要能被测试与埋点按档位断言——
//   光有一句人话，负对照只能靠字符串包含，改一个字就会假绿；档位 + 人话两句都有，才既稳又可读。
namespace Game.World
{
    /// <summary>转场取用失败的原因。</summary>
    public enum WorldTransitionFailure
    {
        /// <summary>没失败。</summary>
        None = 0,

        /// <summary>没有待处理转场（重进、或直接 <c>GoToAsync&lt;WorldSceneState&gt;()</c> 而没先写目标）。</summary>
        NoPending = 1,

        /// <summary>配置表还没就绪，查不了目标场景。</summary>
        TableNotReady = 2,

        /// <summary>目标场景不在 <c>TbScene</c> 里。</summary>
        SceneUnknown = 3,

        /// <summary>目标场景在表里，但 <c>implemented=false</c>（Addressables 里还没有这张图）。</summary>
        SceneNotImplemented = 4,

        /// <summary>目标场景标了已实装，但 <c>scene_address</c> 是空的（表写漏了）。</summary>
        SceneAddressMissing = 5,
    }
}

// 职责：Live2D 演员适配层的程序集锚点。Cubism SDK 缺席时，Game.Live2D 整个程序集
// 靠 asmdef 的 defineConstraints（LIVE2D_CUBISM）不参与编译，见
// PRP/performance-pipeline/prp.md 2.7；这个空文件只是让程序集在 SDK 就位时有内容可编译，
// 不承担业务逻辑。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有任何「SDK 可能缺席」的程序集可参照，
// Game.Runtime 不能引用未必存在的 Live2D.Cubism.* 程序集，只能建独立 asmdef 并靠这个
// 占位文件占住目录，否则空程序集会被 Unity 视为无脚本目录处理。

#if LIVE2D_CUBISM
namespace Game.Live2D
{
    internal static class Live2DAssemblyInfo
    {
    }
}
#endif

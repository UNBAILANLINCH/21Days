// 职责：UI 面板开关的唯一入口契约。
// 为什么新建：architecture.md 5.6 定义了这个契约；实现与契约分开放（将来换 UI Toolkit 只换实现）。

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Core.UI
{
    /// <summary>
    /// UI 服务。玩法与状态开关面板只走这里，不自己 Instantiate 预制体、不自己找 Canvas。
    /// <para>
    /// 面板预制体的 Addressables 地址**必须等于类名**：<c>OpenAsync&lt;TitleView&gt;()</c> 找的是地址 <c>TitleView</c>。
    /// </para>
    /// <para>
    /// 层级规则见 <see cref="UIStack"/>：Panel 单栈、全屏面板隐藏下层，Popup 可叠加，Hud / Top 不进栈。
    /// </para>
    /// </summary>
    public interface IUIService
    {
        /// <summary>
        /// 打开一个面板。已经开着就直接返回那个实例（不会开出两份），并把 <paramref name="arg"/> 再送一次 OnOpenAsync。
        /// 返回的实例由 UI 服务持有，调用方不要自己 Destroy。
        /// <para>
        /// **并发打开同类型会等待第一次打开完成并返回同一实例**：第二次调用不会再实例化一份，
        /// 而是排在第一次后面，第一次成功就拿到同一个实例、第一次失败就收到同一个异常。
        /// 所以按钮连点、两个状态同时开同一个面板都是安全的，调用方不必自己加防抖。
        /// </para>
        /// <para>
        /// 打开途中失败（预制体上没有对应组件、<c>OnOpenAsync</c> 抛异常）时不留残骸：
        /// 实例已经还给资源服务、记账也清干净了，异常照原样抛给调用方。
        /// </para>
        /// </summary>
        UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView;

        /// <summary>关闭一个面板：播淡出 → OnCloseAsync → 出栈并恢复下层 → 归还实例。传 null 或没开过的面板是空操作。</summary>
        UniTask CloseAsync(UIView view, CancellationToken ct = default);

        /// <summary>关掉最上面那个面板（先 Popup 后 Panel）。两条栈都空时是空操作——「返回键没东西可关」不是错误。</summary>
        UniTask CloseTopAsync(CancellationToken ct = default);

        /// <summary>拿已经打开的面板实例，没开返回 null。用来刷新界面，不要拿它当「开没开」的判定后自己 new。</summary>
        T Get<T>() where T : UIView;
    }
}

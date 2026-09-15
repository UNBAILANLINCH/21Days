// 职责：需要在启动阶段串行初始化的框架服务的统一契约。
// 为什么新建：VContainer 只负责构造对象，不负责「按顺序 await 一遍异步初始化」；
// 要让 GameBootstrap 不认识任何具体服务，就必须有这么一个共同接口。

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Core.Boot
{
    /// <summary>
    /// 框架服务。GameBootstrap 会按容器里的注册顺序**串行**调用 InitializeAsync，
    /// 前一个没返回就不会开始下一个——启动顺序是显式的，不靠隐式依赖碰运气。
    /// 实现要点：出错就抛异常（GameBootstrap 会记 Log.Error 并停止启动），不要吞掉。
    /// </summary>
    public interface IGameService
    {
        /// <summary>异步初始化。ct 取消时请尽快返回或抛 OperationCanceledException。</summary>
        UniTask InitializeAsync(CancellationToken ct);
    }
}

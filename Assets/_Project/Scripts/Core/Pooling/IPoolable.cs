// 职责：池化对象的取出/归还回调约定。
// 为什么新建：池要能通知实例「你被复用了 / 你要回去了」，而 MonoBehaviour 的 OnEnable/OnDisable
// 语义不够（SetActive 之外还有别的原因会触发），需要一个独立的显式接口。

namespace Game.Core.Pooling
{
    /// <summary>
    /// 挂在池化预制体上的组件实现本接口，即可在取出与归还时重置自身状态。
    /// 池会对实例根节点上的所有 IPoolable 组件调用（不递归子节点，子节点自己管自己）。
    /// </summary>
    public interface IPoolable
    {
        /// <summary>从池里取出、已经 SetActive(true) 之后调用。在这里把状态重置成「刚出生」。</summary>
        void OnGet();

        /// <summary>归还入池、SetActive(false) 之前调用。在这里断开引用、停掉协程与动效。</summary>
        void OnRelease();
    }
}

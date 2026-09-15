// 职责：存档读写契约（按槽位存读、按类型取分区）。
// 为什么新建：architecture.md 5.7 定义了这个契约；实现与契约分开放，将来换成云存档只换实现。

using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Core.Save
{
    /// <summary>
    /// 存档服务。内存里始终有一份「当前存档」，由若干 <see cref="ISaveData"/> 分区组成；
    /// <see cref="SaveAsync"/> 把它整份写进某个槽位，<see cref="LoadAsync"/> 把某个槽位整份读回内存。
    /// <para>
    /// 玩法拿分区只用 <see cref="Get{T}"/>，改完属性就行，不用「通知存档脏了」；什么时候落盘由调用方决定。
    /// </para>
    /// </summary>
    public interface ISaveService
    {
        /// <summary>
        /// 按类型取分区。首次访问时用无参构造创建一个（属性初始化器给的就是默认存档），
        /// 之后每次返回同一个实例——拿到就能直接改。
        /// </summary>
        T Get<T>() where T : class, ISaveData, new();

        /// <summary>
        /// 把内存里的全部分区写进槽位。先写临时文件再原子替换，中途断电不会留下半个存档。
        /// 返回 false 表示写失败（已记 Error），调用方按需提示玩家，不用 try/catch。
        /// </summary>
        UniTask<bool> SaveAsync(int slot, CancellationToken ct = default);

        /// <summary>
        /// 把槽位读回内存，逐分区反序列化并按需迁移版本。
        /// 槽位不存在、文件损坏、格式不认识都返回 false（已记日志），**不抛异常**——
        /// 存档坏掉不该把游戏带崩，调用方拿到 false 就当新档开。
        /// </summary>
        UniTask<bool> LoadAsync(int slot, CancellationToken ct = default);

        /// <summary>槽位里有没有存档文件。</summary>
        bool Exists(int slot);

        /// <summary>删掉槽位的存档文件（连同可能残留的临时文件）。不存在时是空操作。</summary>
        void Delete(int slot);
    }
}

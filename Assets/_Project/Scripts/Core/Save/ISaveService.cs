// 职责：存档读写契约（按槽位存读、按类型取分区）。
// 为什么新建：architecture.md 5.7 定义了这个契约；实现与契约分开放，将来换成云存档只换实现。

using System;
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
        /// 把内存里的全部分区写进槽位。优先使用临时文件替换；不支持原子替换的平台使用带 .bak 的恢复退路。
        /// 返回 false 表示写失败（已记 Error），调用方按需提示玩家，不用 try/catch。
        /// </summary>
        UniTask<bool> SaveAsync(int slot, CancellationToken ct = default);

        /// <summary>
        /// 把槽位读回内存，逐分区反序列化并按需迁移版本。常见损坏返回 false；取消、迁移代码错误和 IO 取消会抛出，调用方需区分处理。
        /// </summary>
        UniTask<bool> LoadAsync(int slot, CancellationToken ct = default);

        /// <summary>只读候选；失败返回 null，不修改内存分区，也不补默认分区。</summary>
        UniTask<SaveSnapshot> ReadCandidateAsync(int slot, CancellationToken ct = default);
        SaveSnapshot Capture();
        void Commit(SaveSnapshot snapshot);

        /// <summary>
        /// 丢弃内存里的全部分区（「新游戏」用），之后每个 <see cref="Get{T}"/> 都会新建默认实例；不碰磁盘上的任何槽位与档案。
        /// <para>
        /// 与 <see cref="LoadAsync"/> / <see cref="Commit"/> 一样是**整体替换**：之前 Get 出来的分区实例从此与存档脱钩，
        /// 再改它们不会被存下来。所以调用方（各服务）不得把分区实例缓存在字段里，每次用时重新 <see cref="Get{T}"/>。
        /// </para>
        /// </summary>
        void ResetAll();

        /// <summary>
        /// 独立于槽位的玩家档案。名称只允许字母、数字、短横线及下划线。
        /// T 实现 <see cref="ISaveData"/> 时写成版本信封 <c>{ "version": N, "data": {...} }</c>，读时按版本迁移
        /// （无信封的旧裸对象按版本 1；高于代码版本则拒绝读取、返回默认值）；其它 T 按裸对象读写。
        /// </summary>
        UniTask<T> ReadProfileAsync<T>(string name, CancellationToken ct = default) where T : class, new();
        UniTask<T> ReadProfileAsync<T>(string name, Action<T> validate, CancellationToken ct = default) where T : class, new();
        UniTask WriteProfileAsync<T>(string name, T data, CancellationToken ct = default) where T : class;

        /// <summary>槽位里有没有存档文件。</summary>
        bool Exists(int slot);

        /// <summary>删掉槽位的存档文件（连同可能残留的临时文件）。不存在时是空操作。</summary>
        void Delete(int slot);
    }
}

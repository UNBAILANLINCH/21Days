// 职责：配置表的只读访问契约，把 Luban 生成的 cfg.Tables 根暴露给玩法。
// 为什么新建：architecture.md 5.4 定义了这个契约；波 2 之前 Core/Config/ 目录不存在，没有可复用/可扩展的文件。

namespace Game.Core.Config
{
    /// <summary>
    /// 配置表服务。玩法读表一律 <c>config.Tables.TbXxx.Get(id)</c>，
    /// 不自己 new <c>cfg.Tables</c>、不自己读 .bytes。
    /// <para>
    /// 表是**只读**的：Luban 生成的字段都是 readonly，运行期状态请复制到自己的普通类里，不要往配置上挂。
    /// </para>
    /// </summary>
    public interface IConfigService
    {
        /// <summary>
        /// Luban 生成的表根。初始化完成前访问会抛异常——它在启动顺序里排在 Assets 之后、Save 之前，
        /// 任何 IGameService.InitializeAsync 里想读表的服务，注册顺序必须排在 ConfigService 后面。
        /// </summary>
        global::cfg.Tables Tables { get; }

        /// <summary>
        /// 本次加载的配置内容指纹（FNV-1a 64 位），只由表的内容决定，不含代码版本、不含加载顺序。
        /// <para>
        /// 给回放用：录制时把它写进回放文件头，放回放前先比一次。对不上就直接报
        /// 「配置版本不匹配，这份回放录于另一版配置」，而不是甩一串对不上的数据点让人对着代码查一整天。
        /// </para>
        /// <para>
        /// 和 <see cref="Tables"/> 同一条规矩：初始化完成前访问会抛异常。
        /// 不返回 0 之类的哨兵值——那会被悄悄写进回放文件头，等到放回放时才炸，离真因更远。
        /// </para>
        /// </summary>
        ulong ContentHash { get; }
    }
}

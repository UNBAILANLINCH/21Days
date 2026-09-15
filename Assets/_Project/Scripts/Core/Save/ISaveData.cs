// 职责：存档分区的契约——每个分区自带版本号，并负责把旧版本数据迁到当前版本。
// 为什么新建：architecture.md 5.7 定义了这个契约；波 2 之前 Core/Save/ 目录不存在。

namespace Game.Core.Save
{
    /// <summary>
    /// 存档分区。一个分区是一份纯 DTO（普通 C# 类，只有可读写属性、无 MonoBehaviour、无 Unity 引用），
    /// 按**类型**存取：<c>saves.Get&lt;SettingsSaveData&gt;()</c>。
    /// <para>
    /// 加字段：直接加属性并给默认值，<see cref="Version"/> 不用动——Newtonsoft 反序列化时
    /// 老存档里缺的字段会保留属性初始化器给的默认值。
    /// </para>
    /// <para>
    /// 改语义（字段改名、单位变了、值域变了）：<see cref="Version"/> 加一，并在
    /// <see cref="Migrate"/> 里处理从旧版本上来的数据。
    /// </para>
    /// </summary>
    public interface ISaveData
    {
        /// <summary>当前代码里这个分区的版本号，从 1 起。写成常量返回，不要做成可赋值属性。</summary>
        int Version { get; }

        /// <summary>
        /// 把刚从存档里读出来的旧版本数据就地迁到 <see cref="Version"/>。
        /// 只在「存档里的版本 &lt; 当前版本」时被调用一次，参数是存档里那个版本号。
        /// 实现里按版本逐级往上迁（<c>if (fromVersion &lt; 2) {...}</c>），不要写成只处理相邻版本。
        /// </summary>
        void Migrate(int fromVersion);
    }
}

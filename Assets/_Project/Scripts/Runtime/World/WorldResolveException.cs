// 职责：世界结构数据解析不出来时的异常（场景 / 出生点 / 传送点对不上）。
// 为什么新建：一个文件一个类（csharp-code.md「一个文件一个类，文件名等于类名」）；
//   调用方要能把「世界表数据不对」与别的错误分开处理（WorldRules.Resolve 的抛出点）。
using System;

namespace Game.World
{
    /// <summary>
    /// 世界表（TbScene / TbRegion / TbPortal）解析不出结果时抛这个。
    /// 消息里一定带「哪一项、表里的原值、去哪张表改」，不吞成 null 让调用方猜。
    /// </summary>
    public sealed class WorldResolveException : Exception
    {
        public WorldResolveException(string message) : base(message)
        {
        }

        public WorldResolveException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

// 职责：IClock 的本机实现，直接包装 UnityEngine.Time 与 DateTime.UtcNow。
// 为什么新建：接口要有一个默认实现才能注册进容器；这个实现除了转发没有别的职责，
// 塞进 IClock.cs 会让「接口」和「某一个实现」绑死，将来加服务器校时实现时要拆。

using System;
using UnityEngine;

namespace Game.Core.Timing
{
    /// <summary>
    /// 单机实现：时间就是本机时间。将来联网时换成服务器校时实现重新注册即可，玩法代码不动。
    /// </summary>
    public sealed class LocalClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;

        public float GameTime => Time.time;

        public float UnscaledTime => Time.unscaledTime;

        public float DeltaTime => Time.deltaTime;

        public float UnscaledDeltaTime => Time.unscaledDeltaTime;
    }
}

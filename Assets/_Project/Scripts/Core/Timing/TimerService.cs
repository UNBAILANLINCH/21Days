// 职责：ITimerService 的唯一实现，由 VContainer 的 ITickable 驱动，时间只读 IClock。
// 为什么新建：没有现成实现可复用；也不能塞进 LocalClock（时钟只负责报时，管理定时器条目、
// 处理迭代中增删是另一套职责，混在一起后者没法单独测）。

using System;
using System.Collections.Generic;
using Game.Core.Logging;
using VContainer.Unity;

namespace Game.Core.Timing
{
    /// <summary>
    /// 定时器服务。
    /// 迭代安全：Tick 遍历期间新注册的定时器打 PendingStart 标记本帧不参与，
    /// 取消/触发完成的槽位先进 pendingFree 暂存，等遍历结束再回收——
    /// 这样回调里随便注册、取消、自杀都不会打乱正在进行的遍历。
    /// </summary>
    public sealed class TimerService : ITimerService, ITickable, IDisposable
    {
        private readonly IClock clock;
        private readonly List<TimerEntry> entries = new List<TimerEntry>();
        private readonly Stack<int> freeSlots = new Stack<int>();
        private readonly List<int> pendingFree = new List<int>();
        private readonly List<int> pendingStart = new List<int>();

        private bool ticking;
        private bool disposed;

        public TimerService(IClock clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>当前仍在等待触发的定时器数量，供测试与调试台查看。</summary>
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Active)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public TimerHandle Delay(float seconds, Action callback, bool unscaled = false)
        {
            return Register(seconds, callback, false, unscaled);
        }

        public TimerHandle Interval(float seconds, Action callback, bool unscaled = false)
        {
            return Register(seconds, callback, true, unscaled);
        }

        /// <summary>由 VContainer 的 EntryPoint 每帧驱动；EditMode 测试里由测试代码手动调用。</summary>
        public void Tick()
        {
            if (disposed || entries.Count == 0)
            {
                return;
            }

            float scaledNow = clock.GameTime;
            float unscaledNow = clock.UnscaledTime;
            int count = entries.Count;

            ticking = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    TimerEntry entry = entries[i];
                    if (!entry.Active || entry.PendingStart)
                    {
                        continue;
                    }

                    float now = entry.Unscaled ? unscaledNow : scaledNow;
                    if (now < entry.DueTime)
                    {
                        continue;
                    }

                    Action callback = entry.Callback;
                    if (entry.Repeat)
                    {
                        entry.DueTime += entry.Period;
                        if (entry.DueTime <= now)
                        {
                            // 掉帧太久追不上时重新对齐，避免一次 Tick 里补触发几十次
                            entry.DueTime = now + entry.Period;
                        }
                    }
                    else
                    {
                        Release(entry);
                    }

                    try
                    {
                        callback();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"定时器回调抛异常，已跳过：{e}");
                    }
                }
            }
            finally
            {
                ticking = false;
                FlushPendingChanges();
            }
        }

        /// <summary>作用域销毁时由容器调用，取消所有还没触发的定时器。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            for (int i = 0; i < entries.Count; i++)
            {
                TimerEntry entry = entries[i];
                if (!entry.Active)
                {
                    continue;
                }

                entry.Active = false;
                entry.Callback = null;
                entry.Generation++;
            }

            freeSlots.Clear();
            pendingFree.Clear();
            pendingStart.Clear();
        }

        internal bool IsActive(int id, int generation)
        {
            if (id < 0 || id >= entries.Count)
            {
                return false;
            }

            TimerEntry entry = entries[id];
            return entry.Active && entry.Generation == generation;
        }

        internal void Cancel(int id, int generation)
        {
            if (id < 0 || id >= entries.Count)
            {
                return;
            }

            TimerEntry entry = entries[id];
            if (!entry.Active || entry.Generation != generation)
            {
                return;
            }

            Release(entry);
        }

        private TimerHandle Register(float seconds, Action callback, bool repeat, bool unscaled)
        {
            if (disposed)
            {
                Log.Warn("TimerService 已销毁，忽略本次定时器注册");
                return default;
            }

            if (callback == null)
            {
                Log.Error("定时器回调为空，忽略本次注册");
                return default;
            }

            if (repeat && seconds <= 0f)
            {
                Log.Error($"Interval 的周期必须大于 0（收到 {seconds}）；每帧回调请实现 ITickable");
                return default;
            }

            if (seconds < 0f)
            {
                seconds = 0f;
            }

            TimerEntry entry;
            if (freeSlots.Count > 0)
            {
                entry = entries[freeSlots.Pop()];
            }
            else
            {
                entry = new TimerEntry { Slot = entries.Count, Generation = 1 };
                entries.Add(entry);
            }

            float now = unscaled ? clock.UnscaledTime : clock.GameTime;
            entry.Active = true;
            entry.Repeat = repeat;
            entry.Unscaled = unscaled;
            entry.Period = seconds;
            entry.DueTime = now + seconds;
            entry.Callback = callback;
            entry.PendingStart = ticking;
            if (ticking)
            {
                pendingStart.Add(entry.Slot);
            }

            return new TimerHandle(this, entry.Slot, entry.Generation);
        }

        private void Release(TimerEntry entry)
        {
            if (!entry.Active)
            {
                return;
            }

            entry.Active = false;
            entry.Callback = null;
            // 代数自增，让已经发出去的句柄立刻失效，槽位复用后也不会被误取消
            entry.Generation++;

            if (ticking)
            {
                pendingFree.Add(entry.Slot);
            }
            else
            {
                freeSlots.Push(entry.Slot);
            }
        }

        private void FlushPendingChanges()
        {
            for (int i = 0; i < pendingFree.Count; i++)
            {
                freeSlots.Push(pendingFree[i]);
            }

            pendingFree.Clear();

            for (int i = 0; i < pendingStart.Count; i++)
            {
                entries[pendingStart[i]].PendingStart = false;
            }

            pendingStart.Clear();
        }

        /// <summary>一个定时器槽位。槽位对象本身复用，靠 Generation 区分前后两次注册。</summary>
        private sealed class TimerEntry
        {
            public int Slot;
            public int Generation;
            public bool Active;
            public bool Repeat;
            public bool Unscaled;
            public bool PendingStart;
            public float Period;
            public float DueTime;
            public Action Callback;
        }
    }
}

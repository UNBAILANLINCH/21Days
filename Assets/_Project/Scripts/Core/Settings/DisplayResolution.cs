// 职责：一个与刷新率无关的显示分辨率（宽 × 高）。
// 为什么新建：UnityEngine.Resolution 带刷新率、可变、没有相等比较，拿来做「去重后的分辨率列表」不合适；
//   ISettingsService 契约要一个面板能直接显示的只读值类型，工程内没有现成的。

using System;

namespace Game.Core.Settings
{
    /// <summary>显示分辨率（像素）。只读值类型，按宽高比较相等。</summary>
    public readonly struct DisplayResolution : IEquatable<DisplayResolution>
    {
        public DisplayResolution(int width, int height)
        {
            Width = width;
            Height = height;
        }

        /// <summary>宽（像素）。</summary>
        public int Width { get; }

        /// <summary>高（像素）。</summary>
        public int Height { get; }

        public bool Equals(DisplayResolution other)
        {
            return Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object obj)
        {
            return obj is DisplayResolution other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Width * 397) ^ Height;
        }

        /// <summary>面板显示用，形如「1920×1080」。</summary>
        public override string ToString()
        {
            return Width + "×" + Height;
        }
    }
}

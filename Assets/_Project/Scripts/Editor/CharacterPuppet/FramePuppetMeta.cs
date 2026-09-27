// 职责：序列帧目录里可选的 meta.json 的只读视图（fps、画布尺寸、脚底锚点）。
// 为什么新建（project-root.md「加能力的顺序」）：JsonUtility 要一个带序列化字段的类型来承接，
//   FramePuppetRules 是静态规则类放不下字段；一个文件一个类，所以单独成文件。
// 字段名与 scripts/ark-spine-frames/render_frames.py 写出的 meta.json 一致；缺哪个字段就当没给（哨兵 -1）。
using System;
using UnityEngine;

namespace Game.Editor.CharacterPuppet
{
    /// <summary>meta.json 的只读视图。字段缺省用 -1 表示「没给」。</summary>
    [Serializable]
    public sealed class FramePuppetMeta
    {
        private const float Missing = -1f;

        [SerializeField] private float fps = Missing;
        [SerializeField] private int frameWidth = -1;
        [SerializeField] private int frameHeight = -1;
        [SerializeField] private Point pivot = new Point();
        [SerializeField] private Point pivotPx = new Point();

        public float Fps => fps;
        public int FrameWidth => frameWidth;
        public int FrameHeight => frameHeight;
        public bool HasPivot => pivot != null && pivot.IsSet;
        public float PivotX => pivot.X;
        public float PivotY => pivot.Y;
        public bool HasPivotPx => pivotPx != null && pivotPx.IsSet;
        public float PivotPxX => pivotPx.X;
        public float PivotPxY => pivotPx.Y;

        /// <summary>解析 JSON；格式错误抛 <see cref="ArgumentException"/>（JsonUtility 的行为）。</summary>
        public static FramePuppetMeta FromJson(string json)
        {
            var meta = new FramePuppetMeta();
            JsonUtility.FromJsonOverwrite(json, meta);
            return meta;
        }

        [Serializable]
        private sealed class Point
        {
            [SerializeField] private float x = Missing;
            [SerializeField] private float y = Missing;

            public bool IsSet => x >= 0f && y >= 0f;
            public float X => x;
            public float Y => y;
        }
    }
}

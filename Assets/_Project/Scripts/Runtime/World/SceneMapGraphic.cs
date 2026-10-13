// 职责：把场景几何的 XZ 轮廓绘成俯视示意图；不创建额外相机、不逐帧重扫场景。
// 为什么新建：现有地图指引只画箭头，不能复用；扩展 PortalAnchor 会把出口组件变成绘图器。
using System.Collections.Generic;
using Game.Core.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Game.World
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SceneMapGraphic : MaskableGraphic
    {
        private readonly List<Bounds> footprints = new List<Bounds>();
        private readonly List<Color> colors = new List<Color>();
        public Bounds WorldBounds { get; private set; }
        public int FootprintCount => footprints.Count;

        public void SetGeometry(IReadOnlyList<Bounds> bounds, IReadOnlyList<Color> tints, Vector3 playerPosition)
        {
            footprints.Clear();
            colors.Clear();
            Bounds total = new Bounds(playerPosition, Vector3.one);
            for (int i = 0; i < bounds.Count; i++)
            {
                footprints.Add(bounds[i]);
                colors.Add(tints[i]);
                total.Encapsulate(bounds[i]);
            }
            total.Expand(new Vector3(2f, 0f, 2f));
            WorldBounds = total;
            SetVerticesDirty();
        }

        /// <summary>保持 XZ 比例，居中留白；世界 +Z 朝地图上方，+X 朝右。</summary>
        public static Vector2 Project(Bounds bounds, Rect area, Vector3 position)
        {
            float scale = GameMath.Min(area.width / GameMath.Max(bounds.size.x, 0.01f),
                area.height / GameMath.Max(bounds.size.z, 0.01f));
            return area.center + new Vector2(position.x - bounds.center.x, position.z - bounds.center.z) * scale;
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Rect area = rectTransform.rect;
            for (int i = 0; i < footprints.Count; i++)
            {
                Vector2 min = Project(WorldBounds, area, footprints[i].min);
                Vector2 max = Project(WorldBounds, area, footprints[i].max);
                int start = helper.currentVertCount;
                helper.AddVert(new Vector3(min.x, min.y), colors[i], Vector2.zero);
                helper.AddVert(new Vector3(min.x, max.y), colors[i], Vector2.zero);
                helper.AddVert(new Vector3(max.x, max.y), colors[i], Vector2.zero);
                helper.AddVert(new Vector3(max.x, min.y), colors[i], Vector2.zero);
                helper.AddTriangle(start, start + 1, start + 2);
                helper.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}

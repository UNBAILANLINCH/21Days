using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    // 职责：目标模型导入时按部位排列原生 BlendShapes，保留每帧几何和名字。
    // 新建原因：原生 Inspector 按 mesh 索引显示，独立窗口不能修正该列表。
    public sealed class LailaBlendShapeOrder : AssetPostprocessor
    {
        private const string ModelPath = "Assets/_Project/Art/fbx/Head-topo-expression-extended-brow-regions-refined3 1.fbx";

        private sealed class Frame
        {
            public string Name;
            public float Weight;
            public Vector3[] Vertices, Normals, Tangents;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (assetPath != ModelPath) return;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                List<Frame> frames = new();
                Dictionary<string, float> weights = new();
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    weights[name] = renderer.GetBlendShapeWeight(i);
                    for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(i); frame++)
                    {
                        Frame data = new() { Name = name, Weight = mesh.GetBlendShapeFrameWeight(i, frame),
                            Vertices = new Vector3[mesh.vertexCount], Normals = new Vector3[mesh.vertexCount], Tangents = new Vector3[mesh.vertexCount] };
                        mesh.GetBlendShapeFrameVertices(i, frame, data.Vertices, data.Normals, data.Tangents);
                        frames.Add(data);
                    }
                }
                mesh.ClearBlendShapes();
                foreach (Frame frame in frames.OrderBy(f => SortKey(f.Name)).ThenBy(f => f.Weight))
                    mesh.AddBlendShapeFrame(frame.Name, frame.Weight, frame.Vertices, frame.Normals, frame.Tangents);
                foreach (KeyValuePair<string, float> weight in weights)
                    renderer.SetBlendShapeWeight(mesh.GetBlendShapeIndex(weight.Key), weight.Value);
            }
        }

        private static int SortKey(string name)
        {
            int part = name.StartsWith("Mouth_") ? 0 : name.StartsWith("Eye_") ? 1 : name.StartsWith("Brow_") ? 2 : 3;
            int side = name.Contains("_L_") || name.Contains("LipL") ? 0 : name.Contains("_R_") || name.Contains("LipR") ? 1 : 2;
            int region = name.Contains("UpperLip") || name.Contains("UpperLid") || name.Contains("Inner") ? 1
                : name.Contains("LowerLip") || name.Contains("LowerLid") || name.Contains("Mid") ? 2 : name.Contains("Outer") ? 3 : 0;
            int direction = name.EndsWith("_Up") ? 0 : name.EndsWith("_Down") ? 1 : name.EndsWith("_Out") ? 2 : 3;
            return part * 1000 + side * 100 + region * 10 + direction; // lint-ok: 仅导入列表的显示顺序，不是玩法映射
        }
    }
}

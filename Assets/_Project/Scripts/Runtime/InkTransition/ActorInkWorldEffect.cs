// 职责：两次错拍的世界空间墨雾与一条破碎环带；仅接受预览进度，不修改角色、动画或渲染管线。
// 新建原因：全屏 UI Shader 没有场景深度；扩展 SpriteDepthClip 会再次变成衣服染色，因此独立持有网格。
// 网格数量和比例是这次试播的固定美术构成，不参与玩法数值；没有逐帧查找或粒子系统依赖。
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.InkTransition
{
    [DisallowMultipleComponent]
    public sealed class ActorInkWorldEffect : MonoBehaviour
    {
        private const int BurstLobeCount = 18;
        private const int PrimaryLobeCount = 5;
        private const int RingSegmentCount = 72;
        private const float FullCoverProgress = 0.44f;
        private const float MinimumSize = 0.01f;
        private const float DepthSurfaceMargin = 0.055f;
        private static readonly int DensityId = Shader.PropertyToID("_InkDensityTex");
        private static readonly int HasDensityId = Shader.PropertyToID("_HasDensity");
        private static readonly int AgeId = Shader.PropertyToID("_LayerProgress");
        private static readonly int OpacityId = Shader.PropertyToID("_LayerOpacity");
        private static readonly int ShapeId = Shader.PropertyToID("_ShapeKind");

        private Camera viewCamera;
        private Bounds actorBounds;
        private Material runtimeMaterial;
        private LayerGeometry firstBurst;
        private LayerGeometry secondBurst;
        private LayerGeometry ring;
        private bool initialized;
        private bool ownsRoot;
        private bool disposed;

        public bool IsInitialized => initialized;
        public bool IsVisible => initialized && ((firstBurst != null && firstBurst.Renderer.enabled)
            || (secondBurst != null && secondBurst.Renderer.enabled) || (ring != null && ring.Renderer.enabled));
        public float Progress { get; private set; }

        /// <summary>owner 仅提供层与场景；不挂到角色骨骼或 Canvas 下，父对象负责 Dispose。</summary>
        public static ActorInkWorldEffect Create(Transform owner, Bounds bounds, Camera camera,
            Shader shader, Texture densityTexture = null, int renderLayer = -1)
        {
            if (!CanInitialize(bounds, camera, shader)) return null;
            int layer = renderLayer >= 0 && renderLayer <= 31 ? renderLayer : (owner != null ? owner.gameObject.layer : 0);
            if ((camera.cullingMask & (1 << layer)) == 0) return null;
            var root = new GameObject("ActorInkWorldEffect_Runtime") { hideFlags = HideFlags.DontSave, layer = layer };
            if (owner != null)
            {
                if (owner.gameObject.scene.IsValid() && owner.gameObject.scene.isLoaded)
                    SceneManager.MoveGameObjectToScene(root, owner.gameObject.scene);
            }
            var effect = root.AddComponent<ActorInkWorldEffect>();
            effect.ownsRoot = true;
            if (effect.Initialize(bounds, camera, shader, densityTexture)) return effect;
            effect.Dispose();
            return null;
        }

        /// <summary>可重复捕获目标；外部纹理和 Shader 始终只借用，绝不销毁。</summary>
        public bool Initialize(Bounds bounds, Camera camera, Shader shader, Texture densityTexture = null)
        {
            if (disposed || !CanInitialize(bounds, camera, shader))
            {
                ReleaseResources();
                return false;
            }
            if (runtimeMaterial == null || runtimeMaterial.shader != shader)
            {
                ReleaseResources();
                runtimeMaterial = new Material(shader)
                {
                    name = "ActorInkWorld_Runtime",
                    hideFlags = HideFlags.DontSave
                };
                // 拒绝误传 UI Shader；不静默降级成永远盖在角色上面的屏幕贴片。
                if (!runtimeMaterial.HasProperty(ShapeId) || !runtimeMaterial.HasProperty(OpacityId))
                {
                    ReleaseResources();
                    return false;
                }
                firstBurst = CreateLayer("InkMist_Back", BurstLobeCount, false);
                secondBurst = CreateLayer("InkMist_Front", BurstLobeCount, false, true);
                ring = CreateLayer("InkRibbon_Ring", RingSegmentCount, true);
            }
            viewCamera = camera;
            actorBounds = bounds;
            runtimeMaterial.SetTexture(DensityId, densityTexture != null ? densityTexture : Texture2D.grayTexture);
            runtimeMaterial.SetFloat(HasDensityId, densityTexture != null ? 1f : 0f);
            initialized = true;
            Clear();
            return true;
        }

        /// <summary>由调用者合并已缓存的 SpriteRenderer bounds；这里不扫描角色层级。</summary>
        public void SetActorBounds(Bounds bounds)
        {
            if (!IsFinite(bounds.center) || !IsFinite(bounds.size)) return;
            actorBounds = bounds;
        }

        /// <summary>纯进度采样，可暂停和反复拖动；0.18 前留给角色周围起墨，0.44 全遮后隐藏。</summary>
        public void SetProgress(float normalized)
        {
            if (!initialized || disposed) return;
            if (float.IsNaN(normalized) || float.IsInfinity(normalized) || viewCamera == null
                || !viewCamera.isActiveAndEnabled || !isActiveAndEnabled)
            {
                Clear();
                return;
            }
            Progress = Mathf.Clamp01(normalized); // lint-ok: 仅独立预览的视觉进度。
            if (Progress <= 0f || Progress >= FullCoverProgress)
            {
                SetVisible(false);
                return;
            }
            Vector3 right = viewCamera.transform.right;
            Vector3 up = viewCamera.transform.up;
            Vector3 forward = viewCamera.transform.forward;
            float height = Mathf.Max(MinimumSize, ProjectedSize(actorBounds.extents, up)); // lint-ok: 世界空间视觉尺寸防除零。
            float width = Mathf.Max(height * 0.45f, ProjectedSize(actorBounds.extents, right)); // lint-ok: 墨团按身体投影宽度适配。
            UpdateBurst(firstBurst, 0, 0.005f, right, up, forward, width, height);
            UpdateBurst(secondBurst, 1, 0.065f, right, up, forward, width, height);
            UpdateRing(right, forward, width, height);
        }

        /// <summary>仅隐藏，保留已有缓冲和材质，下一次 SetProgress 可直接复用。</summary>
        public void Clear()
        {
            Progress = 0f;
            SetVisible(false);
        }

        /// <summary>终止所有权；可重复调用。Create 创建的独立根节点也一并释放。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ReleaseResources();
            if (ownsRoot) DestroyOwned(gameObject);
        }

        private LayerGeometry CreateLayer(string layerName, int quadCount, bool isRing, bool isForeground = false)
        {
            var child = new GameObject(layerName, typeof(MeshFilter), typeof(MeshRenderer))
            {
                hideFlags = HideFlags.DontSave,
                layer = gameObject.layer
            };
            child.transform.SetParent(transform, false);
            var mesh = new Mesh { name = layerName + "_Runtime", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            var geometry = new LayerGeometry(child, mesh, quadCount);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            geometry.Renderer.sharedMaterial = runtimeMaterial;
            geometry.Renderer.enabled = false;
            geometry.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            geometry.Renderer.receiveShadows = false;
            geometry.Renderer.lightProbeUsage = LightProbeUsage.Off;
            geometry.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            for (int quad = 0; quad < quadCount; quad++)
            {
                int vertex = quad * 4;
                float u0 = isRing ? (float)quad / quadCount : 0f;
                float u1 = isRing ? (float)(quad + 1) / quadCount : 1f;
                geometry.Uvs[vertex] = new Vector2(u0, 0f);
                geometry.Uvs[vertex + 1] = new Vector2(u1, 0f);
                geometry.Uvs[vertex + 2] = new Vector2(u1, 1f);
                geometry.Uvs[vertex + 3] = new Vector2(u0, 1f);
                float seed = isRing ? 0f : Sample(quad + 19);
                // 每拍五片主体、十三片游离碎墨；保留浓墨和留白，不叠成整面灰烟墙。
                float alpha = isRing ? 1f : (quad < PrimaryLobeCount ? 0.5f + 0.28f * Sample(quad + 8) : 0.3f);
                // 仅中央前墨束对齐后拍同片的峰值浓度；其余片与两层总透明度保持不变。
                if (isForeground && quad == 0) alpha = Mathf.Min(1f, alpha * 0.84f / 0.67f); // lint-ok: 单片美术浓度校准，不改全局透明度。
                // 红通道仅携带前片墨芯增密量；两侧较浓，中央少量增强，碎墨与毛边维持原浅墨。
                float coreDensity = isForeground && quad < PrimaryLobeCount ? (quad == 0 ? 0.08f : 0.55f) : 0f;
                for (int corner = 0; corner < 4; corner++)
                {
                    geometry.Seeds[vertex + corner] = new Vector2(seed, Sample(quad + 41));
                    geometry.Colors[vertex + corner] = new Color(coreDensity, 1f, 1f, alpha);
                }
                int triangle = quad * 6;
                geometry.Triangles[triangle] = vertex;
                geometry.Triangles[triangle + 1] = vertex + 1;
                geometry.Triangles[triangle + 2] = vertex + 2;
                geometry.Triangles[triangle + 3] = vertex;
                geometry.Triangles[triangle + 4] = vertex + 2;
                geometry.Triangles[triangle + 5] = vertex + 3;
            }
            mesh.vertices = geometry.Vertices;
            mesh.uv = geometry.Uvs;
            mesh.uv2 = geometry.Seeds;
            mesh.colors = geometry.Colors;
            mesh.triangles = geometry.Triangles;
            return geometry;
        }

        private void UpdateBurst(LayerGeometry layer, int burst, float start, Vector3 right,
            Vector3 up, Vector3 forward, float width, float height)
        {
            float age = Mathf.Clamp01((Progress - start) / 0.27f); // lint-ok: 固定试播美术时序。
            float opacity = Mathf.SmoothStep(0f, 1f, (Progress - start) / 0.025f) // lint-ok: 透明度采样只属于美术表现。
                * (1f - Mathf.SmoothStep(0f, 1f, (Progress - 0.32f) / 0.12f)); // lint-ok: 全遮前隐藏透明墨层。
            layer.Renderer.enabled = opacity > 0f;
            if (!layer.Renderer.enabled) return;
            float swell = 1f - Mathf.Pow(1f - age, 2f); // lint-ok: 视觉墨雾缓慢铺开，不使用弹道或冲击波。
            float depthExtent = ProjectedSize(actorBounds.extents, forward) * 0.5f;
            for (int lobe = 0; lobe < BurstLobeCount; lobe++)
            {
                float seed = Sample(lobe + burst * 37 + 3);
                float angle = lobe * 2.399963f + burst * 1.37f;
                bool fleck = lobe >= PrimaryLobeCount;
                float reach = fleck ? 0.62f : 0.23f + seed * 0.21f;
                float spread = 0.32f + swell * 0.68f;
                float side = Mathf.Cos(angle); // lint-ok: 墨雾网格几何，不参与确定性玩法。
                float rise = Mathf.Sin(angle); // lint-ok: 墨雾网格几何，不参与确定性玩法。
                // 相机 forward 正值更远。以整个 AABB 前后表面为基准，再避开角色最大 .05 世界单位的 DepthBias。
                float clearance = Mathf.Max(DepthSurfaceMargin, height * (burst == 0 ? 0.15f + 0.1f * seed : 0.18f + 0.08f * seed)); // lint-ok: 仅墨层与角色深度表面分离。
                float depth = burst == 0 ? depthExtent + clearance : -depthExtent - clearance;
                float horizontal = side * width * reach * spread;
                float vertical = rise * height * reach * 0.78f * spread + height * (age * 0.12f - 0.04f);
                float size = (fleck ? 0.035f + seed * 0.05f : 0.155f + seed * 0.105f)
                    * height * (0.48f + swell * 0.72f);
                float turn = angle * 0.37f + age * (seed - 0.5f) * 0.35f;
                float halfWidth = size;
                float halfHeight = size * (fleck ? 0.44f : 0.65f + seed * 0.4f);
                if (burst == 1)
                {
                    // 五片按浅 U 形相接：下部浓墨跨衣摆，内外两侧逐步向袖侧抬起，不再叠五条腰线。
                    float lane = lobe == 0 ? 0f : lobe == 1 ? -0.22f : lobe == 2 ? 0.22f : lobe == 3 ? -0.335f : 0.335f;
                    float arcDrop = lobe == 0 ? 0.21f : lobe < 3 ? 0.145f : 0.075f;
                    horizontal = fleck ? side * height * (0.28f + seed * 0.18f) * (0.72f + swell * 0.28f) : lane * height;
                    vertical = -height * (fleck ? 0.15f + seed * 0.18f : arcDrop + age * 0.01f);
                    halfWidth = fleck ? size : height * (lobe == 0 ? 0.235f : lobe < 3 ? 0.145f : 0.11f) * (0.78f + swell * 0.22f);
                    halfHeight = fleck ? size * 0.44f : height * (lobe == 0 ? 0.105f : lobe < 3 ? 0.078f : 0.062f) * (0.82f + swell * 0.18f);
                    float lean = lobe == 0 ? 0.02f : lobe == 1 ? -0.55f : lobe == 2 ? 0.55f : lobe == 3 ? -0.95f : 0.95f;
                    turn = fleck ? (seed - 0.5f) * 0.22f + age * 0.025f : lean + (seed - 0.5f) * 0.045f;
                }
                Vector3 axisA = (right * Mathf.Cos(turn) + up * Mathf.Sin(turn)) * halfWidth; // lint-ok: 仅旋转视觉网格。
                Vector3 axisB = (-right * Mathf.Sin(turn) + up * Mathf.Cos(turn)) // lint-ok: 仅旋转视觉网格。
                    * halfHeight;
                if (burst == 1)
                {
                    float projectedHalfHeight = Mathf.Abs(Vector3.Dot(axisA, up)) + Mathf.Abs(Vector3.Dot(axisB, up)); // lint-ok: 仅限制前层墨束上沿，给脸部留白。
                    vertical = Mathf.Min(vertical, height * 0.02f - projectedHalfHeight); // lint-ok: 前层所有顶点保持在身体中心略上方以下。
                }
                Vector3 center = actorBounds.center + right * horizontal + up * vertical + forward * depth;
                WriteQuad(layer, lobe, center - axisA - axisB, center + axisA - axisB,
                    center + axisA + axisB, center - axisA + axisB);
            }
            Upload(layer, age, opacity * (burst == 0 ? 0.84f : 0.67f), burst == 0 ? 0f : 2f);
        }

        private void UpdateRing(Vector3 cameraRight, Vector3 cameraForward, float width, float height)
        {
            float age = Mathf.Clamp01((Progress - 0.045f) / 0.29f); // lint-ok: 固定试播美术时序。
            float opacity = Mathf.SmoothStep(0f, 1f, (Progress - 0.045f) / 0.045f) // lint-ok: 环带透明度的美术时序。
                * (1f - Mathf.SmoothStep(0f, 1f, (Progress - 0.31f) / 0.13f)); // lint-ok: 全遮前渐隐环带。
            ring.Renderer.enabled = opacity > 0f;
            if (!ring.Renderer.enabled) return;
            Vector3 right = Vector3.ProjectOnPlane(cameraRight, Vector3.up);
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            right.Normalize();
            Vector3 groundForward = Vector3.Cross(right, Vector3.up).normalized;
            if (Vector3.Dot(groundForward, cameraForward) < 0f) groundForward = -groundForward;
            Vector3 center = actorBounds.center - Vector3.up * height * 0.14f;
            float radius = Mathf.Max(width * 0.66f, height * 0.56f) * (0.65f + age * 0.4f); // lint-ok: 环带是包围身体的固定世界比例。
            for (int segment = 0; segment < RingSegmentCount; segment++)
            {
                float a0 = (float)segment / RingSegmentCount * Mathf.PI * 2f; // lint-ok: 视觉环带的均匀角采样。
                float a1 = (float)(segment + 1) / RingSegmentCount * Mathf.PI * 2f; // lint-ok: 视觉环带的均匀角采样。
                WriteQuad(ring, segment,
                    RingPoint(center, right, groundForward, a0, -1f, radius, height, age),
                    RingPoint(center, right, groundForward, a1, -1f, radius, height, age),
                    RingPoint(center, right, groundForward, a1, 1f, radius, height, age),
                    RingPoint(center, right, groundForward, a0, 1f, radius, height, age));
            }
            Upload(ring, age, opacity * 0.72f, 1f);
        }

        private static Vector3 RingPoint(Vector3 center, Vector3 right, Vector3 forward,
            float angle, float edge, float radius, float height, float age)
        {
            float turn = angle + age * 0.23f;
            float warp = 1f + Mathf.Sin(angle * 3f + 0.8f) * 0.11f + Mathf.Sin(angle * 7f) * 0.055f; // lint-ok: 固定环带不规则边缘。
            float thickness = height * (0.045f + 0.04f * (0.5f + 0.5f * Mathf.Sin(angle * 5f + 1.2f))); // lint-ok: 视觉笔触厚度。
            float radial = radius * warp + edge * thickness;
            float lift = height * (Mathf.Sin(angle * 3f) * 0.035f + Mathf.Sin(angle * 5f + 0.7f) * 0.025f); // lint-ok: 环带的世界空间起伏。
            return center + right * (Mathf.Cos(turn) * radial) // lint-ok: 纯美术环带网格。
                + forward * (Mathf.Sin(turn) * radial * 0.84f) + Vector3.up * lift; // lint-ok: 纯美术环带网格。
        }

        private void WriteQuad(LayerGeometry layer, int index, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            // 顶点先在世界空间构造，再转换成根节点局部空间；任何合法根变换都不改变实际场景深度。
            int vertex = index * 4;
            layer.Vertices[vertex] = transform.InverseTransformPoint(a);
            layer.Vertices[vertex + 1] = transform.InverseTransformPoint(b);
            layer.Vertices[vertex + 2] = transform.InverseTransformPoint(c);
            layer.Vertices[vertex + 3] = transform.InverseTransformPoint(d);
        }

        private static void Upload(LayerGeometry layer, float age, float opacity, float shape)
        {
            layer.Mesh.vertices = layer.Vertices;
            layer.Mesh.RecalculateBounds();
            layer.Properties.SetFloat(AgeId, age);
            layer.Properties.SetFloat(OpacityId, opacity);
            layer.Properties.SetFloat(ShapeId, shape);
            layer.Renderer.SetPropertyBlock(layer.Properties);
        }

        private void SetVisible(bool visible)
        {
            if (firstBurst != null && firstBurst.Renderer != null) firstBurst.Renderer.enabled = visible;
            if (secondBurst != null && secondBurst.Renderer != null) secondBurst.Renderer.enabled = visible;
            if (ring != null && ring.Renderer != null) ring.Renderer.enabled = visible;
        }

        private static bool CanInitialize(Bounds bounds, Camera camera, Shader shader) => camera != null && camera.isActiveAndEnabled
            && shader != null && shader.isSupported && IsFinite(bounds.center) && IsFinite(bounds.size)
            && bounds.size.sqrMagnitude > MinimumSize * MinimumSize;

        private static bool IsFinite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static float ProjectedSize(Vector3 extents, Vector3 axis) => 2f *
            (Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z); // lint-ok: 世界包围盒的视觉投影尺寸。

        private static float Sample(int index)
        {
            float value = Mathf.Sin(index * 127.1f + 311.7f) * 43758.5453f; // lint-ok: 固定美术种子，不读取Unity随机状态。
            return value - Mathf.Floor(value); // lint-ok: 仅视觉种子的小数部分。
        }

        private void ReleaseResources()
        {
            Clear();
            initialized = false;
            ReleaseLayer(firstBurst);
            ReleaseLayer(secondBurst);
            ReleaseLayer(ring);
            firstBurst = null;
            secondBurst = null;
            ring = null;
            DestroyOwned(runtimeMaterial);
            runtimeMaterial = null;
            viewCamera = null;
        }

        private static void ReleaseLayer(LayerGeometry layer)
        {
            if (layer == null) return;
            DestroyOwned(layer.Mesh);
            DestroyOwned(layer.Root);
        }

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void OnDisable() => Clear();
        private void OnDestroy() => ReleaseResources();

        private sealed class LayerGeometry
        {
            internal GameObject Root { get; }
            internal Mesh Mesh { get; }
            internal MeshRenderer Renderer { get; }
            internal Vector3[] Vertices { get; }
            internal Vector2[] Uvs { get; }
            internal Vector2[] Seeds { get; }
            internal Color[] Colors { get; }
            internal int[] Triangles { get; }
            internal MaterialPropertyBlock Properties { get; }

            internal LayerGeometry(GameObject root, Mesh mesh, int quadCount)
            {
                Root = root;
                Mesh = mesh;
                Renderer = root.GetComponent<MeshRenderer>();
                Vertices = new Vector3[quadCount * 4];
                Uvs = new Vector2[quadCount * 4];
                Seeds = new Vector2[quadCount * 4];
                Colors = new Color[quadCount * 4];
                Triangles = new int[quadCount * 6];
                Properties = new MaterialPropertyBlock();
            }
        }
    }
}

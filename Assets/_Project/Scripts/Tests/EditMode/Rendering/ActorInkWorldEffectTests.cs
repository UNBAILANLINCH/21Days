// 职责：验证新世界墨层的可重采样几何、真实深度与独占资源清理；不宣称替代 Unity 画面验收。
// 新建原因：原 UI 预览测试不覆盖世界网格；独立组件的资源和深度契约需要直接检查。
using Game.InkTransition;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Rendering
{
    public sealed class ActorInkWorldEffectTests
    {
        private GameObject actor;
        private GameObject cameraObject;
        private ActorInkWorldEffect effect;
        private Camera camera;
        private Shader shader;
        private Bounds bounds;

        [SetUp]
        public void SetUp()
        {
            shader = Shader.Find("21Days/Effects/ActorInkWorld");
            Assert.That(shader, Is.Not.Null, "世界墨层 Shader 必须被 Unity 导入");
            Assert.That(shader.isSupported, Is.True, "需使用受支持的 URP 图形后端；不能把着色器编译失败当作跳过。");
            actor = new GameObject("ActorInkOwner");
            actor.transform.localScale = new Vector3(-2f, 3f, 1f);
            cameraObject = new GameObject("ActorInkCamera", typeof(Camera));
            camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 4f, -8f);
            bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 0.05f));
            camera.transform.LookAt(bounds.center);
            effect = ActorInkWorldEffect.Create(actor.transform, bounds, camera, shader, Texture2D.whiteTexture);
            Assert.That(effect, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            if (effect != null) effect.Dispose();
            if (actor != null) Object.DestroyImmediate(actor);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void SetProgress_StaggeredBurstsAndFullCover_CanScrubBackward()
        {
            Assert.That(effect.IsVisible, Is.False);
            effect.SetProgress(0.03f);
            Assert.That(VisibleRendererCount(), Is.EqualTo(1), "第一拍只有后侧墨雾");
            effect.SetProgress(0.12f);
            Assert.That(VisibleRendererCount(), Is.EqualTo(3), "第二拍含两层墨雾和环带");
            effect.SetProgress(0.44f);
            Assert.That(effect.IsVisible, Is.False, "全屏完全遮住后停画世界墨层");
            effect.SetProgress(0.12f);
            Assert.That(VisibleRendererCount(), Is.EqualTo(3));
            effect.SetProgress(float.NaN);
            Assert.That(effect.IsVisible, Is.False);
        }

        [Test]
        public void MeshGeometry_NegativeActorScale_HasRealFrontAndBackDepth()
        {
            effect.SetProgress(0.17f);
            Assert.That(effect.transform.parent, Is.Null, "墨层不继承角色翻面和缩放");
            MeshFilter[] filters = effect.GetComponentsInChildren<MeshFilter>();
            Assert.That(filters.Length, Is.EqualTo(3));
            int totalVertices = 0;
            float depthExtent = ProjectedExtent(bounds.extents, camera.transform.forward);
            foreach (MeshFilter filter in filters)
            {
                float nearest = float.PositiveInfinity;
                float furthest = float.NegativeInfinity;
                foreach (Vector3 local in filter.sharedMesh.vertices)
                {
                    Vector3 world = filter.transform.TransformPoint(local);
                    float depth = Vector3.Dot(world - bounds.center, camera.transform.forward);
                    nearest = Mathf.Min(nearest, depth);
                    furthest = Mathf.Max(furthest, depth);
                }
                if (filter.name == "InkMist_Back")
                    Assert.That(nearest, Is.GreaterThan(depthExtent + 0.05f), "后拍全部位于最远表面之后");
                else if (filter.name == "InkMist_Front")
                    Assert.That(furthest, Is.LessThan(-depthExtent - 0.05f), "前拍全部越过最近表面及最大角色DepthBias");
                else
                {
                    Assert.That(nearest, Is.LessThan(-0.02f), "环带应有身前顶点");
                    Assert.That(furthest, Is.GreaterThan(0.02f), "环带应有身后顶点");
                }
                totalVertices += filter.sharedMesh.vertexCount;
            }
            Assert.That(totalVertices, Is.EqualTo(432), "缓冲大小有界，不产生无限粒子");
        }

        [TestCase(0.075f)]
        [TestCase(0.12f)]
        [TestCase(0.17f)]
        [TestCase(0.30f)]
        [TestCase(0.43f)]
        public void ForegroundGeometry_ThickTiltedBounds_CrossesWaistAndKeepsFaceClear(float progress)
        {
            camera.fieldOfView = 20f;
            camera.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
            bounds = new Bounds(new Vector3(0f, 1.35f, 0f),
                new Vector3(1.65f, 2.7f * Mathf.Cos(38f * Mathf.Deg2Rad), 2.7f * Mathf.Sin(38f * Mathf.Deg2Rad)));
            camera.transform.position = bounds.center - camera.transform.forward * 18.3f;
            effect.SetActorBounds(bounds);
            effect.SetProgress(progress);
            Vector3 right = camera.transform.right;
            Vector3 up = camera.transform.up;
            Vector3 forward = camera.transform.forward;
            float depthExtent = ProjectedExtent(bounds.extents, forward);
            float height = 2f * ProjectedExtent(bounds.extents, up);
            bool checkedFront = false;
            foreach (MeshFilter filter in effect.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name != "InkMist_Front") continue;
                checkedFront = true;
                var properties = new MaterialPropertyBlock();
                filter.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                Assert.That(properties.GetFloat("_ShapeKind"), Is.EqualTo(2f), "前层必须使用连续墨脊遮罩，不能复用背雾的大镂空");
                Vector3[] vertices = filter.sharedMesh.vertices;
                float centralLeft = float.PositiveInfinity;
                float centralRight = float.NegativeInfinity;
                float faceFloor = camera.WorldToViewportPoint(bounds.center + up * height * 0.05f).y;
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 world = filter.transform.TransformPoint(vertices[index]);
                    Vector3 relative = world - bounds.center;
                    Assert.That(Vector3.Dot(relative, forward), Is.LessThan(-depthExtent - 0.05f), "每个前层顶点都越过角色最近表面和最大DepthBias");
                    Assert.That(Vector3.Dot(relative, up), Is.LessThanOrEqualTo(height * 0.0201f), "前层顶缘不能进入脸部");
                    Assert.That(camera.WorldToViewportPoint(world).y, Is.LessThan(faceFloor), "透视投影仍给脸部留白");
                    if (index >= 4) continue;
                    centralLeft = Mathf.Min(centralLeft, Vector3.Dot(relative, right));
                    centralRight = Mathf.Max(centralRight, Vector3.Dot(relative, right));
                }
                Assert.That(centralLeft, Is.LessThan(-height * 0.1f), "中央墨束应横跨身体中线");
                Assert.That(centralRight, Is.GreaterThan(height * 0.1f));
            }
            Assert.That(checkedFront, Is.True);
        }

        [Test]
        public void ForegroundGeometry_FiveMainStrokes_RiseAroundBothSleeves()
        {
            effect.SetProgress(0.2f);
            Vector3 up = camera.transform.up;
            Vector3 right = camera.transform.right;
            float height = 2f * ProjectedExtent(bounds.extents, up);
            foreach (MeshFilter filter in effect.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name != "InkMist_Front") continue;
                Vector3[] vertices = filter.sharedMesh.vertices;
                var centers = new Vector3[5];
                for (int lobe = 0; lobe < centers.Length; lobe++)
                {
                    for (int corner = 0; corner < 4; corner++)
                        centers[lobe] += filter.transform.TransformPoint(vertices[lobe * 4 + corner]) * 0.25f;
                }
                for (int side = 1; side <= 2; side++)
                {
                    Assert.That(Vector3.Dot(centers[side] - centers[0], up), Is.GreaterThan(height * 0.045f), "内侧笔触必须高于中央衣摆墨，不能再退化成平行腰线");
                    Assert.That(Vector3.Dot(centers[side + 2] - centers[side], up), Is.GreaterThan(height * 0.025f), "外侧笔触应继续向袖侧抬起");
                    float direction = side == 1 ? -1f : 1f;
                    Assert.That(Vector3.Dot(centers[side] - centers[0], right) * direction, Is.GreaterThan(height * 0.2f));
                    for (int lobe = side; lobe <= side + 2; lobe += 2)
                    {
                        Vector3 tangent = filter.transform.TransformVector(vertices[lobe * 4 + 1] - vertices[lobe * 4]);
                        Assert.That(Vector3.Dot(tangent, up) * direction, Is.GreaterThan(height * 0.09f), "左右弧段长轴须分别上扬，而非只改中心位置");
                    }
                }
                return;
            }
            Assert.Fail("必须存在独立前景网格");
        }

        [Test]
        public void ClearAndRecapture_SameShader_ReusesMeshesAndMaterial()
        {
            effect.SetProgress(0.17f);
            MeshFilter[] filters = effect.GetComponentsInChildren<MeshFilter>();
            Mesh mesh = filters[0].sharedMesh;
            Material material = filters[0].GetComponent<MeshRenderer>().sharedMaterial;
            Vector3[] expected = mesh.vertices;
            effect.SetProgress(0.31f);
            effect.SetProgress(0.17f);
            Assert.That(mesh.vertices, Is.EqualTo(expected), "来回拖动不受随机数或真实时间影响");
            effect.Clear();
            Assert.That(effect.IsVisible, Is.False);
            Assert.That(effect.Initialize(bounds, camera, shader), Is.True);
            effect.SetProgress(0.17f);
            Assert.That(filters[0].sharedMesh, Is.SameAs(mesh));
            Assert.That(filters[0].GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(material));
            Assert.That(effect.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(3));
            Vector3 delta = new Vector3(1f, 2f, -1f);
            effect.SetActorBounds(new Bounds(bounds.center + delta, bounds.size));
            effect.SetProgress(0.17f);
            Assert.That(Vector3.Distance(mesh.vertices[0], expected[0] + delta), Is.LessThan(0.0001f));
        }

        [Test]
        public void MeshGeometry_EachBurst_HasFiveMainLobesAndThirteenFlecks()
        {
            effect.SetProgress(0.17f);
            int burstCount = 0;
            foreach (MeshFilter filter in effect.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh.vertexCount != 72) continue;
                burstCount++;
                Color[] colors = filter.sharedMesh.colors;
                int mainCount = 0;
                int fleckCount = 0;
                for (int vertex = 0; vertex < colors.Length; vertex += 4)
                {
                    if (colors[vertex].a > 0.49f) mainCount++;
                    else if (Mathf.Abs(colors[vertex].a - 0.3f) < 0.001f) fleckCount++;
                }
                Assert.That(mainCount, Is.EqualTo(5));
                Assert.That(fleckCount, Is.EqualTo(13));
            }
            Assert.That(burstCount, Is.EqualTo(2));
        }

        [Test]
        public void CentralForeground_DensityCalibration_OnlyMatchesCentralRearPeak()
        {
            effect.SetProgress(0.17f);
            Color[] back = null;
            Color[] front = null;
            foreach (MeshFilter filter in effect.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name == "InkMist_Back") back = filter.sharedMesh.colors;
                else if (filter.name == "InkMist_Front") front = filter.sharedMesh.colors;
            }
            Assert.That(back, Is.Not.Null);
            Assert.That(front, Is.Not.Null);
            Assert.That(front[0].a * 0.67f, Is.EqualTo(back[0].a * 0.84f).Within(0.00001f));
            for (int vertex = 4; vertex < front.Length; vertex++)
                Assert.That(front[vertex].a, Is.EqualTo(back[vertex].a), "其余十七片不可整体加浓");
        }

        [Test]
        public void ForegroundDensity_OnlyCoreParameters_EmphasizeSidesAndKeepCenterLight()
        {
            effect.SetProgress(0.2f);
            foreach (MeshFilter filter in effect.GetComponentsInChildren<MeshFilter>())
            {
                Color[] colors = filter.sharedMesh.colors;
                for (int vertex = 0; vertex < colors.Length; vertex++)
                {
                    int lobe = vertex / 4;
                    float expected = filter.name == "InkMist_Front" && lobe < 5 ? (lobe == 0 ? 0.08f : 0.55f) : 0f;
                    Assert.That(colors[vertex].r, Is.EqualTo(expected).Within(0.00001f), "只给前拍五主片墨芯增密；中央少量、两侧较浓，后拍/环/碎片不改");
                }
            }
        }

        [Test]
        public void Dispose_RepeatedCall_ReleasesOnlyOwnedResources()
        {
            effect.SetProgress(0.12f);
            MeshFilter[] filters = effect.GetComponentsInChildren<MeshFilter>();
            Mesh mesh = filters[0].sharedMesh;
            Material material = filters[0].GetComponent<MeshRenderer>().sharedMaterial;
            effect.Dispose();
            effect.Dispose();
            Assert.That(effect == null, Is.True);
            Assert.That(mesh == null, Is.True);
            Assert.That(material == null, Is.True);
            Assert.That(actor != null, Is.True);
            Assert.That(shader != null, Is.True);
            Assert.That(Texture2D.whiteTexture != null, Is.True);
        }

        [Test]
        public void Initialize_InvalidContext_HidesAndInvalidatesPreviousTarget()
        {
            effect.SetProgress(0.12f);
            Assert.That(effect.Initialize(bounds, null, shader), Is.False);
            Assert.That(effect.IsInitialized, Is.False);
            Assert.That(effect.IsVisible, Is.False);
            Assert.That(effect.Initialize(bounds, camera, shader), Is.True);
            effect.SetProgress(0.12f);
            Assert.That(effect.IsVisible, Is.True);
        }

        [Test]
        public void SetProgress_InactiveCamera_ClearsImmediately()
        {
            effect.SetProgress(0.12f);
            camera.enabled = false;
            effect.SetProgress(0.12f);
            Assert.That(effect.IsVisible, Is.False);
            Assert.That(effect.Initialize(bounds, camera, shader), Is.False);
        }

        [Test]
        public void Create_ExplicitVisibleSpriteLayer_OverridesHiddenOwnerLayer()
        {
            effect.Dispose();
            actor.layer = 0;
            camera.cullingMask = 1 << 1;
            Assert.That(ActorInkWorldEffect.Create(actor.transform, bounds, camera, shader), Is.Null);
            effect = ActorInkWorldEffect.Create(actor.transform, bounds, camera, shader, null, 1);
            Assert.That(effect, Is.Not.Null);
            foreach (MeshRenderer renderer in effect.GetComponentsInChildren<MeshRenderer>())
                Assert.That(renderer.gameObject.layer, Is.EqualTo(1));
        }

        private int VisibleRendererCount()
        {
            int visible = 0;
            foreach (MeshRenderer renderer in effect.GetComponentsInChildren<MeshRenderer>())
                if (renderer.enabled) visible++;
            return visible;
        }

        private static float ProjectedExtent(Vector3 extents, Vector3 axis) =>
            Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;
    }
}

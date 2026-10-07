// 职责：钉住潜行内核的装配层（本波新增的 StealthInstaller）——容器能取出 StealthConfig 与 StealthKernel，
//   内核绑的就是资产里那一份配置（策划改资产会生效）、配置非法在装配处就炸、忘了拖配置时不崩。
// 为什么新建：Stealth 目录下既有的测试全是内核自身的纯规则测试（12 个类、124+ 条），
//   一条都没测过「配置怎么进内核」；而本波要修的正是这个——`StealthKernel` 此前没有调用方，
//   `EncounterStep` 跑的是占位默认值，策划改 `StealthConfig.asset` 不生效。
// 用真容器（同 GameFlowTests）。StealthInstaller 不依赖任何框架服务，容器里空着也能建。
// 真资产走 AssetDatabase：要守的正是 Boot 上拖的那条路径。
using System.Text.RegularExpressions;
using Game.Stealth;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Game.Tests.EditMode.Stealth
{
    /// <summary><see cref="StealthInstaller"/> 的装配测试。</summary>
    public sealed class StealthInstallerTests
    {
        private const string ConfigPath = "Assets/_Project/Data/Stealth/StealthConfig.asset";

        private GameObject host;
        private IObjectResolver container;

        [TearDown]
        public void TearDown()
        {
            container?.Dispose();
            container = null;
            if (host != null)
            {
                Object.DestroyImmediate(host);
            }

            host = null;
        }

        /// <summary>正向：内核取得出来，且它就是拿资产那份配置装配的（占位默认值那条路已经断了）。</summary>
        [Test]
        public void Install_WithConfigAsset_ResolvesKernelBoundToThatAsset()
        {
            StealthConfig asset = AssetDatabase.LoadAssetAtPath<StealthConfig>(ConfigPath);
            Assert.That(asset, Is.Not.Null, ConfigPath + " 读不到：资产被挪走或改名了（Boot 上拖的就是它）");

            container = Build(asset);

            Assert.That(container.Resolve<StealthConfig>(), Is.SameAs(asset));
            StealthKernel kernel = container.Resolve<StealthKernel>();
            Assert.That(kernel, Is.Not.Null);
            Assert.That(kernel.Config, Is.SameAs(asset), "内核必须绑资产那一份，否则改资产不生效");
            Assert.That(kernel.Config.ChaseSpeed, Is.EqualTo(asset.ChaseSpeed));
            Assert.That(kernel.Validate(), Is.Null, "出厂资产的数值必须自检通过（追兵速度要高于步行、不高于奔跑）");
            Assert.That(container.Resolve<StealthKernel>(), Is.SameAs(kernel), "内核是单例：遮挡体喂进去要留在同一份上");
        }

        /// <summary>资产里的数值会真的进到内核的规则对象里，而不是只停在 Config 上、被构造里的默认值盖掉。</summary>
        [Test]
        public void Install_KernelChaseRules_ComeFromTheAssetFields()
        {
            StealthConfig asset = ScriptableObject.CreateInstance<StealthConfig>();
            try
            {
                SetField(asset, "chaseLoseSightGraceSeconds", 7.5f);

                container = Build(asset);

                Assert.That(container.Resolve<StealthKernel>().Chase.Settings.LoseSightGraceSeconds,
                    Is.EqualTo(7.5f).Within(0.0001f), "跟丢宽限要按资产里改过的值装配");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// 配置非法：解析内核时就抛，且消息点名是哪条问题——配置错误不许静默跑（StealthKernel 的契约）。
        /// 这里用 2.5：`04_追逐.md:165` 记的现存缺陷值，低于玩家步行 3，追逐会失去威胁。
        /// </summary>
        [Test]
        public void Install_WithInvalidConfig_ThrowsOnResolve()
        {
            StealthConfig broken = ScriptableObject.CreateInstance<StealthConfig>();
            try
            {
                SetField(broken, "chaseSpeed", 2.5f);
                container = Build(broken);

                Assert.That(() => container.Resolve<StealthKernel>(),
                    Throws.TypeOf<System.InvalidOperationException>()
                        .With.Message.Contains("潜行配置非法")
                        .And.Message.Contains("2.5"));
            }
            finally
            {
                Object.DestroyImmediate(broken);
            }
        }

        /// <summary>忘了拖配置：记一条 Error 点名该拖哪个字段，再用代码默认值顶上（默认值自检通过，所以能建出来）。</summary>
        [Test]
        public void Install_WithoutConfig_LogsErrorAndFallsBackToCodeDefaults()
        {
            LogAssert.Expect(LogType.Error, new Regex("StealthInstaller 的 Config 字段没赋值"));

            container = Build(null);

            Assert.That(container.Resolve<StealthKernel>(), Is.Not.Null, "缺配置时按代码默认值跑，不崩");
            Assert.That(container.Resolve<StealthKernel>().Validate(), Is.Null);
        }

        /// <summary>按 Boot 的接法挂到物体上再 Install；私有序列化字段用 SerializedObject 赋值。</summary>
        private IObjectResolver Build(StealthConfig config)
        {
            host = new GameObject("StealthInstaller(测试)");
            var installer = host.AddComponent<StealthInstaller>();
            var serialized = new SerializedObject(installer);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var builder = new ContainerBuilder();
            installer.Install(builder);
            return builder.Build();
        }

        /// <summary>改私有序列化字段（资产里的占位值本身就是给策划在 Inspector 里改的）。</summary>
        private static void SetField(Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

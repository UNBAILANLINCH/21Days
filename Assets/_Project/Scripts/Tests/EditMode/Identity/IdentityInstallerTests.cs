// 职责：钉住身份内核的装配层（本波新增的 IdentityInstaller）——容器能解析出它注册的六项服务、
//   解析出来的规则绑的就是资产里的那一份数值与定义表、忘了拖配置时不崩（Error + 默认值顶上）。
// 为什么新建：Identity 目录下既有的五个测试类全是纯规则测试（自己 new 对象、自己建表），
//   没有一条走过「装进容器再取出来」；而本波的全部意义正是把这套内核接进正式流程，
//   装配层恰恰是「看着接上了、其实取到的是另一份」这类失效最容易发生的地方。
// 用真容器（VContainer 的 ContainerBuilder，同 GameFlowTests）：IdentityInstaller 只依赖 ITelemetryService，
//   补一个真实 TelemetryService + 假时钟 + 收集型 sink 就能建起来，不需要框架的其余服务。
// 真资产走 AssetDatabase（同 MonsterKindCatalogTests 用真 .bytes、SampleSceneObstacleWiringTests 读真场景）：
//   要守的就是「Boot 上拖的那份资产」这条路，测一条自己造的假路径没有意义。
using System.Text.RegularExpressions;
using Game.Core.Telemetry;
using Game.Identity;
using Game.Tests.EditMode.Telemetry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Game.Tests.EditMode.Identity
{
    /// <summary><see cref="IdentityInstaller"/> 的装配测试。</summary>
    public sealed class IdentityInstallerTests
    {
        private const string ConfigPath = "Assets/_Project/Data/Identity/IdentityConfig.asset";

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

        /// <summary>正向：装完之后六项服务全都取得到，且规则绑的是资产里那一份。</summary>
        [Test]
        public void Install_WithConfigAsset_ResolvesEveryRegisteredService()
        {
            IdentityConfig asset = AssetDatabase.LoadAssetAtPath<IdentityConfig>(ConfigPath);
            Assert.That(asset, Is.Not.Null, ConfigPath + " 读不到：资产被挪走或改名了（Boot 上拖的就是它）");

            container = Build(asset);

            IdentitySettings settings = container.Resolve<IdentitySettings>();
            IdentityCatalog catalog = container.Resolve<IdentityCatalog>();
            IdentityState state = container.Resolve<IdentityState>();
            IdentityLedger ledger = container.Resolve<IdentityLedger>();
            SuspicionState suspicion = container.Resolve<SuspicionState>();
            IdentityRules rules = container.Resolve<IdentityRules>();

            Assert.That(settings, Is.SameAs(asset.Settings), "数值块必须是资产里那一份，否则策划改资产不生效");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Count, Is.EqualTo(asset.DefinitionCount), "定义表按资产的 definitions 建");
            Assert.That(state, Is.Not.Null);
            Assert.That(ledger, Is.Not.Null);
            Assert.That(suspicion, Is.Not.Null);
            Assert.That(suspicion.Limit, Is.EqualTo(settings.SuspicionLimit), "怀疑度上限来自数值块");
            Assert.That(rules, Is.Not.Null);
            Assert.That(rules.Settings, Is.SameAs(settings));
            Assert.That(rules.Catalog, Is.SameAs(catalog), "规则与容器里那张表是同一个对象");
        }

        /// <summary>单例：两次解析拿到同一个状态对象，否则「借了身份」这件事会随解析点凭空消失。</summary>
        [Test]
        public void Install_ResolvingTwice_ReturnsTheSameSingleton()
        {
            container = Build(AssetDatabase.LoadAssetAtPath<IdentityConfig>(ConfigPath));

            Assert.That(container.Resolve<IdentityState>(), Is.SameAs(container.Resolve<IdentityState>()));
            Assert.That(container.Resolve<IdentityLedger>(), Is.SameAs(container.Resolve<IdentityLedger>()));
            Assert.That(container.Resolve<IdentityRules>(), Is.SameAs(container.Resolve<IdentityRules>()));
        }

        /// <summary>
        /// 忘了拖配置：记一条 Error 点名该拖哪个字段，然后用代码默认值顶上——不崩、不静默。
        /// 默认资产的定义表为空是合法状态（任何身份都借不到），所以这里断言的是「空表 + 服务齐备」而不是抛异常。
        /// </summary>
        [Test]
        public void Install_WithoutConfig_LogsErrorAndFallsBackToDefaults()
        {
            LogAssert.Expect(LogType.Error, new Regex("IdentityInstaller 的 Config 字段没赋值"));

            container = Build(null);

            Assert.That(container.Resolve<IdentityState>(), Is.Not.Null, "缺配置也要能建出容器");
            Assert.That(container.Resolve<IdentityLedger>(), Is.Not.Null);
            Assert.That(container.Resolve<SuspicionState>(), Is.Not.Null);
            Assert.That(container.Resolve<IdentityRules>(), Is.Not.Null);
            Assert.That(container.Resolve<IdentityCatalog>().Count, Is.EqualTo(0), "默认资产的 definitions 为空");
        }

        /// <summary>
        /// 按 Boot 的接法把注册器挂到一个物体上再调 <c>Install</c>：配置字段是私有序列化字段，
        /// 这里用 SerializedObject 赋值（同 SampleSceneObstacleWiringTests 读它的做法），
        /// 不为了测试给生产代码开一个 setter。
        /// </summary>
        private IObjectResolver Build(IdentityConfig config)
        {
            host = new GameObject("IdentityInstaller(测试)");
            var installer = host.AddComponent<IdentityInstaller>();
            var serialized = new SerializedObject(installer);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var builder = new ContainerBuilder();
            builder.RegisterInstance(TelemetryOptions.Default);
            builder.RegisterInstance(new FakeTelemetryClock()).As<ITelemetryClock>();
            builder.RegisterInstance(new ITelemetrySink[] { new RecordingTelemetrySink() });
            builder.Register<TelemetryService>(Lifetime.Singleton).As<ITelemetryService>();

            installer.Install(builder);
            return builder.Build();
        }
    }
}

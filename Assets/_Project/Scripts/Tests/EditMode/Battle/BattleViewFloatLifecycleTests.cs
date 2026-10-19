// 职责：钉住战斗界面飘字跟着界面生命周期走（PRP/combat-iteration W7c）——
//   W6b Monster 回放（批次 20261016-000310）在升级出来的回合制进行中收尾：标题面板（全屏）盖上来，UIService 把 BattleView
//   SetActive(false)；BOSS 那一下的伤害飘字恰好此时起飘，LitMotion 的 AddTo 链接件挂在一个没激活过的物体上，Awake / OnDestroy 都不跑；
//   随后根作用域销毁面板，句柄没人掐，下一帧 Bind 回调写已销毁的 RectTransform → MissingReferenceException（BattleView.cs 飘字回调）。
//   三条：① 面板被盖住时不起飘（负对照：显示着照常飘）；② 正常关闭（OnCloseAsync）把在飘的字掐掉收回；
//   ③ 面板没走关闭就被整个销毁后，回调不再写已销毁的字（测试框架把未预期的 Exception 日志判失败）。
// 为什么新建：BattleViewContractTests 钉的是 Esc / 暂停菜单契约，职责不同；BattleView 之前没有飘字的测试。
//   不读预制体（unity-tests：测试不依赖具体资产路径）：只搭飘字用到的节点，序列化私有字段经反射赋值（同 BattleActorTests）。
//   ③ 要 LitMotion 真的走帧：EditMode 下它挂在 EditorApplication.update 上（EditorMotionDispatcher），[UnityTest] 每 yield 一次走一帧；
//   而 EditMode 下 MonoBehaviour 的 Awake / OnDestroy 不跑——正好是 W6b「链接件没醒、销毁时没人掐句柄」的条件。
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Game.Battle;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleViewFloatLifecycleTests
    {
        /// <summary>飘字时长（秒）：比预制体的 0.9 短，③ 等它自然飘完不用等太久。</summary>
        private const float FloatSeconds = 0.4f;

        /// <summary>③ 等飘字开始上升的上限（真实秒）：编辑器失焦时 EditorApplication.update 会被降频。</summary>
        private const float RiseTimeoutSeconds = 5f;

        private GameObject viewHost;
        private GameObject cameraHost;
        private BattleView view;
        private RectTransform floatRoot;
        private TMP_Text template;

        [SetUp]
        public void SetUp()
        {
            cameraHost = new GameObject("StageCamera", typeof(Camera));
            cameraHost.transform.position = new Vector3(0f, 0f, -10f);

            viewHost = new GameObject("BattleView", typeof(RectTransform));
            floatRoot = new GameObject("FloatRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            floatRoot.SetParent(viewHost.transform, false);
            var templateHost = new GameObject("FloatTemplate", typeof(RectTransform));
            templateHost.transform.SetParent(floatRoot, false);
            template = templateHost.AddComponent<TextMeshProUGUI>();
            templateHost.SetActive(false); // 同 OnOpenAsync：模板隐藏，按需复制

            view = viewHost.AddComponent<BattleView>();
            SetField("floatRoot", floatRoot);
            SetField("floatTemplate", template);
            SetField("floatSeconds", FloatSeconds);
            view.Configure("测试 BOSS", 0, cameraHost.GetComponent<Camera>(), Vector3.zero, Vector3.zero);
        }

        [TearDown]
        public void TearDown()
        {
            if (viewHost != null) Object.DestroyImmediate(viewHost);
            if (cameraHost != null) Object.DestroyImmediate(cameraHost);
        }

        [Test]
        public void ShowFloat_WhileViewHidden_StartsNothing()
        {
            viewHost.SetActive(false); // 被全屏面板盖住：UIService.ApplyVisibility 对被盖住的面板 SetActive(false)

            view.ShowFloat(Vector3.zero, "-3", BattleFloatTone.Damage, false, 0);

            Assert.That(ActiveFloats(), Is.Empty, "面板没在显示：不起飘");
            Assert.That(floatRoot.childCount, Is.EqualTo(1), "也没从池里复制新字（飘字根下只有模板）");

            viewHost.SetActive(true);
            view.ShowFloat(Vector3.zero, "-3", BattleFloatTone.Damage, false, 0);
            Assert.That(ActiveFloats().Count, Is.EqualTo(1), "负对照：面板显示着就照常飘");
        }

        [Test]
        public void OnCloseAsync_WithFloatsInFlight_CancelsAndHidesThem()
        {
            view.ShowFloat(Vector3.zero, "-3", BattleFloatTone.Damage, false, 0);
            view.ShowFloat(Vector3.zero, "+2", BattleFloatTone.Heal, true, 1);
            Assert.That(ActiveFloats().Count, Is.EqualTo(2), "前置：两行字在飘");

            view.OnCloseAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(ActiveFloats(), Is.Empty, "关面板：在飘的字当场掐掉并收回池里（隐藏），之后不再有回调写它们");
        }

        [UnityTest]
        public IEnumerator ShowFloat_ViewDestroyedWithoutClosing_CallbackStopsWriting()
        {
            view.ShowFloat(Vector3.zero, "-3", BattleFloatTone.Damage, false, 0);
            List<TMP_Text> floats = ActiveFloats();
            Assert.That(floats.Count, Is.EqualTo(1), "前置：起飘了一行字");
            RectTransform rect = floats[0].rectTransform;
            float startY = rect.anchoredPosition.y;

            // 前置：EditMode 下 LitMotion 确实在走帧——不然后面「销毁后没报错」就是空过。
            float riseDeadline = Time.realtimeSinceStartup + RiseTimeoutSeconds;
            while (rect.anchoredPosition.y <= startY + 0.01f && Time.realtimeSinceStartup < riseDeadline)
            {
                yield return null;
            }

            Assert.That(rect.anchoredPosition.y, Is.GreaterThan(startY + 0.01f), "前置：飘字在上升（补间随编辑器帧推进）");

            // 面板没走 OnCloseAsync 就被整个销毁（W6b：根作用域销毁时 UIService 直接释放面板实例）。
            Object.DestroyImmediate(viewHost);
            viewHost = null;

            // 再走到飘字时长结束之后：期间每帧的 Bind 与结束时的 OnComplete 若还写已销毁的字，
            // LitMotion 会 Debug.LogException（MissingReferenceException），测试框架把它判为失败。
            float until = Time.realtimeSinceStartup + FloatSeconds + 0.3f;
            int frames = 0;
            while (Time.realtimeSinceStartup < until || frames < 3)
            {
                frames++;
                yield return null;
            }

            Assert.That(rect == null, Is.True, "飘字随面板一起销毁了（回调里面对的就是已销毁的对象）");
        }

        [Test]
        public void OnDisable_WithFloatsInFlight_CancelsAndReturnsToPool()
        {
            view.ShowFloat(Vector3.zero, "-3", BattleFloatTone.Damage, false, 0);
            Assert.That(ActiveFloats().Count, Is.EqualTo(1));
            // EditMode不会自动派发MonoBehaviour隐藏消息，显式派发真实生命周期入口。
            typeof(BattleView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
            Assert.That(ActiveFloats(), Is.Empty, "覆盖战斗面板时应停止短动效并收回文字");
        }

        private List<TMP_Text> ActiveFloats()
        {
            var found = new List<TMP_Text>();
            for (int i = 0; i < floatRoot.childCount; i++)
            {
                Transform child = floatRoot.GetChild(i);
                if (child == template.transform || !child.gameObject.activeSelf) continue;
                found.Add(child.GetComponent<TMP_Text>());
            }

            return found;
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(BattleView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"前置：BattleView 的序列化字段名是 {name}");
            field.SetValue(view, value);
        }
    }
}

// 收卷依赖真实帧与 LitMotion；现有格式化测试不能验证裁切、轴尺寸及取消复位。
using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Session;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.PlayMode.Session
{
    public sealed class SaveSlotsLoadTransitionTests
    {
        private GameObject root;
        private SaveSlotsView view;
        private RectTransform list;
        private Texture2D texture;
        private Sprite sprite;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            root = new GameObject("Slots", typeof(RectTransform), typeof(CanvasGroup));
            view = root.AddComponent<SaveSlotsView>();
            list = Child("List",root.transform);
            RectTransform row = Child("Row",list);
            row.sizeDelta = new Vector2(10,100);
            var button = row.gameObject.AddComponent<Button>();
            RectTransform mask = Child("RollMask",row);
            mask.sizeDelta = row.sizeDelta;
            mask.gameObject.AddComponent<RectMask2D>().enabled = false;
            RectTransform paper = Child("Paper",mask);
            paper.sizeDelta = row.sizeDelta;
            var image = paper.gameObject.AddComponent<Image>();
            texture = new Texture2D(10,100);
            sprite = Sprite.Create(texture,new Rect(0,0,10,100),new Vector2(0.5f,1f));
            image.sprite = sprite; button.targetGraphic = image;
            Child("Slot",row).gameObject.AddComponent<TextMeshProUGUI>();
            Child("Primary",paper).gameObject.AddComponent<TextMeshProUGUI>();
            Child("Secondary",paper).gameObject.AddComponent<TextMeshProUGUI>();
            Child("Delete",row).gameObject.AddComponent<Button>();
            Child("RollFooter",row).gameObject.AddComponent<RawImage>().gameObject.SetActive(false);
            Set("listRoot",list); Set("rowTemplate",row.gameObject); Set("rollFooterHeight",10f);
            view.Bind(new[] { new SlotInfo(1,SlotState.Available,System.DateTime.UtcNow,0,"进度") },SlotsMode.Load);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = previousTimeScale;
            Object.Destroy(root); Object.Destroy(sprite); Object.Destroy(texture);
        }

        [UnityTest]
        public IEnumerator PausedRoll_ClipsPaperAndKeepsFooterSize() => UniTask.ToCoroutine(async () =>
        {
            Time.timeScale = 0f;
            await view.PlayLoadTransitionAsync(1,0.1f,CancellationToken.None);
            var row = list.GetChild(1);
            Assert.That(row.Find("RollMask").GetComponent<RectTransform>().sizeDelta.y,Is.Zero);
            Assert.That(row.Find("RollFooter").GetComponent<RectTransform>().sizeDelta,Is.EqualTo(new Vector2(10,10)));
            Assert.That(row.localScale,Is.EqualTo(Vector3.one));
            Assert.That(root.GetComponent<CanvasGroup>().interactable,Is.False);
            Assert.That(root.GetComponent<CanvasGroup>().blocksRaycasts,Is.False);
            view.ResetLoadTransition();
            Assert.That(row.Find("RollMask").GetComponent<RectTransform>().sizeDelta.y,Is.EqualTo(100f));
            Assert.That(row.Find("RollFooter").gameObject.activeSelf,Is.False);
            Assert.That(root.GetComponent<CanvasGroup>().interactable,Is.True);
            Assert.That(root.GetComponent<CanvasGroup>().blocksRaycasts,Is.True);
        });

        [UnityTest]
        public IEnumerator CancelRoll_RestoresPaperAndInteraction() => UniTask.ToCoroutine(async () =>
        {
            using (var cancel = new CancellationTokenSource())
            {
                var task = view.PlayLoadTransitionAsync(1,0.4f,cancel.Token);
                await UniTask.Delay(50,ignoreTimeScale:true);
                cancel.Cancel();
                Assert.That(await task.SuppressCancellationThrow(),Is.True);
                var row = list.GetChild(1);
                Assert.That(row.Find("RollMask").GetComponent<RectMask2D>().enabled,Is.False);
                Assert.That(row.Find("RollFooter").gameObject.activeSelf,Is.False);
                Assert.That(root.GetComponent<CanvasGroup>().interactable,Is.True);
            }
        });

        private void Set(string name,object value) => typeof(SaveSlotsView).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(view,value);
        private static RectTransform Child(string name,Transform parent)
        {
            var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false); return rect;
        }
    }
}

// 职责：ShowcaseScenario 的「Boot 真实流程」部分（partial）——从标题点「开始」进世界、收尾时经流程退回标题并销毁根作用域。
//   让回放走和玩家一样的入口：Boot → 标题 → 「开始」→ MonsterEncounterState 经 Addressables 加载 SampleScene → 容器里的服务驱动玩家。
//
// 用法：
//   protected override string ScenePath => null;          // 世界由流程加载，基类不要再叠一份 SampleScene
//   protected override bool LoadBootScene => true;         // 默认就是 true
//   [UnityTest] public IEnumerator Xxx()
//   {
//       yield return EnterWorldFromTitle();                // 等标题 → 点「开始」→ 等进世界 → 等相机
//       yield return WaitUntil("模块自己的就绪条件", () => ..., 20f);
//       ...
//   }
//   收尾不用写：基类 ShowcaseTearDown 在 LoadBootScene 为真时自动退回标题并销毁所有 GameLifetimeScope（幂等）。
//   用例中途要回标题（验「继续」这类）用 BeginLeaveToTitle(done)，自己 WaitUntil done。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— Exploration / Session / ScenePerformance 各自复制了一份进场 + 收尾样板，没有公共件可复用。
//   扩展 —— 进场 / 收尾属于基类（要调 Step / WaitUntil 等 protected 成员、要挂进 ShowcaseTearDown），
//           但塞进 ShowcaseScenario.cs 会让「报告节奏引擎」与「游戏启动流程」混在一个文件里；
//           按职责拆成 partial：同一个类、单独一个文件。
using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.UI;
using Game.Core.UI.Views;
using Game.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Tests.Showcase
{
    public abstract partial class ShowcaseScenario
    {
        /// <summary>
        /// 根作用域销毁后额外等待的真实秒数。默认 0（只等一帧）。
        /// 离场会触发 Forget 出去的异步写盘（Session 的离场保存）时覆写成 0.5 左右，
        /// 免得写盘落在基类删除临时存档目录之后再报错。
        /// </summary>
        protected virtual float BootShutdownSettleSeconds
        {
            get { return 0f; }
        }

        /// <summary>
        /// 从标题「开始」进世界，走 Boot 真实流程：
        /// ① 等流程停在 <see cref="TitleState"/>、标题界面已打开；
        /// ② 先把虚拟手柄与键盘一起建出来（<see cref="ShowcaseInputDriver.Prime"/>：设备中途加入会复位已按住的动作），
        ///    再点标题界面下的「StartButton」（记一步，hold 0）；
        /// ③ 等标题关闭、流程离开标题、SampleScene 已加载、<see cref="PlayerModel"/> 可解析；
        /// ④ 「等相机跟到玩家」停 1 秒（记一步）。
        /// 模块特有的就绪条件（HUD、箱子登记、存档槽……）在这之后自己 <c>WaitUntil</c>。
        /// <para>前提：<see cref="LoadBootScene"/> 为真、<see cref="ScenePath"/> 返回 null（SampleScene 由流程加载；
        /// 基类再加载一份会叠出两个 SampleScene）。</para>
        /// </summary>
        /// <param name="bootTimeout">等标题就绪的上限（秒，不乘节奏倍率）。</param>
        /// <param name="enterTimeout">点「开始」后等进世界的上限（秒，不乘节奏倍率）。</param>
        /// <param name="startStepTitle">「点开始」这一步的标题；null 用默认「点标题界面「开始」」。
        /// 给需要在标题里写清前提的用例（例如「三个槽都空：新游戏应落在槽 1」）。</param>
        protected IEnumerator EnterWorldFromTitle(float bootTimeout = 20f, float enterTimeout = 20f, string startStepTitle = null)
        {
            if (!string.IsNullOrEmpty(ScenePath))
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] EnterWorldFromTitle 要求 ScenePath 返回 null："
                                 + $"当前 {ScenePath} 已由基类加载，流程再加载一份会叠出两个场景");
            }

            yield return WaitUntil(
                "流程停在标题状态、标题界面已打开",
                () =>
                {
                    IGameFlow flow = ResolveService<IGameFlow>();
                    IUIService ui = ResolveService<IUIService>();
                    return flow != null && flow.Current is TitleState && ui != null && ui.Get<TitleView>() != null;
                },
                bootTimeout);

            Input.Prime();
            yield return Step(
                string.IsNullOrEmpty(startStepTitle) ? "点标题界面「开始」" : startStepTitle,
                () => RequireTitleButton("StartButton").onClick.Invoke(),
                0f);

            string worldScene = DemoSceneName();
            yield return WaitUntil(
                $"进入世界：标题关闭、流程离开标题、场景 {worldScene} 已加载、玩家模型可用",
                () =>
                {
                    IGameFlow flow = ResolveService<IGameFlow>();
                    IUIService ui = ResolveService<IUIService>();
                    return flow != null && !(flow.Current is TitleState)
                           && ui != null && ui.Get<TitleView>() == null
                           && SceneManager.GetSceneByName(worldScene).isLoaded
                           && ResolveService<PlayerModel>() != null;
                },
                enterTimeout);

            yield return Step("等相机跟到玩家", null, 1f);
        }

        /// <summary>
        /// 取标题界面下名为 <paramref name="objectName"/> 的按钮（StartButton / ContinueButton / LoadButton…）。
        /// 标题没开或找不到就抛 <see cref="InvalidOperationException"/>——放在 <c>Step</c> 的 act 里调用时只记这一步失败。
        /// </summary>
        protected Button RequireTitleButton(string objectName)
        {
            IUIService ui = ResolveService<IUIService>();
            TitleView title = ui == null ? null : ui.Get<TitleView>();
            Button button = title == null ? null : FindDeep<Button>(title.transform, objectName);
            if (button == null)
            {
                throw new InvalidOperationException($"标题界面没开，或找不到「{objectName}」");
            }

            return button;
        }

        /// <summary>
        /// 后台经流程切回标题，完成（无论成败）时回调 <paramref name="done"/>；异常只记 Warning。
        /// 用例中途要回标题时用（同一帧里先拍期望快照再调它，离场保存捕获的现场就与快照一致），之后自己 WaitUntil。
        /// </summary>
        protected void BeginLeaveToTitle(IGameFlow flow, Action done)
        {
            LeaveToTitleAsync(flow, Module, done).Forget();
        }

        /// <summary>
        /// 等加载黑幕完全揭开（<see cref="ILoadingCurtain.IsCovered"/> 变回 false）。只在「场景重载之后要截图」的步骤前调：
        /// 重载完成的条件（新场景对象出现、遭遇重新开始）往往早于揭幕结束，不等的话截到的是半透明黑幕下的画面。
        /// 超时或解析不到黑幕按检查点失败记录（同 <see cref="WaitUntil"/>，不中断），报告里写明原因。
        /// </summary>
        /// <param name="timeout">最多等多久（秒，不乘节奏倍率）。揭幕本身只有 UIConfig.LoadingFadeSeconds（默认 0.25 秒）。</param>
        protected IEnumerator WaitCurtainRevealed(float timeout = 5f)
        {
            ILoadingCurtain curtain = ResolveService<ILoadingCurtain>();
            if (curtain == null)
            {
                yield return Check("能从根容器解析出 ILoadingCurtain（解析不到就无法确认加载黑幕已揭开，截图可能发暗）", () => false);
                yield break;
            }

            yield return WaitUntil(
                "加载黑幕完全揭开（ILoadingCurtain.IsCovered 变回 false；超时说明黑幕卡在屏上或揭幕没做完，截图会发暗）",
                () => !curtain.IsCovered,
                timeout);
        }

        /// <summary>在 <paramref name="root"/> 下按物体名递归找组件（含未激活）；找不到返回 null。</summary>
        protected static T FindDeep<T>(Transform root, string objectName) where T : Component
        {
            if (root == null)
            {
                return null;
            }

            T[] candidates = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].name == objectName)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 收尾：流程不在标题就先经 <c>GoToAsync&lt;TitleState&gt;</c> 退回（让遭遇状态自己卸载场景、释放 Addressables 句柄；
        /// 直接销毁根作用域会留下被强制释放的场景句柄，下一条用例点「开始」时场景加载不出来——实测），
        /// 再按类型名销毁所有 GameLifetimeScope 的物体、等一帧。
        /// <b>幂等</b>：子类自己的 DestroyBootScope 已经销毁过时，这里取不到流程也找不到实例，静默跳过。
        /// </summary>
        private IEnumerator ShutdownBootFlow()
        {
            IGameFlow flow = ResolveService<IGameFlow>();
            if (flow != null && !(flow.Current is TitleState))
            {
                bool left = false;
                BeginLeaveToTitle(flow, () => left = true);
                float deadline = Time.realtimeSinceStartup + 20f;
                while (!left && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (!left)
                {
                    Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] 收尾切回标题 20 秒没完成，直接销毁根作用域");
                }
            }

            Type scopeType = Type.GetType(ScopeTypeName);
            if (scopeType == null)
            {
                yield break;
            }

            UnityEngine.Object[] scopes = UnityEngine.Object.FindObjectsOfType(scopeType);
            if (scopes.Length == 0)
            {
                yield break;
            }

            for (int i = 0; i < scopes.Length; i++)
            {
                Component component = scopes[i] as Component;
                if (component != null)
                {
                    UnityEngine.Object.Destroy(component.gameObject);
                }
            }

            yield return null;
            if (BootShutdownSettleSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(BootShutdownSettleSeconds);
            }
        }

        private static async UniTaskVoid LeaveToTitleAsync(IGameFlow flow, string module, Action done)
        {
            try
            {
                await flow.GoToAsync<TitleState>();
            }
            catch (Exception e)
            {
                // 只能 Warning：LogError 会被 UTF 当未预期错误打断收尾。
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{module}] 切回标题失败：{e.GetType().Name}：{e.Message}");
            }
            finally
            {
                if (done != null)
                {
                    done();
                }
            }
        }

        /// <summary>回放舞台的场景名（由 <see cref="ShowcaseOptions.DemoScenePath"/> 推出，= "SampleScene"）。</summary>
        private static string DemoSceneName()
        {
            return System.IO.Path.GetFileNameWithoutExtension(ShowcaseOptions.DemoScenePath);
        }
    }
}

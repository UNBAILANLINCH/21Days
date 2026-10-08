// 职责：战斗期间的相机交接——关掉世界里正在渲染的相机、打开战斗相机；收场只把「进来时开着、被我们关掉的」相机重新打开。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：PerformanceService 的世界舞台是「主相机保持 enabled、遮罩清零、舞台相机叠深度」，那是演出在世界里就地播；
//      战斗舞台在远离世界原点的另一张场景里，世界画面整段不要，按 PRP W2a 口径直接关掉世界相机。
//      FallbackCamera 只在场景加载 / 卸载时让位，战斗中途不会被触发，也不该承担这件事。
//   2. 扩展不行：塞进 BattleScenePresenter 就得带着场景跑才测得到「只恢复进来前的状态」；拆成普通类，EditMode 直接 new 相机测。
// 只关 Camera 组件、不关物体：世界相机上的 AudioListener 照常工作（战斗场景不放第二个 AudioListener），
//   跟随脚本等其它组件也不受影响。期间 Camera.main 为 null——战斗相机故意不打 MainCamera 标签：
//   QuestSceneBinder 等在场景加载 / 卸载时缓存 Camera.main，打了标签会把这台随场景销毁的相机缓存进去。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Battle
{
    /// <summary>相机交接（一次战斗一个实例，或复用：<see cref="Release"/> 后可再 <see cref="Take"/>）。</summary>
    public sealed class BattleCameraHandoff
    {
        private readonly List<Camera> switchedOff = new List<Camera>();
        private Camera stage;
        private bool stageWasEnabled;
        private bool active;

        /// <summary>正在接管中。</summary>
        public bool IsActive => active;

        /// <summary>这次接管关掉了几台相机。</summary>
        public int SwitchedOffCount => switchedOff.Count;

        /// <summary>
        /// 接管：<paramref name="others"/> 里开着的相机（跳过战斗相机自己与已销毁的）逐个关掉并记下，再打开战斗相机。
        /// 已在接管中再调用是空操作（返回 false），不会把「关掉的名单」覆盖掉。
        /// </summary>
        public bool Take(Camera stageCamera, IReadOnlyList<Camera> others)
        {
            if (active) return false;
            if (stageCamera == null) throw new System.ArgumentNullException(nameof(stageCamera));

            stage = stageCamera;
            stageWasEnabled = stageCamera.enabled;
            switchedOff.Clear();
            if (others != null)
            {
                for (int i = 0; i < others.Count; i++)
                {
                    Camera camera = others[i];
                    if (camera == null || camera == stageCamera || !camera.enabled) continue;
                    camera.enabled = false;
                    switchedOff.Add(camera);
                }
            }

            stageCamera.enabled = true;
            active = true;
            return true;
        }

        /// <summary>
        /// 交还：战斗相机回到接管前的开关，被关掉的相机重新打开（期间已销毁的跳过）。幂等；没接管过是空操作。
        /// </summary>
        public void Release()
        {
            if (!active) return;
            active = false;
            try
            {
                if (stage != null) stage.enabled = stageWasEnabled;
            }
            finally
            {
                for (int i = 0; i < switchedOff.Count; i++)
                {
                    if (switchedOff[i] != null) switchedOff[i].enabled = true;
                }

                switchedOff.Clear();
                stage = null;
            }
        }

        /// <summary>
        /// 让战斗相机的「底色」与世界一致：清屏方式、背景色（SampleScene 里等于雾色）、后处理开关、Volume 遮罩、抗锯齿。
        /// 透视参数与位姿保留场景里摆好的值；渲染器索引在 BattleArena 场景里已设成 1（UniversalRenderer，高低两档同号）。
        /// <paramref name="world"/> 为空时什么都不做。
        /// </summary>
        public static void CopyLook(Camera world, Camera stageCamera)
        {
            if (world == null || stageCamera == null) return;
            stageCamera.clearFlags = world.clearFlags;
            stageCamera.backgroundColor = world.backgroundColor;
            // 不用 GetUniversalAdditionalCameraData 取世界相机：那个扩展在缺组件时会 AddComponent，改到世界相机上（同 PerformanceService）。
            if (world.TryGetComponent(out UniversalAdditionalCameraData worldData)
                && stageCamera.TryGetComponent(out UniversalAdditionalCameraData stageData))
            {
                stageData.renderPostProcessing = worldData.renderPostProcessing;
                stageData.volumeLayerMask = worldData.volumeLayerMask;
                stageData.antialiasing = worldData.antialiasing;
            }
        }
    }
}

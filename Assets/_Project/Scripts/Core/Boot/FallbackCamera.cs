// 职责：Boot 常驻场景里那台兜底相机的「让位」——玩法场景带了自己的相机时自动禁用自己，没有时再启用。
// 为什么不复用：SceneGameState 只管场景的加载与卸载，不知道也不该知道场景里有哪些相机。
// 为什么不扩展 GameBootstrap：它是启动入口，只负责串行初始化服务、进标题状态，不该管渲染。
// 所以新建一个挂在相机物体上的小组件，只靠场景加载 / 卸载事件驱动，不每帧轮询。

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Core.Boot
{
    /// <summary>
    /// 兜底相机。挂在 Boot 场景的 Main Camera 上。
    /// 玩法场景带相机时自动让位（禁用自身，避免在玩法相机之上重画、也让 <c>Camera.main</c> 取到玩法相机）；
    /// 场景卸载回标题、再没有别的相机时恢复启用，保证标题界面有东西画。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FallbackCamera : MonoBehaviour
    {
        private Camera own;

        private void Awake()
        {
            own = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            Refresh();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Refresh();
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            Refresh();
        }

        /// <summary>
        /// 有「除自己以外」的启用相机就禁用自己，否则启用自己。
        /// 注意 <c>Camera.allCameras</c> 只含启用的相机：自己被禁用后不在里面，所以判断必须排除自己而不是看数量。
        /// </summary>
        private void Refresh()
        {
            if (own == null)
            {
                own = GetComponent<Camera>();
            }

            bool otherCameraExists = false;
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != own)
                {
                    otherCameraExists = true;
                    break;
                }
            }

            own.enabled = !otherCameraExists;
        }
    }
}

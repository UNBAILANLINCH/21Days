// 职责：输入服务的对外接口，玩法通过它拿到生成的 GameInput 动作集。
// 为什么新建：GameInput 是 Input System 生成物（不手改），需要一个手写的外壳负责它的
// 创建、启用与释放；工程内没有这样的接口。

namespace Game.Core.Input
{
    /// <summary>
    /// 输入服务。玩法只读动作（Actions.Gameplay.Move 这类），
    /// 不读具体按键、不读 Input.touches、不自己 new GameInput。
    /// </summary>
    public interface IInputService
    {
        /// <summary>生成的动作集。初始化之前为 null。</summary>
        GameInput Actions { get; }

        /// <summary>启用一个 Action Map（"Gameplay" / "UI"）。名字不存在时记 Warn 并忽略。</summary>
        void EnableMap(string map);

        /// <summary>禁用一个 Action Map。</summary>
        void DisableMap(string map);
    }
}

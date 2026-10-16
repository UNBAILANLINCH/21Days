// 职责：同一焦点对象的特殊动作（F / 手柄 East / HUD 第二按钮），与普通交互 E 分离。
namespace Game.Interaction
{
    public interface ISecondaryInteractable
    {
        bool CanInteractSecondary { get; }
        InteractionPrompt SecondaryPrompt { get; }
        void InteractSecondary();
    }
}

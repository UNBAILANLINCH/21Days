// 职责：仅在 Showcase 中把 Gameplay/Move 应用到 3D Rigidbody，保留重力与 3D 碰撞。
// 为什么新建：现有 IsometricPlayerController 已服务 2D 验证场景，改造会破坏其 Rigidbody2D 接线。
using Game.IsometricExploration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Tests.Showcase.IsometricExploration
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(PlayerInput))]
    public sealed class IsometricPlayerController3D : MonoBehaviour
    {
        [SerializeField] private IsometricExplorationConfig config;

        private Rigidbody body;
        private Vector2 moveInput;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.freezeRotation = true;
        }

        private void FixedUpdate()
        {
            float moveSpeed = config == null ? 0f : config.MoveSpeed;
            body.velocity = new Vector3(
                moveInput.x * moveSpeed,
                body.velocity.y,
                moveInput.y * moveSpeed);
        }

        public void SetMoveInput(Vector2 direction)
        {
            moveInput = Vector2.ClampMagnitude(direction, 1f);
        }

        public void Stop()
        {
            moveInput = Vector2.zero;
        }

        private void OnMove(InputValue value)
        {
            SetMoveInput(value.Get<Vector2>());
        }

        private void OnDisable()
        {
            Stop();
            if (body != null)
            {
                body.velocity = new Vector3(0f, body.velocity.y, 0f);
            }
        }
    }
}

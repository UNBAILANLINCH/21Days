// 职责：拼接小人的表现门面——持有分件 SpriteRenderer 与 Animator，对外只暴露朝向、移动状态、整体染色三个口子。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：EncounterSceneView 只驱动单张纸片（位置、flipX、状态色），没有分件与 Animator 概念；
//   2. 扩展不行：把分件动画塞进 EncounterSceneView 会让遭遇视图依赖具体美术结构，以后换 Spine 要改遭遇代码。
using UnityEngine;

namespace Game.CharacterPuppet
{
    [DisallowMultipleComponent]
    public sealed class ChibiPuppet : MonoBehaviour
    {
        private static readonly int MovingHash = Animator.StringToHash("Moving");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        [SerializeField] private Animator animator;
        [Tooltip("全部分件渲染器（腿、躯干、臂、头、发）；SetTint 以各自预制体里的颜色为基底相乘")]
        [SerializeField] private SpriteRenderer[] parts;

        private Color[] baseColors;
        private float baseScaleX = 1f;
        private bool faceLeft;
        private bool moving;

        public Animator Animator => animator;
        public bool FaceLeft => faceLeft;
        public bool IsMoving => moving;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            float scaleX = transform.localScale.x;
            baseScaleX = scaleX < 0f ? -scaleX : scaleX;
            faceLeft = scaleX < 0f;

            int count = parts == null ? 0 : parts.Length;
            baseColors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                baseColors[i] = parts[i] == null ? Color.white : parts[i].color;
            }
        }

        /// <summary>翻面：根节点 localScale.x 取 ±原始幅值，保留实例上的整体缩放。</summary>
        public void SetFacing(bool left)
        {
            faceLeft = left;
            Vector3 scale = transform.localScale;
            scale.x = left ? -baseScaleX : baseScaleX;
            transform.localScale = scale;
        }

        /// <summary>写 Animator 参数：Moving 切待机 / 走路，playbackRate 写入 Speed 作为走路状态的播放速率。</summary>
        public void SetMoving(bool isMoving, float playbackRate)
        {
            moving = isMoving;
            if (animator == null)
            {
                return;
            }

            animator.SetBool(MovingHash, isMoving);
            animator.SetFloat(SpeedHash, playbackRate);
        }

        /// <summary>整体染色：各分件颜色 = 预制体基底色 × tint；传白色即还原。</summary>
        public void SetTint(Color tint)
        {
            if (parts == null || baseColors == null)
            {
                return;
            }

            for (int i = 0; i < parts.Length && i < baseColors.Length; i++)
            {
                if (parts[i] != null)
                {
                    parts[i].color = baseColors[i] * tint;
                }
            }
        }
    }
}

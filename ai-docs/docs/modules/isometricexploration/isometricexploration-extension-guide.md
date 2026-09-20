---
type: extension-guide
module: isometricexploration
layer: runtime
maturity: stable
---

# IsometricExploration 扩展指南

## 更换玩家或敌人纸片

1. 保留 `player` 与 `enerme` / `enemy` 根对象名，或同步修改适配器查找契约。
2. 让可见子节点拥有启用的 `SpriteRenderer` 和 `CameraBillboard`。
3. Rigidbody、Collider 留在根节点，纸片倾斜只发生在 Visual。
4. 运行 IsometricExploration Showcase，检查潜行色、敌对色与死亡色。

## 调整巡逻范围

当前适配器以敌人初始位置为第一个点，沿世界 X 正方向四单位创建第二个点。
若需要策划布点，优先在场景增加显式 Marker，再让适配器读取；不要把巡逻点反推自碰撞体或画面位置。

## 验证

坐标映射与适配器接线放 EditMode 测试；玩家可见行为放 IsometricExploration Showcase。
场景资产只通过 Unity 编辑器或 Unity MCP 修改，不手改 YAML。

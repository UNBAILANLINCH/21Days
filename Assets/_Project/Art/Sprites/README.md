# Sprites — 图片

角色、场景、UI 切图、图标，所有 2D 图片放这里。

**放进来会自动发生什么**：图片一进这个目录（含任意子目录），
`Scripts/Editor/Importers/SpriteImportProcessor.cs` 会在**首次导入**时自动设成
**Sprite 类型 / 每单位像素 100 / Bilinear 过滤 / 压缩 / 生成 mipmap**（高清手绘纸片预设，不是像素风），Console 会打一行中文提示。
之后你在 Inspector 里手调的参数**不会被覆盖**；反过来，规则改了老图也不会自动跟上，需要右键 **Reimport**。
放进别的目录 = 一条规则都不生效。

**子目录**：`Characters/` 角色纸片，贴图 **Pivot 设为 Bottom**（根节点对齐脚底），配套材质见
`Art/Materials/Character/`；`Fx/` 脚下贴片（`BlobShadow`、`SelectRing` 等）与其它特效图；`Dialogue/` 对话相关立绘/图标。

**命名**：全小写 + 下划线，`类别_名字_状态`，例如 `ui_btn_start_normal.png`、`chr_player_idle_01.png`。
不用空格、不用中文。

**不要放**：音频、预制体、材质、字体；单张超过 2048×2048 的贴图（会被「资产体检」报出来）。

**不要在资源管理器里改名 / 移动 / 删除**——`.meta` 掉队会让所有引用静默失效，一律在 Unity 的 Project 窗口里操作。

详见 [`docs/artist-guide.md`](../../../../docs/artist-guide.md) 第 3、4、5 章。

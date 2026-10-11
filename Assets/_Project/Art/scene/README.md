# scene — 3D 场景资产

美术交付的 3D 场景资产放这里 —— 美术那边的「传送专用文件夹」就是这个目录。

## 怎么组织：一个交付一个目录

```
Art/scene/
  env_well/                  ← 一次交付 = 一个目录，目录名 = 资产名（env_well.glb 的 env_well）
    env_well.glb             ← Unity 用的那一份
    env_well.fbx             ← 美术的对照件（可无）
    env_well_colors.txt      ← 配色清单等参考资料（可无）
    README.md                ← 这一份资产自己的说明
  README.md                  ← 本文件
```

**为什么按资产分而不是按文件类型分**：一次 3D 交付天然是一个整体 —— 模型、对照件、配色清单、
说明是同一件事的几面，散到 `Models/` `Refs/` 里就再也拼不回「这是哪一版的谁」。
工程里已有同样的先例：`Art/Sprites/Characters/<名字>/`、`Art/Animations/Characters/<名字>/`。
新资产进来时**新建一个同名目录**，不要往别人目录里放。

**放进来会自动发生什么**：**没有自动规则**，这里只是约定的位置。
`.glb` / `.fbx` 在 `.gitattributes` 里已按二进制处理；`.glb` 由 `com.siccity.gltfutility`
以 ScriptedImporter 的形式导入，拖进场景即得到带材质的预制体。

**命名**：全小写 + 下划线，`类别_名字`，场景类前缀 `env_`。不用空格、不用中文
（见 [`docs/artist-guide.md`](../../../../docs/artist-guide.md) 第 5 章）。

## 提交前留意

- **`.blend` 不入库**（`.gitignore` 挡着 `*.blend` / `*.blend1` 连同它们的 `.meta`）。
  Blender 源文件动辄几十上百 MB，`.glb` 已经够用。
  **源件可以就放在资产自己的目录里**（如 `env_well/env_well.blend`），只是不进 git ——
  本地留着方便改，别人拉下来没有它也不影响使用。
- **`.glb` 单文件容易顶到 GitHub 的 50 MB 警告线**。入库前先降贴图分辨率
  （见 [`env_well/README.md`](env_well/README.md) 里 4096 → 2048 的做法），别再往上堆。
- **贴图是 `.glb` 的子资产**，不走 Unity 的 Texture Importer —— Max Size、压缩格式、平台覆盖
  在它身上**不生效**，按源分辨率以未压缩格式进内存。控制显存只能在导出前压源贴图。

## 当前内容

| 目录 | 是什么 |
| --- | --- |
| [`env_well/`](env_well/README.md) | 水井小景 diorama：水井、木桶、铺石地面、泥地、蕨类与高草、芦苇、落叶。103 网格 / 34 材质 / 33 贴图 |

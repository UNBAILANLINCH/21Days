# Materials — 材质

自己做的材质（`.mat`）与配套的 Shader 产物放这里。

**放进来会自动发生什么**：**没有自动规则**，这里只是约定的位置。
`.mat` / `.shader` 在 `.gitattributes` 里已按文本处理，能正常 diff 和提交。自己写的 Shader 放
`Art/Shaders/`（例如角色纸片用的 `SpriteDepthClip.shader`），材质在这里引用它。

**子目录**：`Character/` 角色纸片材质（如 `M_SpriteDepthClip.mat`，配 `Art/Shaders/SpriteDepthClip.shader`，
写深度、受雾不受光，接法见 `docs/developer-guide.md` 6.13）；`Graybox/` 场景灰盒材质，正式美术替换灰盒时按模块化
Prefab 的形式接进 `Environment_Graybox` 根节点下。

**命名**：全小写 + 下划线，`类别_用途`，例如 `fx_dissolve.mat`、`ui_grayscale.mat`。
不用空格、不用中文。

**不要放**：URP 的渲染管线资产与画质分档配置——那些在 `Assets/Settings/`，
是 Unity 模板自带的，**原位不动**。TextMesh Pro 字体自带的材质也留在它自己的目录里。

**不要在资源管理器里改名 / 移动 / 删除**——一律在 Unity 的 Project 窗口里操作，否则 `.meta` 掉队、引用静默失效。

详见 [`docs/artist-guide.md`](../../../../docs/artist-guide.md) 第 3、5 章。

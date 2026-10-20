# 第一批小部件 UI 置入（2026-10-19）

## 范围与确认

本批沿工作区现状增量修改。用户明确指定：新阴阳对话框用于 **NPC 头顶无树台词气泡**，默认阳间，阴间只作预览。底部 `DialogueView`、`PerformanceView` 与 SampleScene 地板不在本批替换范围。未提交或推送。

入口：`DialogueInteractable.OnBubbleRequested → DialogueSpeechBubble.Show`；`LootService → INotificationService.Show → NotificationView`；`ExplorationHudPresenter → ExplorationHudView / IHudVisibility`。检索未发现重建这三份目标预制体的编辑器生成器；Addressables 仍以原 UIView 类名加载通知与探索 HUD，世界气泡仍由场景预制体实例引用。

## 12 张交付素材映射

源路径仅记交付目录内的相对名称。全部原样复制、保留源文件；参考图不进入动态控件的图片绑定。

| 源相对文件 | 类型及依据 | 项目资源（相对 `Assets/_Project/Art/Sprites/UI/`） | 目标 |
| --- | --- | --- | --- |
| 对话框/阳间/阳间对话.png | Photoshop 正文/色值参考 | Dialogue/Delivery20261019/References/yang_body.png | 气泡 Body 的 TMP 样式 |
| 对话框/阳间/阳间名字.png | Photoshop 姓名/字族参考 | Dialogue/Delivery20261019/References/yang_name.png | 气泡 Name 的 TMP 样式 |
| 对话框/阳间/阳间对话框.png | 无文字透明底框，434×211 | Dialogue/Delivery20261019/Yang/ui_speech_panel.png | World/DialogueSpeechBubble 的 Content/Frame |
| 对话框/阳间/阳间小三角.png | 透明白色继续标，36×17 | Dialogue/Delivery20261019/Yang/ui_speech_continue.png | Content/Arrow，改 Image，保留原 GameObject 引用 |
| 对话框/阴间/阴间对话.png | Photoshop 正文白色色值参考 | Dialogue/Delivery20261019/References/yin_body.png | 阴间预览 Body |
| 对话框/阴间/阴间名字.png | Photoshop 姓名红色色值参考 | Dialogue/Delivery20261019/References/yin_name.png | 阴间预览 Name |
| 对话框/阴间/阴间对话框.png | 无文字透明底框，434×211 | Dialogue/Delivery20261019/Yin/ui_speech_panel.png | World/DialogueSpeechBubbleYin 预览变体 Frame |
| 对话框/阴间/三角形.png | 透明白色继续标，36×17 | Dialogue/Delivery20261019/Yin/ui_speech_continue.png | 阴间预览 Arrow |
| 获得物资/底框.png | 无文字装饰底框，628×137 | Notification/ui_reward_panel.png | UI/NotificationView 的 Card |
| 获得物资/获得物资信息.png | Photoshop 标题/色值/Heavy 字重参考 | Notification/References/reward_title.png | Card/Title 的 TMP 样式 |
| 获得物资/具体文字信息.png | Photoshop 正文/字号参考 | Notification/References/reward_body.png | Card/Body 的 TMP 样式 |
| 沉浸按钮.png | 157×158，固定书法标志已烘焙「沉浸」 | Exploration/ui_immersive.png | UI/ExplorationHudView 的 ImmersiveButton |

所有切片 Sprite Single、Bilinear、Clamp、透明通道开启、关闭 mipmap 和压缩。九切边距（左/下/右/上）：气泡 85/50/35/35，通知 160/55/140/70；后者把上方菱形与下方波纹保留在固定边界，避免多行增长时拉长装饰。参考截图按普通 Texture 导入，无运行时引用。Unity 自动生成 `.meta`。

`manage_texture` 的导入参数在当前桥接版本存在 snake_case/camelCase 转发不一致，实测未成功写入；因此通过 MCP `execute_code` 调用 TextureImporter。预制体关联引用及 TMP→Image 的组件更换也通过 MCP 的 Unity API 完成，未手改 YAML 或 `.meta`。

## 静态样式与差异

- 气泡复用有 OFL 许可证的 `SourceHanSerifCN-Bold`，与参考所示思源宋体 CN Bold 对应。姓名 25.27，颜色阳 `#1B174F` / 阴 `#CB1507`；正文 24，阳 `#216A79` / 阴 `#FFFFFF`；不叠伪粗体。姓名动态加 `【】`，空名不显示括号。正文换行、全文只赋一次，逐字继续用 TMP `maxVisibleCharacters`。
- 气泡宽 434，沿用内容自适应高度、世界空间尺寸、朝向相机与视口钳制；内边距 28/28/18/48，行间额外 3，姓名行高 34，正文最小高 92。继续标 18×8.5，右下 (-24,52)。阴间为继承同一基底的预览变体，未挂任何场景判断。
- 通知标题 31.78、`#B5FFDB`，正文 26.58、白色；参考 Heavy 字重工程未安装，**采用现有 Bold 近似**，未擅自下载字体。Card 宽 628、最低高 137，高度按正文自适应，内边距 190/118/24/22，间距 6；使标题居于底图两枚菱形之间。沿用共享卡片，所以任务等其它队列标题也使用本批通用底框，业务文字、队列与角落保存提示不变。
- 沉浸按钮保留右上区域，位置 (-48,-80)，142×143，图案只显示一次。正常态不再叠加同名 TMP；沉浸时图下显示可编辑的「退出沉浸」，CanvasGroup alpha 0.75，仍可点击。现有 IHudVisibility、H/手柄右摇杆、取消键与对白开启恢复逻辑保持不变。
- 气泡 Body、通知 Card、沉浸按钮各隔离必要的小 Canvas；装饰与文字不接收射线，按钮子 Canvas 保留 GraphicRaycaster。保留既有安全区与 CanvasScaler：1920×1080、按高度缩放，最低 1280×720，宽屏横向扩展。不新增每控件 Canvas、不新增 UI 框架或依赖。
- 现场截图发现 SampleScene 两个气泡实例（Npc_Villager、Yao_WellWoman）的 Name/Body/Arrow 仍覆盖 180° 反向旋转和旧颜色，导致 Billboard 后文字镜像。通过 MCP 撤回这几项子节点覆盖、将实例宽同步为 434，保留位置、缩放、target 与 CameraBillboard。与保存前场景逐行对照，仅这两处气泡变化，地板内容未变。

## 恰好三版动效方案

以下为设计参数；A 已采用，B/C 只留文字，没有三套运行时切换系统。业务已有停留时间优先，不延长物资队列占用。

| 方案 | 触发、焦点与节奏 | 停留、退场与交互 | 中断、暂停与刷新成本 |
| --- | --- | --- | --- |
| **A 克制递进（推荐、已接入）** | NPC 交互：框体 0–180ms OutQuad 淡入，姓名先建立阅读起点，正文立即按既有 30 字/秒由左到右显示；通知：200ms alpha + 下方 24px 向上归位，OutQuad；没有额外叠加根过渡。 | 气泡完整后沿用 4 秒停留，140ms 淡出；通知沿用服务持有时间与原 200ms 上移退场。按钮悬停/导航选中 80ms 青色亮度反馈，按压 80ms 缩至 0.94、抬起复位；没有持续呼吸。 | 重触发先取消旧句补间，换句重新逐字；禁用/关闭/销毁停止并还原。全程 unscaled，暂停中仍可响应；稳定后补间不再更新，TMP 字符数未变不重复写。 |
| B 轻快回弹（备选） | 气泡主体 220ms 从 0.97 到 1，OutBack；姓名在 40ms 后进入，正文在 80ms 后逐字；通知标题/正文相隔 50ms，自下向上 8px；焦点放在物资标题。 | 沿用原停留，退场 140ms 淡出；按钮 hover 100ms 增亮、press 70ms 到 0.94、release 140ms OutBack 回稳，不持续弹跳。 | 新触发取消旧补间、直接从作者基准开始；中途隐藏恢复 scale/alpha，暂停用 unscaled；比 A 多局部缩放与两段错峰，须另测文字网格与批次。 |
| C 仪式展开（备选） | 低频重要提示触发：框体 320ms 水平展开，OutCubic；姓名 100–240ms 淡入，正文从 200ms 进入，总入场约 600ms；视线由姓名移至正文、最后落在继续标。 | 不增加物资默认停留，退场 180ms；按钮 hover 120ms 增亮、press 80ms 收缩。可跳过入场直接到终态，避免阻塞高频交互。 | 重复触发取消旧序列，中断还原展开尺寸与文字显隐；unscaled。持续变尺寸容易触发布局重建，实施时只动隔离视觉层、不可逐帧改 LayoutGroup；成本最高，故本批不选。 |

A 最贴近纸张、书法与高频探索提示；复用现有通知、按钮、打字机，仅补气泡入场和禁用收尾。没有为了错峰再加计时器或改变原业务节奏。

## 验证记录

- Unity MCP 实读：21Days，Unity 2022.3.62f2c1；开始与静态操作时为非 Play，SampleScene 未脏；其它地板写入聊天空闲。
- 三份 C# 手动 project-lint 退出码 0。新增 2 个测试覆盖重复入场取消旧句、入/退场中禁用取消补间并恢复隐藏状态。
- 定向 EditMode 33 项完成、无已报告失败（该次 job 的最终汇总字段为 null）；扩大回归 **201/201 通过，失败 0、跳过 0**，覆盖 Dialogue、Loot、UIService 与沉浸规则。
- 用真实预制体渲染 1920×1080、1280×720、2560×1080，逐张查看并调整标题避让、继续标位置。这里是隔离渲染预览，不冒充探索现场截图。
- 扩大 EditMode 收尾后捕获到 4 条现有 `DialogueView.ApplyNamePunch` 对已销毁 TMP 的 MissingReferenceException（底部对白测试收尾路径）；本批未改该面板，需与真实探索回放结果分开判断。
- Profiler 工具可访问，但 UI 类别只返回 GUI/TextRendering 等计数，未取得 UGUI BuildBatch/Layout 的可比较前后数据；**性能未实测，不宣称零分配或性能提升**。

### 最终运行证据

本批专用 `DialogueArtShowcase.BatchArt_PauseQueueImmersiveAndBubbleLifecycle` **1/1 通过、失败 0、跳过 0，10 个检查点与 2 个等待全部通过、运行时异常 0**。最终报告：[20261019-180512](../../Logs/verify/dialogue/20261019-180512/report.md)。它从真实 Boot 取得 UI、通知、暂停、输入服务，再叠加现有 SampleScene，通过气泡公开显示入口、真实通知队列、按钮射线命中与 UI Submit 验证本批表现；不冒充从正式新游戏走完序章后的端到端验收。

| 已执行 | 结果与证据 |
| --- | --- |
| 暂停中淡入、逐字、完整正文与朝向/视口 | 通过；[现场气泡](ui-beautify-20261019/runtime-bubble.png) |
| 重复触发、入场中禁用、重新启用、长中文、空正文与自然淡出 | 通过；长中文 5 行完整，未裁切；测试同时检查字形朝向，防止镜像回归 |
| 物资连发同标题合并、多项数量与空正文行隐藏 | 通过；[多行物资](ui-beautify-20261019/runtime-reward.png)，修正九切后菱形不再随高度拉长 |
| 沉浸按钮射线、点击、退出提示、UI Submit 恢复、气泡隐藏 | 通过；[沉浸状态](ui-beautify-20261019/runtime-immersive.png) |
| 按钮正常、悬停、导航选中、按压与禁用外观 | [五态预览](ui-beautify-20261019/button-states.png)，使用真实预制体与 Button 状态终值隔离渲染；按压缩放 0.94、禁用不可交互有断言，过渡时序不由静态图证明 |
| 项目基准、最低与宽屏渲染 | [1920×1080](ui-beautify-20261019/ui-1920x1080.png)、[1280×720](ui-beautify-20261019/ui-1280x720.png)、[2560×1080](ui-beautify-20261019/ui-2560x1080.png) 均已实际渲染并查看；这些为隔离预览 |
| 动效预览 | [逐字采样动图](ui-beautify-20261019/typing-sampled.gif)：来自最终 Play 回放 12 张真实截图、约每 0.2–0.3 秒采样，不是完整帧率录屏，不用于测量 180ms 入场曲线 |

旧正式入口回放仍为 **3 条失败**，保留原报告：[气泡入口](../../Logs/verify/dialogue/20261019-174207/report.md)、[探索入口](../../Logs/verify/exploration/20261019-174207/report.md)。从标题「开始」当前会自动进入说书人序章，`SpotlightSceneBindings.Display` 隐藏 legacyExamples，旧用例的 Npc_Villager/Crate_A 与兴趣点前提失效。没有为了让它们变绿而改正式剧情或删除原用例。正式剧情何时重新展示这些旧示例仍由既有内容规则决定。

两份工作区原有大体积 Dynamic 字体经过 Unity `ClearFontAssetData(true)` 清空字形与孤立图集：Noto 14,904,884 → 6,404 字节，思源宋体 8,633,320 → 6,329 字节。最终回放与预览完成后再次清理，字形数均为 0；操作中断言源字体、材质引用、图集宽高/边距/模式与多图集开关保持不变，未回滚字体配置。运行时会重新构建字形。

新图片、预览变体、回放脚本均有 Unity 自动 `.meta`；未留 InitTestScene。底部 DialogueView/PerformanceView 文件哈希与本批开始时一致；SampleScene 保存前后只改变两处气泡覆盖和其新子 Canvas 的着色器通道。未新增 ProjectSettings 改动。C#、Markdown 的 diff 空白检查通过；Unity 序列化预制体的空值字段仍有其原生尾空格提示，未手改 YAML 清除。

收尾复核：12 张归档图片逐一与原稿比较，字节一致；三份 C# lint 通过；`gc_scan.py` 全量通过（文档链接、目录、钩子引用、自测及静态不变量）。五态隔离预览清理曾留下 `Releasing render texture that is set as Camera.targetTexture!` 编辑器日志，随后实读确认只剩未脏的 SampleScene、非 Play、所有场景相机均无 targetTexture，临时场景与渲染目标未残留。该日志不计入此前最终 Play 回放的异常统计。

仍未实测：目标硬件性能、UGUI 前后重建/GC 对比、背景模糊与 HUD/世界气泡之间的自动避让。本批未新增这些机制。物资 Heavy 字重采用 Bold 近似，阴间保持预览。

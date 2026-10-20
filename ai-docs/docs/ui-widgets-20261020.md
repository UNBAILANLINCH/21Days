# 小部件 UI 接续（2026-10-20）

本批接续已提交的前三批 UI，只补左侧小任务追踪 HUD、完成通知、交互提示，以及本次附件的明确映射。Tab 任务面板、背包布局、空态底板、NPC 阳间台词框、物资通知、沉浸按钮继续复用。没有自动提交或推送。

## 资源与真实入口

源目录按交付包相对描述：`小部件ui/小部件ui/`。15 张 PNG 原样归档，未复制、引用或导入源包的 PSD/PSB；Unity 生成配套 meta。文字信息截图均放 References，不用作游戏底板。

| 交付源 | 归档位置（相对 Art/Sprites/UI） | 绑定与依据 |
| --- | --- | --- |
| 左侧小任务栏/任务导航底框.png | Quest/Delivery20261020/Slices/tracker-panel.png | QuestHudView/Root；独立常驻 HUD，真实 QuestHudPresenter 的追踪状态，不用 Tab 大面板替代 |
| 左侧小任务栏/任务名称文字信息.png、任务详情文字信息.png、tab文字详情.png | Quest/Delivery20261020/References/ 三张 type 图 | 分别核对标题、当前目标、任务键；动态 TMP |
| 完成任务/底框.png | Notification/Delivery20261020/completion-panel.png | 原 NotificationView 的 successBackground；QuestNotificationPresenter 仍是唯一任务通知订阅者 |
| 完成任务/完成任务文字信息.png、具体信息.png | Notification/Delivery20261020/References/ 两张 type 图 | 标题与实际任务名的字体、颜色依据 |
| 交互提示/底框.png、E.png | Interaction/Delivery20261020/prompt-panel.png、key-e.png | InteractPromptHudView 的 Root 与 KeyBadge/EArtwork；E 图只在当前键鼠有效绑定显示为 E 时使用 |
| 交互提示/文字信息.png | Interaction/Delivery20261020/References/prompt-type.png | 动态「动词 · 名字」参考，未烘焙运行时文案 |
| 附件 B／C：省略号／叹号 | Dialogue/Delivery20261020/marker-ellipsis.png、marker-exclamation.png | SampleScene 原 DialogueInteractableMarker 的可交互未聚焦／当前焦点两态，16 处 SpriteRenderer 引用；QuestTargetMarker 同步叹号，保留既有接管与黄色染色 |
| 附件 A：金色长形器物 | Inventory/Delivery20261020/Icons/gold-belt-hook.png | 用户明确确认金带钩 1804，绑定 InventoryPanelView.itemIcons |
| 附件 D：金色环形器物 | Inventory/Delivery20261020/Icons/gold-ring-clasp.png | 用户明确确认金环扣 1805，同上 |
| 附件 E：金色双瓣器物 | Inventory/Delivery20261020/Icons/gold-lapel-clasp.png | 用户明确确认金襟扣 1803，同上 |

器物初查没有相同文件哈希；物品表存在 1803／1804／1805，但绑定依据是用户后续确认，未把外观猜测当事实。前批福肉、卷轴、龙牙、归业簿、赈粮册五图仍只归档预览。名簿 1601 与上批越框修正保持原绑定和详情区域。

[附件透明底核对](ui-widgets-20261020/attachment-alpha-preview.png)：源图查看器里金饰的异常彩线、黑边，以及气泡外侧青色方框位于完全透明区域；棋盘合成未显示这些线条，没有重画或删除原图像素。省略号／叹号内部透明与半透明区域也予以保留。新世界图标以 196.875 PPU 保留原约 1.28 世界单位的贴图宽度；不改原缩放与头顶锚点。

## 样式、字体与布局

- 任务标题：参考明确为思源宋体 CN Heavy，27.36px、`#216A79`；当前目标：思源宋体 CN SemiBold，22.53px、`#5C95A1`。两者复用项目有 OFL 的宋体 Bold，字重均是近似，不宣称同款 Heavy／SemiBold。
- Tab 提示：参考为思源黑体 CN Regular，22.53px、`#5C95A1`。只复制本批提供的 Regular OTF；已读取其内嵌字族、2014／2015 Adobe 版权与 SIL OFL 信息，许可证置于 Fonts/SourceHanSansCN。未导入其余六个字重，未全局换字体。TMP 使用 Dynamic、90pt、Padding 9、SDFAA、1024×1024、多图集。
- 完成通知：标题 31.78px、`#206978`，正文 26.58px、`#5C95A1`，参考 Heavy 仍以现有宋体 Bold 近似。QuestConfig 的完成标题为「完成任务」，正文另传真实任务名。完成样式结束后恢复原物资底板与字色。
- 交互提示：宋体 Bold 近似 Heavy，26.58px、`#5C95A1`；底条高 58，宽按实际文案与按键徽标在 270–640 之间计算。长名称省略，不改变目标或操作；特殊动作提示继续保留独立输入与按钮。
- 小任务栏保留左上 `(24,-332)`，位于现有小地图下方。宽 394，标题一行省略，目标最多两行，底板按目标实际字高在 85–130 间增高；空追踪保留底板与任务键。首轮发现宋体标题字高 39.32 超过原 36px 框，已把标题框加至 44px，再生成截图。
- 基准 1920×1080、按高度缩放、最低 1280×720、宽屏 2560×1080；沿用 UIService 安全区与原地址。新子 Canvas 只放任务动态内容、交互条；装饰与文字关闭 Raycast Target，原整条按钮与导航保留。

InputBindingDisplay 只读取动作有效绑定与动作设备通知。任务键和普通／特殊交互键在设备切换或绑定变化时更新，没有在每帧反复拼接或写入。普通交互按键仍只由 InteractionFocus 读取；E／F 分工、焦点优先级、半径和对白让位保持现有链路。

INotificationService.Show 新增可选 `success=false`；旧调用默认行为保留。成功卡片按标题与正文一起合并，避免多条「完成任务」吞掉不同任务；普通物资通知仍按同标题合并。仅改显示数据与队列合并，不新增完成事件、奖励或第二个订阅者。相应测试替身只同步签名。

## 恰好三版动效方案

| 方案 | 触发、节奏与动线 | 中断、交互与刷新 |
| --- | --- | --- |
| A 克制递进（推荐并落地） | 任务内容真正改变时 160ms OutQuad 淡入；交互条由无到有时同样 160ms，切目标直接换文案；完成通知复用原 200ms alpha＋24px 上移与既有停留，队列接续 | 按钮保留现有悬停／选中／按压反馈；离开范围及对白立即收起；暂停用 unscaled；隐藏／禁用／关闭取消并复位，不做常驻循环。文本与布局只在数据改变时更新 |
| B 轻快回弹（仅文字） | 任务换项 200ms 从 0.98 回稳，目标延后 40ms；交互条 180ms 从下方 6px 归位；成功通知 240ms 小幅回稳、正文延后 50ms，沿用有效停留，140ms 退场 | 悬停提亮、按压短缩放；重复触发先取消旧动画，隐藏立即终止，暂停走 unscaled。仅短时增加 transform／alpha 写入，无空闲更新 |
| C 仪式展开（仅文字） | 常驻 HUD 仍即时更新；仅低频完成卡片 320ms 横向展开、标题延后 60ms，正文再延后 80ms，总入场约 500ms，140ms 退场，停留沿用队列 | 可直接跳到终值；连发时逐条接续，取消／隐藏清理句柄，暂停不冻结。展开期间成本高于 A，不加额外布局重建或三套运行时切换框架 |

A 与频繁交互、常驻小任务栏相符，复用已有通知节奏，避免延迟真正的操作入口。时长是设计值；未取得性能对照采样，不宣称降低 CPU、批次或 GC。

## 验证记录

- 首轮本批 Play 回放：`QuestHudArtShowcase.HudPromptCompletionAndConfirmedIcons`，`Logs/verify/quest/20261020-005615/report.md`，1／1 PASS、22 个检查点、0 条运行时异常。正式新游戏入场，任务900来自真实服务；长文本／交互对象／连续成果是明确夹具；完成900通过公开 Report 上报目标，未直接发布完成事件；金饰通过 Loot.GrantItems 发放，不代表正式剧情已发奖励。隔离存档由回放框架收尾。
- EditMode：交互、任务、对白与通知／绑定显示规则共 371／371 PASS（job `22b80037e97c494492809696a0061dea`）；本批 C# 首轮24文件 lint 无违规。
- 最终复验：`046e4eeb71ea4ccf95d3a39cede883ee` 两条回放均 PASS。`Logs/verify/quest/20261020-011049/report.md` 为本批22个检查点；`Logs/verify/dialogue/20261020-011049/report.md` 为前批气泡／物资／沉浸回归加新标记，共12个检查点。NPC标记使用既有NPC运行时副本摆在玩家半径内，经公开点选入口转移焦点；玩家未瞬移，场景与半径未改。标题状态夹具临时启用Gameplay，结束后恢复。
- 前批任务面板14个检查点、背包16个检查点已在 `d8535fa079e748aab6700aff86ba3f51` 同次回归中通过，报告批次 `20261020-010346`。该批次整体最初失败：新增标记检查对范围外NPC点选、金饰检查在背包开场结束前读列表。修正的是夹具摆位与等待 `InventoryPanelController.IsOpen`，没有放宽半径、删断言或改生产业务。两条失败用例随后单独复跑通过；四个相关用例最终共64个检查点通过，报告均无运行时异常。
- 静态预制体渲染已覆盖三种分辨率的正常、长文、空态；低分辨率与宽屏是布局渲染，真实输入目前在 1920×1080 验证。新金饰运行时三张截图均在继承的详情框内。
- 三件金饰各补三种分辨率预览，共9张；小任务栏与交互按钮补Normal／Highlighted／Selected／Pressed／Disabled五态图，采用预制体原状态颜色，只在预览实例中把过渡时间设0以采集终态，不代表动效性能测量。
- [完成通知动图](ui-widgets-20261020/completion-motion.gif) 来自最终回放13个真实采样帧，采样跨度0.393秒、末帧另留900ms供阅读；是采样预览，截图读回不能用作性能数据。三份Dynamic字体经Unity清空本次生成字形，字符表均回到0；源字体、材质与图集尺寸／Padding／渲染模式／多图集配置断言不变。四个相关UI预制体Missing Script为0。结束时非Play、SampleScene已保存，Game视图与节奏仍为原Full HD／x1；已释放共享编辑器给下一窗口，之后未再写Unity资产或运行测试。
- 性能未实测；UI 重建、批次、CPU、GC 没有前后可比采样。最终视觉验收仍由开发者确认。
- 最终文件检查：25个本批C#文件lint通过，源代码／文档diff空白检查通过；15张PNG原图哈希与meta配对通过，仅新增一份Sans Regular源。原有暂存内容未纳入本批，也未自动提交。`gc_scan.py` 已执行，但全库检查尚未全绿：另一窗口处理中的[Battle文档](modules/battle/battle-module-guide.md)第209行保留了失效代码行号，按共享文件写入串行约定未改该窗口文件；本批新增链接未被报告失效。

## 查看证据

| 内容 | 预览 |
| --- | --- |
| 实际小任务栏与地图边界 | [正式追踪](ui-widgets-20261020/runtime-01-小任务栏_真实追踪.png)、[空追踪](ui-widgets-20261020/runtime-02-小任务栏_未追踪.png)、[长目标](ui-widgets-20261020/runtime-03-小任务栏_长目标.png) |
| 交互提示 | [键鼠E](ui-widgets-20261020/runtime-04-交互提示_E与旅人.png)、[手柄与任务键](ui-widgets-20261020/runtime-05-交互提示_手柄与任务键.png) |
| 完成通知 | [真实事件](ui-widgets-20261020/runtime-06-完成任务_现有事件入口.png)、[长名称暂停](ui-widgets-20261020/runtime-07-完成任务_长名称暂停.png)、[动图](ui-widgets-20261020/completion-motion.gif) |
| 真实金饰选择 | [金襟扣](ui-widgets-20261020/runtime-08-确认金饰_金襟扣.png)、[金带钩](ui-widgets-20261020/runtime-09-确认金饰_金带钩.png)、[金环扣](ui-widgets-20261020/runtime-10-确认金饰_金环扣.png) |
| 既有焦点状态 | [当前焦点叹号](ui-widgets-20261020/runtime-marker-01-叹号_当前焦点.png)、[未聚焦省略号](ui-widgets-20261020/runtime-marker-02-省略号_可交互未聚焦.png) |
| 三种分辨率（正常） | [1920×1080](ui-widgets-20261020/widgets-normal-1920x1080.png)、[1280×720](ui-widgets-20261020/widgets-normal-1280x720.png)、[2560×1080](ui-widgets-20261020/widgets-normal-2560x1080.png) |
| 长文本／空态 | [长文低分辨率](ui-widgets-20261020/widgets-long-1280x720.png)、[空态宽屏](ui-widgets-20261020/widgets-empty-2560x1080.png)；同目录另有各三种分辨率 |
| 金饰布局边界 | [金襟扣低分辨率](ui-widgets-20261020/gold-lapel-1280x720.png)、[金带钩宽屏](ui-widgets-20261020/gold-hook-2560x1080.png)、[金环扣低分辨率](ui-widgets-20261020/gold-ring-1280x720.png)；同目录每件各三种分辨率 |
| 按钮五态 | [Normal](ui-widgets-20261020/widgets-button-Normal.png)、[Highlighted](ui-widgets-20261020/widgets-button-Highlighted.png)、[Selected](ui-widgets-20261020/widgets-button-Selected.png)、[Pressed](ui-widgets-20261020/widgets-button-Pressed.png)、[Disabled](ui-widgets-20261020/widgets-button-Disabled.png) |

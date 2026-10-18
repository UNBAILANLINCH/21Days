# 聚光灯飞书资产映射

> 角色交付核对日期：2026-10-18；其余类别沿用各节注明的快照。定义资源归属与接入位置；服务器资产同步仅处理明确绑定的交付文件，启用状态以服务器配置和推送实测为准。
> 新建原因：现有策划转写只保存原文，美术手册只定义通用目录，均未承载逐项飞书资源与项目目标的对应关系；本表放在知识层，避免飞书重拉覆盖人工映射。

本表对应外部飞书文档《2026gamejam聚光灯》的资产部分。策划原文继续维护在 [聚光灯策划目录](../../docs/design/spotlight/README.md)，不再建立第二份正文。角色拆件交付按用户确认保留；是否为逐件序列帧，必须检查实际交付包才能判断。

## 来源和定位

| 资源 | 飞书来源 | 现有本地内容 |
| --- | --- | --- |
| 主线角色 | 表格 `Bfqms9IUKh4ZtjtzQBIcW486nEg` / 子表 `ddVEoY` | [美术需求表](../../docs/design/spotlight/04_美术需求表.md) |
| 玩法角色 | 同表 / `pKSkSs` | 同上 |
| 模型 | 同表 / `iIfihK` | 同上 |
| UI | 同表 / `4sjpvZ` | 同上 |
| 小游戏界面 | 同表 / `hGHTpX` | 同上 |
| 附件与画板 | 正文中的资源 token | `docs/design/spotlight/01` 至 `05` 对应转写；补交 `06`、`07` 另有来源，不能被飞书同步删除 |

2026-10-09 初次读取五张表的版本均为 165，完整范围依次为 `A1:D11`、`A1:D11`、`A1:C22`、`A1:C27`、`A1:B4`，当时角色、模型附件列均为空。2026-10-18 重新完整读取两张角色表，版本为 204，已取得下文九个交付包。下表行号仅用于当前定位，后续以子表 token + 原文名称 + 本表资产 ID 识别；新增或插行后重新定位。不能将需求文字或参考图当成正式交付。

## 已有目录和接入契约

表内 `Art/`、`Prefabs/`、`Scenes/` 均相对于 `Assets/_Project/`。标为“拟”的路径是本表分配的位置，尚未创建；没有交付内容时不预建空目录。“现有”只证明目标资产存在，不代表该飞书需求已实现或通过视觉验收。

| 类型 | 复用位置 | 接入限制 |
| --- | --- | --- |
| 角色拆件 PNG | `Art/Sprites/Characters/<资产ID>/parts/`（拟） | 不混入 `Ark/` 占位素材；实际部件、帧名、pivot 和层次尚待交付包核实 |
| 角色整帧序列（如交付包含） | `Art/Sprites/Characters/<资产ID>/`（拟） | `chr_<资产ID>_<状态>_<NN>.png` 才能交给现有 FramePuppetGenerator；拆件文件不得误当整帧 |
| 角色动画与预制体 | `Art/Animations/Characters/<资产ID>/`、`Prefabs/Characters/Chibi_<资产ID>.prefab`（目标；顾客两套现有） | `customers`、`drunk_customer` 已按当前 PSD 标定拆件关节；其余角色不能直接套用这份专用标定 |
| 对话立绘 | `Art/Sprites/Dialogue/Portrait_<资产ID>_<表情>.png`（拟） | 配合 `Tables/Data/dialogue_character.json` 和 `Dialogue/Portrait_<资产ID>_<表情>` 地址；不把 elder/traveler 默认映射成剧情人物 |
| 建筑和环境交付包 | 现有 `Art/scene/`，每项拟按资产 ID 分目录 | `readme.txt` 已说明此处是美术场景资产接收目录；正式模型、纹理和材质归属需检查交付包，不另建平行接收根目录 |
| 环境运行时预制体 | `Prefabs/World/Environment/<资产ID>.prefab`（拟） | 与接收文件分开；通过 Unity 组装，当前没有对应正式建筑预制体的证据 |
| UI 切图与面板 | 现有 `Art/Sprites/UI/`、`Art/Sprites/Dialogue/`、`Prefabs/UI/` | 复用现有面板与 Addressables 类名地址；不按飞书中文名创建第二套面板 |
| 设计附件、流程、参考图 | 现有 `docs/design/spotlight/` 和 `images/` | 属策划参考；不能直接塞进运行时 Sprites、Textures 或场景 |

导入依据：[美术手册](../../docs/artist-guide.md)、[资产规则](../../.claude/rules/unity-assets.md)、[角色模块](modules/characterpuppet/characterpuppet-module-guide.md)。`SpriteImportProcessor` 只对 `Art/Sprites/` 首次导入的图片设置 Sprite、PPU100、Bilinear、压缩、mipmap；不能假设目录内的旧图会重新套默认值。

## 角色逐项映射

以下资产 ID 是目录标识，不是角色表 ID。顾客与醉酒顾客已按下文的本地 PSD 记录接收，并应用拆件待机、走路、奔跑动画；其飞书补交版本单独接收，未替换动画使用的图件。妻子及另外六个角色已接收拆件，尚未组装。既有占位预制体资产保留，角色实例只按核实身份替换。

| 子表与当前行 | 原文名称 | 资产 ID | 拆件接收位置 | 复用或接入说明 |
| --- | --- | --- | --- | --- |
| ddVEoY / 2 | 主角 | `player` | `Art/Sprites/Characters/player/parts/`（拟） | 组装及动画接入待检查实际交付包 |
| ddVEoY / 3 | 妻子 | `wife` | `Art/Sprites/Characters/wife/parts/`（现有） | 已接收《妻子-何素.zip》的 13 张静态拆件，Unity 已生成 meta；组装、动画和服务器绑定仍待完成 |
| ddVEoY / 4 | 轮回堂值守 | `lunhuitang_guard` | `Art/Sprites/Characters/lunhuitang_guard/parts/`（现有） | 已接收 15 张拆件；组装及动画待接入 |
| ddVEoY / 5 | 泾阳老吏 | `jingyang_clerk` | `Art/Sprites/Characters/jingyang_clerk/parts/`（拟） | 组装及动画接入待检查实际交付包 |
| ddVEoY / 6 | 民众 | `villagers` | `Art/Sprites/Characters/villagers/parts/`（现有） | 已接收 19 张拆件；不按编号推断动作帧或部位 |
| ddVEoY / 7 | 镜中乡老 | `mirror_elder` | `Art/Sprites/Characters/mirror_elder/parts/`（拟） | 原文允许复用民众；保留独立逻辑 ID，是否共用图件待确认 |
| ddVEoY / 8 | 钱塘龙君 | `qiantang_dragon` | `Art/Sprites/Characters/qiantang_dragon/parts/`（拟） | 组装及动画接入待检查实际交付包 |
| ddVEoY / 9 | 王录业 | `wang_luye` | `Art/Sprites/Characters/wang_luye/parts/`（现有） | 已接收 15 张拆件；组装及动画待接入 |
| ddVEoY / 10 | 弥勒教信众 | `mile_believers` | `Art/Sprites/Characters/mile_believers/parts/`（拟） | 原文允许复用民众；保留独立逻辑 ID，是否共用图件待确认 |
| ddVEoY / 11 | 赵十四 | `zhao_shisi` | `Art/Sprites/Characters/zhao_shisi/parts/`（拟） | 组装及动画接入待检查实际交付包 |
| pKSkSs / 2 | 都知 | `duzhi` | `Art/Sprites/Characters/duzhi/parts/`（拟） | 组装及动画接入待检查实际交付包 |
| pKSkSs / 3 | 乐正 | `yuezheng` | `Art/Sprites/Characters/yuezheng/parts/`（拟） | 组装及动画接入待检查实际交付包 |
| pKSkSs / 4 | 学徒 | `apprentice` | `Art/Sprites/Characters/apprentice/parts/`（拟） | 组装及动画接入待检查实际交付包 |
| pKSkSs / 5 | 裁衣匠 | `tailor` | `Art/Sprites/Characters/tailor/parts/`（拟） | 组装及动画接入待检查实际交付包 |
| pKSkSs / 6 | 顾客 | `customers` | `Art/Sprites/Characters/customers/parts/`（现有） | 一套 17 件拆件外观，已有待机、走路、奔跑；应用衣肆三顾客及酒肆酒客，不代表已交付多套外观 |
| pKSkSs / 7 | 金银匠 | `goldsmith` | `Art/Sprites/Characters/goldsmith/parts/`（现有） | 已接收 19 张拆件；组装及动画待接入 |
| pKSkSs / 8 | 官证 | `official_witness` | `Art/Sprites/Characters/official_witness/parts/`（现有） | 已接收 16 张拆件；组装及动画待接入 |
| pKSkSs / 9 | 杂役 | `handyman` | `Art/Sprites/Characters/handyman/parts/`（现有） | 已接收 18 张拆件；组装及动画待接入 |
| pKSkSs / 10 | 阶段一BOSS | `stage1_boss` | `Art/Sprites/Characters/stage1_boss/parts/`（拟） | 组装及动画接入待检查实际交付包 |
| pKSkSs / 11 | 小怪（醉酒的顾客） | `drunk_customer` | `Art/Sprites/Characters/drunk_customer/parts/`（现有） | 独立 PSD，14 张源拆件；运行时将脚层裁成左右两件并提供待机、走路、奔跑，应用酒肆两顾客；保留 ObservationOnly 行为，未新增攻击动作 |

主页另列“女主”“乡中老人”“仪式民众”等名称，而当前主线表只有上述十项。不能自动把女主合并到妻子、乡中老人合并到镜中乡老；先核对是否别名或新增需求。

## 模型逐项映射

接收位置统一为 `Art/scene/<资产ID>/`（拟），运行时目标统一为 `Prefabs/World/Environment/<资产ID>.prefab`（拟）。下面的人间和妖界地点可以按原文复用建筑，但未确认同一交付物前保留独立 ID，不因“同名”互相覆盖。

| 子表当前行 | 原文名称 | 资产 ID | 现有场景归属或注意事项 |
| --- | --- | --- | --- |
| iIfihK / 1 | 泾阳·官府 | `jingyang_yamen` | 现有场景 `Scenes/HumanJingyang.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 2 | 泾阳·过渡平台 | `jingyang_platform` | 现有场景 `Scenes/HumanJingyang.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 3 | 泾阳·井底 | `jingyang_well` | 现有场景 `Scenes/HumanJingyang.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 4 | 泾阳·戏班 | `jingyang_stage` | 现有场景 `Scenes/HumanJingyang.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 5 | 泾阳·衣肆 | `jingyang_tailor_shop` | 现有场景 `Scenes/HumanJingyang.unity`；仅为地点归属，未验证具体放置；检查实际交付成果后再接收 |
| iIfihK / 6 | 泾阳·金银行 | `jingyang_goldsmith_shop` | 现有场景 `Scenes/HumanJingyang.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 7 | 泾阳·客栈 | `jingyang_inn` | 现有场景 `Scenes/HumanJingyang.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 8 | 泾阳·酒肆 | `jingyang_tavern` | 现有场景 `Scenes/HumanJingyang.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 9 | 泾阳·民间祠堂 | `jingyang_shrine` | 现有场景 `Scenes/HumanJingyang.unity`；仅为地点归属，未验证具体放置；不能自动覆盖已有制作成果 |
| iIfihK / 10 | 坊市·轮回堂 | `fangshi_lunhuitang` | 现有场景 `Scenes/YaoFangshi.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 11 | 坊市·过渡平台 | `fangshi_platform` | 现有场景 `Scenes/YaoFangshi.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 12 | 坊市·洞庭府 | `fangshi_dongtingfu` | 现有场景 `Scenes/YaoFangshi.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 13 | 坊市·戏班 | `fangshi_stage` | 现有场景 `Scenes/YaoFangshi.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 14 | 坊市·衣肆 | `fangshi_tailor_shop` | 现有场景 `Scenes/YaoFangshi.unity`；仅为地点归属，未验证具体放置；检查实际交付成果后再接收 |
| iIfihK / 15 | 坊市·金银行 | `fangshi_goldsmith_shop` | 现有场景 `Scenes/YaoFangshi.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 16 | 坊市·客栈 | `fangshi_inn` | 现有场景 `Scenes/YaoFangshi.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 17 | 坊市·酒肆 | `fangshi_tavern` | 现有场景 `Scenes/YaoFangshi.unity`；仅为地点归属，未验证具体放置 |
| iIfihK / 18 | 坊市·乡集祠堂 | `fangshi_shrine` | 现有场景 `Scenes/YaoFangshi.unity`；仅为地点归属，未验证具体放置；不能自动覆盖已有制作成果 |
| iIfihK / 19 | 街道地面/角色可移动范围 | `street_ground` | 两界可共用；可站立部分须按现有 Ground 接法验证 |
| iIfihK / 20 | 遮挡物 | `cover` | 两界可共用；可站立部分须按现有 Ground 接法验证 |
| iIfihK / 21 | 层间楼梯 | `stairs` | 两界可共用；可站立部分须按现有 Ground 接法验证 |
| iIfihK / 22 | 山石 | `rocks` | 两界可共用；可站立部分须按现有 Ground 接法验证 |

现有 `Art/fbx/` 中核实到的是 Head-topo 相关模型，不能当作环境模型的已映射目标。

## UI 逐项映射

| 子表当前行 | 原文名称 | 映射 ID | 已有目标或拟接收位置 |
| --- | --- | --- | --- |
| 4sjpvZ / 1 | 标题界面 | `title` | 现有 `Prefabs/UI/TitleView.prefab`；切图复用 `Art/Sprites/UI/Title/` 三张同名 PNG |
| 4sjpvZ / 2 | 主菜单 | `pause_menu` | 现有 `Prefabs/UI/PauseMenuView.prefab`、`SaveSlotsView.prefab`；切图拟放 `Art/Sprites/UI/Menu/` |
| 4sjpvZ / 3 | 设置界面 | `settings` | 现有 `Prefabs/UI/SettingsView.prefab`；切图拟放 `Art/Sprites/UI/Settings/` |
| 4sjpvZ / 4 | 加载界面（Loading） | `loading` | 现有 `Prefabs/UI/LoadingView.prefab`；切图拟放 `Art/Sprites/UI/Loading/` |
| 4sjpvZ / 5 | 屏幕边缘导航箭头 | `navigation_arrow` | 尚未确认专用控件；切图拟放 `Art/Sprites/UI/World/`，不能只导入就宣称导航功能完成 |
| 4sjpvZ / 6 | 交互提示按钮 | `interaction_prompt` | 现有 `Prefabs/UI/DialogueInteractHudView.prefab`；非对话交互需另核实 |
| 4sjpvZ / 7 | 可交互物体高亮描边 | `interaction_outline` | 运行时描边表现待核实；不能将一张图当作完整高亮效果 |
| 4sjpvZ / 8 | NPC头顶标记 | `npc_marker` | 现有 `Art/Sprites/Dialogue/Marker_Idle.png`、`Marker_Focus.png`、`Bubble_Frame.png`；任务目标另核实 `Prefabs/World/QuestTargetMarker.prefab` |
| 4sjpvZ / 9 | 快捷物品栏 | `quick_items` | 尚未确认快捷栏目标；切图拟放 `Art/Sprites/UI/Inventory/` |
| 4sjpvZ / 10 | 对话框 | `dialogue` | 现有 `Prefabs/UI/DialogueView.prefab`；切图复用 `Art/Sprites/Dialogue/` |
| 4sjpvZ / 11 | 对话选项按钮 | `dialogue_choice` | 现有 `DialogueView.prefab` 的选项；图标复用 `Art/Sprites/Dialogue/ChoiceIcon_*.png` |
| 4sjpvZ / 12 | 过场字幕条 | `subtitle` | 候选 `Prefabs/UI/PerformanceView.prefab`；切图复用 `Art/Sprites/UI/Performance/` |
| 4sjpvZ / 13 | 章节标题卡 | `chapter_title` | 尚未确认专用标题卡；切图拟放 `Art/Sprites/UI/Performance/` |
| 4sjpvZ / 14 | 转场黑幕+过渡文字 | `world_transition` | 候选 `PerformanceView.prefab` / `LoadingView.prefab`；两界转场接线待核实 |
| 4sjpvZ / 15 | 线索/卷宗面板 | `clue_panel` | 不把 `QuestPanelView.prefab` 自动当作卷宗面板；切图拟放 `Art/Sprites/UI/Clues/` |
| 4sjpvZ / 16 | 账册/文书翻阅界面 | `document_reader` | 尚未确认阅读器；切图拟放 `Art/Sprites/UI/Clues/` |
| 4sjpvZ / 17 | 线索关联/推理面板 | `deduction` | 尚未确认推理面板；切图拟放 `Art/Sprites/UI/Clues/` |
| 4sjpvZ / 18 | 场景地图/导航缩略图 | `map` | 尚未确认运行时地图；切图拟放 `Art/Sprites/UI/World/`；飞书俯视图仍属设计参考 |
| 4sjpvZ / 19 | 背包界面 | `inventory` | 现有 `Prefabs/UI/InventoryPanelView.prefab`；切图拟放 `Art/Sprites/UI/Inventory/` |
| 4sjpvZ / 20 | 物品详情弹窗界面 | `item_detail` | 候选 `InventoryPanelView.prefab`；是否有对应详情槽位待核实 |
| 4sjpvZ / 21 | 通用按钮组 | `common_button` | 现有 `Art/Sprites/UI/Title/ui_btn_menu_normal.png`；共用引用需检查，避免全局误替换 |
| 4sjpvZ / 22 | 通用功能图标集 | `common_icon` | 切图拟放 `Art/Sprites/UI/Common/`；逐个登记实际使用者 |
| 4sjpvZ / 23 | Toast/弹窗提示 | `notification` | 现有 `Prefabs/UI/NotificationView.prefab`、`ConfirmView.prefab`；切图拟放 `Art/Sprites/UI/Common/` |
| 4sjpvZ / 24 | BOSS血条 | `boss_hp` | 不能因存在 `RhythmView.prefab` 就绑定 BOSS；音游叠加需求待裁定，切图拟放 `Art/Sprites/UI/Battle/` |
| 4sjpvZ / 25 | 章节完成提示 | `chapter_complete` | 候选 `NotificationView.prefab`；章节提示事件与内容待核实 |
| 4sjpvZ / 26 | 结算界面 | `result` | 音游结算与当前玩法要求有分歧；候选 `RhythmView.prefab`，未确定独立结算目标 |
| 4sjpvZ / 27 | 关键道具图标 | `key_item` | 切图复用 `Art/Sprites/Loot/`；物品 ID 与配置表需核实，不按中文名自动建配置 |

已有 UI Addressables 地址与类名一致，本表不改这些地址。换已有切图应保留原 `.meta` 和导入设置；面板内只替换确定槽位的图片，不因同名需求删改层级。

## 小游戏逐项映射

| 子表当前行 | 原文名称 | 映射 ID | 拟接收位置与限制 |
| --- | --- | --- | --- |
| hGHTpX / 1 | 界面背景 | `goldsmith_background` | `Art/Sprites/UI/Goldsmith/`（拟）；未确认现有金银行专用面板，不映射到 RhythmView |
| hGHTpX / 2 | 圆盘打造交互区 | `goldsmith_disc` | `Art/Sprites/UI/Goldsmith/`（拟）；未确认现有金银行专用面板，不映射到 RhythmView |
| hGHTpX / 3 | 物品收集栏（A/B/C） | `goldsmith_collection` | `Art/Sprites/UI/Goldsmith/`（拟）；未确认现有金银行专用面板，不映射到 RhythmView |
| hGHTpX / 4 | 按钮UI | `goldsmith_button` | `Art/Sprites/UI/Goldsmith/`（拟）；未确认现有金银行专用面板，不映射到 RhythmView |

## 已知差异和整合条件

1. **拆件与序列帧不能仅凭名称判冲突。** 用户确认拆件正确，并提出可能是拆件序列帧。实际交付格式仍未验证；`ChibiPuppet.parts` 可持有多个渲染器，但当前 FramePuppetGenerator 生成单个 Sprite、把 parts 数组设为 1。这说明现有生成入口不支持自动组装逐件动画，不能据此要求改成交整帧，也不能宣称拆件已接通。旧拼接方案仅可供研究，不自动恢复旧文件。
2. **角色尺寸分口径。** 飞书写 512×512；手册对整帧序列建议高384。这些可能对应不同交付阶段，先检查包内画布、单件位置、锚点与目标显示高度，不自动缩放或裁剪。
3. **白色是否免做仍以飞书颜色为准。** 目前读取已请求 style，但未拿到单元格样式，不能把所有行都标成必做。
4. **需求与实现分开。** 音游界面、BOSS 血条叠加和结算需求与主页“点击光点、不另开音游界面”存在待裁定差异；保留需求，不自动绑定现有 RhythmView。
5. **模型接收不等于落场景。** 先检查已有衣肆、祠堂制作成果和两界灰盒，模型进接收目录后仍需验证比例、材质、碰撞、可走层和预制体引用。
6. **原文变化不自动删除资产。** 移除需求或附件只提示，确认引用和工作区状态后才能处理。已有 `.meta` 不手改、不替换；新素材交 Unity 生成 `.meta`。

每次整合记录来源、资产 ID、附件 token、包内文件、目标路径、已有 GUID、变更摘要和检查结果。附件为空或格式未确认时，只更新映射。资产更新和提交另按项目流程执行。

## 2026-10-13 本地顾客 PSD 接入

用户提供 `顾客_512x512.psd` 与 `醉酒顾客（小怪）_512x512.psd`，均为 512×512 静态拆件，不含动作序列。通过 Photoshop 原生脚本导出可见图层，排除隐藏背景；保留原图层名、透明像素、完整画布和层序，不保存源 PSD。此交付来自本地文件，没有飞书附件 token。

- `customers/parts/` 共 17 张 PNG；`drunk_customer/parts/` 共 14 张 PNG。各角色目录另有 `parts.json`（来源文件名、画布、层序）与 `composite.png`（原合成预览）。拆件重组与原合成的最大通道误差为 1/255。
- Unity 生成 `Prefabs/Characters/Chibi_customers.prefab`（GUID `5db29df9f3d9b534095b8c352cb6df4f`）、`Chibi_drunk_customer.prefab`（GUID `88421727b4a8f494cba6aef31f9157bd`）与新资产的 `.meta`；PPU 320、脚底 pivot `(0.5, 0)`，画布世界高度 1.6。原层序由 SpriteRenderer 排序保留，SortingGroup 避免不同角色拆件交错；所有关节位于共用 `Sprite/Rig` 下，使既有交互回弹仍覆盖全身。
- `Main_1.prefab` 中 `shop_customer0/1/2` 与 `Main_4.prefab` 中 `shop_guest` 使用顾客外观；`Main_4.prefab` 中 `shop_customer_a/b` 使用醉酒顾客外观。保留原角色根、身份、逻辑纸片、位置、控制和交互；没有把酒肆 BOSS 换成小怪。
- 两套预制体各有 Idle / Walk / Run 循环剪辑与同名控制器，走跑时上臂摆动并带动肘部交替屈伸；普通顾客为 17 个渲染件，醉酒顾客由 14 张源图形成 15 个渲染件，酒葫芦和双脚可独立摆动。醉酒顾客另有 3.6 秒 Drink 动作，静止一轮待机后自动举壶饮酒，移动会立即打断并切回 Walk / Run。动画只写 `Sprite/Rig` 后代关节，不写角色根或交互回弹使用的 `Sprite` 变换。
- 重新打开实际 SampleScene 后验证六个实例的预制体来源、trackedRoot、facingSource、32 个渲染件、6 个移动循环剪辑、Drink 动作及控制器；CharacterPuppet EditMode 74 项通过，专用 PlayMode 2 项验证 Idle / Walk / Run、时停待机、自动喝酒和移动打断。动作品质仍需在最终游戏镜头下人工验收。
- 新资产与目录的 `.meta` 检查通过。全项目 gc_scan 仍报告两处现有动态字体数据膨胀（NotoSansSC 与 SourceHanSerifCN），本次未修改字体资产，不能将全项目扫描报告为通过。
- 这是刚性拆件关节动画，不是网格骨骼形变。FramePuppetGenerator 仍只负责整帧动画；本次仅复用其控制器生成逻辑，关节坐标与图层映射专用于这两份 PSD，不构成通用拆件动画系统。

本次操作脚本为 `Tools/export-character-parts.jsx`、`Tools/apply-customer-parts.txt`、`Tools/animate-customer-parts.txt`、`Tools/refine-customer-animation.txt`；校验为 `Tools/verify-character-parts.py`、`Tools/verify-customer-unity.txt`，预览采样脚本为 `Tools/preview-customer-animation.txt`。应用与动画脚本均按当前交付保护既有结构，不能当成任意角色生成器。原 ShopPlaytestBuilder 重建店铺仍会选用占位小人，重建后需重新核对美术接线。

## 2026-10-17 妻子飞书拆件接收

主线角色表 `ddVEoY` 的 D3 当前交付为《妻子-何素.zip》，228946 字节。包内有 13 张 512×512 PNG 静态拆件，不含 `.meta`；缺少 `.meta` 不构成美术交付不合格，应由项目导入流程通过 Unity 生成。

- 原始 PNG 按既有映射接收到 `Art/Sprites/Characters/wife/parts/`，保留包内图名、透明像素和完整画布；逐文件散列与交付包一致。
- Unity 生成 13 份图片 `.meta` 和 wife、parts 两份目录 `.meta`。已核对图片为 Sprite Single、GUID 唯一，且通过服务器发布代码的资产校验；未组装预制体、制作动画或替换场景角色。
- 已按真实包内成员形成 13 项文件绑定清单。服务器配置尚未安装本批绑定，远端素材提交、PR 和服务部署均未完成；不能把本地接收或映射表条目当作已启用自动同步的证据。

## 2026-10-18 九包接收与后续规则

两张角色表版本 204 的九个 ZIP 均已下载并检查：合计 146 张 512×512 RGBA PNG；六个包另含 PSD，原件保留在本地原始 ZIP，不导入运行时图片目录。全部 PNG 保留原始字节、画布和文件名。

以下路径相对 `Art/Sprites/Characters/`；交付区仅保存候选源图，不表示已接入预制体。

| 来源单元格 | 交付包 | PNG 数 | 本地接收位置与结果 |
| --- | --- | --- | --- |
| ddVEoY D3 | 妻子-何素.zip | 13 | `wife/parts/`；与已接收文件逐字节一致，不重复写入 |
| ddVEoY D4 | 轮回堂值守.zip | 15 | `lunhuitang_guard/parts/`；新增 |
| ddVEoY D6 | chr_citizen.zip | 19 | `villagers/parts/`；新增 |
| ddVEoY D9 | 王录业.zip | 15 | `wang_luye/parts/`；新增 |
| pKSkSs D6 | chr_customer.zip | 17 | `customers/deliveries/20261018/parts/`；与既有图件像素不一致，保留候选，不覆盖当前动画 |
| pKSkSs D7 | chr_goldsmith.zip | 19 | `goldsmith/parts/`；新增 |
| pKSkSs D8 | chr_official_witness.zip | 16 | `official_witness/parts/`；新增 |
| pKSkSs D9 | chr_servant.zip | 18 | `handyman/parts/`；新增 |
| pKSkSs D11 | chr_drunk _customer.zip | 14 | `drunk_customer/deliveries/20261018/parts/`；与既有图件像素不一致，保留候选，不覆盖当前动画 |

接收规则：

1. 表内新增或替换的交付附件属于实质性素材变化，必须进入下载、包内检查和映射核对流程；不能只报单元格变化后静默结束。新增角色按本表资产 ID 定位，不用附件文件名另建角色 ID。
2. ZIP 先检查路径穿越、符号链接、大小及重名冲突，仅接收明确核实的图片成员；PSD 等源文件保留原包，不把 ZIP 整包解压进 Assets。编号 PNG 不自动当作整帧序列或推断身体部位。
3. 同内容不重复写入；已有动画角色须先按内容比较并核对图层与关节绑定。不同内容或部件关系不明确时，接收到该角色的 `deliveries/<接收日期>/parts/`，保留现有 `parts/`、`.meta`、pivot、PPU、控制器和预制体引用，待明确对应后再替换。
4. 美术无需提供 `.meta`；新图片及新目录由本项目 Unity 导入生成。已有目标 `.meta` 必须保留。缺少本地 Unity 导入结果时标为“已落盘、待导入”，不能宣称可提交或已自动发布。
5. 文件绑定必须记录实际附件 token、包内完整成员和目标文件；原包内的空格和大小写保持精确。缺绑定时通知具体角色和缺项，不能把“提醒成功”当作“资产同步成功”。删除或改名只提醒，不自动删除项目文件。
6. 发布通知分开说明“发现交付 / 已落盘 / 已导入 / 已推送分支 / PR 链接或未建原因”。仅真实文件差异触发推送及 PR，仍由用户合入主支；策划正文与表格快照不随美术发布。

本批新增 133 张 PNG，13 张妻子图件复用已有文件。正在运行的 Unity 构建进程已自动导入新增文件；146 张图片均为 Sprite Single，图片及所属目录的 `.meta` 齐全、GUID 唯一，原图散列与交付一致，并通过现有 `assets.validate_files` 校验。已生成真实包内成员与目标文件的绑定记录；服务器配置未安装本批文件绑定，不代表服务器自动接收规则已部署。未组装新角色或替换已有动画。

## 服务器资产同步范围

已确认的交付素材提交、推送到本人仓库的 `codex/feishu-sync` 分支，审核后再手动合并。策划文档自动更新的当前实现仅同步初始基线之后新增的 `.docx`、`.pdf` 策划附件及其后续更新，目标为 `docs/design/spotlight/auto-sync/`；正文、需求表、画板和参考图只监测变化并提醒，不上传。现有人工转写不直接覆盖，配置与验证情况见下节。

执行入口为 `scripts/feishu-monitor/monitor.py`，复用服务器定时器；`assets.py` 只接收服务器私有配置 `asset_sync.bindings` 中的交付 token、压缩包内文件名（如有）与完整目标文件路径。本表提供目标目录；首个交付包核对后再建立文件绑定，不能根据需求名称猜测源文件。服务器凭据、绑定信息和监测基线不提交到仓库。

自动写入仅限 `Assets/_Project/Art/`、`Assets/_Project/Audio/` 下绑定的素材，压缩包只读取明确绑定的成员，不整包解压。已有 `.meta` 必须保留原样；新素材及目录必须具备 Unity 生成的 `.meta` 才能提交。缺绑定、缺 `.meta`、路径冲突或推送失败时只通知并保留状态，不自动删除资产、不强制推送、不写主分支或上游仓库。

## 策划文档自动更新

执行载体复用 `feishu-monitor.timer`，代码入口为 `monitor.py` → `documents.py`。服务器私有 `config.json` 的 `document_sync` 对象复用 `asset_sync` 的 `repository`、`ssh_command`、`author_name`、`author_email` 四项仓库配置，并须配置初始附件 token 列表 `baseline_tokens`（不需要 `bindings`）；缺少基线时拒绝上传，避免把存量文档全部发布。仅配置素材同步不会启用文档同步。

- `auto-sync/README.md` 保存新增策划附件的索引，附件保留原件；仅处理名称后缀为 `.docx`、`.pdf` 且 token 不在初始基线中的附件。正文、表格、画板、参考图、xlsx 及基线内的存量附件不在文档发布范围。
- `00–05` 保留人工整理版本；`06`、`07`、`source/`、功能拆分文档及本映射表不被覆盖。源引用删除后只从最新索引移除链接，不自动删除历史文件。
- 资源读取失败、附件读取期间内容变化、附件超过 50MiB 或格式不支持时，保留上次同步基线并通知；下一轮重试。成功推送并核对远端后才保存 `document_hashes`、`document_commit`。
- 更新进入 `codex/feishu-sync` 分支，审核后手动合并，本地工作区不会自动拉取。撤销 `document_sync` 即停止文档发布。

2026-10-11 已核对本地提交 `4b55a52`（`docs(design): 同步新增策划附件`），并已通过 PR #12 合入当前历史：新增 [附件索引](../../docs/design/spotlight/auto-sync/README.md) 与 [重要角色与势力.docx](../../docs/design/spotlight/auto-sync/media_EEFlbeZQIopbPuxIxrfcddqDnqb.docx)。这证明附件已进入仓库；不单凭提交记录确认服务器定时器当前仍在运行。

上线检查：查看 timer、`state.json` 中 `checked_at` / `document_commit` / `errors`，并核对远端分支中的 `auto-sync/README.md`。本次未复核服务器当前状态；两张角色表已按上文日期重新核对，其余三张表仍沿用早期快照。

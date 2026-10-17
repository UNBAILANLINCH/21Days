# 通俗表剧情内容导入

本批来源为说书人版第一章更新1018工作簿的“通俗用法”，共478条，覆盖序章与入镜、名册勘察、验堤三个阶段。源工作簿未改动。

## 已接入的内容

- 剧情源表：`Tables/Data/narrative/spotlight_current.json`，剧情编号 `spotlight_current`。
- 对白源表：`Tables/Data/dialogue/21000.json` 至 `21020.json`，共21段、355条说书人、对白及文书。
- 逐行对照：同目录 `writer-import.json`，保留全部478条内容、来源序号、地点、时间、声音、演出说明、任务建议和物品说明。
- 二进制配置由 `scripts/gen-tables.ps1` 生成，不手工编辑。原示例内容与五店战斗配置保留。
- 后续接线新增21100教学／挑战桥接对白、21101五店角色对白。21101来自《第一章与五店_文策填写表》的角色台词，按身份、领取及醉酒状态选择；原表问答酒令按用户后续定案改为划拳。

说话人姓名写入 `speakerName`，未定头像暂不引用立绘资源。演出说明和策划说明共123条不显示为玩家台词，也未伪装成已制作的动画或音效。声音仅保留来源，不自动绑定未确认的声音资源。

## 玩法接线边界

剧情阶段图有20个操作等待点：14个地点衔接、6个源稿明确的玩法或演出环节。同一说书梦境内的地点切换连续播放。

这些等待点使用既有 `WaitAction`。本轮已接正常新局至会面结束：序章地点交互、实际教学切磋、接案／书库／入镜／轮回堂交互、真实三店成果与酒肆局部BOSS胜利、楼梯口及坊市巷会面。角色和地点通过SampleScene预摆锚点表现，未制作独立完整人间地图。下一段稳定停在 `enter_r0293`，名簿翻阅、合成准备、老吏制服与抄录演出仍等待后续关卡实现。

关卡接线使用 `NarrativeService`：有效且已登记的 `NarrativeTrigger` 作为目标，调用 `StartAsync("spotlight_current", targetId)`。等待操作完成时，按当前 `Capture().Current` 的身份构造 `NarrativeIntent`，通过 `SubmitAsync` 提交 `Success`；不能直接改存档阶段，也不能对未实际完成的操作统一自动提交。等待点与源行号见对照文件的 `waits`。

已新增正式任务900，以真实对白完成、三店背包成果、酒肆胜利推进到会面；阶段二目标不自动完成。源稿八处建议的后续调查／奖励尚未全部制作。流程抵达 `CurrentContentEnd` 只代表本批文本结束，不代表全游戏结案。完整验证结果见 `PRP/tavern-story-guidance/tasks.md`。

## 更新方式

使用带 `openpyxl` 的 Python 运行 `scripts/import-writer-story.py <工作簿路径>`，输出补丁供 `apply_patch` 审阅和应用；不会直接修改工作簿或生成物。之后运行 `scripts/gen-tables.ps1`，刷新 Unity，并运行 DialogueCatalogTests 和 NarrativeCatalogTests。

转换器自检：`python scripts/import-writer-story.py --self-test`。未知选项和未拥有的对白编号冲突会拒绝导入。更新后需检查旧对白是否已不再被阶段引用，再明确处理废弃源文件，不自动删除其它内容。

---
type: extension-guide
module: rhythm
layer: runtime
maturity: stable
---
# Rhythm 扩展指南

先读 [指南](rhythm-module-guide.md) 与 [公开接口](rhythm-external-api.md)。

## 调整当前试玩

在 ChongErFei 配置中编辑片段起点、长度、下落预览时间与判定窗口。
旧版 noteTimes 是相对片段起点的秒数，noteLanes 一一对应；迁移菜单转为版本 2 并清空旧数组。
新版只编辑 notes 的毫秒记录：唯一 id、lane 0～3、timeMs、Tap/Hold、durationMs；Tap 时长为零，Hold 正时长。稳定排序只发生在运行副本。
同轨头部间隔大于两倍 Good 窗口，下一枚头窗不能进入前一 Hold 占用；对齐后终点在片段内，运行时另留尾窗缓冲。
BPM 段可留空；它是制谱网格元数据，不能与音符秒数同时作为可编辑真值。当前没有正式外部格式转换器；PRP 内的起音分析与自动网格脚本只生成待人工审核的测试谱。
首音符可为零，负歌曲时间提供倒数与预滚，不把首音符整体移到倒数之后。
先试听确认起点与音符，再改轨道密度。固定轨道模式不是已验证的音乐设计。
新键位通过 RhythmInput 资产修改；Lane0～Lane3 名字保持不变。
四轨布局固定，变更轨道数需一起改输入接线、判定边界和界面。

## 延迟调节

持续晚按时调正值，持续早按时调负值；先用稳定单音节奏体验。
当前校准播放预约参考音，统计原始跟拍误差的中位数/MAD，并检查时间分块覆盖与漂移。固定主轮8+32，补测一次8拍；分析/试听/取消保持旧值，明确应用才保存。试听改变显示的校正误差，不移动参考声音。补测分类与主轮共用配置 MAD 上限，修改阈值须增加非30ms回归；20ms分块/独立一致门仍为待真人验证的工程策略。这是用户综合偏差，不能称为测出硬件物理延迟。
chartOffsetMs 修正歌曲谱面对齐、OffsetMs 修正输入、VisualOffsetMs 只移动显示时间；正视觉值延后画面。不要跨项重复补偿。
未来分步视觉/听觉向导需独立定义符号与可辨识范围，两项玩家采样都有输入偏差，不能直接相加。
不要用画面落点或 Update 时间估计输入时间。
真实校准失败先看本地calibration-v1 JSON的原始样本、剔除标记、各块覆盖和跨度；旧日志仅reason/matched，无法反推历史原始数据。新增诊断不能放宽原门槛或自动应用建议。调整诊断保留策略须覆盖容量标记、文件限额、保存失败和取消清理，测试使用SaveRootOverride并标识isolated-test。补测资格必须同时落实View灰态与State入口，不能提示一个必然不能挽救Drift的操作。
输入水位取上一轮已完成 Dynamic 输入批次的同域时刻；Fixed/Manual 在开始前拒绝，演奏中切换则终止。不要改回 DSP 当前时刻，也不要将迟到事件钳到水位来换取通过。批次延后只影响反馈送达，不改判定时间与补偿符号。

## 新玩法类型

现有 Hold 头部命中后保持到尾成功，早放失败；统计按整枚而非头尾双倍计分。改为精确尾部 Release 判定属于玩法变更，须先确认。
2026-10-04 用户已取消伪透视；视觉扩展复用 RhythmTrackGraphic 的等宽四轨与线性时间映射，长条头尾走同一映射，头部大小不随高度改变；不能用屏幕距离判定。保留可用古风/琴弦元素，皮肤保持乐器无关，不把弦数当键数。透视仅在未来确有需求时再评估，当前优先试听改谱、长按手感与校准；后续见 [规格](../../../../PRP/rhythm-followup/spec.md)。
歌曲成绩已使用现有 ISaveService 的独立 Profile，不另建保存服务。
正式衔接流程由玩法路由调用 RhythmState，先处理结束去向与原场景模块暂停。当前仅隔离 RhythmDemo：Boot 接线、正式 UI prefab 制作与迁移后置。
独立 DemoEntry 只用于试玩场景，不能和正式标题路由一起注册。

## 回归

新增曲目先创建独立 RhythmConfig 与音频引用，再加入 RhythmSongCatalog：给唯一稳定 songId/ChartId、可读标题、描述标签、非空 revision/rulesetId/scoringVersion 和 1～100 的整数通过百分比；前置留空或引用已有曲目 ID。曲库拒绝循环依赖，运行时 ScrollRect 按条目生成行；扩充数量应检查滚动、锁定、选择保持和详情布局。当前一曲一谱，difficulty 标签不产生难度分支，不为扩展预生成其他谱。

修改音符或片段明确提升 revision；更改判定/评分分别提升 rulesetId/scoringVersion。当前最佳只比较五项版本键完全一致的完整局，不把 legacy 未知版本高分当新纪录。修改标题不换内容身份；重新利用另一首 songId 会污染解锁和历史归属。保留已获得 UnlockedSongs，纯谱面修订不撤销曲目进入资格；当前谱面通关另查。

新增纪录字段须从一份真实 RhythmRunResult 派生，不取不同局的 bestScore/bestCombo 拼成假单局。新增评分规则需同步结果统计守卫和版本成绩隔离测试。未知旧判定数保留 legacy 展示，不用最高分反推历史统计。

存档扩展先定义支持版本与迁移；保留原 legacy 容器，迁移幂等。真实档读取前用 RhythmProgressArchive 原 bytes/SHA256 备份，未知版本或坏结构须拒绝覆盖；不可通过删玩家档来测试恢复。SaveRootOverride 隔离测试应覆盖备份 bytes 相等、重复迁移、未来版本拒绝和保存重开。

## 后续乐师接线

先由 caller 用真实 TamingRules 的 IsTargetTamed、CanControl、CurrentControlId 与稳定角色 ID 实现 IRhythmEntryPermission；职业/场景资格来自玩法，不在音游猜类型。已驯服开放曲库；所有外部场景演奏包括 FreePlay 均须当前控制乐师，context 失效结果拒收；未配置外部请求的 Demo 自由局不绑定 Boot。以新 runId 配置 RhythmPlayRequest 和 ExternalSession，再由现有流程进入；普通自由局不得转发战斗效果。

战斗成功标准实现 IRhythmCombatPolicy，伤害/奖励/失败效果由 caller 的成功/失败回调负责，未定数值不写默认惩罚。中断和技术错误分别通知，不当作失败或自然完成。场景卸载主动 Invalidate，重试新 runId；策略/回调异常时不得重新投递同份结果。去重只在同一 consumer 实例生命周期内有效，不把新建实例当全局恰好一次。回调接后续帧路由，避免结束清理期间立即重入。

先完成隔离回归再规划 Boot/正式 prefab：明确注册范围、进入/返回位置、世界暂停/恢复及 UI 生命周期；当前暂停令牌冻结世界 tick，未来战斗是否实时须先决定。不能把 DemoEntry 与正式标题路由一起注册。当前 runtime ScrollRect 不代表正式 UI prefab 已制作，不改既有驯服/控制模块或怪物生命字段。

保持 PCM/DecompressOnLoad 与音频预加载，片段起点和长度必须在实际解码音频内；片段末尾须容纳最后音符、Good 窗与最大 +300ms 输入补偿。自动制谱、完整字段及音频 hash 的只读验证见 `PRP/rhythm-song-progression-20261004/verify-charts.mjs` 和 `verify-installed.mjs`。浏览器解码与 Unity PCM 的人工对照仍需另做，不自动施加固定 11ms 补偿。

进度与选曲调整增加 RhythmProgressTests/纪录迁移测试；外部契约跑 RhythmExternalSessionTests，覆盖重复/过期/错误身份、自由局隔离、中断/技术错误及回调异常。选曲接线跑 RhythmLibraryShowcase：20 曲滚动、锁定、选择保持、准备取消、重试、外部权限/结果、真实读盘与重入恢复。测试沿用 SaveRootOverride 隔离。2026-10-05 最终EditMode171/171、五项新增曲库回放通过；定向八项整体7通过/1失败，确认式校准Suggested预设收到Drift，仍须定位/复验，详见[实施记录](../../../../PRP/rhythm-library-integration-20261005/implementation-report.md)。

规则调整先跑 RhythmRulesTests，校准分类跑 RhythmCalibrationFlowTests；音频/输入/UI 接线调整跑 RhythmShowcase。校准回放使用默认8+32验证无输入和漂移，仅应用接线允许短配置副本加速，并须真实读盘确认旧值保持与确认后重入恢复。跨轨组合复用 Tap/Hold，同轨冲突仍拒绝，不新增组合枚举。
核对补偿符号、重复输入、掉帧漏按、重新开始清零、退出资源恢复。
谱面或视觉改动还需人工试听、检查四轨可读性与手感。

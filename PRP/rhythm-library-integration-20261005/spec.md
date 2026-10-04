# 隔离音游曲库、纪录和外部演奏契约

用户已授权在RhythmDemo增量完成一曲一谱曲库、个人纪录、旧档兼容和最小乐师玩法接口。两首进阶曲作为测试正曲，不精修听感；Boot正式接线、正式UI prefab制作与迁移后置。没有多难度选择、新音符或在线榜。

## 验收

1. 旧最高分/通关不丢；迁移重复幂等；纯谱面修订不撤回已开放歌曲；未知历史统计以legacy保留。
2. 单局结果不可变、含runId/模式/完成原因/曲谱和评分身份/真实统计；完整自由局纪录不混拼不同局。
3. 大于三曲的UGUI列表可滚动，锁定原因对应真实前置，选中身份与滚动位置保持；换曲、重试和重入正确。
4. 已驯服与当前控制权限分开；过期/重复结果拒绝；自由局不调用场景效果；中断/技术错误不冒充战斗失败。
5. 真实隔离存档保存/迁移/失败路径、EditMode及Showcase通过，保留历史失败；校准和DSP/input批次语义回归。

## 决定与文件所有权

- 保留SongData的一份Chart和现schemaV2，增加显式曲谱revision与规则/评分身份。不可变RunResult负责单局事实；Progress v2保留旧字段为legacy，新增同局纪录和歌曲永久开放集合。
- 迁移先保留原始Profile备份，再经框架版本读/写；无损未知字段不假造。原始坏档/未来版本保留并停止覆盖，而不是悄悄当空档写回。
- View复用UIView，在运行时构造ScrollRect条目和详情；不制作新正式prefab。
- 外部权限/成功策略由caller注入；读取真实TamingRules/EncounterStep控制契约，不猜Musician职业类型，不实现伤害/惩罚或世界事务。
- records代理独占ProgressData/Rules、SongData、CatalogConfig、新RunResult/Record及专属EditMode测试；entry_contract代理独占新外部请求/权限/消费类及专属测试。主线程独占State/View/Showcase/既有测试调整、文档、Unity与Git协调。

当前已实际读取Unity为干净RhythmDemo、idle/非Play/无测试/倍率1/Console error=[]。后续操作仍保护用户Play/脏场景；不提交/push/build或改Boot/驯服模块。

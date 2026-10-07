# 独立音游 Windows 测试预设

预设通过 `scripts/build.ps1 -Target Windows -RhythmTest` 显式选择；默认 Windows/Android 构建不启用它。不需要改 Build Settings 或保存一个不同的场景。

构建前必须关闭整个 Unity Editor，仅停止 Play 不够。不要删除 UnityLockfile 或从编辑器菜单绕过批处理入口。构建命令示例（输出目录每轮另取名字）：

```powershell
powershell -NoProfile -File scripts/build.ps1 -Target Windows -RhythmTest -OutputPath Builds/RhythmTest-20261003/21Days-RhythmTest.exe -UnityPath '<已安装的本项目版本 Unity.exe>'
```

预设只以 `Assets/_Project/Scenes/RhythmDemo.unity` 启动，包含它直接引用的音乐、输入与配置。Addressables 保留 Config 标签表，以及 RhythmView、TitleView、SettingsView、PauseMenuView、NotificationView、ConfirmView、SaveSlotsView 七个 UI 地址；其他打包组临时排除，不关闭占位素材闸门。默认场景清单、原分组、Entry 地址/标签/只读状态、包含开关、默认组、产品名、版本号及原配置文件字节均在作用域退出时恢复。批处理退出在恢复之后执行。

测试包的临时产品名为 `21Days-RhythmTest`，Player 编译附加 `GAME_ISOLATED_TEST_BUILD`。全部游戏 JSON 数据位于独立产品数据目录中的 `isolated-test/saves`，包括 `profile-settings.json`、补偿、存档槽、备份和损坏文件；没有向正式目录回退读取。编辑器和普通包保持原产品名、`saves` 路径及 `rhythm-calibration` key，独立测试包使用 `rhythm-test-calibration` key。不迁移或删除玩家现有数据。

编辑器中可通过 `21Days/音游/检查独立 Windows 测试预设（不构建）` 做短暂应用/恢复与依赖闸门检查。这不验证构建许可证、Player 启动或物理音画延迟。

## Player 试玩

1. 运行 EXE，应直接进入音游准备页，原 Boot/SampleScene 不作为起始场景。
2. 点“虫儿飞试玩”，按 D/F/J/K 测试 56Tap，重开后分数归零。
3. 点“短 Tap/Hold 练习”：D/F 单击，J/K 按住到尾，看到完成反馈后松开；另试提前松开，应整条失败。
4. 点“参考拍自动校准”，默认先听 8 拍，再跟拍 32 次。无样本或不稳定应保留旧值；稳定接受后可手调。参考拍是单独生成的节拍器。
5. 返回标题打开设置，改变音量后重启测试包检查保存。正式包/编辑器的 settings 和补偿不应被修改。
6. 检查失焦终止、标题重入和测试包再次启动。首次 Player 验证仍须实际构建后执行。

## 局部试听

打开同目录 `index.html`，分别听原声、A 当前目标点击、B 候选目标点击。每份重复四次，可循环；开始一条时其他音轨会暂停。中段为原曲 34.9–37.5 秒，后段为 46.8–48 秒。先听原声判断目标声部，再比较 A/B，记录更自然的一组或两组都不合适。

`decoder-comparison.json` 记录已有 Windows/Unity PCM 对比：三个窗口均有 11ms 固定原点差。`candidate-diff.csv` 均为 applied=False。候选没有写入正式 56Tap；频谱分析不是实际试听，本轮不声称音乐贴合度或手感已通过。

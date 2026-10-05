# 原仓库后续功能候选清单

本清单按 TOHE 原仓库的后续提交审计，不代表 Innersloth 原版更新日志。
比较范围为回退基线 `3676f644e`（TOHE 2.1.1 / AU 2024.10.29）至本地
`upstream/main` 的 `3ab9cefd9`（2026-02-13）。范围共有 4,135 个提交，包含合并与重复开发历史；
以下以最终代码实际实现为准。用户随后选择了 A、B、E、F、G、H、I、J；当前分支的重新实现、
用法和验证边界见 [选定功能实现说明](selected-features-2026.8.18.md)。其余候选保持未选择。

## 可选择的功能包

| 编号 | 功能与实际收益 | 证据提交、上游文件 | 成本与注意事项 |
| --- | --- | --- | --- |
| A | 原版 Detective / Viper 纳入模组职业池：配置嫌疑人数、尸体溶解时间 | `a7cde690e`；`Roles/Vanilla/DetectiveTOHE.cs`、`ViperTOHE.cs` | 中；区别于已有自定义 Detective，保留当前 2026 原版设置同步方式 |
| B | 预设保存、载入、分享：`/save`、`/load` 操作可读 JSON `.preset` | `c0b8dd203`、`6ea70a29a`、`f30faf98d`、`4438d43c9`；`Modules/OptionCopier.cs` | 低至中；版本不匹配、未知选项和越界值需要验证，不能无条件覆盖当前预设 |
| C | 职业草稿与牌组：玩家选择候选职业，房主配置牌组，`/draft`、`/deck` | `3cf983041`、`791587ef1`、`a5c48e29f`、`4d103b845`、`c9cfc0b58`；`Roles/Core/AssignManager/DraftAssign.cs` | 高；依赖新职业分类、阵营和分配生命周期，适合完整玩法包移植 |
| D | SpeedRun：任务与生存竞速，任务提供短暂加速/护盾，完成任务后可击杀；结束展示成绩 | `19547ced0`、`30d5ba568`；`GameModes/SpeedRun.cs` | 高；涉及任务、胜利、击杀、HUD 和同步 |
| E | 房间浏览 UI：可滚动更多房间、显示真实房主名/房间码；服务器列表多列排列 | `ceae88295`、`a07955922`、`9982e43da`；`Patches/RegionMenuPatch.cs` | 中；需要重新对照 2026 匹配界面和布局，模组过滤 TODO 不在已实现范围 |
| F | 本地标签与授权：按 FriendCode 设置称号、渐变色、指定 GM、管理命令权限 | `02f536a98`、`83b7fa9da`、`033276a4a`；`Modules/TagManager.cs` | 中；建议将外观和管理权限分开移植，严格验证执行者及房主身份 |
| G | AFK 检测、豁免与定向黑屏修复 | `61053e0c3`；`Modules/AFKDetector.cs`、`Patches/ChatCommandPatch.cs` | 中至高；需避免将加载/正常静止误判，修复涉及角色与位置重建 |
| H | 首刀冷却独立开关 | `bc37235c5`；`Modules/OptionHolder.cs`、`Patches/IntroPatch.cs` | 低；保留当前开局之后同步的实现，不能带回开局前 KCD=0 |
| I | 禁用隐藏职业转换：关闭 Jester→Sunnyboy 等隐式随机替换 | `ff78046c9`；`Modules/OptionHolder.cs`、`Roles/Core/AssignManager/RoleAssign.cs` | 低；选角结果更可预测 |
| J | 附加职业短名：减少头顶、会议、结果界面文字拥挤 | `e6c1599c1`；`Modules/OptionHolder.cs`、`Modules/Utils.cs` | 低至中；应覆盖全部显示入口和语言 |
| K | 非船员公共任务随机化：伪装任务不必与船员公共任务完全一致 | `7fc51e126`；`Modules/OptionHolder.cs`、`Patches/TaskAssignPatch.cs` | 低；需沿用当前原生任务分配和房主权限约束 |
| L | Coven 巫师阵营：独立胜利、Necronomicon 转交、招募及一组职业 | `095f94bb3` 起；`Roles/Coven/CovenManager.cs` 及阵营职业 | 很高；涉及分配、猜测、名称、胜利和附加职业规则，不适合直接摘取单个提交 |

建议先选择 A、B、E、H、I、J；C、D 为第二档玩法升级，L 单独规划。

## 可单独考虑的新职业

| 职业 | 行为 | 证据 | 依赖/成本 |
| --- | --- | --- | --- |
| Socialite | 邀请目标进入保护名单，阻挡名单外玩家击杀 | `74a1e12d3`；`Roles/Crewmate/Socialite.cs` | 中 |
| Catalyst | 给目标增加技能次数并降低冷却 | `9f2a62484`；`Roles/Crewmate/Catalyst.cs` | 中；依赖中央 AbilityUseManager，上游实验性 |
| Cupid | 配对 Lovers 并提供保护 | `6fbf34d5b`；`Roles/Neutral/Cupid.cs` | 高；依赖 Lovers 重构与技能同步，上游实验性 |
| Inquisitor、Lich、Starspawn、Summoner/Randomizer | 新胜利关系、阵营或动态职业玩法 | `56918d36b`、`28eeb5b0a`、`95ea955cd`、`c2c698b73` | 高；建议在核心角色生命周期稳定后逐个审计 |

## 不重复合并的内容

- 大厅左上角预览已在当前迁移分支重写，参考 `7d7a47dea`、`9e51c20c6`；不重复移植旧 GUI 生命周期。
- `/poll` 与多个职业的“完成任务补充技能次数”当前已经存在。
- `b586cef86` 的 AbilityUseManager 是中央技能次数重构，不等于首次提供任务充能玩法。
- `Patches/FindAGameManagerPatch.cs` 内模组过滤只是注释 TODO，不能列为可用功能。
- 上游目标为 AU 2025.9.9 / net6 / BepInEx 692。不得覆盖当前 RPC123、原生 Mod GUID、私密命令、Phantom/Judge、开局选项及任务迁移代码。
- 不恢复用户已要求删除的 TOHE API、混淆和隐藏控制台逻辑；Ratelimit 反作弊也不在候选范围。

证据为本地 Git 对象和静态调用链；成本是移植范围估算，不是功能兼容性验证。

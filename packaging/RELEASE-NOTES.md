# TOHEE v2.1.1-au20260929.4

支持 Among Us **2026.9.29 / 19.0**，基于 TOHE 2.1.1。

- 接入原版新幽灵职业 Spirit Guide（灵媒），支持生成概率、人数和冷却设置。
- 修复游戏内模组设置菜单首次打开、标签切换及关闭重开的问题。
- 修复 Spirit Guide 技能按钮的 Message / Cancel 提示被普通幽灵文字覆盖的问题。
- 更新至 V12 游戏设置格式。
- 整合上游 Remove GC Max Time Slice 1.0.0 插件。

| 平台 | 下载 |
| --- | --- |
| Steam、Epic Games、Microsoft Store | `TOHEE.v2.1.1-au20260929.4_Steam-Epic-Games-Microsoft-Store_x64.zip` |
| itch.io Windows | `TOHEE.v2.1.1-au20260929.4_itch.io_x86.zip` |
| 已安装本版本 BepInEx 的用户，仅更新插件 | `TOHE.dll` |
| 对应源码 | `TOHEE.v2.1.1-au20260929.4_Source.zip` |

关闭游戏后，将整合包内容解压到 `Among Us.exe` 所在目录。首次启动会生成该游戏安装的 interop；请等待初始化完成。

两份整合包均包含 BepInEx **6.0.0-be.755+3fab71a**、TOHE 和上游同款 Mini.RegionInstall 1.2.0，预置 Niko233(NA) 区域。包内附安装说明，`SHA256SUMS.txt` 提供发布附件校验值。DLL 为 AnyCPU，整合包中的原生加载器与运行时按平台位数区分。

验证：itch.io 19.0 已完成设置菜单和真实双客户端幽灵职业收发回归；Steam / Epic / Microsoft Store 包完成原生 PE 架构及文件检查，尚未在这些商店客户端中实测。官方服务器多人局和所有模组职业的完整回归尚未完成。

---

Supports **Among Us 2026.9.29 / 19.0**, based on TOHE 2.1.1.

Adds the native Spirit Guide ghost role with spawn chance, count and cooldown options, fixes the in-game mod settings menu and Spirit Guide ability labels, and updates game settings to V12.
Both packages include the upstream Remove GC Max Time Slice 1.0.0 plugin.

Use the **x64** package for Steam, Epic Games and Microsoft Store. Use **x86** only for itch.io. Extract beside `Among Us.exe` with the game closed. First launch generates interop assemblies for your installation.

The itch.io client passed settings-menu and real two-client Spirit Guide send/receive tests. The x64 package has been checked structurally; store-specific runtime tests and official-server multiplayer validation remain outstanding.

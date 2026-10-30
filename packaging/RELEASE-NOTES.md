# TOHEE v2.1.1-20261030.1

- Client Options 新增自动重载标签：房主每 5 秒检查本地标签文件变化，大厅中自动刷新名字；`/tags reload` 仍可手动使用。
- 修复混合客户端会议和流放后的 AntiBlackout 接收者保护、延迟死亡及幽灵状态同步。
- 修复会议结束后原生职业、冷却时间和个人选项的恢复。

本版仍为实验性版本。上述游戏流程尚未经过正式服务器验证。

---

- Added Auto Reload Tags to Client Options. Hosts check local tag files every 5 seconds and refresh lobby names when they change; `/tags reload` remains available.
- Improved recipient protection, delayed deaths, and ghost state synchronization around mixed-client meetings and exile.
- Fixed restoration of vanilla roles, cooldowns, and per-player options after meetings.

This is an experimental release. These gameplay flows have not been validated on official servers.

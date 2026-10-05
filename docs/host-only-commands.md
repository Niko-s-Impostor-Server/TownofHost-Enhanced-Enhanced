# /cmd 私有命令

Among Us 18 的 [官方 host-only 命令通道](https://github.com/Innersloth-LLC/AmongUsModdingInformation)
使用字节零开始的小写 `/cmd`。TOHEE 将它作为已有指令的外层封包：

- `/cmd id` 与 `/cmd /id` 均分发已有 `/id`。
- `/cmd guess 1 sheriff` 分发已有猜测指令；角色、阶段及次数仍由原处理器检查。
- 大小写不同或带前导空格的输入不是私有封包。`/cmd`、`/cmdfoo`、空正文及
  多行输入会被消费，但不会执行，也不会作为公共聊天显示。

模组非房主通过自己的 PlayerControl 发出原版 Reliable SendChat RPC，目标为
HostId。原文保留 `/cmd` 前缀，不添加名称格式使用的换行，不额外发送第二次角色
RPC。房主自己的输入在本地处理并取消广播。原版非房主的 `/cmd` 依靠支持该功能的
服务器路由；只读 Nmpostor 参考源码在 host-authoritative 房间内将该前缀消息发给房主。

同步分发期间，公共聊天历史、20 条隐藏刷屏、历史重放和原指令公开回显全部跳过。
私人未知指令也不会触发普通公共聊天的“催开局”关键词警告。普通聊天与旧的 `/指令`
继续原有行为；本地召回历史保留完整 `/cmd` 前缀，防止召回后意外变成公开指令。

命令效果与响应目标仍由原处理器决定，例如公开技能结果、投票或公告仍可能向
全房间显示。`/cmd` 只隐藏指令输入，不将原本的公开效果改成私密效果，也不授予房主
或管理员权限。同步作用域不会跨越延迟任务，以免误吞后续正常消息。

离线回归运行：

```powershell
dotnet run --project tests/HostOnlyChatCommand/HostOnlyChatCommand.csproj
```

测试直接链接解析器并提取生产分发/发送/隐藏路径，覆盖 145 项边界。原生网络、
Unity 与角色业务效果使用 stub，不能代替真实客户端的送达及第三方不可见验证。
实机范围记录在 [迁移记录](2026.8.18-migration.md)。

# /cmd 离线回归

从仓库根目录运行（需要 Python 3 和 .NET 8 SDK）：

```powershell
dotnet run --project tests/HostOnlyChatCommand/HostOnlyChatCommand.csproj
```

直接链接生产 `Modules/HostOnlyChatCommand.cs`。每次编译时，`generate.py` 从当前生产源码提取接收包装、完整 `RpcSendChatPatch.Prefix`、本地输入解析/原始召回历史/最终取消段、接收核心的前置角色分发及实际 default 防刷屏条件、完整 `ChatManager.SendMessage`、重放前置条件、4 个角色隐藏前置条件和 8 处原指令公开回显条件。生成文件位于 `obj/`，不提交；未复制一份解析或隐私判断逻辑。

覆盖：

- `/cmd` 小写前缀、`/cmdfoo` 等畸形封包消费、可省略 `/`、tab 归一化、换行拒绝、大小写和前导空白边界。
- 嵌套 scope、重复 Dispose、异常及线程隔离；包装总消费私有输入，非房主不执行，未知私有指令跳过公开聊天关键词检测。
- 自己拥有且已连接的非房主向 HostId 发原版 SendChat RPC，Reliable、保留字节零前缀、无格式化换行或本地回显；房主只本地分发一次。
- 本地输入在普通分支重置 canceled 后仍取消私有指令；输入历史保留 `/cmd`，公共重放历史不收录原始封包和作用域内的解包指令。
- 私有作用域内关闭角色隐藏刷屏、历史重放和原指令公开回显；普通消息保留格式化、公共发送、历史、关键词检测及隐藏/回显条件。

这是离线生产路径回归。Unity/IL2CPP、Hazel/native RPC 写入、HUD、权限和角色业务效果使用边界 stub；本地巨大指令 switch 和接收指令 switch 的具体业务分支不包含在此 harness 中。结果不能证明真实客户端送达、服务端定向转发、跨客户端无泄漏、游戏内角色效果或完整权限检查。这些需要另外的真实客户端测试。

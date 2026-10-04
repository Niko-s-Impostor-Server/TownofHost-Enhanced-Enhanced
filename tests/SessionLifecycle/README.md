# 会话生命周期回归检查

需要 .NET 8 或更新 SDK、Python 3。无需游戏客户端、BepInEx 或服务器。

```powershell
dotnet run --project tests/SessionLifecycle/SessionLifecycle.csproj --configuration Release
```

当前运行结果：`SESSION_LIFECYCLE_PASS (43 assertions; extracted production methods, offline stubs)`。断言数包含每次场景重置时验证 pending key 已释放。

构建时 `generate.py` 从当前生产源码抽取：

- `Modules/RPC.cs` 的 `CustomRPC` enum、`PendingVersionRequests` 字段、两个公开版本请求入口以及完整 `WaitAndSendVersion` 协程。
- `Patches/PlayerJoinAndLeftPatch.cs` 的 `Generation` 属性、`IsCurrentSession`、`IsCurrentClient`、`Postfix` 及完整 `WaitForOptions` 协程。

抽取的方法保持原文，生成文件位于 `obj`；方法找不到时构建失败。测试没有另写一份版本等待、会话判定或去重实现。完整 `InitializeSession` 不在此测试范围内，仅用计数桩观察何时被调用；测试容器增加只读 pending 数量观测。Unity 时间、协程调度、客户端、玩家、配置和 writer 均为桩。调度桩在 `StartCoroutine` 时推进到第一次 yield，之后由测试逐帧推进，并对完成或显式取消的 IEnumerator 调用 Dispose。

六组场景覆盖 generation 变化、断开、主机变化、八秒超时、当前 ready 的一次发送以及同会话同类型去重与释放。另检查复用 client ID 但 native pointer 改变时拒绝旧对象、等待选项加载后只初始化一次、十秒选项加载超时，以及 writer 失败时释放等待 key。payload 断言只覆盖正常版本公告；测试不启用 VersionCheat。

测试只记录桩消息，没有 socket、服务器连接或认证调用。结果验证实际抽取方法在所列状态转换下的行为，不验证 Unity/IL2CPP 协程安装、真实 Disconnect/HostMigration 事件时序、网络传输或完整房间初始化，也不覆盖其他加入/离开延迟回调。产品程序集构建和游戏实机回归仍需单独执行。

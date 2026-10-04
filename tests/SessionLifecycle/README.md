# 会话生命周期回归检查

需要 .NET 8 或更新 SDK、Python 3。无需游戏客户端、BepInEx 或服务器。

```powershell
dotnet run --project tests/SessionLifecycle/SessionLifecycle.csproj --configuration Release
```

当前运行结果：`SESSION_LIFECYCLE_PASS (90 assertions; extracted production methods, offline stubs)`。断言数包含每次版本场景重置时验证 pending key 已释放。

构建时 `generate.py` 从当前生产源码抽取：

- `Modules/RPC.cs` 的 `CustomRPC` enum、`PendingVersionRequests` 字段、两个公开版本请求入口以及完整 `WaitAndSendVersion` 协程。
- `Patches/PlayerJoinAndLeftPatch.cs` 的 `Generation` 属性、`IsCurrentSession`、`IsCurrentClient`、`Postfix` 及完整 `WaitForOptions` 协程。
- `Patches/IntroPatch.cs` 的完整 `IntroCutsceneDestroyPatch.Prefix`。
- `Patches/PlayerControlPatch.cs` 的完整 `UnShapeShifter` 判断和循环语句，以及实际包围它的房主、任务阶段、非 low-load、本地主机玩家四层条件；生成器检查这四层嵌套，变化时构建失败。其他 FixedUpdate 工作没有抽取。
- `Modules/Utils.cs` 的完整 `IsMethodOverridden` 反射判断，用桩职业的真实虚方法覆盖关系验证。

抽取的方法保持原文，生成文件位于 `obj`；方法找不到时构建失败。测试没有另写一份版本等待、会话判定或去重实现。完整 `InitializeSession` 不在此测试范围内，仅用计数桩观察何时被调用；测试容器增加只读 pending 数量观测。Unity 时间、协程调度、客户端、玩家、配置和 writer 均为桩。调度桩在 `StartCoroutine` 时推进到第一次 yield，之后由测试逐帧推进，并对完成或显式取消的 IEnumerator 调用 Dispose。

六组场景覆盖 generation 变化、断开、主机变化、八秒超时、当前 ready 的一次发送以及同会话同类型去重与释放。另检查复用 client ID 但 native pointer 改变时拒绝旧对象、等待选项加载后只初始化一次、十秒选项加载超时，以及 writer 失败时释放等待 key。payload 断言只覆盖正常版本公告；测试不启用 VersionCheat。

`UnShapeShiftRegression` 覆盖延迟回调遇到断开、新 generation、客户端替换或消失、主机失去或变化、房间变化、游戏结束、返回大厅、退出游戏阶段、GameManager 替换或消失、PlayerStates 换局时不发送。正常回调保持变身、拒绝变身、重置外观与就绪标记；目标离开、Data 消失、断开、职业失效或更换、同 ID 玩家替换、注册集合变化均不会触发旧目标。固定更新覆盖多个失效 ID 的快照清理、集合仅剩失效 ID、已变身玩家不重复触发、没有可用目标，以及现有任务/房主/本地主机/low-load/mixup/就绪条件。LateTask 桩只保存并手动执行一次回调，不模拟实际三秒计时；变身及外观桩记录调用并模拟 outfit 状态，没有执行游戏方法。

测试只记录桩消息，没有 socket、服务器连接或认证调用。结果验证实际抽取方法和语句在所列状态转换下的行为，不验证 Unity/IL2CPP 协程或 Harmony 安装、真实 Disconnect/HostMigration 事件时序、网络传输、完整 FixedUpdate 或完整房间初始化，也不覆盖其他加入/离开延迟回调。产品程序集构建和游戏实机回归仍需单独执行。

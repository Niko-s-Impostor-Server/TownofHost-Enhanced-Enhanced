# 任务安装与统计回归

需要 .NET 8 SDK 与 Python 3：

```powershell
dotnet run --project tests/TaskLifecycle/TaskLifecycle.csproj --configuration Release
```

项目直接链接当前 `Patches/RecomputeTaskPatch.cs`，并在构建时从 `Modules/GameState.cs` 抽取实际 `TaskState.Init` 方法；不复制补丁或 Init 实现。

当前执行结果：`TASK_LIFECYCLE_PASS (28 assertions; linked production patch and extracted TaskState.Init)`。

场景覆盖先观察空列表造成缓存 0、实际列表已安装但缓存尚未更新、SetTasks postfix 修复初值、完整批次总数、保留已有完成进度及 Workhorse 累计总数、个人角色任务和全局任务胜利资格的区分、断开跳过、未就绪/过期数据，以及无模组房主、FreePlay、HideNSeek、非普通模式的原生放行。

游戏对象及 Harmony attribute 是离线桩。`Utils.HasTasks` 的个人/任务胜利资格由场景明确指定，只验证补丁使用两个资格参数的方式，不验证所有角色资格规则。原生放行验证 prefix 返回 true 且不修改计数，不模拟原生统计实现。测试显式调用生产 postfix，不证明 Harmony 在 IL2CPP 的绑定、原生 RpcSetTasks 调用或协程时序；双实例实机仍需验证安装任务后两端统计与完成一次任务后的变化。没有网络、身份或服务器操作。

# 职业生命周期回归检查

运行条件：.NET 8 或更新 SDK、Python 3。无需游戏客户端、BepInEx 或联网服务。

```powershell
dotnet run --project tests/RoleLifecycle/RoleLifecycle.csproj
```

构建前，`generate.py` 从当前仓库的 Agitater、Deathpact、Mastermind、Vampire、Poisoner、Fireworker、Instigator、Pelican、Shroud 源文件中抽取被测方法，生成到项目 `obj` 目录。测试没有另写一份这些方法的实现；仅移除 `override`，将实际方法放入测试容器类中。容器中的字段、选项值和游戏对象由桩提供，抽取的具体方法见 `generate.py`。

54 项断言检查旧炸弹任务及跨局任务隔离、传递后的爆炸归属、失效炸弹目标、活动契约的目标数量和期限、角色移除及会议清理、失效契约成员、Mastermind 的延迟冷却恢复、Vampire/Poisoner 死亡回调的重复进入、Fireworker 房主权限和重复状态推进、Instigator 混合投票者筛选、Shroud 不同持有者的集合隔离、固定更新回调注销、会议死亡回调重入，以及 Pelican 空对象、持有者离开/职业移除后的目标释放、位置回退、速度及报告权限恢复。Pelican/Shroud 独立清理 RPC 的实际发送/接收方法验证编号保持、房主权限、对象缺失、按持有者隔离和重复接收；RPC 入口验证取消状态不会进入后置处理，reader 副本正常及异常路径均回收。

Pelican 的 `SyncEatenList` 在测试中仅统计调用次数，消息写入器由桩提供；这些断言不验证客户端收到更新。`Modules/RPC.cs` 的 enum、TrustedRpc、Prefix、ValidateRpc，以及 Postfix 的入口检查和 Pelican/Shroud 清理分支从源码抽取；其余分支不参与此测试，替换为计数桩。EAC、聊天及 Harmony 参数特性由桩模拟，不运行 Harmony patch 安装或实际网络消息。角色配置的初始数量限制和 Amnesiac 继承链由源码审查确认，测试直接构造多个角色实例以验证共享状态归属。

这些检查验证的是被抽取方法在所述状态转换下的行为。桩不运行真实 Unity 对象生命周期、IL2CPP、网络 RPC、会议界面、游戏保护机制或多客户端同步，因此通过结果不能替代游戏端验证，也不证明其他职业没有逻辑缺陷。该测试不验证投票 API 和游戏选项的版本适配；该部分依赖实际目标程序集构建和游戏内测试。

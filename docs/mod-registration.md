# TOHEE Mod GUID 注册

模组加载时，将稳定派生的 GUID 写入原版
`CurrentModRegistration.ModRegistrationGuidString`。Among Us 18 的内置逻辑会自动
使用 `HostModdedGame`（tag 25）、序列化 GUID，并在查找房间时添加 `mod` 筛选。
依据：[Innersloth 注册说明](https://github.com/Innersloth-LLC/AmongUsModdingInformation#built-in-registration-helpers-among-us-180)。

在线房间采用 AU MCI 的全客户端身份注册。Innersloth 官方服务的设计是原版客户端无法
加入注册房间，公共匹配池与普通房间分离，搜索限定相同 GUID。GUID 包含版本，因此
不同 `PluginVersion` 的 TOHEE 版本会使用不同搜索身份。这些设计行为来自
[官方 MCI 说明](https://github.com/Innersloth-LLC/AmongUsModdingInformation#all-client-mod-client-identification)。
既有协议版本 `+25` 房主权限标记仍独立生效。

本次目标是 NikoCN 私服，不能把官方服务的入房规则直接当作 Nmpostor 的行为。只读
参考源码支持 tag 25，保存 `Game.ModGuid` 并用于房间列表筛选；是否允许原版通过房间
码加入、部署版本与参考源码是否相同，仍须混合客户端实机验证。注册本身不证明
拒绝或允许原版入房。

## 派生规则

使用 [RFC 9562 UUIDv5](https://www.rfc-editor.org/rfc/rfc9562.html#section-5.5)：

- 命名空间：标准 DNS UUID `6ba7b810-9dad-11d1-80b4-00c04fd430c8`。
- 名称：UTF-8 编码的 `TOHEE:` + `Main.PluginVersion`，不添加换行或其他字符。
- 输入为内部版本（例如 `2026.1005.211.1`），展示版本
  `PluginDisplayVersion`（例如 `2.1.1-au20260818`）不参与派生。
- 同一内部版本在不同安装、启动、分支或提交中生成相同 GUID；更新内部版本生成
  新身份。BepInEx 的 `Main.PluginGuid` 仍用于插件身份与 Harmony。

固定向量：

| 版本输入 | GUID |
| --- | --- |
| `2026.1005.211.1` | `d15f9333-446f-5203-aab4-4b09191bc94e` |
| `2026.1005.211.2` | `1f14ee03-780e-54a6-9308-c1aa5e633ff6` |
| `2.1.1-au20260818` | `9e4f4a85-7cda-5f96-847e-ae35563176b3` |

UUID 哈希输入与结果使用 RFC 字节顺序；结果通过规范字符串解析成 .NET GUID，
避免 `Guid(byte[])` 的混合大小端影响派生。发包由原版处理，使用其
`Guid.ToByteArray()` 顺序；首个向量的 16 字节为
`33 93 5f d1 6f 44 03 52 aa b4 4b 09 19 1b c9 4e`。

## 生命周期与验证

原版字段为空时注册；已是同一 GUID 时可重复注册。如果字段非空且不是本版本
GUID（包括无效 GUID），加载明确报错，保留已有值。卸载先移除本插件的 Harmony
补丁，再仅清除自身拥有的 GUID；其他模组后来替换的身份会保留。
现有切换原版按钮调用该卸载入口。

本地房间与自由练习例外：目标版本的原版 `InnerNetServer.HandleMessage` 仅处理
`HostGame`（tag 0），没有 `HostModdedGame`（tag 25）。
`LocalHostModRegistrationPatch` 在 `InnerNetClient.HostGame` 的同步调用期间暂时清空
自身拥有的 GUID，使本地建房使用原版 tag 0；Finalizer 在成功和异常路径均恢复。
仅 `LocalGame` / `FreePlay` 生效，在线建房继续使用注册 GUID。
暂停期间卸载会撤销恢复拥有权，其他模组替换字段时不会被覆盖。

源码调用链为 `InnerNetClient.CoConnect(MatchMakerModes, string)` 的
`HostAndClient` 分支 → 同步 `HostGame`，不跨协程等待暂停身份。
目标源码没有 `CoCreateGame`。缓存的同日期
[Steam x86 方法数据](https://allofus.dev/il2cpp/2026.8.18/method_analysis_steam_x86.json)
将 `HostGame(IGameOptions, GameFilterOptions)` 标为 `matched`，Mono/Xref 均为 1，
唯一调用者是 `_CoConnect_d__71.MoveNext`；没有 inline 标记。
这仅用于选择 hook，Steam 标签不能证明 Itch 平台的 detour 运行结果。

离线检查：

```powershell
dotnet run --project tests/ModRegistration/ModRegistration.csproj --configuration Release
dotnet build tests/ModRegistration/NativeCompile/NativeCompile.csproj --configuration Release
```

前者链接生产模块，验证独立 Python UUIDv5 固定向量、GUID 字节顺序、重复注册、
冲突保护、拥有权清理，以及本地建房的成功、异常、嵌套、外部替换和卸载路径。
后者仅使用已安装的原生 interop 和 BepInEx 程序集编译，
验证真实字段与生命周期 API；可通过 `-p:LocalAmongUsPath=...` 指定安装目录。
两者均不启动游戏，也不证明 IL2CPP 运行时、官方服务端入房或匹配联机结果。

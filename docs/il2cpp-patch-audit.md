# IL2CPP patch inline 风险审计

生成时间：2026-10-05T05:11:13.335923+00:00。本报告根据当前源码自动生成；结构化记录见 [il2cpp-patch-audit.json](il2cpp-patch-audit.json)。

目标是 Among Us **2026.8.18 / Itch Windows x86**。本机只读 GameAssembly.dll PE header：`0x14c` / `x86`，未启动游戏。网站实际提供的同日期数据是 **2026.8.18 / steam-x86 / 游戏版本 18.0.0**；该版本仅列出 `steam-x86`、`android-arm64`，没有 Itch。版本匹配：`true`；平台匹配：`false`。**Steam 标签只能用于筛选风险，不能据此断言 Itch 的方法被内联、被剥离或不可 patch。**

## 数据来源与判定边界

网站 [分析器页面](https://allofus.dev/il2cpp/) 的 Select Version 从 [available_versions.json](https://allofus.dev/il2cpp/available_versions.json) 加载；路径由 [实际 JS 资产](https://allofus.dev/static/js/il2cpp.js?v=9) 的 `setupVersionSelector/loadFileFromUrl` 确认。

本次完整 JSON：[同日期 Steam x86 数据](https://allofus.dev/il2cpp/2026.8.18/method_analysis_steam_x86.json)，共 15982 个方法，SHA-256 `89b87a1cb2fc1f0f64f60f6e1902db8775befdd6b66ffb2c07c0a9a1ec74ce67`。缓存获取时间：2026-10-05T03:12:39.389633+00:00；本次模式：`offline-cache`。公开数据缓存位于 ignored `artifacts/il2cpp-audit/`。本审计没有启动游戏、安装 detour、连接游戏服务器或发游戏包。

页面 Help 明确：动态调用（抽象/接口/虚方法、Unity 消息等）不会贡献普通 Xref；Potentially Inlined 可能由于动态调用产生误判。因此 `XrefCount=0`、`MonoCount>XrefCount` 或单独的 `inlined` 标签都不等价于“不可 patch”。`used-by-inline` 与 `inlined` 分开：前者通常仍可 patch。`matched` 也不保证所有调用点都会经过已安装 hook。

- `stripped`：网站未在它的 IDA dump 找到方法；当前目标平台须另外确认 native pointer、入口与安装结果。不可仅按 Steam 标签删除 Itch patch。
- `empty-body`：当前反编译源码只有空方法体；独立于网站标签。可能没有有效 detour 入口，或被合并到共享空函数，必须避免盲目 hook。
- `shared native address`：需要目标平台真实方法指针/二进制地址证据。本次静态审计没有测量地址，也没有将空 C# 方法等同于已证共享 native 地址。
- `Potentially Inlined`：某些调用点可能绕过方法入口；需要沿具体 native 调用链验证或把逻辑移至已确认的 caller。

## 覆盖范围

扫描得到 290 个未注释的 `HarmonyPatch` 属性，折叠 type-only 容器和 overload 选择器后为 268 个目标声明、229 个唯一目标。267 个声明按类型、方法名及参数签名匹配网站；1 个没有网站记录；未解析动态目标 0 个。当前没有 `TargetMethod/TargetMethods` 动态选择器。

scanner 处理类/方法级属性、字符串方法名、显式 `Type[]`/单独 `typeof` 参数、继承声明、`MethodType.Getter/Setter`、`ref/out`、C# 与 IDA 基础类型别名，并排除行/块注释。未指定 overload 时采用实际 source signature；发现多个 overload 会标记 ambiguity 而不猜测。条件编译未求值；这份源码清单不能替代 Harmony 在目标运行时的最终 `MethodBase` 解析。所有源文件 SHA-256、完整目标签名、源码位置和网站调用列表保留在 JSON。

协程目标均记录 `coroutine_factory=true`：Harmony 指向的是返回 IEnumerator 的 factory，不是生成状态机 `MoveNext`。例如 `HandleGameDataInner`、`WrapUpAndSpawn`、`ShowRole`，需要区分 factory 入口与协程每次恢复的业务位置；不会把网站某个 `_..._d__::MoveNext` 自动替换成项目 target。

## 本轮空方法事故与优先判断

`RoleBehaviour.AdjustTasks(PlayerControl)` 当前不再出现在 patch 清单。对应只读目标源码 `RoleBehaviour.cs:420` 的 `public virtual void AdjustTasks` 方法完全为空；全目录只找到这一处定义，没有 override。网站同版本 Steam 数据为 `stripped`、Mono/Xref `1/0`。集成工作流本轮已报告该新 hook 引起 DMD 启动 NRE，并删除后进入菜单；本审计未重启游戏复现。这个案例支持避免对空基类方法盲目 hook，不能推广为“所有 Xref 0 或所有 virtual 方法都要删除”。现有目标源码未检出完全空的方法体；auto-property getter 是返回字段的 accessor，不算空方法。

原 5 个 Steam `stripped` target 中 5 个已从当前 patch 清单移除/迁移；下表记录当前状态与替代入口，已迁移项不再列为仍未修复的问题。网站标签仍是不同平台的比较证据。

| 原入口 | 当前替代/控制点 | 替代目标的比较数据 | 覆核与验证边界 |
|---|---|---|---|
| `AprilFoolsMode.ShouldFlipSkeld`；已移除/迁移 | HnS map 3 的 CoStartGameHost postfix 预加载，然后继续原 native IEnumerator | `AmongUsClient::CoStartGameHost(void)` matched 2/2 | 保留 HnS readiness、SelectRoles、Begin；尚未 HnS 实机覆盖 |
| `StringOption.Start`；已移除/迁移 | StringOption.Initialize postfix 恢复显示值、oldValue、翻译文本与按钮 | `StringOption::Initialize(void)` matched 0/0 | 避开 forwarding Start；排除自定义 OptionList，不触发配置 write/callback |
| `GameOptionsMenu.OnDisable`；已移除/迁移 | owning ChangeTab 调用 HideModMenu/CancelBuild，Close 与 OnDisable 清理所有 owned tab | `GameSettingMenu::ChangeTab(int,bool)` matched 7/7; `GameSettingMenu::Close(void)` inlined 4/3; `GameSettingMenu::OnDisable(void)` matched 0/0 | 不再 hook forwarding OnDisable；Close 仍有 inline 比较标签，matched owning OnDisable 复用同一 cleanup |
| `LobbyBehaviour.Update`；已移除/迁移 | 现有 GameStartManager.Update 调用 guarded UpdateMusic | `GameStartManager::Update(void)` matched 0/0 | 检查活跃大厅、声音列表；避免逐帧重复开始/停止 |
| `MovingPlatformBehaviour.get_IsDirty`；已移除/迁移 | 删除 accessor hook；Start/SetSide MarkClean，Use/SetTarget 阻止 disabled 状态移动 | `MovingPlatformBehaviour::Start(void)` matched 0/0; `MovingPlatformBehaviour::SetTarget(unsignedint,bool)` matched 2/2; `MovingPlatformBehaviour::SetSide(bool)` matched 3/3; `MovingPlatformBehaviour::Use(PlayerControl)` matched 2/2 | SetTarget 覆盖 native Deserialize(initialState) 调用链；MarkClean 是 matched 0/0；尚未 Airship 实机覆盖 |

Dleks 的 CoStartGameHost postfix 仅作用于房主、mod host、HnS、非 FreePlay、map 3：先加载 ShipPrefabs[3]，然后逐次 MoveNext 原 native IEnumerator；normal-mode 的现有 prefix 对 HnS 放行，因此没有用自写 readiness/SelectRoles/Begin 替代 vanilla HnS 协程。AllMapIcons 改用 new MapIconByName，并检查重复图标；没有再 Instantiate 整个 GameStartManager，避免复制 Start、控件和网络处理器。HnS/Dleks 载入和 Airship 平台禁用仍未完成实机覆盖。

GameOptionsMenu 的 HideModMenu 在 owning ChangeTab 路径先 CancelBuild，再隐藏 tab；GameSettingMenu.Close 逐个 CancelBuild 并 ReleaseMenus，OnDisable 的 matched 入口复用同一清理。StringOption.Initialize 补回显示值、oldValue、翻译标签与按钮状态，且排除自定义选项，没有触发 UpdateValue 的配置写入。大厅音乐从现有 GameStartManager.Update 调用 UpdateMusic，检查活跃大厅并避免重复开始/停止音效。

| 优先级 | 目标/入口 | 证据与触发 | 实际影响与修复方向 |
|---|---|---|---|
| 中，已完成迁移、待实机 | HnS Dleks preload / Airship 平台 state guard | 新 CoStartGameHost、SetTarget/SetSide/Use 都是 matched；分别预加载所选船体、阻止被禁用平台启动移动并 MarkClean | HnS map 3 readiness/选角/Begin，以及 Airship 初始化与中途移动路径仍须实测，不以静态迁移称为游戏功能通过 |
| 中，需调用链实证 | `GameOptionsMapPicker.Initialize(int)`、`ToggleOption.UpdateValue()` | Steam Potentially Inlined 3/0、1/0 | GUI map 初始化和值更新可能有调用绕过。验证真实 UI 开关与新建/切页路径；必要时放入确认可达的 picker/menu caller，不以测试桩直接调用证明 |
| 已获本轮实机证据 | Phantom 远程 UseAbility → native CheckVanish → host skill → StartAppear | 本轮双 Itch LocalGame 实机已报告两人传送、保持可见并按冷却生效；Steam CheckVanish 缺失的 CmdCheckVanish Xref 已由 prefix 覆盖 | 保留现有 Cmd/native role-RPC 接收边界；没有基于网站 inline 标签修改 native 接收。此证据不扩大为 HnS、Airship 或所有 Phantom 分支通过 |
| 中，协议路径验证 | `InnerNetClient.HandleGameDataInner`、`Constants.GetBroadcastVersion`、`PlayerControl.RpcMurderPlayer` | Steam Potentially Inlined；分别为 coroutine factory、版本 getter、native sender | 若业务依赖某一入口必须检查真实 caller；自定义接收以 PlayerControl.HandleRpc 为边界，不能用 factory hook 证明每次 coroutine resume 均受拦截 |

只读残留检查：原 static 缓存仅由 Start 更新的先后序风险已修复：guard 现在每次读取当前 option，初始 Deserialize 先于 Start 或跨局 option 变化也不会沿用旧缓存。仍需 Airship 实机验证实际移动/禁用行为。

## 新 Judge、Phantom、自定义 RPC 与注册

当前 Judge 的 MeetingHud.Start/OnDestroy、HudManager.Update、PlayerVoteArea.JudgeOverruleVote、MeetingHud.CmdQueueOverruleVotes、JudgeRole.TryOverrule、JudgeRole.IsBlockedByTasks、ImpostorRole.Deinitialize 均按完整签名匹配 `matched`。其中 TryOverrule 的实际参数是 `InnerNet.PlayerId`，CmdQueueOverruleVotes 为两个 PlayerId 加 UInt16。新增 AdjustTasks hook 已移除，其他 Judge hooks 没有网站 inline/stripped 警告；仍须验证实际 UI 和 RPC 路径。

`PlayerControl.HandleRpc(byte, Hazel.MessageReader)` 与 `PlayerPhysics.HandleRpc(byte, Hazel.MessageReader)` 都是 `matched 0/0`，与动态分派吻合。`ShouldProcessRpc(RpcCalls, byte)` 为 matched 1/1；`InnerNetClient.StartRpcImmediately(uint, byte, SendOption, int)` 为 used-by-inline 27/41，而不是 Potentially Inlined。outer 123 接收仍应由 HandleRpc 入口解包；0 Xref 不构成删除理由。

`PhantomRole.UseAbility`、`PlayerControl.CmdCheckVanish`、`CmdCheckAppear`、`HandleServerAppear` 为 matched；`CheckVanish/CheckAppear` 的 caller 差异已有 Cmd prefix 覆盖。本轮集成工作流已报告双 Itch 本地联网的远程 UseAbility → native CheckVanish → host skill → StartAppear 路径通过，两人传送、保持可见和冷却均符合预期；本审计没有重启游戏复测。`SetRoleInvisibility` 为 used-by-inline 5/7。

`CustomRpcTransport.Register()` 是项目自有方法，由 main.cs 在 Harmony.PatchAll 前调用 `ClassInjector.RegisterTypeInIl2Cpp<TOHERpcMessage>()`；它不是预编译游戏中的 Harmony target。注入类的 SerializeValues 回调与基类虚接口属于 ClassInjector/IL2CPP vtable 集成验证，网站无法给这个新类型提供 inline 标签。没有为获得网站标签而给空基类/abstract placeholder 增加 hook。

## Stripped 比较项（与空方法分开）

| 项目位置 | 实际签名 | Mono/Xref | 目标源码 |
|---|---|---|---|
| 当前无 active target | 原 5 项均已移除/迁移，见替代入口表 | — | 与 empty/shared 判定保持分开 |

## Potentially Inlined 比较项

以下保留重复 patch 声明，避免漏掉同一 native 入口上的多个模块。

| 项目位置 | 实际签名 | Mono/Xref | 协程 factory |
|---|---|---|---|
| Patches/AprilFoolsModePatch.cs:6 | `AprilFoolsMode::ShouldShowAprilFoolsToggle(void)` | 9/8 | False |
| Patches/CheckGameEndPatch.cs:15 | `GameManager::CheckEndGameViaTasks(void)` | 2/1 | False |
| Patches/ExilePatch.cs:35 | `AirshipExileController::WrapUpAndSpawn(void)` | 1/0 | True |
| Patches/ExilePatch.cs:206 | `PbExileController::PlayerSpin(void)` | 1/0 | True |
| Patches/GameOptionsMenuPatch.cs:569 | `ToggleOption::UpdateValue(void)` | 1/0 | False |
| Patches/GameOptionsPatch.cs:3 | `RoleOptionSetting::UpdateValuesAndText(AmongUs::GameOptions::IRoleOptionsCollection)` | 3/2 | False |
| Patches/GameSettingMenuPatch.cs:467 | `GameSettingMenu::Close(void)` | 4/3 | False |
| Patches/GameStartManagerPatch.cs:280 | `TextBoxTMP::SetText(System::String,System::String)` | 9/8 | False |
| Patches/HashRandomPatch.cs:17 | `HashRandom::Next(int)` | 3/2 | False |
| Patches/HauntMenuMinigamePatch.cs:3 | `HauntMenuMinigame::SetFilterText(void)` | 1/0 | False |
| Patches/InnerNetClientPatch.cs:21 | `InnerNet::InnerNetClient::HandleGameDataInner(Hazel::MessageReader,int)` | 1/0 | True |
| Patches/IntroPatch.cs:78 | `IntroCutscene::ShowRole(void)` | 1/0 | True |
| Patches/MapPickerMenuPatch.cs:13 | `GameOptionsMapPicker::Initialize(int)` | 3/0 | False |
| Patches/PhantomRolePatch.cs:53 | `PlayerControl::CheckVanish(void)` | 2/1 | False |
| Patches/PhantomRolePatch.cs:106 | `PlayerControl::CheckAppear(bool)` | 2/1 | False |
| Patches/PlayerControlPatch.cs:516 | `PlayerControl::RpcMurderPlayer(PlayerControl,bool)` | 4/2 | False |
| Patches/PlayerControlPatch.cs:1715 | `PlayerControl::CmdCheckName(System::String)` | 1/0 | False |
| Patches/ServerVersionPatch.cs:3 | `Constants::GetBroadcastVersion(void)` | 7/3 | False |
| Patches/ShipStatusPatch.cs:74 | `ShipStatus::UpdateSystem(SystemTypes,PlayerControl,unsignedchar)` | 10/5 | False |
| Patches/ShipStatusPatch.cs:271 | `ShipStatus::SpawnPlayer(PlayerControl,int,bool)` | 4/1 | False |
| Patches/ShowHostMeetingPatch.cs:31 | `IntroCutscene::ShowRole(void)` | 1/0 | True |
| Patches/TextBoxPatch.cs:7 | `TextBoxTMP::SetText(System::String,System::String)` | 9/8 | False |
| Patches/TOHEOnlySearch.cs:18 | `FilterTagsMenu::ChooseOption(ChatLanguageButton,System::String)` | 1/0 | False |

## 完整现有 patch 清单

`no-site-warning` 表示比较数据没有 inline/stripped 标签；不等于已通过 Itch 实机验证。`ActivityManager.UpdateActivity` 来自项目 `using Discord`，未在此游戏方法 JSON 中找到；未据此判 stripped，也未确认它在当前安装中属于托管还是 IL2CPP 包装器。

| 项目位置 | 声明类 | 解析目标签名 | 网站标签 | 匹配 |
|---|---|---|---|---|
| GameModes/FFAManager.cs:413 | FixedUpdateInGameModeFFAPatch | `PlayerControl::FixedUpdate(void)` | matched | exact-signature |
| Modules/BanManager.cs:249 | BanMenuSelectPatch | `BanMenu::Select(int)` | used-by-inline | exact-signature |
| Modules/DisableDevice.cs:143 | RemoveDisableDevicesPatch | `ShipStatus::Start(void)` | matched | exact-signature |
| Modules/GuessManager.cs:593 | StartMeetingPatch | `MeetingHud::Start(void)` | matched | exact-signature |
| Modules/GuessManager.cs:1071 | MeetingHudOnDestroyGuesserUIClose | `MeetingHud::OnDestroy(void)` | matched | exact-signature |
| Modules/ModUpdater.cs:31 | ModUpdater | `MainMenuManager::Start(void)` | matched | exact-signature |
| Modules/OptionHolder.cs:24 | Options | `TranslationController::Initialize(void)` | matched | exact-signature |
| Modules/RPC.cs:128 | ShouldProcessRpcPatch | `PlayerControl::ShouldProcessRpc(RpcCalls,unsignedchar)` | matched | exact-signature |
| Modules/RPC.cs:144 | RPCHandlerPatch | `PlayerControl::HandleRpc(unsignedchar,Hazel::MessageReader)` | matched | exact-signature |
| Modules/RPC.cs:774 | PlayerPhysicsRPCHandlerPatch | `PlayerPhysics::HandleRpc(unsignedchar,Hazel::MessageReader)` | matched | exact-signature |
| Modules/RPC.cs:1154 | StartRpcImmediatelyPatch | `InnerNet::InnerNetClient::StartRpcImmediately(unsignedint,unsignedchar,Hazel::SendOption,int)` | used-by-inline | exact-signature |
| Modules/Zoom.cs:8 | Zoom | `HudManager::Update(void)` | matched | exact-signature |
| Patches/AirShipElectricalDoors.cs:38 | ElectricalDoorsInitializePatch | `ElectricalDoors::Initialize(void)` | matched | exact-signature |
| Patches/AirshipStatus.cs:4 | AirshipStatusPrespawnStepPatch | `AirshipStatus::PrespawnStep(void)` | matched | exact-signature |
| Patches/AnnouncementPatch.cs:114 | ModNews | `AmongUs::Data::Player::PlayerAnnouncementData::SetAnnouncements(Assets::InnerNet::Announcement[])` | matched | exact-signature |
| Patches/AnnouncementPatch.cs:140 | ModNews | `AnnouncementPanel::SetUp(Assets::InnerNet::Announcement)` | matched | exact-signature |
| Patches/AprilFoolsModePatch.cs:6 | ShouldShowTogglePatch | `AprilFoolsMode::ShouldShowAprilFoolsToggle(void)` | inlined | exact-signature |
| Patches/AprilFoolsModePatch.cs:15 | GetNormalBodyType_Patch | `NormalGameManager::GetBodyType(PlayerControl)` | matched | exact-signature |
| Patches/AprilFoolsModePatch.cs:34 | GetHnsBodyType_Patch | `HideAndSeekManager::GetBodyType(PlayerControl)` | matched | exact-signature |
| Patches/AprilFoolsModePatch.cs:92 | LongBoiPatches | `LongBoiPlayerBody::Awake(void)` | matched | exact-signature |
| Patches/AprilFoolsModePatch.cs:104 | LongBoiPatches | `LongBoiPlayerBody::Start(void)` | matched | exact-signature |
| Patches/AprilFoolsModePatch.cs:130 | LongBoiPatches | `LongBoiPlayerBody::SetHeighFromDistanceHnS(float)` | matched | exact-signature |
| Patches/AprilFoolsModePatch.cs:141 | LongBoiPatches | `HatManager::CheckLongModeValidCosmetic(System::String,bool)` | matched | exact-signature |
| Patches/ChatBubblePatch.cs:7 | ChatBubbleSetRightPatch | `ChatBubble::SetRight(void)` | matched | exact-signature |
| Patches/ChatBubblePatch.cs:15 | ChatBubbleSetNamePatch | `ChatBubble::SetName(System::String,bool,bool,UnityEngine::Color)` | matched | exact-signature |
| Patches/ChatCommandPatch.cs:20 | ChatCommands | `ChatController::SendChat(void)` | matched | exact-signature |
| Patches/ChatCommandPatch.cs:3300 | ChatUpdatePatch | `ChatController::Update(void)` | matched | exact-signature |
| Patches/ChatCommandPatch.cs:3383 | UpdateCharCountPatch | `FreeChatInputField::UpdateCharCount(void)` | matched | exact-signature |
| Patches/ChatCommandPatch.cs:3398 | RpcSendChatPatch | `PlayerControl::RpcSendChat(System::String)` | matched | exact-signature |
| Patches/ChatControlPatch.cs:6 | ChatControllerUpdatePatch | `ChatController::Update(void)` | matched | exact-signature |
| Patches/CheckGameEndPatch.cs:15 | CheckEndGameViaTasksForNormalPatch | `GameManager::CheckEndGameViaTasks(void)` | inlined | exact-signature |
| Patches/CheckGameEndPatch.cs:24 | CheckTaskCompletionPatch | `GameManager::CheckTaskCompletion(void)` | matched | exact-signature |
| Patches/CheckGameEndPatch.cs:37 | GameEndCheckerForNormal | `LogicGameFlowNormal::CheckEndCriteria(void)` | matched | exact-signature |
| Patches/ClientOptionsPatch.cs:6 | OptionsMenuBehaviourStartPatch | `OptionsMenuBehaviour::Start(void)` | matched | exact-signature |
| Patches/ClientOptionsPatch.cs:149 | OptionsMenuBehaviourClosePatch | `OptionsMenuBehaviour::Close(void)` | used-by-inline | exact-signature |
| Patches/ClientPatch.cs:8 | MakePublicPatch | `GameStartManager::MakePublic(void)` | matched | exact-signature |
| Patches/ClientPatch.cs:34 | MMOnlineManagerStartPatch | `MMOnlineManager::Start(void)` | matched | exact-signature |
| Patches/ClientPatch.cs:66 | SplashLogoAnimatorPatch | `SplashManager::Update(void)` | matched | exact-signature |
| Patches/ClientPatch.cs:78 | BanMenuSetVisiblePatch | `BanMenu::SetVisible(bool)` | matched | exact-signature |
| Patches/ClientPatch.cs:91 | InnerNetClientCanBanPatch | `InnerNet::InnerNetClient::CanBan(void)` | matched | exact-signature |
| Patches/ClientPatch.cs:100 | KickPlayerPatch | `InnerNet::InnerNetClient::KickPlayer(int,bool)` | matched | exact-signature |
| Patches/ClientPatch.cs:141 | InnerNetObjectSerializePatch | `InnerNet::InnerNetClient::SendAllStreamedObjects(void)` | matched | exact-signature |
| Patches/ControlPatch.cs:10 | ControllerManagerUpdatePatch | `ControllerManager::Update(void)` | matched | exact-signature |
| Patches/ControlPatch.cs:425 | ConsoleJoystickHandleHUDPatch | `ConsoleJoystick::HandleHUD(void)` | matched | exact-signature |
| Patches/ControlPatch.cs:433 | KeyboardJoystickHandleHUDPatch | `KeyboardJoystick::HandleHud(void)` | matched | exact-signature |
| Patches/CredentialsPatch.cs:9 | PingTrackerUpdatePatch | `PingTracker::Update(void)` | matched | exact-signature |
| Patches/CredentialsPatch.cs:276 | VersionShowerStartPatch | `VersionShower::Start(void)` | matched | exact-signature |
| Patches/CredentialsPatch.cs:352 | ModManagerLateUpdatePatch | `ModManager::LateUpdate(void)` | matched | exact-signature |
| Patches/CustomRpcLifecyclePatch.cs:3 | CustomRpcJoinPatch | `AmongUsClient::OnGameJoined(System::String)` | matched | exact-signature |
| Patches/CustomRpcLifecyclePatch.cs:9 | CustomRpcDisconnectPatch | `AmongUsClient::OnDisconnected(void)` | matched | exact-signature |
| Patches/DeconSystemPatch.cs:3 | DeconSystemUpdateSystemPatch | `DeconSystem::UpdateSystem(PlayerControl,Hazel::MessageReader)` | matched | exact-signature |
| Patches/DialogueBoxPatch.cs:6 | DialogueBoxPatch | `DialogueBox::Show(System::String)` | matched | exact-signature |
| Patches/DialogueBoxPatch.cs:17 | DialogueBoxPatch | `DialogueBox::Show(System::String)` | matched | exact-signature |
| Patches/DialogueBoxPatch.cs:26 | DialogueBoxPatch | `DialogueBox::Hide(void)` | matched | exact-signature |
| Patches/DisconnectPatch.cs:3 | OnDisconnectedPatch | `AmongUsClient::OnDisconnected(void)` | matched | exact-signature |
| Patches/DiscordPatch.cs:9 | DiscordRPC | `ActivityManager::UpdateActivity` | 未覆盖 | not-in-dataset |
| Patches/DleksPatch.cs:8 | DleksPatch | `AmongUsClient::CoStartGameHost(void)` | matched | exact-signature |
| Patches/DleksPatch.cs:40 | AllMapIconsPatch | `GameStartManager::Start(void)` | matched | exact-signature |
| Patches/DleksPatch.cs:75 | AutoSelectDleksPatch | `StringOption::Initialize(void)` | matched | exact-signature |
| Patches/DleksPatch.cs:95 | VentSetButtonsPatch | `Vent::SetButtons(bool)` | used-by-inline | exact-signature |
| Patches/DleksPatch.cs:143 | VentTryMoveToVentPatch | `Vent::TryMoveToVent(Vent,System::String&)` | matched | exact-signature |
| Patches/DleksPatch.cs:156 | VentUpdateArrowsPatch | `Vent::UpdateArrows(VentilationSystem)` | matched | exact-signature |
| Patches/EndGameManagerPatch.cs:7 | EndGameManagerPatch | `EndGameManager::ShowButtons(void)` | matched | exact-signature |
| Patches/ExilePatch.cs:11 | BaseExileControllerPatch | `ExileController::WrapUp(void)` | matched | exact-signature |
| Patches/ExilePatch.cs:35 | AirshipExileControllerPatch | `AirshipExileController::WrapUpAndSpawn(void)` | inlined | exact-signature |
| Patches/ExilePatch.cs:206 | PolusExileHatFixPatch | `PbExileController::PlayerSpin(void)` | inlined | exact-signature |
| Patches/FreeplayPatch.cs:163 | FreeplayTutorialStartPatch | `TutorialManager::Awake(void)` | matched | exact-signature |
| Patches/FreeplayPatch.cs:172 | FreeplayTutorialClosePatch | `TutorialManager::OnDestroy(void)` | matched | exact-signature |
| Patches/FreeplayPatch.cs:178 | FreeplayShipBeginPatch | `ShipStatus::Begin(void)` | matched | exact-signature |
| Patches/GameManagerPatch.cs:5 | GameManagerSerializeFix | `GameManager::Serialize(Hazel::MessageWriter,bool)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:132 | GameOptionsMenuPatch | `GameOptionsMenu::Initialize(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:149 | GameOptionsMenuPatch | `GameOptionsMenu::Initialize(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:181 | GameOptionsMenuPatch | `GameOptionsMenu::CreateSettings(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:440 | GameOptionsMenuPatch | `GameOptionsMenu::ValueChanged(OptionBehaviour)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:556 | ToggleOptionPatch | `ToggleOption::Initialize(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:569 | ToggleOptionPatch | `ToggleOption::UpdateValue(void)` | inlined | exact-signature |
| Patches/GameOptionsMenuPatch.cs:600 | NumberOptionPatch | `NumberOption::Initialize(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:642 | NumberOptionPatch | `NumberOption::UpdateValue(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:668 | NumberOptionPatch | `NumberOption::FixedUpdate(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:690 | NumberOptionPatch | `NumberOption::Increase(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:713 | NumberOptionPatch | `NumberOption::Decrease(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:740 | StringOptionPatch | `StringOption::Initialize(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:813 | StringOptionPatch | `StringOption::UpdateValue(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:845 | StringOptionPatch | `StringOption::FixedUpdate(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:874 | StringOptionPatch | `StringOption::Increase(void)` | matched | exact-signature |
| Patches/GameOptionsMenuPatch.cs:887 | StringOptionPatch | `StringOption::Decrease(void)` | matched | exact-signature |
| Patches/GameOptionsPatch.cs:3 | ChanceChangePatch | `RoleOptionSetting::UpdateValuesAndText(AmongUs::GameOptions::IRoleOptionsCollection)` | inlined | exact-signature |
| Patches/GameSettingMenuPatch.cs:25 | GameSettingMenuPatch | `GameSettingMenu::Start(void)` | matched | exact-signature |
| Patches/GameSettingMenuPatch.cs:325 | GameSettingMenuPatch | `GameSettingMenu::ChangeTab(int,bool)` | matched | exact-signature |
| Patches/GameSettingMenuPatch.cs:415 | GameSettingMenuPatch | `GameSettingMenu::OnEnable(void)` | matched | exact-signature |
| Patches/GameSettingMenuPatch.cs:467 | GameSettingMenuPatch | `GameSettingMenu::Close(void)` | inlined | exact-signature |
| Patches/GameSettingMenuPatch.cs:493 | GameSettingMenuPatch | `GameSettingMenu::OnDisable(void)` | matched | exact-signature |
| Patches/GameSettingMenuPatch.cs:496 | FixInputChatField | `FreeChatInputField::UpdateCharCount(void)` | matched | exact-signature |
| Patches/GameSettingMenuPatch.cs:511 | FixDarkThemeForSearchBar | `ChatController::Update(void)` | matched | exact-signature |
| Patches/GameSettingMenuPatch.cs:526 | RpcSyncSettingsPatch | `PlayerControl::RpcSyncSettings(System::Byte[])` | used-by-inline | exact-signature |
| Patches/GameStartManagerPatch.cs:13 | GameStartManagerMinPlayersPatch | `GameStartManager::Update(void)` | matched | exact-signature |
| Patches/GameStartManagerPatch.cs:28 | GameStartManagerStartPatch | `GameStartManager::Start(void)` | matched | exact-signature |
| Patches/GameStartManagerPatch.cs:108 | GameStartManagerUpdatePatch | `GameStartManager::Update(void)` | matched | exact-signature |
| Patches/GameStartManagerPatch.cs:280 | HiddenTextPatch | `TextBoxTMP::SetText(System::String,System::String)` | inlined | exact-signature |
| Patches/GameStartManagerPatch.cs:289 | GameStartManagerBeginGamePatch | `GameStartManager::BeginGame(void)` | matched | exact-signature |
| Patches/GameStartManagerPatch.cs:411 | ResetStartStatePatch | `GameStartManager::ResetStartState(void)` | matched | exact-signature |
| Patches/GameStartManagerPatch.cs:426 | UnrestrictedNumImpostorsPatch | `IGameOptionsExtensions::GetAdjustedNumImpostors(AmongUs::GameOptions::IGameOptions,int)` | matched | exact-signature |
| Patches/GuardAngelPatch.cs:4 | ProtectedRecentlyPatch | `MeetingIntroAnimation::Start(void)` | matched | exact-signature |
| Patches/HashRandomPatch.cs:8 | HashRandomPatch | `HashRandom::FastNext(int)` | matched | exact-signature |
| Patches/HashRandomPatch.cs:17 | HashRandomPatch | `HashRandom::Next(int)` | inlined | exact-signature |
| Patches/HashRandomPatch.cs:26 | HashRandomPatch | `HashRandom::Next(int,int)` | matched | exact-signature |
| Patches/HauntMenuMinigamePatch.cs:3 | HauntMenuMinigameSetFilterTextPatch | `HauntMenuMinigame::SetFilterText(void)` | inlined | exact-signature |
| Patches/HideBanButtonPatch.cs:3 | CancelBanMenuStuckPatch | `ChatController::Toggle(void)` | matched | exact-signature |
| Patches/HideNSeek/CheckGameEndPatchHnS.cs:3 | GameEndCheckerForHnS | `LogicGameFlowHnS::CheckEndCriteria(void)` | matched | exact-signature |
| Patches/HideNSeek/CheckGameEndPatchHnS.cs:15 | OnGameEndForHnS | `LogicGameFlowHnS::OnGameEnd(void)` | matched | exact-signature |
| Patches/HideNSeek/PlayerControlPatchHnS.cs:8 | CheckMurderInHidenSeekPatch | `PlayerControl::CheckMurder(PlayerControl)` | matched | exact-signature |
| Patches/HideNSeek/PlayerControlPatchHnS.cs:56 | MurderPlayerInHidenSeekPatch | `PlayerControl::MurderPlayer(PlayerControl,MurderResultFlags)` | matched | exact-signature |
| Patches/HideNSeek/PlayerControlPatchHnS.cs:95 | FixedUpdateInHidenSeekPatch | `PlayerControl::FixedUpdate(void)` | matched | exact-signature |
| Patches/HudPatch.cs:11 | HudManagerPatch | `HudManager::Update(void)` | matched | exact-signature |
| Patches/HudPatch.cs:176 | ToggleHighlightPatch | `PlayerControl::ToggleHighlight(bool,RoleTeamTypes)` | used-by-inline | exact-signature |
| Patches/HudPatch.cs:192 | SetVentOutlinePatch | `Vent::SetOutline(bool,bool)` | matched | exact-signature |
| Patches/HudPatch.cs:205 | SetHudActivePatch | `HudManager::SetHudActive(PlayerControl,RoleBehaviour,bool)` | matched | exact-signature |
| Patches/HudPatch.cs:234 | VentButtonDoClickPatch | `VentButton::DoClick(void)` | matched | exact-signature |
| Patches/HudPatch.cs:249 | MapBehaviourShowPatch | `MapBehaviour::Show(MapOptions)` | matched | exact-signature |
| Patches/HudPatch.cs:267 | TaskPanelBehaviourPatch | `TaskPanelBehaviour::SetTaskText(System::String)` | matched | exact-signature |
| Patches/HudSpritePatch.cs:12 | HudSpritePatch | `HudManager::Update(void)` | matched | exact-signature |
| Patches/InnerNetClientPatch.cs:21 | GameDataHandlerPatch | `InnerNet::InnerNetClient::HandleGameDataInner(Hazel::MessageReader,int)` | inlined | exact-signature |
| Patches/InnerNetClientPatch.cs:157 | StartGameHostPatch | `AmongUsClient::CoStartGameHost(void)` | matched | exact-signature |
| Patches/InnerNetClientPatch.cs:176 | AuthTimeoutPatch | `AuthManager::CoConnect(System::String,unsignedshort,System::String)` | matched | exact-signature |
| Patches/InnerNetClientPatch.cs:177 | AuthTimeoutPatch | `AuthManager::CoWaitForNonce(float)` | matched | exact-signature |
| Patches/IntroPatch.cs:17 | CoShowIntroPatch | `HudManager::CoShowIntro(void)` | matched | exact-signature |
| Patches/IntroPatch.cs:67 | CoBeginPatch | `IntroCutscene::CoBegin(void)` | matched | exact-signature |
| Patches/IntroPatch.cs:78 | SetUpRoleTextPatch | `IntroCutscene::ShowRole(void)` | inlined | exact-signature |
| Patches/IntroPatch.cs:303 | BeginCrewmatePatch | `IntroCutscene::BeginCrewmate(System::Collections::Generic::List<PlayerControl>)` | matched | exact-signature |
| Patches/IntroPatch.cs:528 | BeginImpostorPatch | `IntroCutscene::BeginImpostor(System::Collections::Generic::List<PlayerControl>)` | matched | exact-signature |
| Patches/IntroPatch.cs:572 | IntroCutsceneDestroyPatch | `IntroCutscene::OnDestroy(void)` | matched | exact-signature |
| Patches/JoinGameButtonPatch.cs:6 | JoinGameButtonPatch | `JoinGameButton::OnClick(void)` | matched | exact-signature |
| Patches/LadderPatch.cs:63 | LadderPatch | `PlayerPhysics::ClimbLadder(Ladder,unsignedchar)` | matched | exact-signature |
| Patches/LobbyPatch.cs:7 | LobbyStartPatch | `LobbyBehaviour::Start(void)` | matched | exact-signature |
| Patches/LobbyPatch.cs:98 | HostInfoPanelUpdatePatch | `HostInfoPanel::SetUp(void)` | matched | exact-signature |
| Patches/MainMenuManagerPatch.cs:10 | MainMenuManagerStartPatch | `MainMenuManager::Start(void)` | matched | exact-signature |
| Patches/MainMenuManagerPatch.cs:56 | MainMenuManagerPatch | `MainMenuManager::Start(void)` | matched | exact-signature |
| Patches/MainMenuManagerPatch.cs:307 | MainMenuManagerPatch | `MainMenuManager::OpenGameModeMenu(void)` | matched | exact-signature |
| Patches/MainMenuManagerPatch.cs:308 | MainMenuManagerPatch | `MainMenuManager::OpenAccountMenu(void)` | matched | exact-signature |
| Patches/MainMenuManagerPatch.cs:309 | MainMenuManagerPatch | `MainMenuManager::OpenCredits(void)` | matched | exact-signature |
| Patches/MainMenuManagerPatch.cs:315 | MainMenuManagerPatch | `MainMenuManager::ResetScreen(void)` | matched | exact-signature |
| Patches/MapPickerMenuPatch.cs:13 | GameOptionsMapPickerPatch | `GameOptionsMapPicker::Initialize(int)` | inlined | exact-signature |
| Patches/MapPickerMenuPatch.cs:86 | GameOptionsMapPickerPatch | `GameOptionsMapPicker::FixedUpdate(void)` | matched | exact-signature |
| Patches/MapPickerMenuPatch.cs:111 | MenuMapPickerPatch | `CreateOptionsPicker::Awake(void)` | matched | exact-signature |
| Patches/MeetingAbilityPatch.cs:6 | MeetingAbilityStartPatch | `MeetingHud::Start(void)` | matched | exact-signature |
| Patches/MeetingAbilityPatch.cs:13 | MeetingAbilityEndPatch | `MeetingHud::OnDestroy(void)` | matched | exact-signature |
| Patches/MeetingAbilityPatch.cs:20 | MeetingAbilityTickPatch | `HudManager::Update(void)` | matched | exact-signature |
| Patches/MeetingAbilityPatch.cs:28 | MeetingAbilityClickPatch | `PlayerVoteArea::JudgeOverruleVote(void)` | matched | exact-signature |
| Patches/MeetingAbilityPatch.cs:41 | MeetingAbilityNativeQueuePatch | `MeetingHud::CmdQueueOverruleVotes(InnerNet::PlayerId,InnerNet::PlayerId,unsignedshort)` | matched | exact-signature |
| Patches/MeetingAbilityPatch.cs:48 | MeetingAbilityNativeTryPatch | `JudgeRole::TryOverrule(InnerNet::PlayerId)` | matched | exact-signature |
| Patches/MeetingAbilityPatch.cs:62 | MeetingAbilityTaskGatePatch | `JudgeRole::IsBlockedByTasks(void)` | matched | exact-signature |
| Patches/MeetingAbilityPatch.cs:76 | MeetingAbilityPreserveHeaderPatch | `ImpostorRole::Deinitialize(PlayerControl)` | matched | exact-signature |
| Patches/MeetingHudPatch.cs:18 | CheckForEndVotingPatch | `MeetingHud::CheckForEndVoting(void)` | used-by-inline | exact-signature |
| Patches/MeetingHudPatch.cs:657 | CastVotePatch | `MeetingHud::CastVote(InnerNet::PlayerId,InnerNet::PlayerId)` | used-by-inline | exact-signature |
| Patches/MeetingHudPatch.cs:829 | MeetingHudStartPatch | `MeetingHud::Start(void)` | matched | exact-signature |
| Patches/MeetingHudPatch.cs:1215 | MeetingHudUpdatePatch | `MeetingHud::Update(void)` | matched | exact-signature |
| Patches/MeetingHudPatch.cs:1277 | SetHighlightedPatch | `PlayerVoteArea::SetHighlighted(bool)` | matched | exact-signature |
| Patches/MeetingHudPatch.cs:1288 | MeetingHudOnDestroyPatch | `MeetingHud::OnDestroy(void)` | matched | exact-signature |
| Patches/ModRegistrationPatch.cs:8 | LocalHostModRegistrationPatch | `InnerNet::InnerNetClient::HostGame(AmongUs::GameOptions::IGameOptions,InnerNet::GameFilterOptions)` | matched | exact-signature |
| Patches/MovingPlatformBehaviourPatch.cs:12 | MovingPlatformBehaviourPatch | `MovingPlatformBehaviour::Start(void)` | matched | exact-signature |
| Patches/MovingPlatformBehaviourPatch.cs:24 | MovingPlatformBehaviourPatch | `MovingPlatformBehaviour::SetTarget(unsignedint,bool)` | matched | exact-signature |
| Patches/MovingPlatformBehaviourPatch.cs:26 | MovingPlatformBehaviourPatch | `MovingPlatformBehaviour::SetSide(bool)` | matched | exact-signature |
| Patches/MovingPlatformBehaviourPatch.cs:32 | MovingPlatformBehaviourPatch | `MovingPlatformBehaviour::Use(PlayerControl)` | matched | exact-signature |
| Patches/NoBlackoutPatch.cs:3 | DontBlackoutPatch | `LogicGameFlowNormal::IsGameOverDueToDeath(void)` | matched | exact-signature |
| Patches/NotificationPopperPatch.cs:6 | NotificationPopperAwakePatch | `NotificationPopper::Awake(void)` | matched | exact-signature |
| Patches/OneWayShadowsPatch.cs:5 | OneWayShadowsIsIgnoredPatch | `OneWayShadows::IsIgnored(LightSource)` | matched | exact-signature |
| Patches/onGameStartedPatch.cs:17 | ChangeRoleSettings | `AmongUsClient::CoStartGame(void)` | matched | exact-signature |
| Patches/onGameStartedPatch.cs:259 | StartGameHostPatch | `AmongUsClient::CoStartGameHost(void)` | matched | exact-signature |
| Patches/onGameStartedPatch.cs:705 | SelectRolesPatch | `RoleManager::SelectRoles(void)` | matched | exact-signature |
| Patches/onGameStartedPatch.cs:737 | RpcSetRoleReplacer | `PlayerControl::RpcSetRole(AmongUs::GameOptions::RoleTypes,bool)` | matched | exact-signature |
| Patches/OutroPatch.cs:17 | EndGamePatch | `AmongUsClient::OnGameEnd(EndGameResult)` | matched | exact-signature |
| Patches/OutroPatch.cs:160 | SetEverythingUpPatch | `EndGameManager::SetEverythingUp(void)` | matched | exact-signature |
| Patches/PhantomRolePatch.cs:19 | PhantomRolePatch | `PlayerControl::CmdCheckVanish(float)` | matched | exact-signature |
| Patches/PhantomRolePatch.cs:39 | PhantomRolePatch | `PlayerControl::CmdCheckAppear(bool)` | matched | exact-signature |
| Patches/PhantomRolePatch.cs:53 | PhantomRolePatch | `PlayerControl::CheckVanish(void)` | inlined | exact-signature |
| Patches/PhantomRolePatch.cs:98 | PhantomRolePatch | `PlayerControl::HandleServerAppear(bool)` | matched | exact-signature |
| Patches/PhantomRolePatch.cs:106 | PhantomRolePatch | `PlayerControl::CheckAppear(bool)` | inlined | exact-signature |
| Patches/PhantomRolePatch.cs:149 | PhantomRolePatch | `PlayerControl::SetRoleInvisibility(bool,bool,bool)` | used-by-inline | exact-signature |
| Patches/PhantomRolePatch.cs:224 | PhantomRoleUseAbilityPatch | `PhantomRole::UseAbility(void)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:24 | CheckProtectPatch | `PlayerControl::CheckProtect(PlayerControl)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:67 | CheckMurderPatch | `PlayerControl::CheckMurder(PlayerControl)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:352 | MurderPlayerPatch | `PlayerControl::MurderPlayer(PlayerControl,MurderResultFlags)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:516 | RpcMurderPlayerPatch | `PlayerControl::RpcMurderPlayer(PlayerControl,bool)` | inlined | exact-signature |
| Patches/PlayerControlPatch.cs:545 | CheckShapeshiftPatch | `PlayerControl::CheckShapeshift(PlayerControl,bool)` | used-by-inline | exact-signature |
| Patches/PlayerControlPatch.cs:659 | ShapeshiftPatch | `PlayerControl::Shapeshift(PlayerControl,bool)` | used-by-inline | exact-signature |
| Patches/PlayerControlPatch.cs:708 | ReportDeadBodyPatch | `PlayerControl::ReportDeadBody(NetworkedPlayerInfo)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:935 | FixedUpdateInNormalGamePatch | `PlayerControl::FixedUpdate(void)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1457 | PlayerStartPatch | `PlayerControl::Start(void)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1474 | CoEnterVentPatch | `PlayerPhysics::CoEnterVent(int)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1518 | EnterVentPatch | `Vent::EnterVent(PlayerControl)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1540 | CoExitVentPatch | `PlayerPhysics::CoExitVent(int)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1561 | PlayerControlCompleteTaskPatch | `PlayerControl::CompleteTask(unsignedint)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1649 | PlayerControlCheckNamePatch | `PlayerControl::CheckName(System::String)` | used-by-inline | exact-signature |
| Patches/PlayerControlPatch.cs:1701 | RpcSetColorPatch | `PlayerControl::SetColor(int)` | used-by-inline | exact-signature |
| Patches/PlayerControlPatch.cs:1715 | CmdCheckNameVersionCheckPatch | `PlayerControl::CmdCheckName(System::String)` | inlined | exact-signature |
| Patches/PlayerControlPatch.cs:1723 | PlayerControlProtectPlayerPatch | `PlayerControl::ProtectPlayer(PlayerControl,int)` | used-by-inline | exact-signature |
| Patches/PlayerControlPatch.cs:1731 | PlayerControlRemoveProtectionPatch | `PlayerControl::RemoveProtection(void)` | used-by-inline | exact-signature |
| Patches/PlayerControlPatch.cs:1739 | PlayerControlMixupOutfitPatch | `PlayerControl::MixUpOutfit(NetworkedPlayerInfo::PlayerOutfit)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1758 | PlayerControlFixMixedUpOutfitPatch | `PlayerControl::FixMixedUpOutfit(void)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1772 | PlayerControlCheckSporeTriggerPatch | `PlayerControl::CheckSporeTrigger(Mushroom)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1785 | PlayerControlCheckUseZiplinePatch | `PlayerControl::CheckUseZipline(PlayerControl,ZiplineBehaviour,bool)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1799 | PlayerControlDiePatch | `PlayerControl::Die(DeathReason,bool)` | used-by-inline | exact-signature |
| Patches/PlayerControlPatch.cs:1811 | PlayerControlSetRolePatch | `PlayerControl::RpcSetRole(AmongUs::GameOptions::RoleTypes,bool)` | matched | exact-signature |
| Patches/PlayerControlPatch.cs:1918 | PlayerControlLocalSetRolePatch | `PlayerControl::CoSetRole(AmongUs::GameOptions::RoleTypes,bool)` | matched | exact-signature |
| Patches/PlayerJoinAndLeftPatch.cs:15 | OnGameJoinedPatch | `AmongUsClient::OnGameJoined(System::String)` | matched | exact-signature |
| Patches/PlayerJoinAndLeftPatch.cs:180 | DisconnectInternalPatch | `InnerNet::InnerNetClient::DisconnectInternal(DisconnectReasons,System::String)` | matched | exact-signature |
| Patches/PlayerJoinAndLeftPatch.cs:189 | OnPlayerJoinedPatch | `AmongUsClient::OnPlayerJoined(InnerNet::ClientData)` | matched | exact-signature |
| Patches/PlayerJoinAndLeftPatch.cs:333 | OnPlayerLeftPatch | `AmongUsClient::OnPlayerLeft(InnerNet::ClientData,DisconnectReasons)` | matched | exact-signature |
| Patches/PlayerJoinAndLeftPatch.cs:569 | InnerNetClientSpawnPatch | `InnerNet::InnerNetClient::Spawn(InnerNet::InnerNetObject,int,InnerNet::SpawnFlags)` | matched | exact-signature |
| Patches/RandomSpawnPatch.cs:12 | SnapToPatch | `CustomNetworkTransform::SnapTo(UnityEngine::Vector2,unsignedshort)` | used-by-inline | exact-signature |
| Patches/RandomSpawnPatch.cs:23 | CustomNetworkTransformHandleRpcPatch | `CustomNetworkTransform::HandleRpc(unsignedchar,Hazel::MessageReader)` | matched | exact-signature |
| Patches/RandomSpawnPatch.cs:99 | SpawnInMinigameSpawnAtPatch | `SpawnInMinigame::SpawnAt(SpawnInMinigame::SpawnLocation)` | matched | exact-signature |
| Patches/RecomputeTaskPatch.cs:3 | CustomTaskCountsPatch | `GameData::RecomputeTaskCounts(void)` | used-by-inline | exact-signature |
| Patches/RecomputeTaskPatch.cs:35 | InstalledTaskStatePatch | `NetworkedPlayerInfo::SetTasks(System::Byte[])` | matched | exact-signature |
| Patches/RecomputeTaskPatch.cs:69 | InitialInstalledTaskCountsPatch | `ShipStatus::Begin(void)` | matched | exact-signature |
| Patches/RegionMenuPatch.cs:8 | RegionMenuPatch | `RegionMenu::OnEnable(void)` | matched | exact-signature |
| Patches/SabotageButtonPatch.cs:5 | SabotageButtonDoClickPatch | `SabotageButton::DoClick(void)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:18 | ReactorSystemTypePatch | `ReactorSystemType::Deteriorate(float)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:60 | HeliSabotageSystemPatch | `HeliSabotageSystem::Deteriorate(float)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:86 | LifeSuppSystemTypePatch | `LifeSuppSystemType::Deteriorate(float)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:122 | MushroomMixupSabotageSystemUpdateSystemPatch | `MushroomMixupSabotageSystem::UpdateSystem(PlayerControl,Hazel::MessageReader)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:139 | MushroomMixupSabotageSystemPatch | `MushroomMixupSabotageSystem::Deteriorate(float)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:199 | SwitchSystemUpdatePatch | `SwitchSystem::UpdateSystem(PlayerControl,Hazel::MessageReader)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:259 | ElectricTaskInitializePatch | `ElectricTask::Initialize(void)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:269 | ElectricTaskCompletePatch | `ElectricTask::Complete(void)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:281 | SabotageSystemTypeRepairDamagePatch | `SabotageSystemType::UpdateSystem(PlayerControl,Hazel::MessageReader)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:344 | SecurityCameraSystemTypeUpdateSystemPatch | `SecurityCameraSystemType::UpdateSystem(PlayerControl,Hazel::MessageReader)` | matched | exact-signature |
| Patches/SabotageSystemPatch.cs:372 | DoorsSystemTypePatch | `DoorsSystemType::UpdateSystem(PlayerControl,Hazel::MessageReader)` | matched | exact-signature |
| Patches/ServerVersionPatch.cs:3 | ServerUpdatePatch | `Constants::GetBroadcastVersion(void)` | inlined | exact-signature |
| Patches/ServerVersionPatch.cs:22 | IsVersionModdedPatch | `Constants::IsVersionModded(void)` | matched | exact-signature |
| Patches/ShipStatusPatch.cs:12 | ShipFixedUpdatePatch | `ShipStatus::FixedUpdate(void)` | matched | exact-signature |
| Patches/ShipStatusPatch.cs:34 | MessageReaderUpdateSystemPatch | `ShipStatus::UpdateSystem(SystemTypes,PlayerControl,Hazel::MessageReader)` | matched | exact-signature |
| Patches/ShipStatusPatch.cs:74 | UpdateSystemPatch | `ShipStatus::UpdateSystem(SystemTypes,PlayerControl,unsignedchar)` | inlined | exact-signature |
| Patches/ShipStatusPatch.cs:156 | ShipStatusCloseDoorsPatch | `ShipStatus::CloseDoorsOfType(SystemTypes)` | matched | exact-signature |
| Patches/ShipStatusPatch.cs:175 | StartPatch | `ShipStatus::Start(void)` | matched | exact-signature |
| Patches/ShipStatusPatch.cs:238 | StartMeetingPatch | `ShipStatus::StartMeeting(PlayerControl,NetworkedPlayerInfo)` | matched | exact-signature |
| Patches/ShipStatusPatch.cs:257 | ShipStatusBeginPatch | `ShipStatus::Begin(void)` | matched | exact-signature |
| Patches/ShipStatusPatch.cs:271 | ShipStatusSpawnPlayerPatch | `ShipStatus::SpawnPlayer(PlayerControl,int,bool)` | inlined | exact-signature |
| Patches/ShipStatusPatch.cs:286 | PolusShipStatusSpawnPlayerPatch | `PolusShipStatus::SpawnPlayer(PlayerControl,int,bool)` | matched | exact-signature |
| Patches/ShipStatusPatch.cs:306 | ShipStatusSerializePatch | `ShipStatus::Serialize(Hazel::MessageWriter,bool)` | matched | exact-signature |
| Patches/ShowHostMeetingPatch.cs:15 | ShowHostMeetingPatch | `PlayerControl::OnDestroy(void)` | matched | exact-signature |
| Patches/ShowHostMeetingPatch.cs:31 | ShowHostMeetingPatch | `IntroCutscene::ShowRole(void)` | inlined | exact-signature |
| Patches/ShowHostMeetingPatch.cs:40 | ShowHostMeetingPatch | `MeetingHud::Update(void)` | matched | exact-signature |
| Patches/ShowHostMeetingPatch.cs:51 | ShowHostMeetingPatch | `MeetingHud::Start(void)` | matched | exact-signature |
| Patches/TaskAdderPatch.cs:8 | ShowFolderPatch | `TaskAdderGame::ShowFolder(TaskFolder)` | matched | exact-signature |
| Patches/TaskAdderPatch.cs:145 | TaskAdderClosePatch | `TaskAdderGame::OnDisable(void)` | matched | exact-signature |
| Patches/TaskAdderPatch.cs:151 | TaskAddButtonUpdatePatch | `TaskAddButton::Update(void)` | matched | exact-signature |
| Patches/TaskAdderPatch.cs:169 | TaskAddButtonStartPatch | `TaskAddButton::Start(void)` | matched | exact-signature |
| Patches/TaskAdderPatch.cs:174 | AddTaskButtonPatch | `TaskAddButton::AddTask(void)` | matched | exact-signature |
| Patches/TaskAssignPatch.cs:10 | AddTasksFromListPatch | `ShipStatus::AddTasksFromList(System::Int32&,int,System::Collections::Generic::List<unsignedchar>,System::Collections::Generic::HashSet<TaskTypes>,System::Collections::Generic::List<NormalPlayerTask>)` | matched | exact-signature |
| Patches/TaskAssignPatch.cs:110 | RpcSetTasksPatch | `NetworkedPlayerInfo::RpcSetTasks(System::Byte[])` | matched | exact-signature |
| Patches/TaskAssignPatch.cs:255 | HandleRpcPatch | `NetworkedPlayerInfo::HandleRpc(unsignedchar,Hazel::MessageReader)` | matched | exact-signature |
| Patches/TextBoxPatch.cs:7 | TextBoxTMPSetTextPatch | `TextBoxTMP::SetText(System::String,System::String)` | inlined | exact-signature |
| Patches/TOHEOnlySearch.cs:9 | FilterTagManagerPatch | `FilterTagManager::RefreshTags(void)` | matched | exact-signature |
| Patches/TOHEOnlySearch.cs:18 | FilterTagsMenuChooseOptionPatch | `FilterTagsMenu::ChooseOption(ChatLanguageButton,System::String)` | inlined | exact-signature |
| Patches/UsablesPatch.cs:6 | CanUsePatch | `Console::CanUse(NetworkedPlayerInfo,System::Boolean&,System::Boolean&)` | matched | exact-signature |
| Patches/UsablesPatch.cs:16 | EmergencyMinigamePatch | `EmergencyMinigame::Update(void)` | matched | exact-signature |
| Patches/UsablesPatch.cs:26 | CanUseVentPatch | `Vent::CanUse(NetworkedPlayerInfo,System::Boolean&,System::Boolean&)` | matched | exact-signature |
| Patches/VentSystemPatch.cs:9 | PerformVentOpPatch | `VentilationSystem::PerformVentOp(unsignedchar,VentilationSystem::Operation,unsignedchar,SequenceBuffer<VentilationSystem::VentMoveInfo>)` | matched | exact-signature |
| Patches/VentSystemPatch.cs:31 | VentSystemDeterioratePatch | `VentilationSystem::Deteriorate(float)` | matched | exact-signature |
| Patches/VentSystemPatch.cs:162 | VentSystemIsVentCurrentlyBeingCleanedPatch | `VentilationSystem::IsVentCurrentlyBeingCleaned(int)` | matched | exact-signature |
| Patches/VoteBanSystemPatch.cs:3 | VoteBanSystemPatch | `VoteBanSystem::AddVote(int,int)` | used-by-inline | exact-signature |
| Roles/Crewmate/Judge.cs:329 | StartMeetingPatch | `MeetingHud::Start(void)` | matched | exact-signature |
| Roles/Crewmate/Retributionist.cs:220 | StartMeetingPatch | `MeetingHud::Start(void)` | matched | exact-signature |
| Roles/Crewmate/Swapper.cs:376 | StartMeetingPatch | `MeetingHud::Start(void)` | matched | exact-signature |
| Roles/Impostor/Councillor.cs:353 | StartMeetingPatch | `MeetingHud::Start(void)` | matched | exact-signature |
| Roles/Impostor/DoubleAgent.cs:308 | StartMeetingPatch | `MeetingHud::Start(void)` | matched | exact-signature |
| Roles/Impostor/Nemesis.cs:230 | StartMeetingPatch | `MeetingHud::Start(void)` | matched | exact-signature |

## 重跑

```powershell
python tools/il2cpp_patch_audit.py
python tools/il2cpp_patch_audit.py --offline
```

默认读取本机只读反编译目录；其他机器用 `--native-source <目录>` 指定。`--version/--platform` 控制比较数据，`--target-version/--target-platform` 单独标记实际目标，版本/平台不一致会写入报告。每次 HTTP 下载单独进程执行，总时限 10 秒；offline 模式仅使用 ignored 缓存。默认输出 JSON 与本 Markdown，不修改生产代码或游戏。

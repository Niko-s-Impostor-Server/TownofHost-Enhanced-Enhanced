# 本地好友码标签与授权

房主的 `TOHE-DATA/LocalPlayerTags.json` 使用 UTF-8 JSON（可带 UTF-8 BOM）。文件不存在时，首次房主加载只创建一个没有玩家条目的模板，不会给任何人默认权限。配置来自本地固定文件，不访问外部 API，不接收客户端上传的标签或授权配置。

以下好友码是示例，应替换为实际玩家的好友码：

```json
{
  "version": 1,
  "players": {
    "sampleuser#1234": {
      "tag": {
        "text": "房间管理",
        "startColor": "#33CCFF",
        "endColor": "FF99CC"
      },
      "permissions": ["moderate", "chat"],
      "gameMaster": false
    },
    "otheruser#0000": {
      "permissions": ["end", "execute"],
      "gameMaster": true
    }
  }
}
```

`players` 按完整好友码匹配。好友码必须包含 1–27 个小写 ASCII 字母、一个 `#` 和四个 ASCII 数字；不去除空格、不转换大小写、不使用部分匹配，也不把好友码拼接到文件路径中。单个文件最多 128 KiB、128 个玩家，JSON 深度最多 8 层。顶层必须包含 `version: 1` 和 `players` 对象；玩家中的三项字段均可省略，省略意味着无标签、无权限、非指定 GM。

| 字段 | 作用 |
| --- | --- |
| `tag.text` | 最多 48 个 Unicode 标量的非空标签文字。控制字符、格式字符（包括双向文本控制和零宽连接符）、换行和段落分隔符无效。`<`、`>`、`&` 转成全角文字，用户内容不能生成富文本标记。 |
| `tag.startColor` | 六位 RGB 十六进制颜色，可以带一个 `#`；省略时为 `FFFFFF`。 |
| `tag.endColor` | 同样格式；省略时使用起始色。关闭渐变时只使用起始色。渐变逐个 Unicode 标量生成颜色，避免拆开 UTF-16 代理对。 |
| `permissions` | 独立授权数组，允许的字符串只有 `moderate`、`chat`、`end`、`execute`，不可重复。 |
| `gameMaster` | 仅允许 JSON 布尔值。表示角色选择时指定为 GM，不赋予聊天或管理命令权限。 |

`moderate` 仅供踢出/封禁命令的额外授权判断；`chat` 供 `/say` 广播；`end` 供结束游戏；`execute` 供处决玩家。这些授权互不隐含。原有房主、开发者和管理员规则应保留；指定 GM 或拥有标签本身不意味着有上述权限。

任何未知字段、重复 JSON 键、重复权限、非法好友码、错误类型或超出限制，都会使整份文件加载失败，并立即清空这个服务原先的标签和授权。读取失败也同样清空。失败不会覆盖用户文件，错误消息不包含玩家配置内容。已有内置身份权限不属于这个配置快照，不受配置失败影响。

加载和重载是显式操作。修改文件后需要重新加载；服务没有文件监听器。服务只提供标签和权限查询、GM 标志查询；不会自动踢人、封禁、处决、结束游戏或发送 RPC。标签结果仅用于显示名称，不应写入真实名称缓存、身份字段或其他 RPC 的身份参数。

## 接入接口

接口位于 `TOHE.Modules.LocalPlayerTags`：

```csharp
LocalPlayerTags.Reset();
bool loaded = LocalPlayerTags.Reload(out string error);
int count = LocalPlayerTags.EntryCount;
bool allowed = LocalPlayerTags.HasPermission(actualSender, LocalPlayerPermission.Chat);
bool gm = LocalPlayerTags.IsDesignatedGameMaster(player);
bool hasTag = LocalPlayerTags.TryGetRenderedTag(player, gradientEnabled, out string tag);
```

房主加入会话后加载配置；会话重置时调用 `Reset`。显式重载只能由房主发起。所有运行时查询都要求当前客户端为房主、玩家存在且未断开，并且读取实际玩家 `Data.FriendCode`；接口没有接受 RPC 提供的好友码字符串的授权入口。

名称接入点是 `Utils.ApplySuffix`：自定义标签应先参与标签显示资格判断，在显示分支中覆盖内置开发者/VIP/管理员标签，并保留隐藏标签选项、原始名称缓存和默认外观限制。管理命令接入点是 `ChatCommands.OnReceiveChatCore`，授权对象必须是实际聊天发送者；踢出和封禁目标的管理员保护也应考虑明确授予 `Moderate` 的玩家。

普通自定义模式角色分配接入点是 `Roles/Core/AssignManager/RoleAssign.cs` 的 `StartSelect`：在预设/随机角色分配前，将指定玩家写入 `RoleResult` 为 `CustomRoles.GM`，并从待分配列表移除；不要让后续预设角色覆盖它。FFA 和游戏原生躲猫猫有独立分配路径，接入时应明确各模式的支持范围。

## 离线验证

```powershell
dotnet run --project tests/LocalPlayerTags/LocalPlayerTags.csproj
```

测试直接链接生产服务和解析器，用最小游戏类型替身验证：严格配置结构、重复与未知字段、字节/玩家/文字限制、独立授权、真实 `Data` 身份取值、客户端和断线玩家拒绝、错误重载撤销旧授权、标签字符清洗与 Unicode 渐变端点。它不证明原生 IL2CPP 类型兼容、角色分配接入或真实游戏客户端的显示/命令行为；这些需要独立的编译和实际游戏验证。

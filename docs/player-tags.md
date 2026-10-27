# 房主本地玩家标签

房主可以用两种本地文件给玩家设置标签。标签显示在玩家名字前面，其他玩家通过房主发送的名字更新看到它。**同一好友码同时出现在两处时，`TOHE-DATA/Tags/<好友码>.txt` 的有效 `TagName` 优先；`TOHE-DATA/tags.txt` 仅作显示后备。** 两者都只保存在房主设备上。

## TOHE 单玩家 TXT：标签和权限

房主第一次加载标签时，模组会在 `TOHE-DATA/Tags/Tag_Template.txt` 建立字段模板。复制该文件，改名为目标玩家的完整好友码，例如 `TOHE-DATA/Tags/sampleuser#1234.txt`，然后编辑：

```text
TagName: 测试标签
TagColor: 33CCFF
PermissionLevel: 0
SayCommandAccess: false
EndCommandAccess: false
ExecuteCommandAccess: false
AssignGameMaster: false
```

`TagColor` 不带 `#`；六位十六进制颜色可设置单色。开启渐变标签选项时，也可写成两个颜色，如 `33CCFF FF99CC`。留空 `TagColor` 会显示原样文字；填写无效颜色会回退为黄色。若希望标签和名字之间有空格，在 `TagName` 末尾保留空格。

| `PermissionLevel` | 授权 |
| --- | --- |
| 0 | 仅标签显示 |
| 1 | `/rn`、`/color` |
| 2 | 再加 `/warn`、玩家列表和替他人使用 `/fix` |
| 3 | 再加 `/start` |
| 4 | 再加 `/kick` |
| 5 | 再加 `/ban` |

`SayCommandAccess`、`EndCommandAccess`、`ExecuteCommandAccess` 分别授权 `/say`、`/end`、`/exe`；`AssignGameMaster` 指定 GM，**不会**自动授予管理命令。这些字段接受 `true`、`yes`、`y`、`t`、`1`，其余值视为关闭。权限仅由房主本地的单玩家 TXT 文件授予，标签文字或 `tags.txt` 都不能授予权限。

单玩家 TXT 也可以放在 `Tags` 的普通子目录中。若同一好友码在多个位置有文件，普通子目录优先于 `Tags` 根目录；子目录按名称排序。`MOD_TAGS`、`VIP_TAGS`、`SPONSOR_TAGS` 是专用颜色目录，不作为这种配置文件读取。匹配按完整好友码进行。

## 在游戏中添加或删除

房主输入：

```text
/addtag <玩家编号> <六位十六进制颜色> <标签文字>
/deletetag <玩家编号>
```

例如，`/addtag 3 33CCFF 测试标签` 会给编号为 3 的玩家添加浅蓝色**后备标签**。`/createtag` 与 `/addtag` 相同。添加命令在游戏和大厅都能使用；名字中的标签在大厅显示。删除命令只能在大厅使用。玩家必须仍连接在当前房间，且有好友码。

颜色写成六位 RGB 十六进制，不带 `#`，例如 `FF00AA`。颜色或参数无效时，命令会提示用法，不会保存标签。对同一玩家再次使用 `/addtag` 会替换 `tags.txt` 中的旧标签。`/deletetag` 只删除 `tags.txt` 的条目，不会修改单玩家 TXT 文件；若该文件有有效 `TagName`，它会继续优先显示。

## 手动编辑后备标签文件

首次成功添加标签后，游戏目录中会出现 `TOHE-DATA/tags.txt`。这是房主设备上的文件，不会上传给其他房主。每行按“好友码=带颜色的标签”保存，例如：

```text
sampleuser#1234=<color=#33CCFFFF>测试标签 </color>
otheruser#5678=<color=#FF00AAFF>活动主持人 </color>
```

等号只在好友码后面的第一个位置充当分隔符，因此颜色标记和标签文字中的 `=` 可以正常读取。手工编辑后，房主在大厅输入 `/tags reload` 即可刷新显示；重新开房也会重新加载。删除文件中的某行并重载，会移除该玩家的标签。文件不存在时不会自动给任何人加标签。

`LocalPlayerTags.json` 已停用，里面的数据不会迁移。单玩家 TXT 的权限设置不会从 `tags.txt` 中推断出来。

单玩家 TXT 标签会占用原有的 MOD/VIP 标签显示位置；内置开发者标签仍可显示在前面。没有有效单玩家 `TagName` 时，`tags.txt` 标签可以与现有 MOD/VIP 标签一起显示。如果启用了“隐藏所有标签和文字”，本地标签也会隐藏。

## 与 EHR 的关系

`tags.txt` 的命令形式和“好友码=标签”格式参考了 EHR。本模组按第一个 `=` 分隔，确保 `<color=#...>` 在重开房间后仍能加载。EHR 的私有标签会给玩家反作弊信任状态；TOHEE 的 `tags.txt` 标签不提供这种豁免。好友码从当前连接的玩家所有者读取，以免过期的玩家对象匹配到错误标签。

# PeakMasterMenu

[PEAK](https://store.steampowered.com/app/3527290/PEAK/) 的 BepInEx 模组：一个游戏内多功能菜单 + HUD 增强，纯中文界面，按 `F8` 呼出。

- Thunderstore：`PeakModder-PeakMasterMenu`
- 依赖：`BepInEx-BepInExPack_PEAK` 5.4.75301
- 目标框架：`netstandard2.1`

## 功能

菜单共 9 个分页：

| 分页 | 内容 |
| --- | --- |
| 移动 | 飞行、速度、跳跃、无坠落伤害等移动相关开关 |
| 攀爬 | 体力 / 抓握相关的攀爬辅助 |
| 生存 | 饥饿、体温、伤病等生存状态控制 |
| 物品 | 全物品目录，支持搜索、收藏（★）与一键给予 / 生成 |
| 绘制 | 物品、玩家、雕像等的屏幕标记与距离显示 |
| 传送 | 队友与小队之间的传送、假人投放 |
| 大厅 | 房间 / 大厅相关操作 |
| 世界 | 地图、时间、生物群系与刷怪 |
| 界面 | 菜单缩放、透明度、HUD 各元素的显示开关 |

菜单外的常驻 HUD：

- 屏幕左上角坐标 / 延迟，右上角本局地图信息，正上方游戏内时间
- 队友屏幕标记（走散时很有用）
- 队友体力条：复制游戏原生体力条，显示附近队友的体力、异常状态、额外体力，可选显示数值与背包格子
- 物品悬浮提示（Tooltip），描述自带中英双语，可在 `BepInEx/config` 下自行修改
- 按住可打开的随身背包轮盘

## 安装

### 方式一：安装器（推荐）

仓库里的 `installer/PeakMasterMenuInstaller` 是一个 Windows 窗体安装器，会自动定位 PEAK 安装目录、装好 BepInEx 前置，再从 Thunderstore 拉取最新版模组。

### 方式二：手动

1. 装好 [BepInExPack_PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/)（可用 Thunderstore Mod Manager / r2modman）
2. 把 `PeakModder.PeakMasterMenu.dll` 放进 `BepInEx/plugins/`
3. 进游戏按 `F8`

## 编译

需要 .NET SDK（见 `global.json`，10.0 及以上，rollForward 到最新大版本）与一份本地 PEAK 安装。

```sh
# 1. 复制本地配置模板并填好自己的路径
cp Config.Build.user.props.template Config.Build.user.props
```

`Config.Build.user.props` 里要填：

- `PEAKGameRootDir`：PEAK 游戏根目录（默认 `C:\Program Files (x86)\Steam\steamapps\common\PEAK\`）
- `PEAKBepInExDir`：BepInEx 目录（用 Thunderstore Mod Manager 时通常在 `%APPDATA%\Thunderstore Mod Manager\DataFolder\PEAK\profiles\<Profile>\BepInEx`）
- `DeployModFiles`：`true` 时构建产物会自动部署到上面的 `plugins/`

这个文件已被 `.gitignore` 排除，不会提交。

```sh
# 调试构建（只出 dll）
dotnet build -c Debug

# 发布构建（同时打出 Thunderstore 包，产物在 ./artifacts/thunderstore/）
dotnet build -c Release

# 直接发布到 Thunderstore
dotnet build -c Release -p:PublishTS=true
```

安装器是独立的 .NET Framework 4.8 工程，需要 MSBuild / Visual Studio：

```sh
cd installer
./build.ps1
```

## 目录结构

```
src/PeakMasterMenu/          模组本体
  Plugin.cs                  BepInEx 入口、配置项、热键
  Patches.cs                 Harmony 补丁
  Cheats.cs / MobSpawner.cs  作弊与刷怪实现
  UI/                        菜单窗口与分页、HUD、物品目录、Tooltip
  Assets/                    内嵌的物品描述（中英双语）
installer/                   Windows 安装器（.NET Framework 4.8，WinForms）
```

## 许可

[MIT](LICENSE)。

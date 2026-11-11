# HPU 宿舍用电 Windows 小组件

在桌面卡片或 Windows 任务栏查看河南理工大学宿舍的照明、空调余额和预计可用天数。独立项目，非学校官方软件。

## 下载和使用

1. 从 [GitHub Releases](https://github.com/2563297296/hpu-power-widget/releases/latest) 下载最新的 Windows x64 EXE。
2. 保存到「下载」或其他普通文件夹，双击运行。无需安装 Node.js，也无需把程序移到「文档」。
3. 首次打开时，依次选择校区、楼栋、楼层和寝室，然后保存。实时查询需要能访问学校查询服务。

右键桌面卡片、任务栏读数条或托盘图标，可刷新、切换显示模式、打开设置或退出。如果时钟旁没有图标，可先查看任务栏的「隐藏的图标」。升级前请先退出旧版。

## 功能

- 桌面卡片和任务栏读数条，支持选择外观及任务栏左右位置。
- 定时刷新，查询失败时保留上次成功的数据。
- 低电提醒默认阈值为 10 度，可在设置里调整，设为 0 关闭。每项余额跌破阈值时提醒一次，余额恢复后才会再次提醒。
- 可设置开机自启动，自动记住显示模式和卡片位置。

## 本地数据

配置、缓存、日志和内置查询环境会保存在首个可写位置的 `HPU-Power-Widget` 子目录。程序依次尝试 Windows「文档」、`%LOCALAPPDATA%` 和 EXE 所在目录。分享应用时只需发送 EXE。提交问题时请去掉寝室号及本机路径等个人信息。

仓库中的 `room_catalog.json` 是构建所需的房间目录，包含校区、楼栋、楼层和房间查询编码。用户的已选寝室和用电余额保存在本地配置和缓存中。

## 从源码构建

需要 Windows x64、.NET Framework 4.x 自带的 C# 编译器，以及 Windows x64 Node.js 24.20.0。确保 `node.exe` 在 PATH 中，再从仓库根目录运行：

```powershell
node --version
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建完成后，运行 `dist\HPU-Power-Widget-Windows.exe`。也可向构建脚本传入 `-NodeExe` 指定 Node.js 路径。使用其他版本时需提供对应的 Node.js 许可证文件。

| 文件 | 用途 |
| --- | --- |
| `Program.cs`、`WidgetForm.cs` | 应用入口、桌面卡片和刷新流程 |
| `TaskbarWidget.cs`、`TrayIcon.cs` | 任务栏读数条和系统托盘 |
| `SettingsForm.cs`、`RoomDirectory.cs` | 设置和逐级选房 |
| `SchoolClient.cs`、`fetch-usage.js` | 用电查询 |
| `AppData.cs`、`LowBalanceReminder.cs` | 本地数据和低电提醒 |
| `build.ps1`、`build-room-directory.js` | Windows 构建和房间目录生成 |

## 第三方许可

单文件版内置 Node.js。相关许可见 [NODE-LICENSE-v24.20.0.txt](NODE-LICENSE-v24.20.0.txt)。

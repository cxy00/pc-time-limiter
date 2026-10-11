# cmjLimit —— PC 使用时长限制器

[English version / 英文版](./README_en.md)

一款用于 **Windows** 的家长控制 / 自律工具：为指定的系统账户设置**每日可用电脑时长**，
时间耗尽后**自动注销当前用户**，并把剩余时间以常驻悬浮窗的形式显示在屏幕角落。

> 用 C# / .NET 8 (WinForms) 编写，仅依赖 Windows API，无需安装、无第三方库。

---

## ✨ 功能特性

- **按账户限额**：在配置文件中为不同 Windows 用户分别设置每日时长（如孩子账户 1 小时，家长账户不限）。
- **双进程互相守护**：监控进程（Monitor）负责计时与注销，守护进程（Guard）在它被结束后立刻拉起，
  防止被任务管理器轻易关闭。
- **常驻倒计时悬浮窗**：屏幕角落显示「剩余 00:59」，置顶、鼠标穿透、不进 Alt+Tab / 任务栏。
- **到点自动注销**：时间用完时弹出提示，数秒后调用 `ExitWindowsEx` 注销当前用户。
- **提前提醒**：剩余时间低于阈值时弹窗提醒，请提前保存工作。
- **锁屏暂停**：锁屏期间不计入使用时长（可在配置中关闭）。
- **再次登录宽限期**：当天额度用完后若再次登录，会给予一段宽限期再强制注销。
- **登录自启动**：通过注册表 `HKCU\...\Run` 实现随用户登录启动。
- **热重载配置**：修改配置文件后约 5 秒内自动生效，无需重启。
- **集中管理**：支持把配置文件放在 `C:\ProgramData\cmjLimit\cmjLimit.ini`，对所有用户统一生效。

---

## 📦 编译运行

需要 **.NET 8 SDK**（Windows）和 **Windows 10/11**。

```powershell
# 克隆后进入目录
cd cmjLimit

# 编译（Release，x64）
dotnet publish -c Release -r win-x64 --self-contained false

# 运行
dotnet run -c Release
```

编译产物位于 `bin/Release/net8.0-windows/`。直接运行 `cmjLimit.exe` 即可。

> 由于使用了 Windows Forms 与 Windows API，仅在 Windows 上可构建与运行。

---

## ⚙️ 配置文件

程序首次运行会自动生成配置文件（按优先级）：

1. `%LOCALAPPDATA%\cmjLimit\cmjLimit.ini`（当前用户私有）
2. `C:\ProgramData\cmjLimit\cmjLimit.ini`（全机共享 / 集中管理）

仓库根目录自带一份可直接使用的示例 [`cmjLimit.ini`](./cmjLimit.ini)（默认限制 `guest` 用户每天 1 小时），
把它复制到上述任一路径即可生效。

默认内容如下（以 `;` 或 `#` 开头为注释）：

```ini
; cmjLimit 配置文件
[users]
; 每行一个账户：用户名 = 每天允许使用的时长
; 时长写法：1h / 1h30m / 90m / 1.5h / 1:30 / 90（纯数字=分钟）
cmj=1h
lyy=1h30m

[sys]
; 提前多少分钟弹窗提醒
warn=5m
; 当天用完后再次登录的宽限时长
grace=5m
; 悬浮窗位置：TopRight / TopLeft / BottomRight / BottomLeft
position=TopRight
; 整体不透明度 0~1（越小越透）
opacity=0.82
; 悬浮窗字号（磅）
hudfont=13
; 锁屏时是否暂停计时
pauselocked=true
```

修改保存后约 5 秒自动重新加载；增删 `[users]` 中的账户可即时开启 / 关闭对某个账户的监控。

---

## 🔧 命令行参数

在 `cmjLimit.exe` 后追加参数运行：

| 参数           | 作用                                                   |
| -------------- | ------------------------------------------------------ |
| `--install`    | 把程序注册到当前用户的登录自启动（注册表 Run）。       |
| `--uninstall`  | 取消登录自启动。                                       |
| `--stop`       | 写入停止标记，两个进程在数秒内退出。                   |
| `--reset`      | 清零「今日」已用时间（重新开始计时）。                 |

示例（以管理员或当前用户身份）：

```powershell
# 安装并启用自启动
cmjLimit.exe --install

# 临时停止监控
cmjLimit.exe --stop

# 重置今日计时
cmjLimit.exe --reset
```

---

## 📁 运行数据

程序在 `%LOCALAPPDATA%\cmjLimit\` 下维护：

- `cmjLimit.ini` —— 配置文件（见上）。
- `usage.json` —— 当日已用秒数（跨天自动清零）。
- `cmjLimit.log` —— 运行日志，便于排查守护 / 注销行为。

---

## ⚠️ 注意事项

- 自动注销会结束当前会话，请用户务必提前保存工作（程序在注销前会弹窗提示）。
- 本工具以「注销」而非「关机 / 重启」方式限制使用，系统本身数据不受影响。
- 守护进程会让结束监控变得更困难，如需彻底停用，请先 `--stop` 再用 `--uninstall` 取消自启动，
  或删除对应注册表项。
- 本程序仅用于合理的时间管理与家长监护，**请勿用于绕过他人知情同意的限制行为**。

---

## 📄 许可证

本项目以 MIT 许可证开源，详见 [LICENSE](./LICENSE)。

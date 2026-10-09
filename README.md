# ChromeUpdateToggle

Chrome 自动更新开关（Windows，单文件 EXE）。一键禁止 / 恢复 Chrome 自动更新，带基线快照，恢复时精确还原改动前的状态。

前身是 `chrome-update-toggle.bat`，因 BAT 在括号路径、重定向、`for` 通配符上的解析坑太多，重写为 C# WinForms。

## 原理（按本机 155/156 实测，不照抄网文）

Chrome 156 的更新器已转纯服务 + COM 拉起模式，光禁服务拦不住（`prefs.json last_checked` + `updater.log` 显示服务 Stopped 照样每天 ForceInstall）。所以要掐三处：

1. **服务**：只禁 2 个 Updater 自动服务（`GoogleUpdaterService*` / `GoogleUpdaterInternalService*`），`GoogleChromeElevationService` 保持 Manual 不动（它只是装软件时提权用）。服务发现是双保险：服务名命中模式 **且** 可执行路径含 `Google`，防第三方撞名误伤（`test-fake-service.ps1` 反向验证过）。
2. **注册表**：官方 kill-switch，`HKLM\SOFTWARE\Policies\Google\Update` 下 `UpdateDefault=0` 等 4 个 DWORD，COM 拉起也认。
3. **文件锁**：对本机实际存在的 2 个更新主程序加 DENY（执行+写），服务/COM 拉起也拒绝访问——
   - `C:\Program Files (x86)\Google\GoogleUpdater\<版本>\updater.exe`（156 现役）
   - `C:\Program Files (x86)\Google\Update\GoogleUpdate.exe`（legacy 残留）

计划任务（`GoogleUpdateTaskMachine*` / `GoogleUpdaterTaskSystem*`）有则禁、无则跳过（156 上已基本无任务）。

## 用法

双击即弹 UAC（manifest `requireAdministrator`，免右键）。界面：单选「禁止更新/恢复更新」+「确定」，右上角大字显示当前状态（**已禁止更新**红 / **更新正常**绿 / **状态不一致**橙）+ 明细 + 日志。

```text
ChromeUpdateToggle.exe            # 打开界面
ChromeUpdateToggle.exe --disable  # 禁止更新（需管理员，exit 0 成功）
ChromeUpdateToggle.exe --enable   # 按最新基线恢复
ChromeUpdateToggle.exe --reset    # 回出厂默认（服务Auto/任务启用/删策略键/解锁）
ChromeUpdateToggle.exe --status   # 只读查状态，免 UAC
```

基线存在 exe 旁 `baseline\<时间>-disable\baseline.json`（服务 StartMode / 任务状态 / 注册表值 / 文件列表 / Elevation 记录），恢复时自动读最新一份；无基线时按出厂默认恢复。

## 分发

- `bin/Release/fx/publish/ChromeUpdateToggle.exe`（约 2MB，框架依赖，需 .NET 8 Desktop 运行时）——本机自用。
- `bin/Release/net8.0-windows/win-x64/publish/ChromeUpdateToggle.exe`（约 154MB，自包含）——发给没装 .NET 的机器，单个文件即跑。

```bash
dotnet publish -c Release                                  # 自包含单文件
dotnet publish -c Release --no-self-contained -o bin/Release/fx/publish
```

## 测试

管理员 PowerShell 跑 `test-csharp.ps1`：记录基线 → `--disable` 断言（服务全 Disabled / `UpdateDefault=0` / 任务 Disabled / 基线生成 / 双 DENY + 写拦截）→ `--enable` 断言（DENY 清 / 服务回基线）→ `--reset` 断言（服务全 Auto / 任务无 Disabled / 策略键删除）。21/21 PASS。

## 源码结构

- `UpdateManager.cs` — 服务（WMI）/ 任务（schtasks）/ 注册表 / ACL（FileSecurity）/ 基线 JSON / 状态判定三支柱
- `Form1.cs` — 单选+确定 UI，大字状态 + 明细 + 日志
- `Program.cs` — CLI 入口（非管理员跑写操作时自动拉起提权副本）
- `app.manifest` — `requireAdministrator`
- ![image-20261009195104454](C:\Users\27598\AppData\Roaming\Typora\typora-user-images\image-20261009195104454.png)

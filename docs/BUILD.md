# 构建说明

## 环境

- 64 位 Windows；当前主要在 Windows 11 环境开发，其他版本未全面验证。
- .NET Framework 4.8 及系统自带的 64 位 `csc.exe`；Windows 元数据位于系统 `WinMetadata` 目录。
- TinyCC 0.9.27 Windows 64 位版，仅用于编译两个原生辅助程序，不包含在仓库中。

## 安装工具并构建

在仓库根目录打开 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-tinycc.ps1
powershell -ExecutionPolicy Bypass -File .\构建.ps1
```

脚本从 TinyCC 官方下载站获取固定版本压缩包，校验 SHA-256 后解压到被 Git 忽略的 `tools/tinycc`。网络不可用时，可手动下载脚本中列出的相同文件，核对哈希，并将其中的 `tcc` 目录放在 `tools/tinycc/tcc`。

构建输出是根目录 `DeskBox.exe`；两个原生辅助程序作为资源打包进入主程序。默认运行时用户数据写入 EXE 旁的 `用户数据`，该目录被 Git 忽略，不能提交。构建不安装开机启动项。

## 隔离测试

在程序没有运行、需要的快捷键未被占用的交互式 Windows 会话中：

```powershell
$testData = Join-Path $env:TEMP ('rushbin-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testData | Out-Null
Start-Process -FilePath .\DeskBox.exe -ArgumentList @('--test', '--data-dir', ('"' + $testData + '"')) -Wait
Get-Content .\验证\测试结果.json -Raw
```

测试会建立临时窗口、使用临时数据并进行真实 Windows API 调用；不要在 CI 或生产会话中假设它是纯单元测试。其他 rushbin 实例占用 F5 等快捷键可能使对应测试失败。测试结果不能代替实际鼠标拖放、通知接收和冷启动验证。

## 发布检查

完整本机检查入口：`scripts/test-release.ps1 -ExecutablePath <构建出的DeskBox.exe> -OutputDirectory <隔离检查输出目录>`。先退出其他 rushbin 实例，避免全局快捷键和桌面层级争用。脚本不负责关闭其他实例，遇到占用直接报错。检查会建立自己的临时文件和窗口。

## 1.0.2 构建与原生托盘检查

普通发行包使用默认构建，不传 `-Steam`。构建需要新增的 `TrayModel.cs`、`TrayModelBridge.c`、`StatusMetrics.cs` 和 `SteamEdition.cs` 等源文件，必须使用同一提交的完整构建脚本。原生桥需链接 Ole32；原生输出在临时目录生成后嵌入 EXE，构建不需要复制开发机器上的旧 DLL。

本机完整回归仍用 `scripts/test-release.ps1`。截图专项新增深目录案例。原生托盘的精确版本限制见 [兼容范围](COMPATIBILITY.md)。在已适配、没有其他 rushbin 实例运行的交互式会话中，还可用全新隔离目录启动 `DeskBox.exe --control-check --data-dir <目录>`，待控制中心出现后向该隔离目录的 `control-probe-command.txt` 写入 `tray-model-self-test`，再检查 `tray-model-roundtrip.json` 的 passed 和各项检查。结束后退出隔离实例，恢复原正式程序。

原生托盘测试会创建临时通知图标，调用实际模型的回调、提示、移动和恢复操作；它不是纯模拟，也不是物理鼠标验收。不要用正式数据目录运行。GitHub 托管 CI 只构建和打包，不执行上述交互式桌面检查。

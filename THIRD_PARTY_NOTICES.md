# 第三方说明

- 项目视觉与功能体验受 [GlassBoard / SENLIN-SAMA](https://github.com/SENLIN-SAMA/win-.-) 启发。rushbin 为独立实现，不是原作者发布的版本；原项目及其素材的权利归原作者所有。
- 构建原生辅助程序需要 [TinyCC](https://bellard.org/tcc/)，其官方 Windows 二进制单独下载，受发行包提供的许可证约束；不作为本仓库 MIT 代码的一部分。
- Windows API 的设计参考 [Microsoft Learn](https://learn.microsoft.com/windows/win32/)；托盘、音频和系统接口另参考开源项目公开资料。具体实现与权利请分别查看 [Seelen UI](https://github.com/eythaann/Seelen-UI)、[EarTrumpet](https://github.com/File-New-Project/EarTrumpet)、[Microsoft Psi](https://github.com/microsoft/psi) 和 [TranslucentTB](https://github.com/TranslucentTB)。
- Windows 商标、第三方应用名称和用户本机图标归各自所有者所有。

- 原生隐藏托盘适配使用 Windows 系统接口及对应模块符号信息；仓库不分发 Taskbar.dll、微软 PDB 或 NVIDIA NVML。适配布局和系统行为有版本限制，见 docs/COMPATIBILITY.md。

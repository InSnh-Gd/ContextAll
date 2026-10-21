# 仓库

对应 `engineering-baseline` 的 `repository-baseline`。任务已完成。

Linux、Windows、Android 的源码在这一个 git 仓库里。某个平台新增项目时，项目加进本仓库，不另开产品仓库。

仓库根目录的 `LICENSE` 是 MIT 许可证。

在 Linux 上构建 Linux 程序时，使用 `ContextAll.Tests.slnf` 或 `ContextAll.Linux.slnf`。这两份解决方案过滤器都不包含 `ContextAll.App.Windows`，因此这次构建不编译 WinUI 项目，也不需要 Windows SDK。`ContextAll.App.Windows` 仍在 `ContextAll.slnx` 里，供 Windows 侧单独构建。

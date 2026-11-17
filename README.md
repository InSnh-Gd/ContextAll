# ContextAll

[![test](https://github.com/InSnh-Gd/ContextAll/actions/workflows/test.yml/badge.svg)](https://github.com/InSnh-Gd/ContextAll/actions/workflows/test.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

ContextAll 让同一局域网里的设备共享剪贴板，并把文件发给已信任的对端。设备之间直接走 QUIC，字节不经过云端中继。

Linux、Windows、Android 三个平台的代码在同一个仓库里。目前 Linux 桌面端最完整：守护进程、命令行和 Avalonia 图形界面都已实现并有测试。Windows（WinUI 3）和 Android 端是图形外壳，还在开发中。

## 功能

- 局域网发现与配对。设备 id 由证书公钥算出。配对时两边显示同一个 6 位短码，双方都接受后才写入信任。
- 纯文本剪贴板同步，上限 1 MiB，按对端单独开关。Linux 上通过 `ext-data-control-v1` 或 `wlr-data-control-unstable-v1` 读写剪贴板。
- 文件传输。文件分段校验 SHA-256，断线后只补发缺失的段。默认保存前询问，可以按对端关闭。
- 命令行和终端菜单，用来管理设备、配对和发送文件。

图像和复制的文件不走剪贴板，原因见 [ADR 0002](docs/adr/0002-file-sync-is-a-transfer.md)。

## 环境要求

- [.NET SDK 10.0.401](global.json) 或更新的 10.0 功能版本
- Linux 运行时需要 `libmsquic`（`libmsquic.so.2`）。没有它时 QUIC 监听失败，不会改用 TCP。
- 图形界面需要 Wayland 会话。`WAYLAND_DISPLAY` 为空时界面退出并返回 1；守护进程和命令行不依赖 Wayland。
- 构建 Android 端需要 JDK 21、Android SDK 与 Android NDK（用于 NativeAOT 编译 `libcontextall_native.so`）；构建 Windows 端需要在 Windows 上安装 Windows SDK。

常用环境变量（均可选，未设置时使用系统默认值）：

```sh
# Linux XDG 运行与存储目录
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
export XDG_CONFIG_HOME="${XDG_CONFIG_HOME:-$HOME/.config}"
export XDG_DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
export XDG_STATE_HOME="${XDG_STATE_HOME:-$HOME/.local/state}"

# Wayland 图形界面会话（守护进程和 CLI 不依赖）
export WAYLAND_DISPLAY="${WAYLAND_DISPLAY:-wayland-0}"

# Android 构建工具链（仅构建与测试 app/ 时需要）
export JAVA_HOME="/path/to/temurin-21"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Android/Sdk}"
export ANDROID_NDK_HOME="$ANDROID_HOME/ndk/<version>"
```

## 构建与测试

```sh
git clone https://github.com/InSnh-Gd/ContextAll.git
cd ContextAll

# 核心 .NET 单元测试与集成测试
dotnet test ContextAll.Tests.slnf -c Release

# 全量端到端规格测试
./scripts/test-e2e.sh --all

# Android Kotlin 单元测试与 Lint（需配置 JDK 21 与 Android SDK）
./gradlew :app:testDebugUnitTest :app:lintDebug --no-daemon
```

`ContextAll.Tests.slnf` 包含所有 `net10.0` 项目和测试，CI 和推送前钩子跑的都是这套验证命令。几份解决方案文件的区别：

| 文件 | 内容 |
| --- | --- |
| `ContextAll.slnx` | 全部项目，包括 Windows |
| `ContextAll.Tests.slnf` | Linux 可构建的核心项目及测试，不含 Windows |
| `ContextAll.Linux.slnf` | Linux 桌面与原生共享项目（含 `ContextAll.Native`），不含 Windows |

`ContextAll.Host.Tests` 里的双守护进程测试会用 `unshare` 建网络命名空间和 veth 设备，需要 `ip`、`ethtool`、`libmsquic`，并允许非特权用户命名空间。CI 里的准备步骤见 [`.github/workflows/test.yml`](.github/workflows/test.yml)。

## 打包与运行

### Linux（AppImage 与用户服务）

```sh
packaging/appimage/build.sh
```

脚本发布 NativeAOT 的 `ContextAll.Host` 和图形界面，从 `/usr/lib/x86_64-linux-gnu/libmsquic.so.2` 复制 QUIC 库，下载 `appimagetool`，最后生成 `artifacts/ContextAll.AppImage`。

AppImage 不带参数时打开图形界面，参数是 `serve` 时启动守护进程，其他参数交给命令行。直接运行源码也可以通过 `dotnet run --project src/ContextAll.Host -- serve` 启动守护进程。把守护进程作为用户服务运行：

```sh
install -Dm755 artifacts/ContextAll.AppImage ~/.local/bin/ContextAll.AppImage
install -Dm644 packaging/contextall.service ~/.config/systemd/user/contextall.service
systemctl --user enable --now contextall.service
```

### Android（APK）

```sh
./gradlew :app:assembleDebug --no-daemon
```

Gradle 构建会自动调用 [`scripts/build-android-native.sh`](scripts/build-android-native.sh) 编译 NativeAOT 动态库 `libcontextall_native.so`，并与 `libmsquic.so` 一同打包进 `app/build/outputs/apk/debug/app-debug.apk`，可通过 `adb install` 安装运行。

## 命令行

```sh
ContextAll.AppImage device list
ContextAll.AppImage pair <device-id>
ContextAll.AppImage clipboard <device-id> on|off
ContextAll.AppImage ask-before-save <device-id> on|off
ContextAll.AppImage file send <device-id> <path>
ContextAll.AppImage identity reset --yes
```

这些命令通过本地套接字 `$XDG_RUNTIME_DIR/contextall/daemon.sock` 联系守护进程，守护进程没运行时返回非 0。在终端里不带子命令运行会打开交互菜单。

身份、设置和状态文件的位置：

| 内容 | 路径 |
| --- | --- |
| 设备密钥 | `$XDG_DATA_HOME/contextall/identity/` |
| 设置与信任列表 | `$XDG_CONFIG_HOME/contextall/config.yaml` |
| 剪贴板状态 | `$XDG_STATE_HOME/contextall/clipboard.json` |
| 未完成的传输 | `$XDG_STATE_HOME/contextall/transfers/` |

## 项目结构

```text
src/
  ContextAll.Protocol      帧格式、信标、控制消息 JSON，无 IO
  ContextAll.Core          纯函数决策：状态 + 输入 → 下一状态 + 效果
  ContextAll.Effects       执行效果，QUIC 链路
  ContextAll.Ipc           前端连接守护进程的本地通道客户端
  ContextAll.Host          守护进程和命令行（NativeAOT）
  ContextAll.App           Linux Avalonia 视图
  ContextAll.App.Desktop   Linux 图形入口
  ContextAll.App.Windows   WinUI 3 程序
  ContextAll.Native        Android NativeAOT JNI 引擎与 libmsquic 后端
app/                       Android Kotlin Jetpack Compose 应用与 Gradle 构建
scripts/                   E2E、NativeAOT 构建与 git 钩子脚本
tests/                     xUnit 单元测试与 E2E 规格测试
packaging/                 AppImage 与 systemd 用户单元
docs/                      行为文档与 ADR
openspec/                  需求规格与变更记录
```

模块之间的引用关系见 [docs/architecture.md](docs/architecture.md)。

## 文档

- [文档索引](docs/README.md)：各项功能当前已实现的行为
- [模块边界](docs/architecture.md)
- [词汇](docs/glossary.md)
- [架构决策记录](docs/adr/)
- 需求规格在 [`openspec/specs/`](openspec/specs/)

## 参与开发

见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 许可证

[MIT](LICENSE)

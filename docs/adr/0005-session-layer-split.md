# 会话层与底层效果解耦独立

状态：决策通过

桌面守护进程（ContextAll.Host）与移动端外壳（ContextAll.App.Android）均需运行相同的局域网会话引擎：维护对端路由表、推进证书互认配对状态机、调度 QUIC 会话流、管理密钥/设置/传输持久化存储，以及执行多播信标发现。此前该引擎内嵌于 ContextAll.Host 中，与 Linux 本地 Unix 域套接字监听、CLI 命令行调度、LinuxPaths 路径规则及 WaylandClipboard 剪贴板深度耦合。为支持多平台复用并保障跨平台通信协议等价性（protocol equivalence），必须将核心会话状态机与协调逻辑从宿主进程中剥离，建立独立的跨平台会话层。

会话引擎独立抽取为 ContextAll.Session，而非并入 ContextAll.Effects。ADR 0003 确立的 ContextAll.Effects 职责仅限于纯决策效果委托的顺序执行（SharedEffects.Run）、基于 System.Net.Quic 的底层套接字读写抽象（QuicLink，端口 42461，ALPN 为 contextall）及原始分段落盘。若将会话引擎塞入 Effects 模块，该模块将被迫引入 YamlDotNet 配置解析、持久化存储结构、多播监听套接字及多对端路由协调，破坏 ADR 0003 所确立的「shared effects stay in one module, and orchestration is a separate layer」（共享底层效果集中于单一模块，编排协调属于独立分层）原则，模糊底层 IO 驱动与上层业务协调的边界。因此，ContextAll.Session 作为位于 ContextAll.Effects 之上的独立分层（目标框架 net10.0，约束 IsAotCompatible=true），引用 Core、Effects 与 Protocol，并通过四项平台抽象缝隙（IHostPaths、IClipboardSource、IDiscovery、ITransferSink）与平台宿主解耦。ContextAll.Host 退化为 Linux 宿主适配器，提供 Unix 套接字与 CLI 调度；ContextAll.App.Android 则作为移动端宿主适配器，由 LinkService 前台服务直接在进程内运行 SessionHost，并通过 AndroidMsQuicBackend 驱动打包的 libmsquic.so 完成 QUIC 会话传输。

这一拆分消除了桌面与移动端的协议逻辑重复，使会话层可通过平台缝隙 Mock 进行完备的单元测试，同时保持全链路 NativeAOT 兼容。代价是解决方案增加了项目节点与分层依赖。若未来引入无会话状态的轻量直连端点或将传输重构为统一多路复用网关，再重新评估会话层与效果层的切分。

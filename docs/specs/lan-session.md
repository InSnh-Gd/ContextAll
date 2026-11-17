# 局域网会话

对应 `lan-session`。设备 id、配对、信标、mDNS / Android NSD 发现、网络筛选、路由表和地址变化都有测试。

## 设备 id

设备 id 是 `ca1` 加上 SPKI 的 SHA-256 前 16 字节的 26 位小写、无填充 Base32（RFC 4648）。比较大小时用这 16 字节的无符号大端整数。短码是两个 SPKI 按这个顺序排列后做 SHA-256，取前 30 位，编成 6 个小写 Base32 字符。短码不放进任何帧。

叶子证书必须是 ECDSA P-256（OID `1.2.840.10045.3.1.7`）。不看证书日期。id 相同但 SPKI 不同则关闭会话，信任项保留。

## 配对

两边都立刻发出 HELLO，不等对方。HELLO 带意图和显示名。去重保留摘要更大的那一侧打开的会话，另一侧关闭。短码只在留下的会话上计算。双方都发出 `PAIR_ACCEPT` 之后才写入信任，默认剪贴板同步关、保存前询问开。信任之前不发 `PATH_ANNOUNCE`。配对过程中改名不是第二条 HELLO；信任写入后，新名字作为 `DISPLAY_NAME` 发出。60 秒超时或一次拒绝都不写信任。

解绑立刻删除本地信任。会话还活着时，先写出并刷新 `UNPAIR`，再关闭连接。

## 发现

信标魔数 `CA01`，版本 1，后面是 16 字节摘要、大端端口、大端总长度和 UTF-8 显示名。总长度必须等于数据报长度。本机自己的摘要被忽略。显示名不在 1 到 64 个标量之内，或含小于 U+0020 的字符时，路径保留，名字丢掉。

信标每 5 秒发一次，启动时先发一次。Linux 与 Windows 上 IPv4 发到广播地址，IPv6 对每个有可用地址的接口发到 `ff02::1`。Windows 通过 `IFirewallProfile`（`NetworkListManager` 实现，测试用假对象）读取每个接口所属防火墙配置：Public 上不发信标、不绑定入站 QUIC；Private 与 Domain 正常使用。查询失败时该接口被跳过且不抛异常；网络变化会清空缓存并重新查询（测试：`WindowsDaemonTests.Public_firewall_profile_is_skipped_and_private_or_domain_is_used`、`WindowsDaemonTests.Failed_profile_lookup_skips_the_interface_without_throwing`、`WindowsDaemonTests.Cached_profile_refreshes_on_network_change`）。入站 UDP 42461 与 42462 仅在 Private 配置文件上由安装脚本放行，见 [本地操作](local-operator.md)。

桌面端 mDNS 服务名是 `_contextall._udp.local`，UDP 端口 5353。响应带 PTR、SRV、TXT、A 或 AAAA 记录，设备 id 放在 TXT 的 `id=` 里。记录数、名字长度和压缩指针都有上限；截断、循环指针和超长标签返回空结果，不抛异常。每 60 秒查询一次。5353 被占用时只记一条 `mdns-skipped`，不抛异常。

mDNS 只通告非链路本地地址。没有可通告地址时，接收方用数据报的源地址；链路本地的源地址因此保留接口 scope id。

Linux 生产路径上 `LanHost.TryCreate` 注入 `LinuxDiscovery`。`SessionHost` 只要拿到非空 `IDiscovery`，就不再自己绑定 UDP 42462，也不再启动内建 mDNS；信标与 `_contextall._udp.local` 由这份适配器负责。Windows 上 `CreateLinuxDiscovery` 返回空，发现留在 `SessionHost` 内。`LanHost.CreateIsolated` 与未注入发现的单元测试仍走内建路径，避免双开 42462。Android 另有 `BridgeDiscovery`（`OwnsNetworkAndSockets` 为 true）。见 [ADR 0010](../adr/0010-linux-discovery-is-injected.md)（测试：`PlatformSeamsTests.Production_linux_discovery_is_linux_discovery_adapter`、`PlatformSeamsTests.SessionHost_skips_builtin_beacon_when_discovery_is_injected`、`PlatformSeamsTests.Discovery_Start_And_Announce_Does_Not_Throw`）。

Android 端由 `LanDiscoveryController` 与 `NativeSessionBridge` 协作处理网络筛选与局域网发现：从 `ConnectivityManager` 枚举活动网络时，仅选取 Wi-Fi 与以太网网络，排除蜂窝移动网络与 VPN（测试：`F13_NetworkFilterTests`）。Android 不直接绑原始 5353 套接字，而是在持有 `WifiManager.MulticastLock` 期间通过系统 `NsdManager` 注册并浏览 `_contextall._udp` 服务（TXT `id=` 携带设备 id），解析出的候选端点直接推送给进程内 `SessionHost` 路由表（测试：`F14_AndroidNsdMulticastTests`）。当 `NsdManager` 不可用或注册失败时，软降级为仅信标模式，仍通过 UDP 42462 信标发现对端（测试：`F15_SoftFailureBeaconOnlyTests`）。

## 路由

发现路径满 3 分钟过期。过期不删除信任。会话观察到的地址在关闭之后不再拨号。通告地址最多 8 个，按最近刷新时间排序拨号。

链路本地地址（`169.254.0.0/16`、`fe80::/10`）和不在本机前缀上的地址不进入通告。IPv6 按同样的前缀规则检查；`fe80::1%2` 这样的地址解析成带 scope id 的端点，拨号时保留 scope id。

未信任的对端只有在 `StartPairing` 之后才拨。某次拨号失败后，该候选要等 `RefreshedAt` 变化才再拨；另一个仍然新鲜的候选可以立刻接着拨。

本机可用地址集合变化时，向每个已信任且已进入的会话发 `PATH_ANNOUNCE`，内容替换旧集合；没有可用地址时发空列表。

## 地址变化

枚举状态为 Up 的接口上的 IPv4、IPv6 单播地址，跳过回环、多播和不可用协议族。每项带前缀长度、接口索引和链路本地标记。

`NetworkChange`（桌面端）及 `ConnectivityManager.NetworkCallback`（Android 端）的地址和可用性事件经过防抖后刷新前缀集合；桌面端另有 5 秒一次的核对，兜住没有事件的变化。地址集合真的变化时，立即发一次信标。监听器按地址集合差分重建，先加新地址，再移除旧地址；已有连接不随监听器移除而关闭。

# Linux 发现走注入的 LinuxDiscovery

状态：已接受

`IDiscovery` 在 Android 上已有 `BridgeDiscovery`。Linux 若生产路径仍传空的 `discovery`，仓库里的 `LinuxDiscovery` 就是闲置适配器，UDP 42462 信标和 mDNS 继续长在 `SessionHost` 里，两套实现会抢同一个端口。

`LanHost.TryCreate` 在非 Windows 上构造 `LinuxDiscovery` 并注入 `SessionHost`。`SessionHost` 只要拿到非空 `IDiscovery`，就不再自己绑 42462、也不再启动内建 `MdnsDiscovery`。`LanHost.CreateIsolated` 和未注入发现的测试仍走内建路径。Windows 上 `CreateLinuxDiscovery` 返回空，桌面发现留在 `SessionHost`。

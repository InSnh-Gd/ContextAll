using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Windows.Input;
using ContextAll.App.Controls;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.ViewModels;

/// <summary>View model for the Connection (连接与网络) section: manual dial, LAN endpoints, protocol specs, peer overview, and onboarding guide.</summary>
public sealed class ConnectionViewModel : ViewModelBase
{
    public const int DefaultQuicPort = 42461;
    public const int GuideStepCount = 4;

    public static readonly IReadOnlyList<GuideStepItem> GuideSteps =
    [
        new(
            "无需账号 · 本地直连",
            "1. 同网直连，不经云端",
            "设备连入同一局域网后即可直接建立 QUIC 加密连接，文字和文件都在本地传输，不经过任何云端服务器。"),
        new(
            "双向确认 · 安全信任",
            "2. 6 位配对码双向核对",
            "首次连接新设备时，双方屏幕会显示同一组 6 位配对码。核对无误并在两端确认，即可建立长期信任。"),
        new(
            "按设备控制 · 边界清晰",
            "3. 按设备控制同步与保存策略",
            "在「设备」页可为每台已配对设备单独开启或关闭剪贴板同步，也能自由选择收到文件时先确认或直接保存。"),
        new(
            "有序排队 · 分块校验",
            "4. 多文件排队与分块校验",
            "在「传输」页一次选择或拖入多个文件时，系统会按设备顺序依次发送，并逐块完成 SHA-256 完整性校验。"),
    ];

    public static readonly IReadOnlyList<string> LanPresets =
    [
        "192.168.1.",
        "10.0.0.",
        "172.16.0.",
        "[::1]:42461",
    ];

    private readonly ILocalDaemonClient _client;
    private readonly Action<bool>? _onGuideSeenChanged;
    private LocalShellState _state = LocalShellState.Empty;
    private string _endpointInput = string.Empty;
    private string _aliasInput = string.Empty;
    private string? _dialFeedback;
    private bool _dialIsError;
    private bool _isGuideOpen;
    private int _guideStepIndex;

    public ConnectionViewModel(
        ILocalDaemonClient client,
        Action<bool>? onGuideSeenChanged = null,
        Action<Action>? dispatchToUi = null)
        : base(dispatchToUi)
    {
        _client = client;
        _onGuideSeenChanged = onGuideSeenChanged;
        _state = client.CurrentState;
        _client.StateChanged += next => DispatchToUi(() => ApplyState(next));
        CopyEndpointCommand = new ShellRelayCommand(parameter =>
        {
            if (parameter is string endpoint)
            {
                CopyRequested?.Invoke(endpoint);
            }
        });
        ApplyLanPresetCommand = new ShellRelayCommand(parameter =>
        {
            if (parameter is string preset)
            {
                ApplyLanPreset(preset);
            }
        });
        StartPairingCommand = new ShellRelayCommand(async parameter =>
        {
            if (parameter is string deviceId)
            {
                await StartPairingAsync(deviceId);
            }
        });
        NextGuideStepCommand = new ShellRelayCommand(NextGuideStep);
        PreviousGuideStepCommand = new ShellRelayCommand(PreviousGuideStep);
        CloseGuideCommand = new ShellRelayCommand(CloseGuide);
    }

    public event Action<string>? CopyRequested;

    public ICommand CopyEndpointCommand { get; }

    public ICommand ApplyLanPresetCommand { get; }

    public ICommand StartPairingCommand { get; }

    public ICommand NextGuideStepCommand { get; }

    public ICommand PreviousGuideStepCommand { get; }

    public ICommand CloseGuideCommand { get; }

    public string EndpointInput
    {
        get => _endpointInput;
        set => SetField(ref _endpointInput, value);
    }

    public string AliasInput
    {
        get => _aliasInput;
        set => SetField(ref _aliasInput, value);
    }

    public IReadOnlyList<string> LocalEndpoints
    {
        get
        {
            if (_state.LocalAddresses.Count == 0)
            {
                return [];
            }

            var port = _state.QuicPort;
            return [.. _state.LocalAddresses.Select(addr => FormatEndpoint(addr, port))];
        }
    }

    public int QuicPort => _state.QuicPort;
    public int EffectiveQuicPort => _state.QuicPort > 0 ? _state.QuicPort : DefaultQuicPort;
    public string ClipboardBackendCode => _state.ClipboardBackend;
    public bool HasLocalEndpoints => LocalEndpoints.Count > 0;
    public bool HasDialFeedback => !string.IsNullOrWhiteSpace(DialFeedback);

    public IReadOnlyList<DeviceSnapshotPayload> Devices => _state.Devices;
    public bool HasDevices => _state.Devices.Count > 0;
    public int ConnectedDeviceCount => _state.Devices.Count(d => IsConnectedPresence(d.Presence));

    public string ClipboardStatusText => $"{ClipboardBackendLabel} ({ClipboardBackendCode})";

    public string QuicSpecText => $"QUIC TLS 1.3 · UDP {EffectiveQuicPort}";

    public string PeersSectionTitle => HasDevices
        ? $"设备连接状态 ({Devices.Count})"
        : "设备连接状态";

    public IReadOnlyList<EndpointRowItem> EndpointItems
    {
        get
        {
            var endpoints = LocalEndpoints;
            var count = endpoints.Count;
            var items = new EndpointRowItem[count];
            for (var i = 0; i < count; i++)
            {
                items[i] = new EndpointRowItem
                {
                    Endpoint = endpoints[i],
                    Position = GroupPositions.Of(i, count),
                    CopyCommand = CopyEndpointCommand,
                };
            }

            return items;
        }
    }

    public IReadOnlyList<LanPresetItem> LanPresetItems =>
        [.. LanPresets.Select(preset => new LanPresetItem
        {
            Preset = preset,
            ApplyCommand = ApplyLanPresetCommand,
        })];

    public IReadOnlyList<PeerOverviewItem> PeerOverviewItems
    {
        get
        {
            var devices = Devices;
            var count = devices.Count;
            var items = new PeerOverviewItem[count];
            for (var i = 0; i < count; i++)
            {
                var peer = devices[i];
                items[i] = new PeerOverviewItem
                {
                    DeviceId = peer.DeviceId,
                    DisplayName = peer.DisplayName,
                    PresenceLabel = FormatPresenceLabel(peer.Presence),
                    PresenceTone = IsConnectedPresence(peer.Presence)
                        ? BadgeTone.Primary
                        : IsReachablePresence(peer.Presence)
                            ? BadgeTone.Secondary
                            : BadgeTone.Neutral,
                    EndpointLabel = string.IsNullOrWhiteSpace(peer.Endpoint)
                        ? $"局域网自动发现 · 端口 {EffectiveQuicPort}"
                        : peer.Endpoint,
                    IsTrusted = peer.Trusted,
                    Position = GroupPositions.Of(i, count),
                    StartPairingCommand = StartPairingCommand,
                };
            }

            return items;
        }
    }

    public string ClipboardBackendLabel => _state.ClipboardBackend switch
    {
        "wayland" => "Wayland 原生",
        "x11" => "X11 回退",
        _ => "未就绪",
    };

    public string? DialFeedback
    {
        get => _dialFeedback;
        private set
        {
            if (SetField(ref _dialFeedback, value))
            {
                OnPropertyChanged(nameof(HasDialFeedback));
            }
        }
    }

    public bool DialIsError
    {
        get => _dialIsError;
        private set => SetField(ref _dialIsError, value);
    }

    public bool IsGuideOpen
    {
        get => _isGuideOpen;
        private set => SetField(ref _isGuideOpen, value);
    }

    public int GuideStepIndex
    {
        get => _guideStepIndex;
        private set
        {
            var clamped = Math.Clamp(value, 0, GuideStepCount - 1);
            if (SetField(ref _guideStepIndex, clamped))
            {
                OnPropertyChanged(nameof(CanGoPreviousGuideStep));
                OnPropertyChanged(nameof(IsLastGuideStep));
                OnPropertyChanged(nameof(CurrentGuideStep));
                OnPropertyChanged(nameof(GuideDots));
                OnPropertyChanged(nameof(GuideStepCounterText));
            }
        }
    }

    public bool CanGoPreviousGuideStep => GuideStepIndex > 0;
    public bool IsLastGuideStep => GuideStepIndex >= GuideStepCount - 1;

    public GuideStepItem CurrentGuideStep => GuideSteps[GuideStepIndex];

    public string GuideStepCounterText => $"步骤 {GuideStepIndex + 1} / {GuideStepCount}";

    public IReadOnlyList<GuideDotItem> GuideDots =>
        [.. GuideSteps.Select((_, i) => new GuideDotItem { IsActive = i == GuideStepIndex })];

    public void ApplyState(LocalShellState state)
    {
        _state = state;
        OnPropertyChanged(nameof(LocalEndpoints));
        OnPropertyChanged(nameof(HasLocalEndpoints));
        OnPropertyChanged(nameof(QuicPort));
        OnPropertyChanged(nameof(EffectiveQuicPort));
        OnPropertyChanged(nameof(ClipboardBackendCode));
        OnPropertyChanged(nameof(ClipboardBackendLabel));
        OnPropertyChanged(nameof(ClipboardStatusText));
        OnPropertyChanged(nameof(QuicSpecText));
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(ConnectedDeviceCount));
        OnPropertyChanged(nameof(PeersSectionTitle));
        OnPropertyChanged(nameof(EndpointItems));
        OnPropertyChanged(nameof(PeerOverviewItems));
    }

    public void OpenGuide()
    {
        GuideStepIndex = 0;
        IsGuideOpen = true;
    }

    public void CloseGuide()
    {
        IsGuideOpen = false;
        _onGuideSeenChanged?.Invoke(true);
    }

    public void NextGuideStep()
    {
        if (GuideStepIndex < GuideStepCount - 1)
        {
            GuideStepIndex++;
        }
        else
        {
            CloseGuide();
        }
    }

    public void PreviousGuideStep()
    {
        if (GuideStepIndex > 0)
        {
            GuideStepIndex--;
        }
    }

    public void SetGuideStep(int stepIndex) => GuideStepIndex = stepIndex;

    public void ApplyLanPreset(string preset)
    {
        EndpointInput = preset;
        DialFeedback = null;
        DialIsError = false;
    }

    public async Task<bool> StartPairingAsync(string deviceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        var result = await _client.StartPairingAsync(deviceId, ct);
        if (result.Ok)
        {
            var deviceName = _state.Devices.FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal))?.DisplayName ?? deviceId;
            DialFeedback = $"已向「{deviceName}」发起配对，请在「设备」页核对 6 位配对码。";
            DialIsError = false;
            return true;
        }

        DialFeedback = string.IsNullOrWhiteSpace(result.Error) ? "发起配对失败，请确认对方设备在线。" : result.Error;
        DialIsError = true;
        return false;
    }

    public async Task<bool> DialEndpointAsync(CancellationToken ct = default)
    {
        if (!TryParseEndpoint(_endpointInput, out var host, out var port))
        {
            DialFeedback = BuildEndpointValidationError(_endpointInput);
            DialIsError = true;
            return false;
        }

        var alias = string.IsNullOrWhiteSpace(_aliasInput) ? null : _aliasInput.Trim();
        var result = await _client.ConnectEndpointAsync(host, port, alias, ct);
        if (result.Ok)
        {
            DialFeedback = $"已向 {FormatEndpoint(host, port)} 发起连接。";
            DialIsError = false;
            return true;
        }

        DialFeedback = string.IsNullOrWhiteSpace(result.Error) ? "手动连接未成功，请检查地址和端口后重试。" : result.Error;
        DialIsError = true;
        return false;
    }

    public static string FormatPresenceLabel(string? presence)
    {
        if (IsConnectedPresence(presence))
        {
            return "已连接";
        }

        if (IsReachablePresence(presence))
        {
            return "局域网可达";
        }

        return "离线";
    }

    public static bool IsConnectedPresence(string? presence) =>
        string.Equals(presence, "connected", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(presence, "online", StringComparison.OrdinalIgnoreCase);

    public static bool IsReachablePresence(string? presence) =>
        string.Equals(presence, "reachable", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(presence, "connecting", StringComparison.OrdinalIgnoreCase);

    public static bool TryParseEndpoint(string? input, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (trimmed.StartsWith('['))
        {
            var closeBracket = trimmed.IndexOf(']');
            if (closeBracket <= 1)
            {
                return false;
            }

            var v6HostPart = trimmed[1..closeBracket];
            if (!IPAddress.TryParse(v6HostPart, out var parsedV6) ||
                parsedV6.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }

            if (closeBracket == trimmed.Length - 1)
            {
                host = parsedV6.ToString();
                port = DefaultQuicPort;
                return true;
            }

            if (closeBracket + 2 >= trimmed.Length || trimmed[closeBracket + 1] != ':')
            {
                return false;
            }

            var v6PortPart = trimmed[(closeBracket + 2)..];
            if (!TryParsePort(v6PortPart, out port))
            {
                return false;
            }

            host = parsedV6.ToString();
            return true;
        }

        var firstColon = trimmed.IndexOf(':');
        var lastColon = trimmed.LastIndexOf(':');

        // Bare IPv6 address without brackets (e.g. "::1" or "fd00::1") defaults to DefaultQuicPort.
        if (firstColon >= 0 && firstColon != lastColon)
        {
            if (IPAddress.TryParse(trimmed, out var bareV6) &&
                bareV6.AddressFamily == AddressFamily.InterNetworkV6)
            {
                host = bareV6.ToString();
                port = DefaultQuicPort;
                return true;
            }

            return false;
        }

        if (firstColon >= 0)
        {
            if (firstColon == 0 || firstColon == trimmed.Length - 1)
            {
                return false;
            }

            var hostPart = trimmed[..firstColon];
            var portPart = trimmed[(firstColon + 1)..];
            if (!TryParsePort(portPart, out port))
            {
                return false;
            }

            if (!TryNormalizeHost(hostPart, allowSingleLabelDns: true, out host))
            {
                port = 0;
                return false;
            }

            return true;
        }

        if (!TryNormalizeHost(trimmed, allowSingleLabelDns: false, out host))
        {
            return false;
        }

        port = DefaultQuicPort;
        return true;
    }

    private static bool TryParsePort(string portPart, out int port) =>
        int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
        port is >= 1 and <= 65535;

    private static bool TryNormalizeHost(string hostPart, bool allowSingleLabelDns, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(hostPart) || hostPart.StartsWith('.') || hostPart.EndsWith('.'))
        {
            return false;
        }

        // Require standard dotted-quad notation so partial numeric strings (e.g. "192.168.1") are rejected.
        if (hostPart.Count(c => c == '.') == 3 &&
            IPAddress.TryParse(hostPart, out var parsedIp) &&
            parsedIp.AddressFamily == AddressFamily.InterNetwork)
        {
            normalized = parsedIp.ToString();
            return true;
        }

        if (Uri.CheckHostName(hostPart) != UriHostNameType.Dns)
        {
            return false;
        }

        var labels = hostPart.Split('.');
        if (labels.Length == 0 || labels[^1].All(char.IsDigit))
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }
        }

        if (!allowSingleLabelDns &&
            labels.Length < 2 &&
            !string.Equals(hostPart, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = hostPart;
        return true;
    }

    private static string BuildEndpointValidationError(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "请输入对方设备的 IP 地址或域名，例如 192.168.1.10 或 [::1]:42461。";
        }

        var trimmed = input.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            var closeBracket = trimmed.IndexOf(']');
            if (trimmed.StartsWith('[') && closeBracket > 0 && closeBracket < trimmed.Length - 1)
            {
                var suffix = trimmed[(closeBracket + 1)..];
                if (suffix == ":" || (suffix.StartsWith(':') && !TryParsePort(suffix[1..], out _)))
                {
                    return "端口号无效，请输入 1–65535 之间的数字，或省略端口以使用默认 42461。";
                }
            }
            else if (trimmed.Count(c => c == ':') == 1)
            {
                var idx = trimmed.IndexOf(':');
                var portSlice = trimmed[(idx + 1)..];
                if (!TryParsePort(portSlice, out _))
                {
                    return "端口号无效，请输入 1–65535 之间的数字，或省略端口以使用默认 42461。";
                }
            }
        }

        return "地址格式无效，请输入有效 IP 或域名（如 192.168.1.10、192.168.1.10:42461 或 [::1]:42461）。";
    }

    private static string FormatEndpoint(string host, int port)
    {
        if (port <= 0)
        {
            return host;
        }

        return host.Contains(':', StringComparison.Ordinal) ? $"[{host}]:{port}" : $"{host}:{port}";
    }
}

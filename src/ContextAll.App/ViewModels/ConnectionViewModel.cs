using System.Globalization;
using System.Net;
using ContextAll.Ipc;

namespace ContextAll.App.ViewModels;

/// <summary>View model for the Connection (连接) section: manual IP:port dial, LAN endpoints, clipboard status, usage guide.</summary>
public sealed class ConnectionViewModel : ViewModelBase
{
    private readonly ILocalDaemonClient _client;
    private readonly Action<bool>? _onGuideSeenChanged;
    private LocalShellState _state = LocalShellState.Empty;
    private string _endpointInput = string.Empty;
    private string _aliasInput = string.Empty;
    private string? _dialFeedback;
    private bool _dialIsError;
    private bool _isGuideOpen;

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
    }

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
    public string ClipboardBackendCode => _state.ClipboardBackend;
    public bool HasLocalEndpoints => LocalEndpoints.Count > 0;
    public bool HasDialFeedback => !string.IsNullOrWhiteSpace(DialFeedback);

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

    public void ApplyState(LocalShellState state)
    {
        _state = state;
        OnPropertyChanged(nameof(LocalEndpoints));
        OnPropertyChanged(nameof(HasLocalEndpoints));
        OnPropertyChanged(nameof(QuicPort));
        OnPropertyChanged(nameof(ClipboardBackendCode));
        OnPropertyChanged(nameof(ClipboardBackendLabel));
    }

    public void OpenGuide() => IsGuideOpen = true;

    public void CloseGuide()
    {
        IsGuideOpen = false;
        _onGuideSeenChanged?.Invoke(true);
    }

    public async Task<bool> DialEndpointAsync(CancellationToken ct = default)
    {
        if (!TryParseEndpoint(_endpointInput, out var host, out var port))
        {
            DialFeedback = "端点格式无效，请输入 IP:端口（如 192.168.1.10:42461 或 [::1]:42461）。";
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

        DialFeedback = string.IsNullOrWhiteSpace(result.Error) ? "手动连接失败。" : result.Error;
        DialIsError = true;
        return false;
    }

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

        string hostPart;
        string portPart;

        if (trimmed.StartsWith('['))
        {
            var closeBracket = trimmed.IndexOf(']');
            if (closeBracket <= 1 ||
                closeBracket + 2 >= trimmed.Length ||
                trimmed[closeBracket + 1] != ':')
            {
                return false;
            }

            hostPart = trimmed[1..closeBracket];
            portPart = trimmed[(closeBracket + 2)..];
        }
        else
        {
            var firstColon = trimmed.IndexOf(':');
            var lastColon = trimmed.LastIndexOf(':');
            if (firstColon <= 0 || firstColon != lastColon || firstColon == trimmed.Length - 1)
            {
                return false;
            }

            hostPart = trimmed[..firstColon];
            portPart = trimmed[(firstColon + 1)..];
        }

        if (!int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
            port < 1 ||
            port > 65535)
        {
            host = string.Empty;
            port = 0;
            return false;
        }

        if (IPAddress.TryParse(hostPart, out var parsedIp))
        {
            host = parsedIp.ToString();
            return true;
        }

        if (Uri.CheckHostName(hostPart) == UriHostNameType.Dns)
        {
            host = hostPart;
            return true;
        }

        host = string.Empty;
        port = 0;
        return false;
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

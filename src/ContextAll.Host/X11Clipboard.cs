using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ContextAll.Protocol;

namespace ContextAll.Host;

public enum ClipboardBackendKind
{
    None,
    Wayland,
    X11,
}

internal abstract record X11Event;

internal sealed record X11SelectionOwnerChanged(ulong Selection, ulong Owner, ulong Timestamp) : X11Event;

internal sealed record X11SelectionNotify(ulong Requestor, ulong Selection, ulong Target, ulong Property, ulong Time) : X11Event;

internal sealed record X11PropertyNotify(ulong Window, ulong Atom, ulong Time, bool Deleted) : X11Event;

internal sealed record X11SelectionRequest(ulong Owner, ulong Requestor, ulong Selection, ulong Target, ulong Property, ulong Time) : X11Event;

internal sealed record X11SelectionClear(ulong Window, ulong Selection, ulong Time) : X11Event;

internal sealed record X11PropertyData(bool Ok, ulong ActualType, int Format, byte[] Bytes, ulong[] Words32, ulong BytesAfter);

internal interface IX11Api : IDisposable
{
    ulong Window { get; }
    ulong ClipboardAtom { get; }
    ulong TargetsAtom { get; }
    ulong IncrAtom { get; }
    ulong Utf8StringAtom { get; }
    ulong TextPlainUtf8Atom { get; }
    ulong TextPlainAtom { get; }
    ulong TextAtom { get; }
    ulong StringAtom { get; }
    ulong AtomTypeAtom { get; }
    ulong PropertyAtom { get; }

    void SetSelectionOwner(ulong selection, ulong owner, ulong time);
    void ConvertSelection(ulong selection, ulong target, ulong property, ulong requestor, ulong time);
    X11PropertyData GetWindowProperty(ulong window, ulong property, bool delete);
    void ChangePropertyBytes(ulong window, ulong property, ulong type, int format, ReadOnlySpan<byte> data);
    void ChangePropertyWords32(ulong window, ulong property, ulong type, ReadOnlySpan<ulong> words);
    void DeleteProperty(ulong window, ulong property);
    void SelectPropertyNotify(ulong window);
    void SendSelectionNotify(ulong requestor, ulong selection, ulong target, ulong property, ulong time);
    void Flush();
    X11Event? NextEvent(int timeoutMs);
}

public sealed class X11Clipboard : IDisposable
{
    public const int MaxDirectPropertyBytes = 256 * 1024;
    const int OutboundIncrChunkBytes = 64 * 1024;

    readonly IX11Api api;
    readonly TimeSpan incrTimeout;
    readonly Func<long> clockMs;
    readonly ConcurrentQueue<Action> commands = new();
    readonly CancellationTokenSource cancel = new();
    readonly Thread worker;

    string ownedText = "";
    byte[] ownedUtf8 = [];
    bool ownsSelection;

    PendingIncrRead? pendingRead;
    readonly List<OutboundIncrWrite> outboundWrites = [];
    ulong activeReadSerial;
    ulong lastTimestamp;
    int disposed;

    internal X11Clipboard(IX11Api api, TimeSpan? incrTimeout = null, Func<long>? clockMs = null, bool startWorker = true)
    {
        this.api = api;
        this.incrTimeout = incrTimeout ?? TimeSpan.FromSeconds(2);
        this.clockMs = clockMs ?? (() => Environment.TickCount64);
        worker = new Thread(RunWorker)
        {
            IsBackground = true,
            Name = "contextall-x11-clipboard",
        };
        if (startWorker)
        {
            worker.Start();
        }
    }

    public event Action<string>? PlainText;

    public bool IsOpen => Volatile.Read(ref disposed) == 0;

    public static X11Clipboard? TryOpen(string? display)
    {
        if (string.IsNullOrEmpty(display))
        {
            return null;
        }

        var api = NativeLibraryX11Api.TryConnect(display);
        if (api is null)
        {
            return null;
        }

        return new X11Clipboard(api);
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > FrameLimits.MaxClipboardBytes)
        {
            return;
        }

        commands.Enqueue(() => ApplyLocalSetText(text, bytes));
    }

    internal void PumpOnceForTest()
    {
        DrainCommands();
        var ev = api.NextEvent(0);
        if (ev is not null)
        {
            HandleEvent(ev);
        }

        CleanupExpired();
        api.Flush();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        cancel.Cancel();
        if (worker.IsAlive && Thread.CurrentThread != worker)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }

        api.Dispose();
        cancel.Dispose();
    }

    void RunWorker()
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                DrainCommands();
                var ev = api.NextEvent(25);
                if (ev is not null)
                {
                    HandleEvent(ev);
                }

                CleanupExpired();
                api.Flush();
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                CleanupExpired();
            }
        }
    }

    void DrainCommands()
    {
        while (commands.TryDequeue(out var action))
        {
            action();
        }
    }

    void ApplyLocalSetText(string text, byte[] bytes)
    {
        ownedText = text;
        ownedUtf8 = bytes;
        ownsSelection = true;
        outboundWrites.Clear();
        api.SetSelectionOwner(api.ClipboardAtom, api.Window, lastTimestamp);
        api.Flush();
    }

    void HandleEvent(X11Event ev)
    {
        switch (ev)
        {
            case X11SelectionOwnerChanged changed when changed.Selection == api.ClipboardAtom:
                lastTimestamp = changed.Timestamp != 0 ? changed.Timestamp : lastTimestamp;
                if (changed.Owner == api.Window || changed.Owner == 0)
                {
                    return;
                }

                StartSelectionRead(changed.Timestamp);
                break;

            case X11SelectionNotify notify when notify.Requestor == api.Window && notify.Selection == api.ClipboardAtom:
                HandleSelectionNotify(notify);
                break;

            case X11PropertyNotify prop when !prop.Deleted && prop.Window == api.Window && prop.Atom == api.PropertyAtom:
                HandleIncrReadChunk(prop.Time);
                break;

            case X11PropertyNotify prop when prop.Deleted:
                HandleOutboundIncrDelete(prop.Window, prop.Atom);
                break;

            case X11SelectionRequest req when req.Selection == api.ClipboardAtom:
                HandleSelectionRequest(req);
                break;

            case X11SelectionClear clear when clear.Selection == api.ClipboardAtom:
                ownsSelection = false;
                outboundWrites.Clear();
                break;
        }
    }

    void StartSelectionRead(ulong timestamp)
    {
        activeReadSerial++;
        pendingRead = null;
        api.DeleteProperty(api.Window, api.PropertyAtom);
        api.ConvertSelection(api.ClipboardAtom, api.TargetsAtom, api.PropertyAtom, api.Window, timestamp);
    }

    void HandleSelectionNotify(X11SelectionNotify notify)
    {
        if (notify.Property == 0)
        {
            if (notify.Target == api.TargetsAtom)
            {
                api.ConvertSelection(api.ClipboardAtom, api.Utf8StringAtom, api.PropertyAtom, api.Window, notify.Time);
            }

            return;
        }

        if (notify.Target == api.TargetsAtom)
        {
            var targets = api.GetWindowProperty(api.Window, notify.Property, delete: true);
            if (!targets.Ok)
            {
                return;
            }

            var chosen = PickPlainTarget(targets.Words32);
            if (chosen == 0)
            {
                return;
            }

            api.ConvertSelection(api.ClipboardAtom, chosen, api.PropertyAtom, api.Window, notify.Time);
            return;
        }

        if (!IsSupportedTextAtom(notify.Target))
        {
            api.DeleteProperty(api.Window, notify.Property);
            return;
        }

        var data = api.GetWindowProperty(api.Window, notify.Property, delete: true);
        if (!data.Ok)
        {
            return;
        }

        if (data.ActualType == api.IncrAtom)
        {
            if (data.Words32.Length > 0 && data.Words32[0] > FrameLimits.MaxClipboardBytes)
            {
                return;
            }

            pendingRead = new PendingIncrRead(notify.Target, clockMs() + (long)incrTimeout.TotalMilliseconds);
            return;
        }

        if (data.Bytes.Length > FrameLimits.MaxClipboardBytes)
        {
            return;
        }

        EmitDecoded(notify.Target, data.Bytes);
    }

    void HandleIncrReadChunk(ulong timestamp)
    {
        var current = pendingRead;
        if (current is null)
        {
            return;
        }

        var chunk = api.GetWindowProperty(api.Window, api.PropertyAtom, delete: true);
        if (!chunk.Ok)
        {
            pendingRead = null;
            return;
        }

        if (chunk.Bytes.Length == 0)
        {
            pendingRead = null;
            EmitDecoded(current.TargetAtom, current.Buffer.ToArray());
            return;
        }

        if (current.Buffer.Count + chunk.Bytes.Length > FrameLimits.MaxClipboardBytes)
        {
            pendingRead = null;
            return;
        }

        current.Buffer.AddRange(chunk.Bytes);
        current.DeadlineMs = clockMs() + (long)incrTimeout.TotalMilliseconds;
        _ = timestamp;
    }

    void HandleSelectionRequest(X11SelectionRequest req)
    {
        if (!ownsSelection || req.Owner != api.Window)
        {
            api.SendSelectionNotify(req.Requestor, req.Selection, req.Target, 0, req.Time);
            return;
        }

        var targetProp = req.Property != 0 ? req.Property : req.Target;
        if (req.Target == api.TargetsAtom)
        {
            ReadOnlySpan<ulong> supported =
            [
                api.TargetsAtom,
                api.Utf8StringAtom,
                api.TextPlainUtf8Atom,
                api.TextPlainAtom,
                api.TextAtom,
                api.StringAtom,
            ];
            api.ChangePropertyWords32(req.Requestor, targetProp, api.AtomTypeAtom, supported);
            api.SendSelectionNotify(req.Requestor, req.Selection, req.Target, targetProp, req.Time);
            return;
        }

        if (!IsSupportedTextAtom(req.Target))
        {
            api.SendSelectionNotify(req.Requestor, req.Selection, req.Target, 0, req.Time);
            return;
        }

        var payload = req.Target == api.StringAtom
            ? Encoding.Latin1.GetBytes(ownedText)
            : ownedUtf8;

        if (payload.Length <= MaxDirectPropertyBytes)
        {
            api.ChangePropertyBytes(req.Requestor, targetProp, req.Target, 8, payload);
            api.SendSelectionNotify(req.Requestor, req.Selection, req.Target, targetProp, req.Time);
            return;
        }

        outboundWrites.RemoveAll(w => w.Requestor == req.Requestor && w.Property == targetProp);
        api.SelectPropertyNotify(req.Requestor);
        ReadOnlySpan<ulong> sizeWord = [(ulong)payload.Length];
        api.ChangePropertyWords32(req.Requestor, targetProp, api.IncrAtom, sizeWord);
        outboundWrites.Add(new OutboundIncrWrite(
            req.Requestor,
            targetProp,
            req.Target,
            payload,
            clockMs() + (long)incrTimeout.TotalMilliseconds));
        api.SendSelectionNotify(req.Requestor, req.Selection, req.Target, targetProp, req.Time);
    }

    void HandleOutboundIncrDelete(ulong requestor, ulong property)
    {
        for (var i = 0; i < outboundWrites.Count; i++)
        {
            var write = outboundWrites[i];
            if (write.Requestor != requestor || write.Property != property)
            {
                continue;
            }

            var remaining = write.Payload.Length - write.Offset;
            if (remaining <= 0)
            {
                api.ChangePropertyBytes(write.Requestor, write.Property, write.TargetAtom, 8, ReadOnlySpan<byte>.Empty);
                outboundWrites.RemoveAt(i);
                return;
            }

            var take = Math.Min(OutboundIncrChunkBytes, remaining);
            api.ChangePropertyBytes(
                write.Requestor,
                write.Property,
                write.TargetAtom,
                8,
                write.Payload.AsSpan(write.Offset, take));
            write.Offset += take;
            write.DeadlineMs = clockMs() + (long)incrTimeout.TotalMilliseconds;
            return;
        }
    }

    void CleanupExpired()
    {
        var now = clockMs();
        if (pendingRead is not null && now >= pendingRead.DeadlineMs)
        {
            pendingRead = null;
            api.DeleteProperty(api.Window, api.PropertyAtom);
        }

        for (var i = outboundWrites.Count - 1; i >= 0; i--)
        {
            if (now >= outboundWrites[i].DeadlineMs)
            {
                outboundWrites.RemoveAt(i);
            }
        }
    }

    ulong PickPlainTarget(ulong[] atoms)
    {
        ReadOnlySpan<ulong> order =
        [
            api.Utf8StringAtom,
            api.TextPlainUtf8Atom,
            api.TextPlainAtom,
            api.TextAtom,
            api.StringAtom,
        ];
        foreach (var candidate in order)
        {
            if (candidate != 0 && atoms.Contains(candidate))
            {
                return candidate;
            }
        }

        return 0;
    }

    bool IsSupportedTextAtom(ulong atom) =>
        atom != 0 &&
        (atom == api.Utf8StringAtom
         || atom == api.TextPlainUtf8Atom
         || atom == api.TextPlainAtom
         || atom == api.TextAtom
         || atom == api.StringAtom);

    void EmitDecoded(ulong targetAtom, byte[] bytes)
    {
        string text;
        if (targetAtom == api.StringAtom)
        {
            text = Encoding.Latin1.GetString(bytes);
        }
        else
        {
            try
            {
                text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return;
            }
        }

        PlainText?.Invoke(text);
    }

    sealed class PendingIncrRead(ulong targetAtom, long deadlineMs)
    {
        public ulong TargetAtom { get; } = targetAtom;
        public long DeadlineMs { get; set; } = deadlineMs;
        public List<byte> Buffer { get; } = [];
    }

    sealed class OutboundIncrWrite(ulong requestor, ulong property, ulong targetAtom, byte[] payload, long deadlineMs)
    {
        public ulong Requestor { get; } = requestor;
        public ulong Property { get; } = property;
        public ulong TargetAtom { get; } = targetAtom;
        public byte[] Payload { get; } = payload;
        public int Offset { get; set; }
        public long DeadlineMs { get; set; } = deadlineMs;
    }
}

internal sealed unsafe class NativeLibraryX11Api : IX11Api
{
    const ulong AtomTypeConst = 4;
    const nint PropertyChangeMask = 1 << 22;
    const ulong XFixesSelectionMask = 7;

    readonly nint x11Lib;
    readonly nint xfixesLib;
    readonly nint display;
    readonly int fixesEventBase;

    readonly delegate* unmanaged[Cdecl]<nint, int> fnXCloseDisplay;
    readonly delegate* unmanaged[Cdecl]<nint, ulong, int, int, uint, uint, uint, ulong, ulong, ulong> fnXCreateSimpleWindow;
    readonly delegate* unmanaged[Cdecl]<nint, ulong, int> fnXDestroyWindow;
    readonly delegate* unmanaged[Cdecl]<nint, byte*, int, ulong> fnXInternAtom;
    readonly delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, int> fnXSetSelectionOwner;
    readonly delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, ulong, ulong, int> fnXConvertSelection;
    readonly delegate* unmanaged[Cdecl]<nint, ulong, ulong, nint, nint, int, ulong, ulong*, int*, nuint*, nuint*, byte**, int> fnXGetWindowProperty;
    readonly delegate* unmanaged[Cdecl]<byte*, int> fnXFree;
    readonly delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, int, int, byte*, int, int> fnXChangeProperty;
    readonly delegate* unmanaged[Cdecl]<nint, ulong, ulong, int> fnXDeleteProperty;
    readonly delegate* unmanaged[Cdecl]<nint, ulong, nint, int> fnXSelectInput;
    readonly delegate* unmanaged[Cdecl]<nint, ulong, int, nint, void*, int> fnXSendEvent;
    readonly delegate* unmanaged[Cdecl]<nint, int> fnXFlush;
    readonly delegate* unmanaged[Cdecl]<nint, int> fnXPending;
    readonly delegate* unmanaged[Cdecl]<nint, void*, int> fnXNextEvent;

    NativeLibraryX11Api(
        nint x11Lib,
        nint xfixesLib,
        nint display,
        int fixesEventBase,
        ulong window,
        delegate* unmanaged[Cdecl]<nint, int> fnXCloseDisplay,
        delegate* unmanaged[Cdecl]<nint, ulong, int, int, uint, uint, uint, ulong, ulong, ulong> fnXCreateSimpleWindow,
        delegate* unmanaged[Cdecl]<nint, ulong, int> fnXDestroyWindow,
        delegate* unmanaged[Cdecl]<nint, byte*, int, ulong> fnXInternAtom,
        delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, int> fnXSetSelectionOwner,
        delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, ulong, ulong, int> fnXConvertSelection,
        delegate* unmanaged[Cdecl]<nint, ulong, ulong, nint, nint, int, ulong, ulong*, int*, nuint*, nuint*, byte**, int> fnXGetWindowProperty,
        delegate* unmanaged[Cdecl]<byte*, int> fnXFree,
        delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, int, int, byte*, int, int> fnXChangeProperty,
        delegate* unmanaged[Cdecl]<nint, ulong, ulong, int> fnXDeleteProperty,
        delegate* unmanaged[Cdecl]<nint, ulong, nint, int> fnXSelectInput,
        delegate* unmanaged[Cdecl]<nint, ulong, int, nint, void*, int> fnXSendEvent,
        delegate* unmanaged[Cdecl]<nint, int> fnXFlush,
        delegate* unmanaged[Cdecl]<nint, int> fnXPending,
        delegate* unmanaged[Cdecl]<nint, void*, int> fnXNextEvent)
    {
        this.x11Lib = x11Lib;
        this.xfixesLib = xfixesLib;
        this.display = display;
        this.fixesEventBase = fixesEventBase;
        Window = window;
        this.fnXCloseDisplay = fnXCloseDisplay;
        this.fnXCreateSimpleWindow = fnXCreateSimpleWindow;
        this.fnXDestroyWindow = fnXDestroyWindow;
        this.fnXInternAtom = fnXInternAtom;
        this.fnXSetSelectionOwner = fnXSetSelectionOwner;
        this.fnXConvertSelection = fnXConvertSelection;
        this.fnXGetWindowProperty = fnXGetWindowProperty;
        this.fnXFree = fnXFree;
        this.fnXChangeProperty = fnXChangeProperty;
        this.fnXDeleteProperty = fnXDeleteProperty;
        this.fnXSelectInput = fnXSelectInput;
        this.fnXSendEvent = fnXSendEvent;
        this.fnXFlush = fnXFlush;
        this.fnXPending = fnXPending;
        this.fnXNextEvent = fnXNextEvent;

        ClipboardAtom = Intern("CLIPBOARD");
        TargetsAtom = Intern("TARGETS");
        IncrAtom = Intern("INCR");
        Utf8StringAtom = Intern("UTF8_STRING");
        TextPlainUtf8Atom = Intern("text/plain;charset=utf-8");
        TextPlainAtom = Intern("text/plain");
        TextAtom = Intern("TEXT");
        StringAtom = 31;
        AtomTypeAtom = AtomTypeConst;
        PropertyAtom = Intern("CONTEXTALL_CLIPBOARD_PROP");
    }

    public ulong Window { get; }
    public ulong ClipboardAtom { get; }
    public ulong TargetsAtom { get; }
    public ulong IncrAtom { get; }
    public ulong Utf8StringAtom { get; }
    public ulong TextPlainUtf8Atom { get; }
    public ulong TextPlainAtom { get; }
    public ulong TextAtom { get; }
    public ulong StringAtom { get; }
    public ulong AtomTypeAtom { get; }
    public ulong PropertyAtom { get; }

    public static NativeLibraryX11Api? TryConnect(string displayName)
    {
        if (!NativeLibrary.TryLoad("libX11.so.6", out var x11Lib))
        {
            return null;
        }

        if (!NativeLibrary.TryLoad("libXfixes.so.3", out var xfixesLib))
        {
            NativeLibrary.Free(x11Lib);
            return null;
        }

        if (!TryExport(x11Lib, "XOpenDisplay", out nint pOpenDisplay)
            || !TryExport(x11Lib, "XCloseDisplay", out nint pCloseDisplay)
            || !TryExport(x11Lib, "XDefaultRootWindow", out nint pDefaultRoot)
            || !TryExport(x11Lib, "XCreateSimpleWindow", out nint pCreateWin)
            || !TryExport(x11Lib, "XDestroyWindow", out nint pDestroyWin)
            || !TryExport(x11Lib, "XInternAtom", out nint pInternAtom)
            || !TryExport(x11Lib, "XSetSelectionOwner", out nint pSetOwner)
            || !TryExport(x11Lib, "XConvertSelection", out nint pConvertSel)
            || !TryExport(x11Lib, "XGetWindowProperty", out nint pGetProp)
            || !TryExport(x11Lib, "XFree", out nint pXFree)
            || !TryExport(x11Lib, "XChangeProperty", out nint pChangeProp)
            || !TryExport(x11Lib, "XDeleteProperty", out nint pDeleteProp)
            || !TryExport(x11Lib, "XSelectInput", out nint pSelectInput)
            || !TryExport(x11Lib, "XSendEvent", out nint pSendEvent)
            || !TryExport(x11Lib, "XFlush", out nint pFlush)
            || !TryExport(x11Lib, "XPending", out nint pPending)
            || !TryExport(x11Lib, "XNextEvent", out nint pNextEvent)
            || !TryExport(xfixesLib, "XFixesQueryExtension", out nint pFixesQuery)
            || !TryExport(xfixesLib, "XFixesSelectSelectionInput", out nint pFixesSelect))
        {
            NativeLibrary.Free(xfixesLib);
            NativeLibrary.Free(x11Lib);
            return null;
        }

        if (NativeLibrary.TryGetExport(x11Lib, "XInitThreads", out var pInitThreads))
        {
            _ = ((delegate* unmanaged[Cdecl]<int>)pInitThreads)();
        }

        var fnOpen = (delegate* unmanaged[Cdecl]<byte*, nint>)pOpenDisplay;
        var fnClose = (delegate* unmanaged[Cdecl]<nint, int>)pCloseDisplay;
        var fnDefaultRoot = (delegate* unmanaged[Cdecl]<nint, ulong>)pDefaultRoot;
        var fnCreateWin = (delegate* unmanaged[Cdecl]<nint, ulong, int, int, uint, uint, uint, ulong, ulong, ulong>)pCreateWin;
        var fnDestroyWin = (delegate* unmanaged[Cdecl]<nint, ulong, int>)pDestroyWin;
        var fnIntern = (delegate* unmanaged[Cdecl]<nint, byte*, int, ulong>)pInternAtom;
        var fnFixesQuery = (delegate* unmanaged[Cdecl]<nint, int*, int*, int>)pFixesQuery;
        var fnFixesSelect = (delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, void>)pFixesSelect;

        var utf8Display = Encoding.UTF8.GetBytes(displayName + "\0");
        nint dpy;
        fixed (byte* pName = utf8Display)
        {
            dpy = fnOpen(pName);
        }

        if (dpy == 0)
        {
            NativeLibrary.Free(xfixesLib);
            NativeLibrary.Free(x11Lib);
            return null;
        }

        var fixesEventBase = 0;
        var fixesErrorBase = 0;
        if (fnFixesQuery(dpy, &fixesEventBase, &fixesErrorBase) == 0)
        {
            _ = fnClose(dpy);
            NativeLibrary.Free(xfixesLib);
            NativeLibrary.Free(x11Lib);
            return null;
        }

        var root = fnDefaultRoot(dpy);
        var win = fnCreateWin(dpy, root, 0, 0, 1, 1, 0, 0, 0);
        if (win == 0)
        {
            _ = fnClose(dpy);
            NativeLibrary.Free(xfixesLib);
            NativeLibrary.Free(x11Lib);
            return null;
        }

        var fnSelectInput = (delegate* unmanaged[Cdecl]<nint, ulong, nint, int>)pSelectInput;
        _ = fnSelectInput(dpy, win, PropertyChangeMask);

        var clipName = "CLIPBOARD\0"u8;
        ulong clipAtom;
        fixed (byte* pClip = clipName)
        {
            clipAtom = fnIntern(dpy, pClip, 0);
        }

        fnFixesSelect(dpy, win, clipAtom, XFixesSelectionMask);
        _ = ((delegate* unmanaged[Cdecl]<nint, int>)pFlush)(dpy);

        return new NativeLibraryX11Api(
            x11Lib,
            xfixesLib,
            dpy,
            fixesEventBase,
            win,
            fnClose,
            fnCreateWin,
            fnDestroyWin,
            fnIntern,
            (delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, int>)pSetOwner,
            (delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, ulong, ulong, int>)pConvertSel,
            (delegate* unmanaged[Cdecl]<nint, ulong, ulong, nint, nint, int, ulong, ulong*, int*, nuint*, nuint*, byte**, int>)pGetProp,
            (delegate* unmanaged[Cdecl]<byte*, int>)pXFree,
            (delegate* unmanaged[Cdecl]<nint, ulong, ulong, ulong, int, int, byte*, int, int>)pChangeProp,
            (delegate* unmanaged[Cdecl]<nint, ulong, ulong, int>)pDeleteProp,
            fnSelectInput,
            (delegate* unmanaged[Cdecl]<nint, ulong, int, nint, void*, int>)pSendEvent,
            (delegate* unmanaged[Cdecl]<nint, int>)pFlush,
            (delegate* unmanaged[Cdecl]<nint, int>)pPending,
            (delegate* unmanaged[Cdecl]<nint, void*, int>)pNextEvent);
    }

    public void SetSelectionOwner(ulong selection, ulong owner, ulong time) =>
        _ = fnXSetSelectionOwner(display, selection, owner, time);

    public void ConvertSelection(ulong selection, ulong target, ulong property, ulong requestor, ulong time) =>
        _ = fnXConvertSelection(display, selection, target, property, requestor, time);

    public X11PropertyData GetWindowProperty(ulong window, ulong property, bool delete)
    {
        ulong actualType = 0;
        var actualFormat = 0;
        nuint nitems = 0;
        nuint bytesAfter = 0;
        byte* propPtr = null;

        var status = fnXGetWindowProperty(
            display,
            window,
            property,
            0,
            FrameLimits.MaxClipboardBytes / 4 + 64,
            delete ? 1 : 0,
            0,
            &actualType,
            &actualFormat,
            &nitems,
            &bytesAfter,
            &propPtr);

        try
        {
            if (status != 0 || actualFormat == 0 || propPtr == null)
            {
                return new X11PropertyData(status == 0 && actualType != 0, actualType, actualFormat, [], [], bytesAfter);
            }

            if (actualFormat == 32)
            {
                var count = (int)nitems;
                var words = new ulong[count];
                var nativeLongs = (nuint*)propPtr;
                for (var i = 0; i < count; i++)
                {
                    words[i] = nativeLongs[i];
                }

                return new X11PropertyData(true, actualType, actualFormat, [], words, bytesAfter);
            }

            if (actualFormat == 8)
            {
                var count = (int)nitems;
                var bytes = new byte[count];
                new ReadOnlySpan<byte>(propPtr, count).CopyTo(bytes);
                return new X11PropertyData(true, actualType, actualFormat, bytes, [], bytesAfter);
            }

            return new X11PropertyData(true, actualType, actualFormat, [], [], bytesAfter);
        }
        finally
        {
            if (propPtr != null)
            {
                _ = fnXFree(propPtr);
            }
        }
    }

    public void ChangePropertyBytes(ulong window, ulong property, ulong type, int format, ReadOnlySpan<byte> data)
    {
        fixed (byte* ptr = data)
        {
            _ = fnXChangeProperty(display, window, property, type, format, 0, ptr, data.Length);
        }
    }

    public void ChangePropertyWords32(ulong window, ulong property, ulong type, ReadOnlySpan<ulong> words)
    {
        Span<nuint> nativeWords = words.Length <= 16
            ? stackalloc nuint[words.Length]
            : new nuint[words.Length];
        for (var i = 0; i < words.Length; i++)
        {
            nativeWords[i] = (nuint)words[i];
        }

        fixed (nuint* ptr = nativeWords)
        {
            _ = fnXChangeProperty(display, window, property, type, 32, 0, (byte*)ptr, nativeWords.Length);
        }
    }

    public void DeleteProperty(ulong window, ulong property) =>
        _ = fnXDeleteProperty(display, window, property);

    public void SelectPropertyNotify(ulong window) =>
        _ = fnXSelectInput(display, window, PropertyChangeMask);

    public void SendSelectionNotify(ulong requestor, ulong selection, ulong target, ulong property, ulong time)
    {
        var ev = new XSelectionEventNative
        {
            Type = 31,
            SendEvent = 1,
            Display = display,
            Requestor = (nuint)requestor,
            Selection = (nuint)selection,
            Target = (nuint)target,
            Property = (nuint)property,
            Time = (nuint)time,
        };
        _ = fnXSendEvent(display, requestor, 0, 0, &ev);
    }

    public void Flush() => _ = fnXFlush(display);

    public X11Event? NextEvent(int timeoutMs)
    {
        if (fnXPending(display) <= 0)
        {
            if (timeoutMs <= 0)
            {
                return null;
            }

            Thread.Sleep(timeoutMs);
            if (fnXPending(display) <= 0)
            {
                return null;
            }
        }

        XEventBuffer buf = default;
        _ = fnXNextEvent(display, &buf);
        return DecodeEvent(&buf);
    }

    public void Dispose()
    {
        if (Window != 0)
        {
            _ = fnXDestroyWindow(display, Window);
        }

        if (display != 0)
        {
            _ = fnXCloseDisplay(display);
        }

        NativeLibrary.Free(xfixesLib);
        NativeLibrary.Free(x11Lib);
    }

    ulong Intern(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* ptr = bytes)
        {
            return fnXInternAtom(display, ptr, 0);
        }
    }

    X11Event? DecodeEvent(XEventBuffer* buf)
    {
        var type = *(int*)buf;
        if (type == fixesEventBase)
        {
            var xfixes = *(XFixesSelectionNotifyEventNative*)buf;
            if (xfixes.Subtype == 0)
            {
                return new X11SelectionOwnerChanged(xfixes.Selection, xfixes.Owner, xfixes.Timestamp);
            }

            return null;
        }

        return type switch
        {
            28 => DecodeProperty((XPropertyEventNative*)buf),
            29 => DecodeClear((XSelectionClearEventNative*)buf),
            30 => DecodeRequest((XSelectionRequestEventNative*)buf),
            31 => DecodeNotify((XSelectionEventNative*)buf),
            _ => null,
        };
    }

    static X11Event DecodeProperty(XPropertyEventNative* ev) =>
        new X11PropertyNotify(ev->Window, ev->Atom, ev->Time, ev->State == 1);

    static X11Event DecodeClear(XSelectionClearEventNative* ev) =>
        new X11SelectionClear(ev->Window, ev->Selection, ev->Time);

    static X11Event DecodeRequest(XSelectionRequestEventNative* ev) =>
        new X11SelectionRequest(ev->Owner, ev->Requestor, ev->Selection, ev->Target, ev->Property, ev->Time);

    static X11Event DecodeNotify(XSelectionEventNative* ev) =>
        new X11SelectionNotify(ev->Requestor, ev->Selection, ev->Target, ev->Property, ev->Time);

    static bool TryExport(nint lib, string name, out nint address) =>
        NativeLibrary.TryGetExport(lib, name, out address);

    [InlineArray(24)]
    struct XEventBuffer
    {
        ulong element0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XFixesSelectionNotifyEventNative
    {
        public int Type;
        public ulong Serial;
        public int SendEvent;
        public nint Display;
        public nuint Window;
        public int Subtype;
        public nuint Owner;
        public nuint Selection;
        public nuint Timestamp;
        public nuint SelectionTimestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XPropertyEventNative
    {
        public int Type;
        public ulong Serial;
        public int SendEvent;
        public nint Display;
        public nuint Window;
        public nuint Atom;
        public nuint Time;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XSelectionClearEventNative
    {
        public int Type;
        public ulong Serial;
        public int SendEvent;
        public nint Display;
        public nuint Window;
        public nuint Selection;
        public nuint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XSelectionRequestEventNative
    {
        public int Type;
        public ulong Serial;
        public int SendEvent;
        public nint Display;
        public nuint Owner;
        public nuint Requestor;
        public nuint Selection;
        public nuint Target;
        public nuint Property;
        public nuint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XSelectionEventNative
    {
        public int Type;
        public ulong Serial;
        public int SendEvent;
        public nint Display;
        public nuint Requestor;
        public nuint Selection;
        public nuint Target;
        public nuint Property;
        public nuint Time;
    }
}

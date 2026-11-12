using BetterYYSLS.Core.Map;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BetterYYSLS.Core.Godlike;

/// <summary>
/// Godlike 实时坐标。
/// </summary>
public sealed record GodlikeGameLocation(
    float WorldX,
    float WorldY,
    float WorldZ,
    string SubType,
    MapPoint MapPosition);

/// <summary>
/// 从 yysls.exe 读取玩家世界坐标，并按当前地图区域投影。
/// </summary>
public sealed class GodlikeGameLocationService : IDisposable
{
    private const int ProcessVmRead = 0x0010;
    private const int ProcessQueryInformation = 0x0400;
    private const string ProcessName = "yysls";

    // 2026-10-30 官方 H72_MAP 版本的回退偏移，接口不可用时仍可尝试读取。
    private const ulong FallbackOffsetX = 0x77E485C;
    private const ulong FallbackOffsetY = 0x77E4854;
    private const ulong FallbackOffsetZ = 0x77E4858;

    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private string _subType = "1";

    /// <summary>
    /// 坐标更新事件。
    /// </summary>
    public event EventHandler<GodlikeGameLocation>? LocationChanged;

    /// <summary>
    /// 状态更新事件。
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// 是否正在读取。
    /// </summary>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>
    /// 当前地图 subType。
    /// </summary>
    public string SubType
    {
        get => _subType;
        set => _subType = string.IsNullOrWhiteSpace(value) ? "1" : value;
    }

    /// <summary>
    /// 启动实时坐标读取。
    /// </summary>
    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    /// <summary>
    /// 停止实时坐标读取。
    /// </summary>
    public void Stop()
    {
        lock (_sync)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
        }
    }

    /// <summary>
    /// 释放服务。
    /// </summary>
    public void Dispose()
    {
        Stop();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var credentials = TryLoadCredentials();
        var meta = await TryLoadClientMetaAsync(credentials, cancellationToken);
        var offsetX = meta?.OffsetX ?? FallbackOffsetX;
        var offsetY = meta?.OffsetY ?? FallbackOffsetY;
        var offsetZ = meta?.OffsetZ ?? FallbackOffsetZ;

        // 朝向由相邻坐标差计算，采样间隔越短箭头响应越及时。
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var process = FindProcess();
                if (process is null)
                {
                    StatusChanged?.Invoke(this, "等待游戏进程 yysls.exe");
                }
                else if (TryReadLocation(process, offsetX, offsetY, offsetZ, out var worldX, out var worldY, out var worldZ))
                {
                    var location = new GodlikeGameLocation(
                        worldX, worldY, worldZ, SubType,
                        GodlikeProjection.WorldToMap(SubType, worldX, worldY));
                    LocationChanged?.Invoke(this, location);
                    StatusChanged?.Invoke(this, $"实时坐标 {worldX:F0}, {worldY:F0}, {worldZ:F0}");
                }
                else
                {
                    StatusChanged?.Invoke(this, "游戏进程已找到，但坐标偏移暂不可读");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"实时坐标读取失败：{ex.Message}");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static GodlikeCredentials? TryLoadCredentials()
    {
        try
        {
            return GodlikeCredentials.Load();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<GodlikeClientMeta?> TryLoadClientMetaAsync(
        GodlikeCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (credentials is null)
        {
            return null;
        }

        try
        {
            return await GodlikeMapApi.GetClientMetaAsync(credentials, cancellationToken);
        }
        catch
        {
            // 原生签名接口不可用时使用内置回退偏移。
            return null;
        }
    }

    private static Process? FindProcess()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        if (processes.Length == 0)
        {
            return null;
        }

        for (var index = 1; index < processes.Length; index++)
        {
            processes[index].Dispose();
        }

        return processes[0];
    }

    private static bool TryReadLocation(
        Process process,
        ulong offsetX,
        ulong offsetY,
        ulong offsetZ,
        out float x,
        out float y,
        out float z)
    {
        x = y = z = 0;
        var handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, process.Id);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var moduleBase = TryGetModuleBase(process);
            // client-meta 的偏移是游戏模块内偏移；绝对地址仅作为兼容回退。
            if (TryReadAtBase(handle, moduleBase, offsetX, offsetY, offsetZ, out x, out y, out z))
            {
                return true;
            }

            return TryReadAtBase(handle, 0, offsetX, offsetY, offsetZ, out x, out y, out z);
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private static bool TryReadAtBase(
        IntPtr processHandle,
        nint moduleBase,
        ulong offsetX,
        ulong offsetY,
        ulong offsetZ,
        out float x,
        out float y,
        out float z)
    {
        x = y = z = 0;
        if (!TryReadFloat(processHandle, moduleBase + (nint)offsetX, out x) ||
            !TryReadFloat(processHandle, moduleBase + (nint)offsetY, out y) ||
            !TryReadFloat(processHandle, moduleBase + (nint)offsetZ, out z))
        {
            return false;
        }

        return IsPlausible(x) && IsPlausible(y) && IsPlausible(z);
    }

    private static nint TryGetModuleBase(Process process)
    {
        try
        {
            return process.MainModule?.BaseAddress ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryReadFloat(IntPtr processHandle, nint address, out float value)
    {
        value = 0;
        if (address == 0)
        {
            return false;
        }

        var buffer = new byte[4];
        if (!ReadProcessMemory(processHandle, address, buffer, buffer.Length, out var read) ||
            read != buffer.Length)
        {
            return false;
        }

        value = BitConverter.ToSingle(buffer, 0);
        return float.IsFinite(value);
    }

    private static bool IsPlausible(float value)
    {
        return value is > -32768f and < 32768f && Math.Abs(value) > 0.0001f;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr processHandle,
        nint baseAddress,
        byte[] buffer,
        int size,
        out nint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

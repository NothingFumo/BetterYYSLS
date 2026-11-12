using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BetterYYSLS.Core.Godlike;

/// <summary>
/// 官方 Godlike 登录态的快照。
/// </summary>
public sealed record GodlikeSessionState(
    bool IsAuthenticated,
    string Uid,
    string MapId,
    string Message);

/// <summary>
/// 监听官方客户端登录缓存并刷新 GL 会话。
/// 官方账号登录仍由 Godlike 客户端完成，本服务负责自动发现新 Token。
/// </summary>
public sealed class GodlikeSessionService : IDisposable
{
    private readonly object _sync = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private bool _refreshing;

    /// <summary>
    /// 登录态变化事件。
    /// </summary>
    public event EventHandler<GodlikeSessionState>? StateChanged;

    /// <summary>
    /// 当前登录态。
    /// </summary>
    public GodlikeSessionState State { get; private set; } =
        new(false, string.Empty, string.Empty, "未检测 Godlike 登录态");

    /// <summary>
    /// 刷新登录态并校验当前地图接口。
    /// </summary>
    public async Task<GodlikeSessionState> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_refreshing)
        {
            return State;
        }

        _refreshing = true;
        try
        {
            var credentials = GodlikeCredentials.Load();
            var mapId = await GodlikeMapApi.GetCurrentMapIdAsync(credentials, cancellationToken);
            var state = new GodlikeSessionState(
                true,
                credentials.Uid,
                mapId ?? string.Empty,
                $"登录态有效 · UID {MaskUid(credentials.Uid)}");
            SetState(state);
            EnsureWatcher();
            return state;
        }
        catch (Exception ex)
        {
            var state = new GodlikeSessionState(false, string.Empty, string.Empty,
                $"登录态不可用：{ex.Message}");
            SetState(state);
            return state;
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// 开始监听官方客户端缓存；客户端登录成功并写回 Token 后自动刷新。
    /// </summary>
    public void StartWatching()
    {
        EnsureWatcher();
    }

    private void EnsureWatcher()
    {
        lock (_sync)
        {
            if (_watcher is not null)
            {
                return;
            }

            var path = GodlikeCredentials.FindCachePath();
            var directory = string.IsNullOrWhiteSpace(path)
                ? Path.GetDirectoryName(GodlikeCredentials.GetPrimaryCachePath())
                : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var watcher = new FileSystemWatcher(directory, "godlike_info.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnCacheChanged;
            watcher.Created += OnCacheChanged;
            watcher.Renamed += OnCacheChanged;
            _watcher = watcher;
        }
    }

    private void OnCacheChanged(object sender, FileSystemEventArgs e)
    {
        // 官方客户端可能在短时间内多次写入，防抖后统一刷新。
        lock (_sync)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(async _ =>
            {
                try
                {
                    await RefreshAsync();
                }
                catch
                {
                    // 刷新失败保留旧状态，下一次文件变化继续重试。
                }
            }, null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }
    }

    private void SetState(GodlikeSessionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private static string MaskUid(string uid)
    {
        return uid.Length <= 8 ? uid : $"{uid[..4]}...{uid[^4..]}";
    }

    /// <summary>
    /// 释放监听资源。
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            _watcher?.Dispose();
            _watcher = null;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}

using BetterYYSLS.Core.Input.Backends.Win32;
using System;
using System.Threading;

namespace BetterYYSLS.Core.Input;

/// <summary>
/// 模拟输入的统一入口。
/// 业务代码只通过这里取通道，不直接使用 Simulation / PostMessageSimulator。
/// <para>
/// 通道引用只能在单个任务内持有；跨任务存活的对象必须每次从这里取，
/// 否则后端切换后引用会过期。
/// </para>
/// </summary>
public static class InputHub
{
    private static readonly object AttachLock = new();

    /// <summary>
    /// 未 Attach 时使用未绑定窗口的 Win32 后端：前台可用，后台警告
    /// </summary>
    private static IInputBackend _backend = new Win32InputBackend(IntPtr.Zero);

    public static IInputBackend Backend => Volatile.Read(ref _backend);

    /// <summary>前台通道：SendInput</summary>
    public static IInputChannel Foreground => Backend.Foreground;

    /// <summary>后台通道：PostMessage</summary>
    public static IInputChannel Background => Backend.Background;

    /// <summary>
    /// 替换当前后端并释放旧后端。按键释放由停止流程显式处理。
    /// </summary>
    public static void Attach(IInputBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (AttachLock)
        {
            var old = Backend;
            if (ReferenceEquals(old, backend))
            {
                return;
            }

            Volatile.Write(ref _backend, backend);
            old.Dispose();
        }
    }

    /// <summary>释放当前后端所有通道中处于按下状态的按键</summary>
    public static void ReleaseAll() => Backend.ReleaseAll();
}

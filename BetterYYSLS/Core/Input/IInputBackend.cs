using System;

namespace BetterYYSLS.Core.Input;

/// <summary>
/// 一组可以整体切换的输入输出。当前只实现 Win32（前台 SendInput + 后台 PostMessage）。
/// </summary>
public interface IInputBackend : IDisposable
{
    /// <summary>后端类型</summary>
    InputBackendKind Kind { get; }

    /// <summary>前台通道</summary>
    IInputChannel Foreground { get; }

    /// <summary>后台通道</summary>
    IInputChannel Background { get; }

    /// <summary>释放所有通道中处于按下状态的按键</summary>
    void ReleaseAll();
}

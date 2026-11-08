namespace BetterYYSLS.Core.Input;

/// <summary>
/// 可整体切换的输入后端。
/// </summary>
public enum InputBackendKind
{
    /// <summary>Windows 键鼠：前台 SendInput + 后台 PostMessage</summary>
    Win32,
}

/// <summary>
/// 输入层内部使用的鼠标按键。
/// </summary>
public enum InputMouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

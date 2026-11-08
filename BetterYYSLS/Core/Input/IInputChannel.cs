using Vanara.PInvoke;

namespace BetterYYSLS.Core.Input;

/// <summary>
/// 输入通道：后端内部的一路输出（前台或后台）。
/// </summary>
public interface IInputChannel
{
    /// <summary>键盘输入</summary>
    IKeyboardInput Keyboard { get; }

    /// <summary>鼠标输入</summary>
    IMouseInput Mouse { get; }

    /// <summary>按键是否处于按下状态</summary>
    bool IsKeyDown(User32.VK key);

    /// <summary>等待指定毫秒</summary>
    IInputChannel Sleep(int ms);

    /// <summary>释放本通道中处于按下状态的按键</summary>
    void ReleaseAll();
}

using Vanara.PInvoke;

namespace BetterYYSLS.Core.Input;

/// <summary>
/// 键盘输入。
/// </summary>
public interface IKeyboardInput
{
    IKeyboardInput KeyDown(User32.VK key);

    IKeyboardInput KeyUp(User32.VK key);

    IKeyboardInput KeyPress(User32.VK key);

    IKeyboardInput Sleep(int ms);
}

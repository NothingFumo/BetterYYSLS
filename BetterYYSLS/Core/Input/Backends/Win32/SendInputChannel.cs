using BetterYYSLS.Core.Simulator;
using Fischless.WindowsInput;
using Vanara.PInvoke;

namespace BetterYYSLS.Core.Input.Backends.Win32;

/// <summary>
/// 前台通道：包装 Simulation.SendInput。
/// </summary>
internal sealed class SendInputChannel : InputChannelBase
{
    private static InputSimulator Input => Simulation.SendInput;

    protected override void OnKeyDown(User32.VK key) => Input.Keyboard.KeyDown(key);

    protected override void OnKeyUp(User32.VK key) => Input.Keyboard.KeyUp(key);

    protected override void OnKeyPress(User32.VK key) => Input.Keyboard.KeyPress(key);

    protected override void OnMouseButton(InputMouseButton button, bool down)
    {
        var mouse = Input.Mouse;
        switch (button)
        {
            case InputMouseButton.Left:
                _ = down ? mouse.LeftButtonDown() : mouse.LeftButtonUp();
                break;
            case InputMouseButton.Right:
                _ = down ? mouse.RightButtonDown() : mouse.RightButtonUp();
                break;
            case InputMouseButton.Middle:
                _ = down ? mouse.MiddleButtonDown() : mouse.MiddleButtonUp();
                break;
            case InputMouseButton.X1:
                _ = down ? mouse.XButtonDown(0x0001) : mouse.XButtonUp(0x0001);
                break;
            case InputMouseButton.X2:
                _ = down ? mouse.XButtonDown(0x0002) : mouse.XButtonUp(0x0002);
                break;
        }
    }

    protected override void OnMouseClick(InputMouseButton button)
    {
        var mouse = Input.Mouse;
        switch (button)
        {
            case InputMouseButton.Left:
                mouse.LeftButtonClick();
                break;
            case InputMouseButton.Right:
                mouse.RightButtonClick();
                break;
            case InputMouseButton.Middle:
                mouse.MiddleButtonClick();
                break;
            case InputMouseButton.X1:
                mouse.XButtonClick(0x0001);
                break;
            case InputMouseButton.X2:
                mouse.XButtonClick(0x0002);
                break;
        }
    }

    protected override void OnMoveBy(int dx, int dy) => Input.Mouse.MoveMouseBy(dx, dy);

    protected override void OnMoveTo(double absX, double absY) => Input.Mouse.MoveMouseTo(absX, absY);

    protected override void OnScroll(int clicks) => Input.Mouse.VerticalScroll(clicks);

    /// <summary>
    /// 查询系统实际按键状态
    /// </summary>
    public override bool IsKeyDown(User32.VK key) =>
        (User32.GetAsyncKeyState((int)key) & 0x8000) != 0;
}

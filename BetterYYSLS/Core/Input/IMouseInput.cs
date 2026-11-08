namespace BetterYYSLS.Core.Input;

/// <summary>
/// 鼠标输入。方法名与 Fischless.WindowsInput.IMouseSimulator 保持一致。
/// </summary>
public interface IMouseInput
{
    /// <summary>相对移动（视角控制）</summary>
    IMouseInput MoveMouseBy(int dx, int dy);

    /// <summary>桌面绝对定位，按主屏幕归一化到 0~65535</summary>
    IMouseInput MoveMouseTo(double absX, double absY);

    IMouseInput LeftButtonDown();

    IMouseInput LeftButtonUp();

    IMouseInput LeftButtonClick();

    IMouseInput RightButtonDown();

    IMouseInput RightButtonUp();

    IMouseInput RightButtonClick();

    IMouseInput MiddleButtonDown();

    IMouseInput MiddleButtonUp();

    IMouseInput MiddleButtonClick();

    /// <param name="buttonId">1 = 侧键1，2 = 侧键2</param>
    IMouseInput XButtonDown(int buttonId);

    /// <param name="buttonId">1 = 侧键1，2 = 侧键2</param>
    IMouseInput XButtonUp(int buttonId);

    /// <param name="buttonId">1 = 侧键1，2 = 侧键2</param>
    IMouseInput XButtonClick(int buttonId);

    /// <summary>滚轮，正数向上，负数向下</summary>
    IMouseInput VerticalScroll(int scrollAmountInClicks);

    IMouseInput Sleep(int ms);
}

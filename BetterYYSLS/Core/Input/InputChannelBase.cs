using System;
using System.Collections.Generic;
using System.Threading;
using Vanara.PInvoke;

namespace BetterYYSLS.Core.Input;

/// <summary>
/// 输入通道基类。一个实例同时实现通道、键盘、鼠标三个接口。
/// 负责按下状态记录、不支持操作的限频警告，子类只需实现原子操作。
/// </summary>
public abstract class InputChannelBase : IInputChannel, IKeyboardInput, IMouseInput
{
    private readonly object _stateLock = new();

    private readonly HashSet<User32.VK> _pressedKeys = [];

    private readonly HashSet<InputMouseButton> _pressedButtons = [];

    private readonly HashSet<string> _warnedOperations = [];

    public IKeyboardInput Keyboard => this;

    public IMouseInput Mouse => this;

    public IKeyboardInput KeyDown(User32.VK key)
    {
        lock (_stateLock)
        {
            _pressedKeys.Add(key);
        }

        OnKeyDown(key);
        return this;
    }

    public IKeyboardInput KeyUp(User32.VK key)
    {
        lock (_stateLock)
        {
            _pressedKeys.Remove(key);
        }

        OnKeyUp(key);
        return this;
    }

    public IKeyboardInput KeyPress(User32.VK key)
    {
        OnKeyPress(key);
        return this;
    }

    public IMouseInput LeftButtonDown() => MouseDown(InputMouseButton.Left);

    public IMouseInput LeftButtonUp() => MouseUp(InputMouseButton.Left);

    public IMouseInput LeftButtonClick() => MouseClick(InputMouseButton.Left);

    public IMouseInput RightButtonDown() => MouseDown(InputMouseButton.Right);

    public IMouseInput RightButtonUp() => MouseUp(InputMouseButton.Right);

    public IMouseInput RightButtonClick() => MouseClick(InputMouseButton.Right);

    public IMouseInput MiddleButtonDown() => MouseDown(InputMouseButton.Middle);

    public IMouseInput MiddleButtonUp() => MouseUp(InputMouseButton.Middle);

    public IMouseInput MiddleButtonClick() => MouseClick(InputMouseButton.Middle);

    public IMouseInput XButtonDown(int buttonId) =>
        MouseDown(buttonId == 2 ? InputMouseButton.X2 : InputMouseButton.X1);

    public IMouseInput XButtonUp(int buttonId) =>
        MouseUp(buttonId == 2 ? InputMouseButton.X2 : InputMouseButton.X1);

    public IMouseInput XButtonClick(int buttonId) =>
        MouseClick(buttonId == 2 ? InputMouseButton.X2 : InputMouseButton.X1);

    public IMouseInput MoveMouseBy(int dx, int dy)
    {
        OnMoveBy(dx, dy);
        return this;
    }

    public IMouseInput MoveMouseTo(double absX, double absY)
    {
        OnMoveTo(absX, absY);
        return this;
    }

    public IMouseInput VerticalScroll(int scrollAmountInClicks)
    {
        OnScroll(scrollAmountInClicks);
        return this;
    }

    public IInputChannel Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    IKeyboardInput IKeyboardInput.Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    IMouseInput IMouseInput.Sleep(int ms)
    {
        Thread.Sleep(ms);
        return this;
    }

    public virtual bool IsKeyDown(User32.VK key)
    {
        lock (_stateLock)
        {
            if (_pressedKeys.Contains(key))
            {
                return true;
            }
        }

        return (User32.GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    public virtual void ReleaseAll()
    {
        User32.VK[] keys;
        InputMouseButton[] buttons;
        lock (_stateLock)
        {
            keys = [.. _pressedKeys];
            buttons = [.. _pressedButtons];
            _pressedKeys.Clear();
            _pressedButtons.Clear();
        }

        foreach (var key in keys)
        {
            OnKeyUp(key);
        }

        foreach (var button in buttons)
        {
            OnMouseButton(button, false);
        }
    }

    private IMouseInput MouseDown(InputMouseButton button)
    {
        lock (_stateLock)
        {
            _pressedButtons.Add(button);
        }

        OnMouseButton(button, true);
        return this;
    }

    private IMouseInput MouseUp(InputMouseButton button)
    {
        lock (_stateLock)
        {
            _pressedButtons.Remove(button);
        }

        OnMouseButton(button, false);
        return this;
    }

    private IMouseInput MouseClick(InputMouseButton button)
    {
        OnMouseClick(button);
        return this;
    }

    /// <summary>
    /// 不支持的操作只警告一次，避免刷屏
    /// </summary>
    protected void WarnUnsupported(string operation)
    {
        lock (_stateLock)
        {
            if (!_warnedOperations.Add(operation))
            {
                return;
            }
        }

        Serilog.Log.Warning("输入通道不支持：{Operation}", operation);
    }

    protected abstract void OnKeyDown(User32.VK key);

    protected abstract void OnKeyUp(User32.VK key);

    protected abstract void OnKeyPress(User32.VK key);

    protected abstract void OnMouseButton(InputMouseButton button, bool down);

    protected abstract void OnMouseClick(InputMouseButton button);

    protected abstract void OnMoveBy(int dx, int dy);

    protected abstract void OnMoveTo(double absX, double absY);

    protected abstract void OnScroll(int clicks);
}

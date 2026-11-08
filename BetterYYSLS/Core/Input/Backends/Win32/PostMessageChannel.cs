using BetterYYSLS.Core.Simulator;
using BetterYYSLS.Helpers;
using System;
using System.Drawing;
using Vanara.PInvoke;

namespace BetterYYSLS.Core.Input.Backends.Win32;

/// <summary>
/// 后台通道：包装 PostMessageSimulator，向游戏窗口投递消息。
/// 不支持相对移动、中键、侧键、滚轮（警告后忽略）。
/// </summary>
internal sealed class PostMessageChannel : InputChannelBase
{
    private readonly IntPtr _hWnd;
    private readonly PostMessageSimulator? _simulator;

    /// <summary>后续点击使用的客户区坐标</summary>
    private Point _pointer = new(16, 16);

    public PostMessageChannel(IntPtr hWnd)
    {
        _hWnd = hWnd;
        _simulator = hWnd == IntPtr.Zero ? null : new PostMessageSimulator(hWnd);
    }

    protected override void OnKeyDown(User32.VK key)
    {
        if (TryPrepare("按键"))
        {
            _simulator!.KeyDownBackground(key);
        }
    }

    protected override void OnKeyUp(User32.VK key)
    {
        if (TryPrepare("按键"))
        {
            _simulator!.KeyUpBackground(key);
        }
    }

    protected override void OnKeyPress(User32.VK key)
    {
        if (TryPrepare("按键"))
        {
            _simulator!.KeyPressBackground(key);
        }
    }

    protected override void OnMouseButton(InputMouseButton button, bool down)
    {
        if (button != InputMouseButton.Left)
        {
            WarnUnsupported($"后台{button}键");
            return;
        }

        if (!TryPrepare("鼠标"))
        {
            return;
        }

        _ = down ? _simulator!.LeftButtonDown() : _simulator!.LeftButtonUp();
    }

    protected override void OnMouseClick(InputMouseButton button)
    {
        if (button != InputMouseButton.Left)
        {
            WarnUnsupported($"后台{button}键");
            return;
        }

        if (TryPrepare("鼠标"))
        {
            _simulator!.LeftButtonClickBackground(_pointer.X, _pointer.Y);
        }
    }

    protected override void OnMoveBy(int dx, int dy) => WarnUnsupported("后台相对移动 MoveMouseBy");

    /// <summary>
    /// 换算成屏幕坐标，作为后续后台点击的位置；不移动真实光标
    /// </summary>
    protected override void OnMoveTo(double absX, double absY)
    {
        var screen = PrimaryScreen.WorkingArea;
        _pointer = new Point(
            (int)(absX * screen.Width / 65535d),
            (int)(absY * screen.Height / 65535d));
    }

    protected override void OnScroll(int clicks) => WarnUnsupported("后台滚轮 VerticalScroll");

    /// <summary>
    /// 检查是否绑定了窗口；游戏不在前台时补发 WM_ACTIVATE
    /// </summary>
    private bool TryPrepare(string operation)
    {
        if (_simulator != null)
        {
            return true;
        }

        WarnUnsupported($"后台{operation}（未绑定窗口）");
        return false;
    }
}

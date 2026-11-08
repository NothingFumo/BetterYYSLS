using System.Windows;
using System.Windows.Controls;

namespace BetterYYSLS.View.Controls.Adorners;

public class SizeChrome : Control
{
    static SizeChrome()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SizeChrome), new FrameworkPropertyMetadata(typeof(SizeChrome)));
    }
}
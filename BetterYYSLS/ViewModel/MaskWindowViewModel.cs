using BetterYYSLS.Core.Config;
using BetterYYSLS.GameTask;
using BetterYYSLS.Helpers;
using BetterYYSLS.Model;
using BetterYYSLS.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace BetterYYSLS.ViewModel
{
    /// <summary>
    /// 遮罩窗口 ViewModel：日志、状态和可拖拽控件。
    /// </summary>
    public partial class MaskWindowViewModel : ObservableRecipient
    {
        [ObservableProperty] private Rect _windowRect;

        [ObservableProperty] private ObservableCollection<MaskButton> _maskButtons = new();

        [ObservableProperty] private ObservableCollection<StatusItem> _statusList = new();

        /// <summary>
        /// 配置根
        /// </summary>
        public AllConfig? Config { get; set; }

        /// <summary>
        /// 创建遮罩窗口 ViewModel 并订阅遮罩消息
        /// </summary>
        public MaskWindowViewModel()
        {
            WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
            {
                if (msg.PropertyName == "AddButton")
                {
                    UIDispatcherHelper.Invoke(() =>
                    {
                        if (msg.NewValue is MaskButton button && !_maskButtons.Contains(button))
                        {
                            _maskButtons.Add(button);
                        }
                    });
                }
                else if (msg.PropertyName == "RemoveButton")
                {
                    UIDispatcherHelper.Invoke(() =>
                    {
                        if (msg.NewValue is string buttonName)
                        {
                            var button = _maskButtons.FirstOrDefault(b => b.Name == buttonName);
                            if (button != null)
                            {
                                _maskButtons.Remove(button);
                            }
                        }
                    });
                }
                else if (msg.PropertyName == "RemoveAllButton")
                {
                    UIDispatcherHelper.Invoke(() => { _maskButtons.Clear(); });
                }
                else if (msg.PropertyName == "RefreshSettings")
                {
                    UIDispatcherHelper.Invoke(RefreshSettings);
                }
            });
        }

        /// <summary>
        /// 构建状态指示列表
        /// </summary>
        private void InitializeStatusList()
        {
            if (Config == null)
            {
                return;
            }

            StatusList.Add(new StatusItem("示例触发器", Config.SampleTriggerConfig));
            StatusList.Add(new StatusItem("占位触发器", Config.PlaceholderTriggerConfig));
        }

        [RelayCommand]
        private void OnLoaded()
        {
            RefreshSettings();
            InitializeStatusList();
        }

        /// <summary>
        /// 刷新配置绑定
        /// </summary>
        private void RefreshSettings()
        {
            InitConfig();
            if (Config != null)
            {
                OnPropertyChanged(nameof(Config));
            }
        }

        /// <summary>
        /// 遮罩窗口无法直接使用构造函数依赖注入，这里从容器取配置
        /// </summary>
        private void InitConfig()
        {
            if (Config == null)
            {
                var configService = App.GetService<IConfigService>();
                if (configService != null)
                {
                    Config = configService.Get();
                }
            }
        }
    }
}

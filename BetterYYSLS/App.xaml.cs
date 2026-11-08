using BetterYYSLS.GameTask;
using BetterYYSLS.Helpers;
using BetterYYSLS.Helpers.Extensions;
using BetterYYSLS.Service;
using BetterYYSLS.Service.Interface;
using BetterYYSLS.View;
using BetterYYSLS.View.Pages;
using BetterYYSLS.ViewModel;
using BetterYYSLS.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui;

namespace BetterYYSLS;

/// <summary>
/// 应用入口：依赖注入、日志、页面注册与全局异常处理。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 通用主机，提供依赖注入、配置和日志
    /// </summary>
    private static readonly IHost _host = Host.CreateDefaultBuilder()
        .UseElevated()
        .UseSingleInstance("BetterYYSLS")
        .ConfigureServices(
            (context, services) =>
            {
                // 提前初始化配置
                var configService = new ConfigService();
                services.AddSingleton<IConfigService>(sp => configService);
                var all = configService.Get();

                var logFolder = Path.Combine(AppContext.BaseDirectory, "log");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "better-yysls.log");

                // 遮罩窗口同时是日志输出目标
                var maskWindow = new MaskWindow();
                services.AddSingleton(maskWindow);

                var loggerConfiguration = new LoggerConfiguration()
                    .WriteTo.File(
                        path: logFile,
                        outputTemplate:
                        "[{Timestamp:HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message}{NewLine}{Exception}{NewLine}",
                        rollingInterval: RollingInterval.Day)
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning);
                if (all.MaskWindowConfig.MaskEnabled)
                {
                    loggerConfiguration.WriteTo.RichTextBox(maskWindow.LogBox, LogEventLevel.Information,
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
                }

                Log.Logger = loggerConfiguration.CreateLogger();
                services.AddLogging(c => c.AddSerilog());

                // 应用宿主与导航
                services.AddHostedService<ApplicationHostService>();
                services.AddSingleton<IPageService, PageService>();
                services.AddSingleton<INavigationService, NavigationService>();

                // 主窗口与托盘
                services.AddView<INavigationWindow, MainWindow, MainWindowViewModel>();
                services.AddSingleton<NotifyIconViewModel>();

                // 页面
                services.AddView<HomePage, HomePageViewModel>();
                services.AddView<TriggerSettingsPage, TriggerSettingsPageViewModel>();
                services.AddView<HotKeyPage, HotKeyPageViewModel>();
                services.AddView<CommonSettingsPage, CommonSettingsPageViewModel>();

                // 实时触发器调度器
                services.AddSingleton<TaskTriggerDispatcher>();
            }
        )
        .Build();

    /// <summary>
    /// 获取指定类型的日志记录器
    /// </summary>
    public static ILogger<T> GetLogger<T>()
    {
        return _host.Services.GetService<ILogger<T>>()!;
    }

    /// <summary>
    /// 从依赖注入容器获取服务
    /// </summary>
    public static T? GetService<T>() where T : class
    {
        return _host.Services.GetService(typeof(T)) as T;
    }

    /// <summary>
    /// 从依赖注入容器获取服务
    /// </summary>
    public static object? GetService(Type type)
    {
        return _host.Services.GetService(type);
    }

    /// <summary>
    /// 应用启动
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            RegisterEvents();
            await _host.StartAsync();
            await UrlProtocolHelper.RegisterAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);

            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
        }
    }

    /// <summary>
    /// 应用退出
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);

        await _host.StopAsync();
        _host.Dispose();
    }

    /// <summary>
    /// 注册全局异常事件
    /// </summary>
    private void RegisterEvents()
    {
        // Task 线程内未捕获异常
        TaskScheduler.UnobservedTaskException += TaskSchedulerUnobservedTaskException;

        // UI 主线程未捕获异常
        DispatcherUnhandledException += AppDispatcherUnhandledException;

        // 非 UI 线程未捕获异常
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;
    }

    private static void TaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            HandleException(e.Exception);
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
        finally
        {
            e.SetObserved();
        }
    }

    private static void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            if (e.ExceptionObject is Exception exception)
            {
                HandleException(exception);
            }
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private static void AppDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            HandleException(e.Exception);
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
        finally
        {
            // 标记已处理，避免程序直接退出
            e.Handled = true;
        }
    }

    /// <summary>
    /// 统一异常提示与记录
    /// </summary>
    private static void HandleException(Exception e)
    {
        MessageBox.Show("程序异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" +
                        Environment.NewLine + e.Message);
    }
}

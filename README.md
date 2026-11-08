<div align="center">
  <img src="BetterYYSLS/Assets/logo.png" width="120" alt="BetterYYSLS" />
  <h1>BetterYYSLS</h1>
  <p>更好的燕云十六声 · 基于计算机视觉的自动化框架</p>
</div>

<br/>

> 本项目只保留自动化框架，不包含任何具体游戏功能。  
> 框架来源于 [BetterGI · 更好的原神](https://github.com/babalae/better-genshin-impact)（GPL-3.0）。

## 简介

BetterYYSLS 在 BetterGI 的框架基础上做了减法：移除全部游戏专属任务、识别素材和业务页面，只保留可复用的视觉自动化基础设施。

保留的能力：

* **实时触发器**：固定间隔截图、按优先级调度、独占执行、启停热键
* **画面捕获**：BitBlt、DwmGetDxSharedSurface、WindowsGraphicsCapture（含 HDR / V2）
* **图像识别**：OpenCV 模板匹配、颜色识别、区域裁剪与坐标回算
* **模拟输入**：SendInput 前台输入、PostMessage 后台输入
* **遮罩窗口**：日志、状态指示、识别结果绘制、可拖拽控件
* **界面框架**：启动、实时触发、快捷键、设置四个页面
* **配置持久化**：`User/config.json`，JSON 序列化与自动保存

## 目录结构

```text
BetterYYSLS/
  Assets/         Logo、横幅
  Core/           配置、识别、模拟输入
  GameTask/       运行上下文、实时触发器、调度器、示例触发器
  Service/        配置读写、页面与宿主服务
  View/           WPF 页面、遮罩窗口、控件
  ViewModel/      MVVM ViewModel
```

## 构建

需要 .NET 10 SDK 和 Windows SDK `10.0.22621.0` 或更高版本。

```powershell
dotnet msbuild BetterYYSLS.slnx -restore -p:Configuration=Debug -nologo -tl:off -clp:ErrorsOnly
```

退出码为 0 表示构建成功。

## 使用

1. 启动游戏。
2. 打开 BetterYYSLS，在“启动”页填写游戏进程名，或点击“选择捕获窗口”手动绑定。
3. 选择截图方式，点击“启动”。
4. 在“实时触发”页启用“示例触发器”，遮罩日志中会周期输出画面尺寸。
5. 在“快捷键”页设置启动/停止热键（默认 `F11`）。

> 当前只支持 16:9 分辨率，推荐在 1920×1080 窗口化模式下使用。  
> 窗口大小、分辨率或显示器变化后请重启软件。

## 接入游戏功能

1. 在 `GameTask/Sample/PlaceholderTrigger.cs` 中实现识别与模拟操作。
2. 在 `GameTask/GameTaskManager.cs` 的 `LoadTriggers` 中注册触发器。
3. 在 `Core/Config/AllConfig.cs` 中增加配置并挂接变更通知。
4. 在 `View/Pages/TriggerSettingsPage.xaml` 中增加开关。

## FAQ

**为什么需要管理员权限？**  
游戏通常以管理员权限运行，BetterYYSLS 需要同等权限才能向游戏窗口发送模拟输入。

**会不会封号？**  
本项目只做屏幕识别和模拟输入，不读写游戏内存、不修改游戏文件。但第三方工具和模拟操作可能违反游戏用户协议，风险请自行评估。

**对电脑配置有要求吗？**  
图像识别比较吃性能，建议至少能在中画质 60 帧下流畅运行游戏。

**为什么没有具体游戏功能？**  
本仓库定位为框架。具体功能请基于 `PlaceholderTrigger` 自行实现，避免把游戏素材和业务逻辑混入框架。

## 致谢

* [BetterGI · 更好的原神](https://github.com/babalae/better-genshin-impact)：框架来源
* [Fischless](https://github.com/genshin-matrix/Fischless)：截图、键鼠与热键基础组件
* [OpenCvSharp](https://github.com/shimat/opencvsharp)：图像处理
* [WPF-UI](https://github.com/lepoco/wpfui)：界面组件
* [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)：MVVM
* [Serilog](https://github.com/serilog/serilog)：日志
* [Vanara](https://github.com/dahall/Vanara)：Windows API 封装
* [MouseKeyHook](https://github.com/gmamaladze/globalmousekeyhook)：全局键鼠监听

## 许可证

![GPL-v3](https://www.gnu.org/graphics/gplv3-127x51.png)

本项目以 GNU General Public License v3.0 发布，完整协议见 [LICENSE](LICENSE)。  
来源与修改声明见 [NOTICE](NOTICE)。

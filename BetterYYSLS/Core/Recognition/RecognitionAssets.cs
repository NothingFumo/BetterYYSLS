using BetterYYSLS.Core.Config;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BetterYYSLS.Core.Recognition;

/// <summary>
/// 从任务目录下的 Recognition.json 加载识别对象。
/// 当前支持模板匹配：模板文件 + 相对 ROI 比例 + 阈值。
/// </summary>
public static class RecognitionAssets
{
    /// <summary>识别配置根</summary>
    private sealed class AssetFile
    {
        public int Version { get; set; }

        public Dictionary<string, AssetObject> Objects { get; set; } = new();
    }

    /// <summary>单个识别对象</summary>
    private sealed class AssetObject
    {
        public string Template { get; set; } = "";

        public double X { get; set; }

        public double Y { get; set; }

        public double W { get; set; }

        public double H { get; set; }

        public double Threshold { get; set; } = 0.8;

        public bool Draw { get; set; } = true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly ConcurrentDictionary<string, AssetFile> Cache = new();

    /// <summary>
    /// 获取识别对象。ROI 按当前捕获区域的比例计算，模板按 1920 宽缩放。
    /// </summary>
    /// <param name="feature">任务目录名，例如 AutoSkip</param>
    /// <param name="name">Recognition.json 中的对象名</param>
    /// <param name="width">当前捕获区域宽度</param>
    /// <param name="height">当前捕获区域高度</param>
    public static RecognitionObject Get(string feature, string name, int width, int height)
    {
        var file = Cache.GetOrAdd(feature, Load);
        if (!file.Objects.TryGetValue(name, out var item))
        {
            throw new InvalidOperationException(
                $"Recognition.json 中不存在对象 {feature}/{name}");
        }

        var template = LoadTemplate(feature, item.Template, width);
        var roi = new Rect(
            (int)(width * item.X),
            (int)(height * item.Y),
            (int)(width * item.W),
            (int)(height * item.H));

        return new RecognitionObject
        {
            Name = name,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = template,
            RegionOfInterest = roi,
            Threshold = item.Threshold,
            DrawOnWindow = item.Draw,
        }.InitTemplate();
    }

    /// <summary>
    /// 读取任务目录下的 Recognition.json
    /// </summary>
    private static AssetFile Load(string feature)
    {
        var path = Global.Absolute($@"GameTask\{feature}\Assets\Recognition.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"未找到识别配置：{path}");
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AssetFile>(json, JsonOptions)
               ?? throw new InvalidOperationException($"识别配置为空：{path}");
    }

    /// <summary>
    /// 读取 1920×1080 模板并按当前捕获宽度缩放
    /// </summary>
    private static Mat LoadTemplate(string feature, string file, int width)
    {
        var path = Global.Absolute($@"GameTask\{feature}\Assets\1920x1080\{file}");
        var mat = Cv2.ImRead(path);
        if (mat.Empty())
        {
            throw new FileNotFoundException($"未找到识别模板：{path}");
        }

        var scale = Math.Min(1d, width / 1920d);
        if (scale < 1)
        {
            var resized = new Mat();
            Cv2.Resize(mat, resized, new Size(
                (int)(mat.Width * scale),
                (int)(mat.Height * scale)));
            mat.Dispose();
            return resized;
        }

        return mat;
    }
}

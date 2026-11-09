using BetterYYSLS.Core.Recognition.OCR.Engine;
using BetterYYSLS.Core.Recognition.OCR.Paddle;
using BetterYYSLS.Core.Recognition.ONNX;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlScalar = YamlDotNet.Core.Events.Scalar;

namespace BetterYYSLS.Core.Recognition.OCR;

/// <summary>
/// PP-OCRv6 的 ONNX 实现。全局单例，首次使用时加载模型。
/// </summary>
public sealed class OcrService : IDisposable
{
    /// <summary>全局 OCR 服务</summary>
    public static OcrService Instance { get; } = new();

    private readonly Det _det;
    private readonly Rec _rec;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>低于该置信度的结果会被丢弃</summary>
    public float DropScore { get; set; } = 0.5f;

    private OcrService()
    {
        var factory = new BgiOnnxSessionFactory();
        _det = new Det(BgiOnnxModel.PaddleOcrDetV6, OcrVersionConfig.PpOcrV6, factory);
        _rec = new Rec(
            BgiOnnxModel.PaddleOcrRecV6,
            LoadLabels(BgiOnnxModel.PaddleOcrRecV6),
            OcrVersionConfig.PpOcrV6,
            factory);
    }

    /// <summary>
    /// 识别整张图片，返回每一行文字及其位置。
    /// </summary>
    public OcrResult OcrResult(Mat mat)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (mat.Channels() == 4)
            {
                using var mat3 = mat.CvtColor(ColorConversionCodes.BGRA2BGR);
                return RunAll(mat3);
            }

            return RunAll(mat);
        }
    }

    /// <summary>
    /// 识别整张图片并拼接文字。
    /// </summary>
    public string Ocr(Mat mat) => OcrResult(mat).Text;

    /// <summary>
    /// 识别一张已裁剪好的单行文字图片。
    /// </summary>
    public OcrRecognizerResult OcrResultWithoutDetector(Mat mat)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _rec.Run(mat);
        }
    }

    /// <summary>
    /// 检测 + 裁剪 + 识别。
    /// </summary>
    private OcrResult RunAll(Mat src, int recognizeBatchSize = 0)
    {
        var boxes = SortBoxes(_det.RunBoxes(src));
        var mats = new List<Mat>(boxes.Length);
        try
        {
            foreach (var box in boxes)
            {
                mats.Add(GetRotateCropImage(src, box.Points));
            }

            return new OcrResult(_rec.Run(mats.ToArray(), recognizeBatchSize)
                .Select((result, i) => new OcrResultRegion(boxes[i].Rect, result.Text, result.Score))
                .Where(region => region.Score >= DropScore)
                .ToArray());
        }
        finally
        {
            foreach (var mat in mats)
            {
                mat.Dispose();
            }
        }
    }

    /// <summary>
    /// 按 PaddleOCR 官方 get_rotate_crop_image 的方式做透视裁剪。
    /// </summary>
    private static Mat GetRotateCropImage(Mat src, Point2f[] points)
    {
        if (points.Length != 4)
        {
            throw new ArgumentException("OCR detection box must contain exactly four points.", nameof(points));
        }

        var cropWidth = (int)Math.Max(GetDistance(points[0], points[1]), GetDistance(points[2], points[3]));
        var cropHeight = (int)Math.Max(GetDistance(points[0], points[3]), GetDistance(points[1], points[2]));
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            throw new InvalidOperationException($"Invalid OCR crop size: {cropWidth}x{cropHeight}");
        }

        Point2f[] destinationPoints =
        [
            new(0, 0),
            new(cropWidth, 0),
            new(cropWidth, cropHeight),
            new(0, cropHeight)
        ];
        using var transform = Cv2.GetPerspectiveTransform(points, destinationPoints);
        var cropped = new Mat();
        try
        {
            Cv2.WarpPerspective(src, cropped, transform, new Size(cropWidth, cropHeight),
                InterpolationFlags.Cubic, BorderTypes.Replicate);

            if (cropped.Height * 1.0 / cropped.Width >= 1.5)
            {
                Cv2.Rotate(cropped, cropped, RotateFlags.Rotate90Counterclockwise);
            }

            return cropped;
        }
        catch
        {
            cropped.Dispose();
            throw;
        }
    }

    private static double GetDistance(Point2f first, Point2f second)
    {
        var deltaX = first.X - second.X;
        var deltaY = first.Y - second.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    /// <summary>
    /// 按左上角坐标排序，近似 PaddleOCR 的阅读顺序。
    /// </summary>
    private static PaddleOcrDetectionBox[] SortBoxes(PaddleOcrDetectionBox[] source)
    {
        var boxes = source
            .OrderBy(box => box.Points[0].Y)
            .ThenBy(box => box.Points[0].X)
            .ToArray();

        for (var i = 0; i < boxes.Length - 1; i++)
        {
            for (var j = i; j >= 0; j--)
            {
                if (Math.Abs(boxes[j + 1].Points[0].Y - boxes[j].Points[0].Y) < 10
                    && boxes[j + 1].Points[0].X < boxes[j].Points[0].X)
                {
                    (boxes[j], boxes[j + 1]) = (boxes[j + 1], boxes[j]);
                }
                else
                {
                    break;
                }
            }
        }

        return boxes;
    }

    /// <summary>
    /// 从识别模型的 inference.yml 中读取 character_dict。
    /// </summary>
    private static IReadOnlyList<string> LoadLabels(BgiOnnxModel recModel)
    {
        var configFilePath = Path.Combine(
            Path.GetDirectoryName(recModel.ModalPath) ?? throw new InvalidOperationException("无法获取模型目录"),
            "inference.yml");

        if (!File.Exists(configFilePath))
        {
            throw new FileNotFoundException($"PaddleOCR 配置文件不存在：{configFilePath}");
        }

        using var reader = new StreamReader(configFilePath);
        var parser = new Parser(reader);
        while (parser.MoveNext())
        {
            if (parser.Current is not YamlScalar { Value: "PostProcess" })
            {
                continue;
            }

            parser.MoveNext();
            while (parser.MoveNext())
            {
                if (parser.Current is not YamlScalar { Value: "character_dict" })
                {
                    continue;
                }

                parser.MoveNext();
                var result = new List<string>();
                while (parser.MoveNext())
                {
                    switch (parser.Current)
                    {
                        case SequenceEnd:
                            return result;
                        case YamlScalar scalar:
                            result.Add(scalar.Value);
                            break;
                    }
                }
            }
        }

        throw new InvalidOperationException("未在 inference.yml 的 PostProcess 中找到 character_dict。");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _det.Dispose();
            }
            finally
            {
                _rec.Dispose();
            }
        }
    }
}

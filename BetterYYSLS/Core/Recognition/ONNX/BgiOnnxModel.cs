using BetterYYSLS.Core.Config;

namespace BetterYYSLS.Core.Recognition.ONNX;

/// <summary>
/// ONNX 模型描述。路径相对于程序运行目录。
/// </summary>
public sealed class BgiOnnxModel
{
    /// <summary>模型名称，用于日志</summary>
    public string Name { get; }

    /// <summary>模型相对路径</summary>
    public string ModelRelativePath { get; }

    /// <summary>模型绝对路径</summary>
    public string ModalPath => Global.Absolute(ModelRelativePath);

    private BgiOnnxModel(string name, string modelRelativePath)
    {
        Name = name;
        ModelRelativePath = modelRelativePath;
    }

    /// <summary>PP-OCRv6 文本检测模型</summary>
    public static readonly BgiOnnxModel PaddleOcrDetV6 =
        new("PpOcrDetV6", @"Assets\Model\PaddleOCR\Det\V6\PP-OCRv6_small_det_infer\slim.onnx");

    /// <summary>PP-OCRv6 文本识别模型</summary>
    public static readonly BgiOnnxModel PaddleOcrRecV6 =
        new("PpOcrRecV6", @"Assets\Model\PaddleOCR\Rec\V6\PP-OCRv6_small_rec_infer\slim.onnx");
}

using Microsoft.ML.OnnxRuntime;
using System;

namespace BetterYYSLS.Core.Recognition.ONNX;

/// <summary>
/// ONNX Runtime 会话工厂。当前使用 CPU，模型加载失败时由调用方记录日志。
/// </summary>
public sealed class BgiOnnxSessionFactory
{
    /// <summary>
    /// 创建推理会话。
    /// </summary>
    /// <param name="model">模型描述</param>
    /// <param name="useGpu">当前实现忽略该参数，统一使用 CPU</param>
    public InferenceSession CreateInferenceSession(BgiOnnxModel model, bool useGpu)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
        };
        return new InferenceSession(model.ModalPath, options);
    }
}

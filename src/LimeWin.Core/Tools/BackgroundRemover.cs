using ImageMagick;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Windows.AI.MachineLearning;

namespace LimeWin.Core.Tools;

/// <summary>
/// Cuts the subject out of a photo with BiRefNet (<see cref="ModelStore"/>) on the ONNX Runtime that ships with Windows ML.
/// The result is <c>name (no background).png</c> next to the source. One inference session is built on first use and
/// reused; images go through it one at a time.
/// </summary>
public static class BackgroundRemover
{
    internal const int Side = 1024;
    internal static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    internal static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    /// <summary>
    /// The only accelerator used: NVIDIA's TensorRT-RTX, from the Windows ML catalog. DirectML (the bundled GPU provider)
    /// is left out on purpose: on the development PC it reset the GPU (DXGI device removed) on this model; see decisions.md.
    /// </summary>
    internal const string GpuProvider = "NvTensorRtRtxExecutionProvider";
    const string CpuDevice = "CPU";

    static readonly SemaphoreSlim Gate = new(1, 1); // guards the session; one image at a time
    static InferenceSession? _session;
    static bool _onCpu;
    static readonly Lock RegistrationLock = new();
    static Task? _registration;

    /// <summary>True when the Windows ML runtime (ONNX Runtime) loads in this process. Cheap after the first call.</summary>
    public static bool IsAvailable => Available.Value;

    static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            _ = OrtEnv.Instance();
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException
                                       or TypeInitializationException or OnnxRuntimeException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    });

    /// <summary>True once the session is built (later images skip the slow first load).</summary>
    public static bool IsLoaded => Volatile.Read(ref _session) is not null;

    /// <summary>Which execution provider the session runs on ("CPU" or the accelerator's name); null before loading.</summary>
    public static string? Device { get; private set; }

    /// <summary>
    /// True when the Windows ML catalog offers an accelerator for this PC that is not installed yet (it needs a download).
    /// False on Windows versions without the catalog. Not free (asks the catalog); call off the UI thread.
    /// </summary>
    public static bool AcceleratorsMissing
    {
        get
        {
            try
            {
                var providers = ExecutionProviderCatalog.GetDefault().FindAllProviders() ?? [];
                return providers.Any(p => p.ReadyState == ExecutionProviderReadyState.NotPresent);
            }
            catch (Exception e) when (IsWindowsMlFailure(e))
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Downloads (if needed) and registers the Windows ML accelerators for this PC
    /// (<c>ExecutionProviderCatalog.EnsureAndRegisterCertifiedAsync</c>); progress 0..1. Runs once per process; a failure
    /// is not an error (the CPU still works) and the next call tries again.
    /// </summary>
    public static async Task PrepareAcceleratorsAsync(IProgress<double>? progress, CancellationToken ct)
    {
        Task registration;
        lock (RegistrationLock)
        {
            if (_registration is null || _registration.IsFaulted || _registration.IsCanceled)
                _registration = RegisterAsync(progress);
            registration = _registration;
        }
        try
        {
            await registration.WaitAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException) { /* no accelerator: the CPU still works */ }
    }

    static async Task RegisterAsync(IProgress<double>? progress)
    {
        var operation = ExecutionProviderCatalog.GetDefault().EnsureAndRegisterCertifiedAsync();
        if (progress is not null) operation.Progress = (_, percent) => progress.Report(Math.Clamp(percent / 100, 0, 1));
        await operation;
    }

    /// <summary>Registers accelerators and builds the session if that has not happened yet (slow the first time: seconds).</summary>
    public static async Task LoadAsync(OperationContext ctx)
    {
        await Gate.WaitAsync(ctx.Token);
        try
        {
            await EnsureSessionAsync(ctx.Token);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task<string> RemoveAsync(string source, OperationContext ctx) =>
        ToolRunner.RunAsync(ctx, () => RemoveCoreAsync(source, ctx));

    static async Task<string> RemoveCoreAsync(string source, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var format = FormatCatalog.FormatOf(source);
        if (format is null or "svg" || FormatCatalog.CategoryOf(format) != FormatCategory.Image)
            throw new LimeException("이 파일은 배경을 지울 수 없습니다.");

        // The whole image passes through the gate: only one decoded photo and one 1024 x 1024 tensor exist at a time.
        await Gate.WaitAsync(ctx.Token);
        try
        {
            using var image = await Task.Run(() => ImageConverter.LoadOriented(source, format, singleFrameOnly: true), ctx.Token);
            ctx.Report(0.1);
            await EnsureSessionAsync(ctx.Token);
            ctx.Report(0.2);
            return await Task.Run(() =>
            {
                var tensor = ToTensor(ModelInput(image));
                var mask = MaskBytes(Infer(tensor, ctx.Token));
                ctx.Report(0.9);
                ctx.Token.ThrowIfCancellationRequested();
                ApplyMask(image, mask);

                var output = OutputNaming.ToolOutput(source, "no background", "png");
                ctx.Track(output);
                try
                {
                    ImageConverter.Encode(image, "png", output);
                }
                catch (MagickException)
                {
                    throw new LimeException("이미지를 저장하지 못했습니다.");
                }
                ctx.Report(1);
                return output;
            }, ctx.Token);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Call inside the gate.</summary>
    static async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (_session is not null) return;
        if (!IsAvailable) throw new LimeException("이 컴퓨터에서는 배경 제거 엔진을 쓸 수 없습니다.");
        if (!ModelStore.IsReady) throw new LimeException("배경 제거 모델이 없습니다. 다시 시도하면 내려받습니다.");
        await PrepareAcceleratorsAsync(null, ct);
        ct.ThrowIfCancellationRequested();
        var (session, device) = await Task.Run(() => CreateSession(useAccelerator: !_onCpu), CancellationToken.None);
        Device = device;
        Volatile.Write(ref _session, session);
    }

    static (InferenceSession Session, string Device) CreateSession(bool useAccelerator)
    {
        try
        {
            if (useAccelerator)
            {
                var env = OrtEnv.Instance();
                var devices = env.GetEpDevices();
                if (ChooseDevice([.. devices.Select(d => (d.EpName, d.HardwareDevice.Type))]) is { } index)
                {
                    try
                    {
                        using var options = new SessionOptions();
                        options.AppendExecutionProvider(env, [devices[index]], new Dictionary<string, string>());
                        return (new InferenceSession(ModelStore.ModelPath, options), devices[index].EpName);
                    }
                    catch (OnnxRuntimeException) { /* the accelerator refused the model: use the CPU */ }
                }
            }
            using var cpu = new SessionOptions();
            return (new InferenceSession(ModelStore.ModelPath, cpu), CpuDevice);
        }
        catch (OnnxRuntimeException)
        {
            throw new LimeException("배경 제거 모델을 불러오지 못했습니다.");
        }
    }

    /// <summary>Index of the device to accelerate on (TensorRT-RTX on a GPU), or null for the CPU.</summary>
    internal static int? ChooseDevice(IReadOnlyList<(string EpName, OrtHardwareDeviceType Type)> devices)
    {
        for (var i = 0; i < devices.Count; i++)
            if (devices[i].EpName == GpuProvider && devices[i].Type == OrtHardwareDeviceType.GPU) return i;
        return null;
    }

    /// <summary>Runs the model; an accelerator failure rebuilds the session on the CPU once and retries. Call inside the gate.</summary>
    static float[] Infer(float[] tensor, CancellationToken ct)
    {
        try
        {
            try
            {
                return Run(_session!, tensor, ct);
            }
            catch (OnnxRuntimeException) when (!ct.IsCancellationRequested && Device != CpuDevice)
            {
                _onCpu = true; // this process stays on the CPU
                _session!.Dispose();
                Volatile.Write(ref _session, null);
                var (session, device) = CreateSession(useAccelerator: false);
                Device = device;
                Volatile.Write(ref _session, session);
                return Run(session, tensor, ct);
            }
        }
        catch (OnnxRuntimeException) when (!ct.IsCancellationRequested)
        {
            throw new LimeException("배경을 지우지 못했습니다.");
        }
    }

    static float[] Run(InferenceSession session, float[] tensor, CancellationToken ct)
    {
        using var runOptions = new RunOptions();
        using var stop = ct.Register(() => runOptions.Terminate = true);
        try
        {
            using var input = OrtValue.CreateTensorValueFromMemory(tensor, [1, 3, Side, Side]);
            using var outputs = session.Run(runOptions, [session.InputNames[0]], [input], session.OutputNames);
            return outputs[^1].GetTensorDataAsSpan<float>().ToArray(); // the last output is the finest mask
        }
        catch (OnnxRuntimeException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
    }

    /// <summary>Interleaved RGB bytes of the image squeezed to 1024 x 1024 (transparency flattened onto white).</summary>
    static byte[] ModelInput(MagickImage image)
    {
        using var copy = (MagickImage)image.Clone();
        if (copy.HasAlpha) copy.ColorAlpha(MagickColors.White);
        if (copy.ColorSpace != ColorSpace.sRGB) copy.ColorSpace = ColorSpace.sRGB;
        copy.FilterType = FilterType.Triangle; // bilinear, as the model was trained
        copy.Resize(new MagickGeometry(Side, Side) { IgnoreAspectRatio = true });
        return copy.GetPixels().ToByteArray(PixelMapping.RGB) ?? throw new LimeException("이미지를 읽을 수 없습니다.");
    }

    /// <summary>NCHW float32: each channel <c>(x / 255 - mean) / std</c> with the ImageNet mean and std, planes R, G, B.</summary>
    internal static float[] ToTensor(ReadOnlySpan<byte> rgb)
    {
        var pixels = rgb.Length / 3;
        var tensor = new float[rgb.Length];
        for (var c = 0; c < 3; c++)
        {
            var scale = 1f / (255f * Std[c]);
            var shift = Mean[c] / Std[c];
            var plane = tensor.AsSpan(c * pixels, pixels);
            for (var p = 0; p < pixels; p++) plane[p] = rgb[p * 3 + c] * scale - shift;
        }
        return tensor;
    }

    /// <summary>Sigmoid of the model's logits as 0..255 opacity.</summary>
    internal static byte[] MaskBytes(ReadOnlySpan<float> logits)
    {
        var mask = new byte[logits.Length];
        for (var i = 0; i < logits.Length; i++)
            mask[i] = (byte)Math.Round(255 / (1 + Math.Exp(-logits[i])));
        return mask;
    }

    /// <summary>Stretches the 1024 x 1024 mask back to the image (bilinear) and makes it the alpha channel (times any alpha the image had).</summary>
    static void ApplyMask(MagickImage image, byte[] mask)
    {
        var grey = new byte[mask.Length * 3];
        for (var i = 0; i < mask.Length; i++) grey[i * 3] = grey[i * 3 + 1] = grey[i * 3 + 2] = mask[i];
        using var alpha = new MagickImage();
        alpha.ReadPixels(grey, new PixelReadSettings(Side, Side, StorageType.Char, PixelMapping.RGB));
        alpha.FilterType = FilterType.Triangle;
        alpha.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
        if (image.HasAlpha)
        {
            using var original = (MagickImage)image.Clone();
            original.Alpha(AlphaAction.Extract);
            alpha.Composite(original, CompositeOperator.Multiply);
        }
        if (image.ColorSpace != ColorSpace.sRGB) image.ColorSpace = ColorSpace.sRGB;
        image.Alpha(AlphaAction.Set);
        image.Composite(alpha, CompositeOperator.CopyAlpha);
    }

    static bool IsWindowsMlFailure(Exception e) =>
        e is System.Runtime.InteropServices.COMException or InvalidOperationException or TypeLoadException
            or DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException;
}

using System.Runtime.InteropServices;
using ImageMagick;
using Zest.Core.Catalog;
using Zest.Core.Convert;
using Zest.Core.Jobs;
using Zest.Core.Output;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Windows.AI.MachineLearning;

namespace Zest.Core.Tools;

/// <summary>
/// Cuts the subject out of a photo with BiRefNet (<see cref="ModelStore"/>) on the ONNX Runtime that ships with Windows ML.
/// The result is <c>name (no background).png</c> next to the source. One inference session is built on first use and
/// reused; images go through it one at a time. After <see cref="IdleLimit"/> without use it is released and later rebuilt.
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
    const string EngineMissing = "이 컴퓨터에서는 배경 제거 엔진을 쓸 수 없습니다.";
    const string ProcessFailed = "이미지를 처리하지 못했습니다.";

    static readonly SemaphoreSlim Gate = new(1, 1); // guards the session; one image at a time
    static InferenceSession? _session;
    static bool _onCpu;
    static readonly Lock AcquisitionLock = new();
    static Task? _acquisition; // the one accelerator download/registration attempt of this process

    /// <summary>How long the session may sit unused before it is released (the model holds about 1 GB); rebuilt on the next use.</summary>
    internal static TimeSpan IdleLimit { get; set; } = TimeSpan.FromMinutes(5);

    static Timer? _idleTimer; // touched only while holding the gate

    /// <summary>
    /// The ONNX Runtime environment, created on first use (never at startup) with ONNX Runtime's own telemetry switched off
    /// before anything else (accelerator registration, sessions) touches it.
    /// </summary>
    static readonly Lazy<OrtEnv> Env = new(() =>
    {
        var env = OrtEnv.Instance();
        env.DisableTelemetryEvents();
        return env;
    });

    /// <summary>
    /// True when the Windows ML runtime ships with this app (onnxruntime.dll next to it) and Windows is 10 1903 or later.
    /// Cheap: only looks for the file; the runtime itself is loaded on first use.
    /// </summary>
    public static bool IsAvailable => Available.Value;

    static readonly Lazy<bool> Available = new(() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) && RuntimePresent(AppContext.BaseDirectory));

    /// <summary>onnxruntime.dll in <paramref name="appFolder"/> (RID-specific publish) or in its <c>runtimes\win-‹arch›\native</c> folder.</summary>
    internal static bool RuntimePresent(string appFolder)
    {
        if (File.Exists(Path.Combine(appFolder, "onnxruntime.dll"))) return true;
        var rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => null,
        };
        return rid is not null && File.Exists(Path.Combine(appFolder, "runtimes", rid, "native", "onnxruntime.dll"));
    }

    /// <summary>True once the session is built (later images skip the slow first load).</summary>
    public static bool IsLoaded => Volatile.Read(ref _session) is not null;

    /// <summary>Which execution provider the session runs on ("CPU" or the accelerator's name); null before loading.</summary>
    public static string? Device { get; private set; }

    /// <summary>
    /// Registers accelerators and builds the session if that has not happened yet (seconds; the first time in a process may
    /// also download a Windows ML accelerator). Progress 0..1 on <paramref name="ctx"/>.
    /// </summary>
    public static async Task LoadAsync(OperationContext ctx)
    {
        using (await ctx.EnterAsync(Gate))
        {
            try
            {
                await EnsureSessionAsync(ctx.Token, new Relay(f => ctx.Report(0.8 * f)));
                ctx.Report(1);
            }
            finally
            {
                ScheduleRelease();
            }
        }
    }

    public static Task<string> RemoveAsync(string source, OperationContext ctx) =>
        ToolRunner.RunAsync(ctx, () => RemoveCoreAsync(source, ctx));

    static async Task<string> RemoveCoreAsync(string source, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var format = FormatCatalog.FormatOf(source);
        if (format is null or "svg" || FormatCatalog.CategoryOf(format) != FormatCategory.Image)
            throw new ZestException("이 파일은 배경을 지울 수 없습니다.");

        // The whole image passes through the gate: only one decoded photo and one 1024 x 1024 tensor exist at a time.
        // Waiting for it shows as "대기 중" on the card.
        using (await ctx.EnterAsync(Gate))
        try
        {
            using var image = await Task.Run(() => ImageConverter.LoadOriented(source, format, singleFrameOnly: true), ctx.Token);
            ctx.Report(0.1);
            await EnsureSessionAsync(ctx.Token, null);
            ctx.Report(0.2);
            return await Task.Run(() =>
            {
                var tensor = ToTensor(ModelInput(image));
                var mask = Probabilities(Infer(tensor, ctx.Token));
                ctx.Report(0.8);
                ctx.Token.ThrowIfCancellationRequested();
                ApplyMask(image, mask);
                ctx.Report(0.9);

                var output = OutputNaming.ToolOutput(source, "no background", "png", ctx);
                try
                {
                    ImageConverter.Encode(image, "png", output);
                }
                catch (MagickException)
                {
                    throw new ZestException("이미지를 저장하지 못했습니다.");
                }
                ctx.Report(1);
                return output;
            }, ctx.Token);
        }
        finally
        {
            ScheduleRelease();
        }
    }

    /// <summary>Call inside the gate, after each use: the session is released once it has been idle for <see cref="IdleLimit"/>.</summary>
    static void ScheduleRelease()
    {
        if (_session is null) return;
        _idleTimer ??= new Timer(_ => ReleaseIfIdle());
        _idleTimer.Change(IdleLimit, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Timer callback: disposes the session unless an image is using it (that use schedules a new release when it ends).</summary>
    static void ReleaseIfIdle()
    {
        if (!Gate.Wait(0)) return;
        try
        {
            var session = _session;
            Volatile.Write(ref _session, null);
            Device = null;
            session?.Dispose();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Call inside the gate.</summary>
    static async Task EnsureSessionAsync(CancellationToken ct, IProgress<double>? progress)
    {
        if (_session is not null) return;
        if (!IsAvailable) throw new ZestException(EngineMissing);
        try
        {
            _ = Env.Value;
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException
                                       or TypeInitializationException or OnnxRuntimeException)
        {
            throw new ZestException(EngineMissing);
        }
        if (!ModelStore.IsReady) throw new ZestException("배경 제거 모델이 없습니다. 다시 시도하면 내려받습니다.");
        if (!_onCpu) await AcquireAcceleratorsAsync(progress, ct);
        ct.ThrowIfCancellationRequested();
        var (session, device) = await Task.Run(() => CreateSession(useAccelerator: !_onCpu), CancellationToken.None);
        Device = device;
        Volatile.Write(ref _session, session);
    }

    /// <summary>
    /// <c>ExecutionProviderCatalog.EnsureAndRegisterCertifiedAsync</c> (downloads the accelerators Windows ML offers for this
    /// PC and registers them), attempted once per process; later callers wait for that same attempt. It reports failures per
    /// provider in its result rather than throwing, and whatever did not become ready is simply not used: the session falls
    /// back to the CPU. Nothing here asks the user anything.
    /// </summary>
    static async Task AcquireAcceleratorsAsync(IProgress<double>? progress, CancellationToken ct)
    {
        Task acquisition;
        lock (AcquisitionLock)
        {
            if (_acquisition is null)
            {
                _acquisition = AcquireAsync(progress);
                _acquisition.ObserveFaults(); // a caller that cancelled no longer awaits it; its failure must not go unobserved
            }
            acquisition = _acquisition;
        }
        try
        {
            await acquisition.WaitAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* the catalog failed (or cancelled itself): CPU */ }
    }

    static async Task AcquireAsync(IProgress<double>? progress)
    {
        var operation = ExecutionProviderCatalog.GetDefault().EnsureAndRegisterCertifiedAsync();
        if (progress is not null) operation.Progress = (_, percent) => progress.Report(Math.Clamp(percent / 100, 0, 1));
        var providers = await operation;
        AcceleratorsReady = providers?.Count(p => p.ReadyState == ExecutionProviderReadyState.Ready) ?? 0;
    }

    /// <summary>How many Windows ML catalog accelerators ended up ready in this process (0 before or without them).</summary>
    internal static int AcceleratorsReady { get; private set; }

    static (InferenceSession Session, string Device) CreateSession(bool useAccelerator)
    {
        try
        {
            if (useAccelerator)
            {
                var env = Env.Value;
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
            throw new ZestException("배경 제거 모델을 불러오지 못했습니다.");
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
            throw new ZestException("배경을 지우지 못했습니다.");
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
        try
        {
            using var copy = (MagickImage)image.Clone();
            if (copy.HasAlpha) copy.ColorAlpha(MagickColors.White);
            if (copy.ColorSpace != ColorSpace.sRGB) copy.ColorSpace = ColorSpace.sRGB;
            copy.FilterType = FilterType.Triangle; // bilinear, as the model was trained
            copy.Resize(new MagickGeometry(Side, Side) { IgnoreAspectRatio = true });
            return copy.GetPixels().ToByteArray(PixelMapping.RGB) ?? throw new ZestException(ProcessFailed);
        }
        catch (MagickException)
        {
            throw new ZestException(ProcessFailed);
        }
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

    /// <summary>Sigmoid of the model's logits: opacity 0..1.</summary>
    internal static float[] Probabilities(ReadOnlySpan<float> logits)
    {
        var p = new float[logits.Length];
        for (var i = 0; i < logits.Length; i++) p[i] = 1 / (1 + MathF.Exp(-logits[i]));
        return p;
    }

    /// <summary>
    /// Bilinear resize of a float mask with <c>align_corners=True</c> (corner samples map onto corner pixels), as BiRefNet's
    /// own inference upsamples its prediction.
    /// </summary>
    internal static float[] ResizeMask(float[] src, int srcWidth, int srcHeight, int width, int height)
    {
        var result = new float[width * height];
        var sx = width > 1 ? (srcWidth - 1) / (double)(width - 1) : 0;
        var sy = height > 1 ? (srcHeight - 1) / (double)(height - 1) : 0;
        Parallel.For(0, height, y =>
        {
            var fy = y * sy;
            var y0 = (int)fy;
            var y1 = Math.Min(y0 + 1, srcHeight - 1);
            var wy = (float)(fy - y0);
            for (var x = 0; x < width; x++)
            {
                var fx = x * sx;
                var x0 = (int)fx;
                var x1 = Math.Min(x0 + 1, srcWidth - 1);
                var wx = (float)(fx - x0);
                var top = src[y0 * srcWidth + x0] * (1 - wx) + src[y0 * srcWidth + x1] * wx;
                var bottom = src[y1 * srcWidth + x0] * (1 - wx) + src[y1 * srcWidth + x1] * wx;
                result[y * width + x] = top * (1 - wy) + bottom * wy;
            }
        });
        return result;
    }

    /// <summary>
    /// Stretches the 1024 x 1024 mask to the image, makes it the alpha channel (times any alpha the image had) and replaces
    /// the colours with the estimated foreground colours (<see cref="ForegroundEstimator"/>), so edges carry no background tint.
    /// The image keeps its profiles and metadata.
    /// </summary>
    static void ApplyMask(MagickImage image, float[] mask)
    {
        try
        {
            if (image.ColorSpace != ColorSpace.sRGB) image.ColorSpace = ColorSpace.sRGB;
            int w = (int)image.Width, h = (int)image.Height;
            var alpha = ResizeMask(mask, Side, Side, w, h);
            if (image.HasAlpha)
            {
                var original = image.GetPixels().ToByteArray(PixelMapping.RGBA) ?? throw new ZestException(ProcessFailed);
                for (var i = 0; i < alpha.Length; i++) alpha[i] *= original[i * 4 + 3] / 255f;
            }
            var rgb = image.GetPixels().ToByteArray(PixelMapping.RGB) ?? throw new ZestException(ProcessFailed);
            var foreground = ForegroundEstimator.Estimate(rgb, alpha, w, h);

            var rgba = new byte[alpha.Length * 4];
            for (var i = 0; i < alpha.Length; i++)
            {
                rgba[i * 4] = foreground[i * 3];
                rgba[i * 4 + 1] = foreground[i * 3 + 1];
                rgba[i * 4 + 2] = foreground[i * 3 + 2];
                rgba[i * 4 + 3] = (byte)MathF.Round(alpha[i] * 255);
            }
            image.Alpha(AlphaAction.Set);
            image.ImportPixels(rgba, new PixelImportSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.RGBA));
        }
        catch (MagickException)
        {
            throw new ZestException(ProcessFailed);
        }
    }

    /// <summary>Progress reported on the calling thread.</summary>
    sealed class Relay(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

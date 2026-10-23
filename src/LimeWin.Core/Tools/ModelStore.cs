using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace LimeWin.Core.Tools;

/// <summary>
/// The background-removal model (BiRefNet general, ONNX). It is not shipped with the app: the first use downloads it to
/// <c>%LOCALAPPDATA%\LimeWin\models</c>, and it counts as present only when its size and SHA-256 match.
/// </summary>
public static class ModelStore
{
    public const string FileName = "BiRefNet-general-epoch_244.onnx";
    public const long ExpectedBytes = 972_666_916;

    /// <summary>Computed from the release asset downloaded on 2026-10-23 (GitHub publishes no digest for it).</summary>
    public const string Sha256 = "58f621f00f5d756097615970a88a791584600dcf7c45b18a0a6267535a1ebd3c";

    public static readonly Uri Source = new("https://github.com/ZhengPeng7/BiRefNet/releases/download/v1/BiRefNet-general-epoch_244.onnx");

    static readonly ModelFile Model = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LimeWin", "models", FileName),
        ExpectedBytes, Sha256, Source);

    static readonly Lazy<HttpClient> Http = new(() =>
    {
        // No overall timeout (the file is ~1 GB); ModelFile cancels a download that stalls instead.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LimeWin", "1.0"));
        return http;
    });

    public static string ModelPath => Model.Path;

    /// <summary>True when the file exists with the expected size and SHA-256. A wrong file is deleted. The first check hashes ~1 GB (about a second); keep it off the UI thread.</summary>
    public static bool IsReady => Model.IsReady;

    /// <summary>Downloads the model (progress 0..1). Failures are Korean <see cref="LimeException"/>s; cancelling throws <see cref="OperationCanceledException"/>. Nothing partial is left behind.</summary>
    public static Task DownloadAsync(IProgress<double>? progress, CancellationToken ct) => Model.DownloadAsync(Http.Value, progress, ct);
}

/// <summary>A downloaded file pinned by size and SHA-256 (the logic behind <see cref="ModelStore"/>, testable with any path).</summary>
internal sealed class ModelFile(string path, long bytes, string sha256, Uri source)
{
    internal const string NetworkMessage = "모델을 내려받지 못했습니다. 인터넷 연결을 확인하세요.";
    internal const string CorruptMessage = "내려받은 모델 파일이 올바르지 않습니다. 다시 시도하세요.";
    internal const string DiskMessage = "모델을 저장하지 못했습니다. 디스크 공간을 확인하세요.";
    static readonly TimeSpan DefaultStall = TimeSpan.FromSeconds(30);
    const int BufferSize = 1 << 20;

    readonly Lock _gate = new();
    (long Length, DateTime Written)? _verified;

    public string Path { get; } = path;

    public bool IsReady
    {
        get
        {
            lock (_gate)
            {
                FileInfo info;
                try
                {
                    info = new FileInfo(Path);
                    if (!info.Exists) return false;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return false;
                }
                var stamp = (info.Length, info.LastWriteTimeUtc);
                if (_verified == stamp) return true;
                _verified = null;
                if (info.Length != bytes)
                {
                    TryDelete(Path);
                    return false;
                }
                string actual;
                try
                {
                    using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
                    actual = System.Convert.ToHexStringLower(SHA256.HashData(stream));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return false; // unreadable right now (locked?): not ready, but not proven corrupt either
                }
                if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(Path);
                    return false;
                }
                _verified = stamp;
                return true;
            }
        }
    }

    /// <param name="stallTimeout">How long the server may send nothing before the download is given up (30 s by default).</param>
    public async Task DownloadAsync(HttpClient http, IProgress<double>? progress, CancellationToken ct, TimeSpan? stallTimeout = null)
    {
        var folder = System.IO.Path.GetDirectoryName(Path)!;
        var temp = Path + ".download";
        var stall = stallTimeout ?? DefaultStall;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LimeException(DiskMessage);
        }

        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            string actual;
            long received = 0;
            watchdog.CancelAfter(stall);
            using (var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, watchdog.Token))
            {
                if (!response.IsSuccessStatusCode) throw new LimeException(NetworkMessage);
                await using var body = await response.Content.ReadAsStreamAsync(watchdog.Token);
                await using var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[BufferSize];
                while (true)
                {
                    watchdog.CancelAfter(stall);
                    var n = await ReadAsync(body, buffer, watchdog.Token);
                    if (n == 0) break;
                    hash.AppendData(buffer, 0, n);
                    received += n;
                    if (received > bytes) break; // more than the pinned size: wrong file, stop early
                    await WriteAsync(file, buffer.AsMemory(0, n), ct);
                    progress?.Report(Math.Min(1.0, (double)received / bytes));
                }
                actual = System.Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            if (received != bytes || !actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new LimeException(CorruptMessage);

            try
            {
                File.Move(temp, Path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new LimeException(DiskMessage);
            }
            lock (_gate)
            {
                var info = new FileInfo(Path);
                _verified = (info.Length, info.LastWriteTimeUtc);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryDelete(temp);
            throw new LimeException(NetworkMessage); // the stall watchdog fired, not the user
        }
        catch (HttpRequestException)
        {
            TryDelete(temp);
            throw new LimeException(NetworkMessage);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    static async Task<int> ReadAsync(Stream body, byte[] buffer, CancellationToken ct)
    {
        try
        {
            return await body.ReadAsync(buffer, ct);
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            throw new LimeException(NetworkMessage);
        }
    }

    static async Task WriteAsync(FileStream file, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        try
        {
            await file.WriteAsync(data, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LimeException(DiskMessage);
        }
    }

    static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}

using System.Net;
using System.Security.Cryptography;
using Zest.Core.Tools;

namespace Zest.Core.Tests;

public class ModelStoreTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static readonly byte[] Payload = [.. Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7))];
    static readonly string PayloadHash = System.Convert.ToHexStringLower(SHA256.HashData(Payload));
    static readonly Uri Source = new("https://example.invalid/model.onnx");

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    string ModelPath => Path.Combine(Dir, "models", "model.onnx");
    ModelFile File_() => new(ModelPath, Payload.Length, PayloadHash, Source);

    [Fact]
    public void ModelPath_IsTheBiRefNetMattingFileUnderLocalAppData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LimeWin", "models", "BiRefNet-matting-epoch_100.onnx");
        Assert.Equal(expected, ModelStore.ModelPath);
        Assert.Equal(972_667_742, ModelStore.ExpectedBytes);
        Assert.Equal("6065d27c615ea27308f5b88598dd8db116eb07436c7a323ca40d13b2866c309e", ModelStore.Sha256);
        Assert.EndsWith("/BiRefNet-matting-epoch_100.onnx", ModelStore.Source.AbsoluteUri);
        Assert.Equal(["BiRefNet-general-epoch_244.onnx"], ModelStore.ObsoleteFileNames);
    }

    [Fact]
    public void IsReady_RemovesTheObsoleteModel_ByExactNameInItsOwnFolderOnly()
    {
        var folder = Path.GetDirectoryName(ModelPath)!;
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        var old = Path.Combine(folder, "old.onnx");
        var similar = Path.Combine(folder, "old.onnx.bak");
        var nested = Path.Combine(folder, "sub", "old.onnx");
        var outside = Path.Combine(Dir, "old.onnx");
        foreach (var f in new[] { old, similar, nested, outside }) File.WriteAllBytes(f, [1, 2, 3]);
        var model = new ModelFile(ModelPath, Payload.Length, PayloadHash, Source, obsolete: ["old.onnx", @"..\old.onnx", "sub\\old.onnx"]);

        Assert.False(model.IsReady);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(similar));
        Assert.True(File.Exists(nested));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public async Task Download_RemovesTheObsoleteModel_BeforeCheckingFreeSpace()
    {
        var folder = Path.GetDirectoryName(ModelPath)!;
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "old.onnx");
        File.WriteAllBytes(old, [1, 2, 3]);
        var sawOld = true;
        var model = new ModelFile(ModelPath, Payload.Length, PayloadHash, Source, _ => { sawOld = File.Exists(old); return null; }, obsolete: ["old.onnx"]);
        using var http = new HttpClient(new Serve(Payload));

        await model.DownloadAsync(http, null, CancellationToken.None);

        Assert.False(sawOld);
        Assert.False(File.Exists(old));
        Assert.True(model.IsReady);
    }

    [Fact]
    public void ModelStore_WrongHash_NotReady()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        var fake = (byte[])Payload.Clone();
        fake[123] ^= 0xFF; // same name and size, different content
        File.WriteAllBytes(ModelPath, fake);

        Assert.False(File_().IsReady);
        Assert.False(File.Exists(ModelPath)); // a corrupt file is removed so the next download starts clean
    }

    [Fact]
    public void IsReady_WrongSize_FalseAndDeleted()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        File.WriteAllBytes(ModelPath, Payload[..1000]); // a partial file

        Assert.False(File_().IsReady);
        Assert.False(File.Exists(ModelPath));
    }

    [Fact]
    public void IsReady_MissingFile_False()
    {
        Assert.False(File_().IsReady);
    }

    [Fact]
    public void IsReady_MatchingFile_True()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        File.WriteAllBytes(ModelPath, Payload);

        var model = File_();
        Assert.True(model.IsReady);
        Assert.True(model.IsReady); // the cached answer
    }

    [Fact]
    public async Task Download_Verifies_RenamesIntoPlace_ReportsProgress()
    {
        var model = File_();
        var progress = new Recorder();
        using var http = new HttpClient(new Serve(Payload));

        await model.DownloadAsync(http, progress, CancellationToken.None);

        Assert.Equal(Payload, File.ReadAllBytes(ModelPath));
        Assert.True(model.IsReady);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(ModelPath)!)); // no temp file left
        Assert.NotEmpty(progress.Values);
        Assert.Equal(1.0, progress.Values[^1], 3);
        Assert.True(progress.Values.Zip(progress.Values.Skip(1)).All(p => p.First <= p.Second));
    }

    [Fact]
    public async Task Download_WrongContent_DeletesAndSaysCorrupt()
    {
        var bad = (byte[])Payload.Clone();
        bad[^1] ^= 0xFF;
        using var http = new HttpClient(new Serve(bad));

        var ex = await Assert.ThrowsAsync<ZestException>(() => File_().DownloadAsync(http, null, CancellationToken.None));

        Assert.Equal("내려받은 모델 파일이 올바르지 않습니다. 다시 시도하세요.", ex.Message);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ModelPath)!));
    }

    [Fact]
    public async Task Download_ShortContent_DeletesAndSaysCorrupt()
    {
        using var http = new HttpClient(new Serve(Payload[..5000]));

        var ex = await Assert.ThrowsAsync<ZestException>(() => File_().DownloadAsync(http, null, CancellationToken.None));

        Assert.Equal("내려받은 모델 파일이 올바르지 않습니다. 다시 시도하세요.", ex.Message);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ModelPath)!));
    }

    [Theory]
    [InlineData("network")]
    [InlineData("404")]
    [InlineData("stall")]
    public async Task Download_ConnectionProblem_KoreanMessage_NoFileLeft(string kind)
    {
        HttpMessageHandler handler = kind switch
        {
            "network" => new Throw(),
            "404" => new Serve([], HttpStatusCode.NotFound),
            _ => new Serve(Payload, stallAfter: 1000),
        };
        using var http = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<ZestException>(() =>
            File_().DownloadAsync(http, null, CancellationToken.None, stallTimeout: TimeSpan.FromMilliseconds(300)));

        Assert.Equal("모델을 내려받지 못했습니다. 인터넷 연결을 확인하세요.", ex.Message);
        Assert.False(File.Exists(ModelPath));
        Assert.Empty(Directory.Exists(Path.GetDirectoryName(ModelPath)) ? Directory.GetFiles(Path.GetDirectoryName(ModelPath)!) : []);
    }

    [Fact]
    public void IsReady_NoModel_RemovesLeftoverTempFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        var model = File_();
        File.WriteAllBytes(model.TempPath, Payload[..1000]); // a download that was killed half-way

        Assert.False(model.IsReady);
        Assert.False(File.Exists(model.TempPath));
    }

    [Fact]
    public async Task Download_RemovesLeftoverTempFile_EvenWhenTheServerFails()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        var model = File_();
        File.WriteAllBytes(model.TempPath, Payload[..1000]);
        using var http = new HttpClient(new Serve([], HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<ZestException>(() => model.DownloadAsync(http, null, CancellationToken.None));

        Assert.False(File.Exists(model.TempPath));
    }

    [Fact]
    public async Task Download_TempFileCannotBeCreated_SaysDisk()
    {
        var model = File_();
        Directory.CreateDirectory(model.TempPath); // a folder where the temp file should go: the file cannot be created
        using var http = new HttpClient(new Serve(Payload));

        var ex = await Assert.ThrowsAsync<ZestException>(() => model.DownloadAsync(http, null, CancellationToken.None));

        Assert.Equal("모델을 저장하지 못했습니다. 디스크 공간을 확인하세요.", ex.Message);
        Assert.False(File.Exists(ModelPath));
    }

    [Fact]
    public async Task Download_NotEnoughFreeSpace_RefusedBeforeAnyRequest()
    {
        var asked = new List<string>();
        var model = new ModelFile(ModelPath, Payload.Length, PayloadHash, Source, folder =>
        {
            asked.Add(folder);
            return Payload.Length; // less than size + 10 %
        });
        var handler = new Serve(Payload);
        using var http = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<ZestException>(() => model.DownloadAsync(http, null, CancellationToken.None));

        Assert.StartsWith("디스크 공간이 부족합니다.", ex.Message);
        Assert.Equal([Path.GetDirectoryName(ModelPath)!], asked);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Download_EnoughFreeSpace_Proceeds()
    {
        var model = new ModelFile(ModelPath, Payload.Length, PayloadHash, Source, _ => Payload.Length * 2L);
        using var http = new HttpClient(new Serve(Payload));

        await model.DownloadAsync(http, null, CancellationToken.None);

        Assert.True(model.IsReady);
    }

    [Fact]
    public async Task Download_Cancelled_ThrowsCancelled_NoFileLeft()
    {
        using var cts = new CancellationTokenSource();
        using var http = new HttpClient(new Serve(Payload, stallAfter: 1000));
        var task = File_().DownloadAsync(http, new Recorder(_ => cts.Cancel()), cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ModelPath)!));
    }

    sealed class Recorder(Action<double>? each = null) : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value)
        {
            lock (Values) Values.Add(value);
            each?.Invoke(value);
        }
    }

    /// <summary>Serves <paramref name="body"/>; with <paramref name="stallAfter"/>, sends that many bytes and then hangs until cancelled.</summary>
    sealed class Serve(byte[] body, HttpStatusCode status = HttpStatusCode.OK, int? stallAfter = null) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Stream stream = stallAfter is { } n ? new StallingStream(body[..n]) : new MemoryStream(body);
            var content = new StreamContent(stream);
            content.Headers.ContentLength = body.Length;
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    sealed class Throw : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no route to host");
    }

    sealed class StallingStream(byte[] head) : Stream
    {
        int _sent;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_sent < head.Length)
            {
                var n = Math.Min(buffer.Length, head.Length - _sent);
                head.AsMemory(_sent, n).CopyTo(buffer);
                _sent += n;
                return n;
            }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

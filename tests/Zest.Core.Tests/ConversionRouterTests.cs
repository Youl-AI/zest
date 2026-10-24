using System.Security.AccessControl;
using System.Security.Principal;
using Zest.Core.Convert;
using Zest.Core.Jobs;
using Zest.Core.Tests.Fixtures;

namespace Zest.Core.Tests;

public class ConversionRouterTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();
    static OperationContext Ctx => new(CancellationToken.None);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    sealed class FakeConverter(Func<string, OperationContext, Task<IReadOnlyList<string>>> run) : IConverter
    {
        public bool Called;
        public bool Handles(string from, string to) => from == "png" && to == "jpg";
        public Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx)
        {
            Called = true;
            return run(source, ctx);
        }
    }

    [Fact]
    public async Task NoConverter_ThrowsLimeException()
    {
        var src = FixtureFactory.Image(Dir, "a.png");
        var ex = await Assert.ThrowsAsync<ZestException>(() => new ConversionRouter([]).ConvertAsync(src, "jpg", Ctx));
        Assert.Equal("이 변환은 지원하지 않습니다.", ex.Message);
    }

    [Fact]
    public async Task ReadOnlyFolder_ThrowsLimeException_AndLeavesNoFile()
    {
        var src = FixtureFactory.Image(Dir, "a.png");
        var fake = new FakeConverter((s, c) => Task.FromResult<IReadOnlyList<string>>([]));
        var info = new DirectoryInfo(Dir);
        var sid = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(sid,
            FileSystemRights.WriteData | FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        try
        {
            var ex = await Assert.ThrowsAsync<ZestException>(() =>
                new ConversionRouter([fake, new ImageConverter()]).ConvertAsync(src, "jpg", Ctx));
            Assert.Equal("이 폴더에는 저장할 수 없습니다.", ex.Message);
            Assert.False(fake.Called);
        }
        finally
        {
            acl = info.GetAccessControl();
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
        }
        Assert.False(File.Exists(Path.Combine(Dir, "a.jpg")));
        Assert.Equal(["a.png"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConverterFailure_RemovesNewOutputs_AndKeepsExistingFiles(bool cancel)
    {
        var src = FixtureFactory.Image(Dir, "a.png");
        var existing = Path.Combine(Dir, "a.jpg");
        File.WriteAllText(existing, "keep");
        var converter = new FakeConverter((s, c) =>
        {
            var file = Path.Combine(Dir, "a 2.jpg");
            var folder = Path.Combine(Dir, "a (pages)");
            c.Track(file);
            c.Track(folder);
            File.WriteAllText(file, "partial");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(Dir, "a (pages)", "1.png"), "partial");
            throw cancel ? new OperationCanceledException() : new InvalidOperationException("boom");
        });

        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ConversionRouter([converter]).ConvertAsync(src, "jpg", Ctx));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => new ConversionRouter([converter]).ConvertAsync(src, "jpg", Ctx));

        Assert.Equal("keep", File.ReadAllText(existing));
        Assert.Equal(["a.jpg", "a.png"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName).Order());
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    public async Task IoFailure_BecomesKoreanLimeException_AfterCleanup(Type exceptionType)
    {
        var src = FixtureFactory.Image(Dir, "a.png");
        var converter = new FakeConverter((s, c) =>
        {
            var output = Output.OutputNaming.ConvertOutput(s, "jpg", c);
            File.WriteAllText(output, "partial");
            throw (Exception)Activator.CreateInstance(exceptionType)!;
        });

        var ex = await Assert.ThrowsAsync<ZestException>(() => new ConversionRouter([converter]).ConvertAsync(src, "jpg", Ctx));

        Assert.Equal("파일을 읽거나 저장하지 못했습니다.", ex.Message);
        Assert.Equal(["a.png"], Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Cleanup_DoesNotDeleteUnrelatedFileCreatedDuringFailingCall()
    {
        var src = FixtureFactory.Image(Dir, "a.png");
        var output = Path.Combine(Dir, "a.jpg");
        var unrelated = Path.Combine(Dir, "download.tmp");
        var converter = new FakeConverter((s, c) =>
        {
            c.Track(output);
            File.WriteAllText(output, "partial");
            File.WriteAllText(unrelated, "someone else's file");
            throw new InvalidOperationException("boom");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ConversionRouter([converter]).ConvertAsync(src, "jpg", Ctx));

        Assert.False(File.Exists(output));
        Assert.Equal("someone else's file", File.ReadAllText(unrelated));
    }
}

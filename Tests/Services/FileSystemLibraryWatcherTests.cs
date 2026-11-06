using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class FileSystemLibraryWatcherTests
{
    [Fact]
    public void AffectedRoots_UsesDirectoryBoundariesAndBothRenamePaths()
    {
        var roots = new[] { @"C:\Music", @"C:\Music\Disc", @"C:\Music2" };
        var affected = FileSystemLibraryWatcher.AffectedRoots(roots,
            @"C:\Music\Disc\new.flac", @"C:\Elsewhere\old.flac");
        Assert.Equal(new[] { @"C:\Music", @"C:\Music\Disc" }, affected);
        Assert.DoesNotContain(@"C:\Music", FileSystemLibraryWatcher.AffectedRoots(
            roots, @"C:\Music2\new.flac", null));
    }

    [Fact]
    public void ClassifyError_MapsBufferOverflowToOverflow()
    {
        Assert.Equal(LibraryWatchKind.Overflow,
            FileSystemLibraryWatcher.ClassifyError(new InternalBufferOverflowException()));
        Assert.Equal(LibraryWatchKind.Unavailable,
            FileSystemLibraryWatcher.ClassifyError(new IOException("lost")));
    }

    [Fact]
    public async Task ReplaceRootsAsync_ReportsCreatedFile_AndDisposeStopsMonitoring()
    {
        var root = Path.Combine(Path.GetTempPath(),
            $"dplayer-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var watcher = new FileSystemLibraryWatcher();
        var created = new TaskCompletionSource<LibraryWatchNotification>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var afterDisposeSeen = false;
        watcher.Changed += notification =>
        {
            if (notification.Path is { } path &&
                path.EndsWith("after.flac", StringComparison.OrdinalIgnoreCase))
            {
                afterDisposeSeen = true;
            }
            if (notification.Kind == LibraryWatchKind.Invalidated)
                created.TrySetResult(notification);
        };

        try
        {
            var failed = await watcher.ReplaceRootsAsync(
                1, new[] { root }, TestContext.Current.CancellationToken);
            Assert.Empty(failed);
            Assert.True(watcher.IsMonitoring);

            var file = Path.Combine(root, "new.flac");
            File.WriteAllText(file, "x");
            var notification = await created.Task.WaitAsync(
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(root, notification.Root);
            Assert.Equal(file, notification.Path, ignoreCase: true);

            watcher.Dispose();
            Assert.False(watcher.IsMonitoring);
            File.WriteAllText(Path.Combine(root, "after.flac"), "x");
            await Task.Delay(TimeSpan.FromMilliseconds(200),
                TestContext.Current.CancellationToken);
            Assert.False(afterDisposeSeen);
        }
        finally
        {
            watcher.Dispose();
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}

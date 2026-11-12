using CommunityToolkit.Mvvm.ComponentModel;
using DPlayer.Models;

namespace DPlayer.ViewModels;

public sealed partial class LibraryViewModel
{
    private static readonly LibrarySearchResults EmptySearchResults = new(
        Array.Empty<LibraryArtist>(),
        Array.Empty<LibraryAlbum>(),
        Array.Empty<Track>());

    private LibrarySearchResults _searchResults = EmptySearchResults;
    private LibraryNavigationSnapshot? _searchReturn;
    private long _queryVersion;
    private CancellationTokenSource? _queryCts;
    private Task? _queryTask;
    private volatile bool _searchDirty;

    [ObservableProperty]
    public partial string SearchQuery { get; private set; } = string.Empty;

    public LibrarySearchResults SearchResults
    {
        get => _searchResults;
        private set
        {
            _searchResults = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial string SearchMessage { get; private set; } = string.Empty;

    public Task UpdateSearchAsync(string query)
    {
        if (_cleaned) return Task.CompletedTask;
        query ??= string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            ClearSearch();
            return Task.CompletedTask;
        }

        if (!IsSearchPage)
        {
            _searchReturn = CurrentNavigation();
            Page = LibraryPage.Search;
        }

        SearchQuery = query;
        SearchMessage = string.Empty;
        _searchDirty = false;

        var version = Interlocked.Increment(ref _queryVersion);
        _queryCts?.Cancel();
        var snapshot = BuildSearchSnapshot();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _queryCts = cts;
        return _queryTask = RunSearchAsync(query, version, snapshot, cts, delay: true);
    }

    public void ClearSearch()
    {
        if (_cleaned) return;
        Interlocked.Increment(ref _queryVersion);
        _queryCts?.Cancel();
        _searchDirty = false;
        SearchQuery = string.Empty;
        SearchResults = EmptySearchResults;
        SearchMessage = string.Empty;

        if (Page == LibraryPage.Search)
            RestoreSearchReturn();
        _searchReturn = null;
    }

    public void OpenSearchArtist(LibraryArtist artist)
    {
        var current = Artists.FirstOrDefault(candidate => string.Equals(
            candidate.Name, artist.Name, StringComparison.CurrentCultureIgnoreCase));
        if (current is null) return;

        ClearSearchForNavigation();
        OpenArtist(current);
    }

    public void OpenSearchAlbum(LibraryAlbum album)
    {
        var key = LibraryIdentity.AlbumOf(album);
        var current = Albums.FirstOrDefault(candidate =>
            LibraryIdentity.AlbumComparer.Equals(
                LibraryIdentity.AlbumOf(candidate), key));
        if (current is null) return;

        ClearSearchForNavigation();
        OpenAlbum(current, fromArtist: false);
    }

    public async Task PlaySearchTrackAsync(Track track)
    {
        if (_cleaned) return;

        var album = Albums.FirstOrDefault(candidate => candidate.Tracks.Any(item =>
            string.Equals(item.FilePath, track.FilePath,
                StringComparison.OrdinalIgnoreCase)));
        var index = -1;
        if (album is not null)
        {
            for (var i = 0; i < album.Tracks.Count; i++)
            {
                if (!string.Equals(album.Tracks[i].FilePath, track.FilePath,
                    StringComparison.OrdinalIgnoreCase))
                    continue;
                index = i;
                break;
            }
        }

        if (album is null || index < 0)
        {
            SearchMessage = "该曲目已不在音乐库中";
            return;
        }

        await _playlists.PlayLibraryTracksAsync(
            album.Tracks, index, album.Title).ConfigureAwait(true);
    }

    public LibraryNavigationSnapshot CaptureNavigation()
    {
        if (Page != LibraryPage.Search)
            return CurrentNavigation();

        return ResolveNavigation(_searchReturn ?? new ArtistsNavigation());
    }

    private LibraryNavigationSnapshot CurrentNavigation() => Page switch
    {
        LibraryPage.ArtistDetail when SelectedArtist is { } artist =>
            new ArtistDetailNavigation(artist.Name),
        LibraryPage.AlbumDetail when SelectedAlbum is { } album =>
            new AlbumDetailNavigation(
                SelectedArtist?.Name,
                LibraryIdentity.AlbumOf(album),
                _albumOpenedFromArtist,
                album.Tracks.FirstOrDefault()?.FilePath),
        LibraryPage.Albums => new AlbumsNavigation(),
        _ => new ArtistsNavigation(),
    };

    private LibraryNavigationSnapshot ResolveNavigation(
        LibraryNavigationSnapshot snapshot)
    {
        switch (snapshot)
        {
            case ArtistDetailNavigation detail:
            {
                var artist = Artists.FirstOrDefault(candidate => string.Equals(
                    candidate.Name, detail.ArtistName,
                    StringComparison.CurrentCultureIgnoreCase));
                return artist is null
                    ? new ArtistsNavigation()
                    : new ArtistDetailNavigation(artist.Name);
            }
            case AlbumDetailNavigation detail:
            {
                var album = Albums.FirstOrDefault(candidate =>
                    LibraryIdentity.AlbumComparer.Equals(
                        LibraryIdentity.AlbumOf(candidate), detail.Album));
                if (album is null)
                    return new AlbumsNavigation();

                var artist = detail.OpenedFromArtist
                    ? Artists.FirstOrDefault(candidate => string.Equals(
                        candidate.Name, detail.ArtistName,
                        StringComparison.CurrentCultureIgnoreCase))
                    : null;
                return new AlbumDetailNavigation(
                    artist?.Name,
                    LibraryIdentity.AlbumOf(album),
                    detail.OpenedFromArtist && artist is not null,
                    album.Tracks.FirstOrDefault()?.FilePath);
            }
            case AlbumsNavigation:
                return new AlbumsNavigation();
            default:
                return new ArtistsNavigation();
        }
    }

    private void RestoreSearchReturn()
    {
        var resolved = _searchReturn is { } pending
            ? ResolveNavigation(pending)
            : new ArtistsNavigation();
        _searchReturn = null;
        SelectedArtist = null;
        SelectedAlbum = null;
        _albumOpenedFromArtist = false;

        switch (resolved)
        {
            case ArtistDetailNavigation detail:
                SelectedArtist = Artists.FirstOrDefault(candidate => string.Equals(
                    candidate.Name, detail.ArtistName,
                    StringComparison.CurrentCultureIgnoreCase));
                Page = SelectedArtist is null
                    ? LibraryPage.Artists : LibraryPage.ArtistDetail;
                break;
            case AlbumDetailNavigation detail:
                SelectedAlbum = Albums.FirstOrDefault(candidate =>
                    LibraryIdentity.AlbumComparer.Equals(
                        LibraryIdentity.AlbumOf(candidate), detail.Album));
                if (SelectedAlbum is null)
                {
                    Page = LibraryPage.Albums;
                    break;
                }
                SelectedArtist = detail.ArtistName is { } artistName
                    ? Artists.FirstOrDefault(candidate => string.Equals(
                        candidate.Name, artistName,
                        StringComparison.CurrentCultureIgnoreCase))
                    : null;
                _albumOpenedFromArtist =
                    detail.OpenedFromArtist && SelectedArtist is not null;
                Page = LibraryPage.AlbumDetail;
                _ = EnsureCoverAsync(SelectedAlbum);
                break;
            case AlbumsNavigation:
                Page = LibraryPage.Albums;
                break;
            default:
                Page = LibraryPage.Artists;
                break;
        }
    }

    private void ClearSearchForNavigation()
    {
        if (_cleaned) return;
        Interlocked.Increment(ref _queryVersion);
        _queryCts?.Cancel();
        _searchDirty = false;
        SearchQuery = string.Empty;
        SearchResults = EmptySearchResults;
        SearchMessage = string.Empty;
        _searchReturn = null;
        SelectedArtist = null;
        SelectedAlbum = null;
    }

    private LibrarySearchSnapshot BuildSearchSnapshot() => new(
        Volatile.Read(ref _libraryVersion),
        Artists.ToArray(),
        Albums.ToArray(),
        _tracksByPath.Values.ToArray());

    private async Task RunSearchAsync(
        string query, long version, LibrarySearchSnapshot snapshot,
        CancellationTokenSource cts, bool delay)
    {
        var token = cts.Token;
        try
        {
            if (delay)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), _clock, token)
                    .ConfigureAwait(false);
            }

            var result = await Task.Run(
                () => LibrarySearch.Match(snapshot, query, token), token)
                .ConfigureAwait(false);

            await RunOnUiAsync(() =>
            {
                if (_cleaned || token.IsCancellationRequested ||
                    version != Volatile.Read(ref _queryVersion) || !IsSearchPage)
                    return;
                if (snapshot.Version != Volatile.Read(ref _libraryVersion))
                {
                    _searchDirty = true;
                    return;
                }

                SearchResults = result;
                SearchMessage =
                    result.Artists.Count + result.Albums.Count + result.Tracks.Count == 0
                        ? "未找到匹配的艺术家、专辑或曲目"
                        : string.Empty;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 用户改词/清空或库关闭后，旧查询自行结束。
        }
        finally
        {
            if (_searchDirty && version == Volatile.Read(ref _queryVersion) &&
                !_cleaned)
            {
                try
                {
                    _syncContext.Post(_ => RecomputeSearchOnUi(version), null);
                }
                catch
                {
                    // UI 上下文已关闭。
                }
            }

            // 已被更新查询顶替的 CTS 归本任务所有；仍是当前查询时由 Cleanup 统一释放。
            if (!ReferenceEquals(Volatile.Read(ref _queryCts), cts))
                cts.Dispose();
        }
    }

    private void RecomputeSearchAfterLibraryChange()
    {
        if (_cleaned || !IsSearchPage || string.IsNullOrWhiteSpace(SearchQuery))
            return;

        _searchDirty = true;
        if (_queryTask is { IsCompleted: false }) return;
        RecomputeSearchOnUi(Volatile.Read(ref _queryVersion));
    }

    private void RecomputeSearchOnUi(long version)
    {
        if (_cleaned || !_searchDirty ||
            version != Volatile.Read(ref _queryVersion) ||
            !IsSearchPage || string.IsNullOrWhiteSpace(SearchQuery))
            return;

        _searchDirty = false;
        var snapshot = BuildSearchSnapshot();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _queryCts = cts;
        _queryTask = RunSearchAsync(SearchQuery, version, snapshot, cts, delay: false);
    }
}

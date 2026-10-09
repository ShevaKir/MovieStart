using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MovieStart.Agent.Downloads;
using MovieStart.Agent.Library;
using MovieStart.Shared;
using MovieStart.Shared.Library;

namespace MovieStart.Agent.Tests;

public sealed class LibraryEndpointsTests : IDisposable
{
    private const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

    private const string PosterUrl = "https://image.tmdb.org/t/p/w342/dune.jpg";

    private readonly FakeQbitClient _qbit = new();
    private readonly FakeTmdbImages _tmdb = new();
    private readonly AgentFactory _factory;

    public LibraryEndpointsTests()
    {
        _factory = new AgentFactory(services =>
        {
            services.AddSingleton<IQbitClient>(_qbit);
            services.AddHttpClient<PosterStore>().ConfigurePrimaryHttpMessageHandler(() => _tmdb);
        });
    }

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddsAndListsItems()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(ApiRoutes.Library, new AddToLibraryRequest(MediaKind.Series, "Shogun", Magnet, Season: 1), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<LibraryItem>(Token);
        Assert.Equal(ApiRoutes.LibraryItem(created!.Id), response.Headers.Location?.ToString());

        var items = await client.GetFromJsonAsync<List<LibraryItem>>(ApiRoutes.Library, Token);
        Assert.Equal(MediaKind.Series, Assert.Single(items!).Kind);
    }

    [Fact]
    public async Task ReportsStorage()
    {
        using var client = _factory.CreateClient();

        var storage = await client.GetFromJsonAsync<StorageInfo>(ApiRoutes.Storage, Token);

        Assert.True(storage!.TotalBytes > 0);
        Assert.InRange(storage.FreeBytes, 1, storage.TotalBytes);
    }

    [Fact]
    public async Task UnknownItemIsNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync(ApiRoutes.LibraryItem(Guid.NewGuid()), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PlayingUnfinishedItemIsConflict()
    {
        using var client = _factory.CreateClient();
        var created = await (await client.PostAsJsonAsync(ApiRoutes.Library, new AddToLibraryRequest(MediaKind.Movie, "Dune", Magnet), Token))
            .Content.ReadFromJsonAsync<LibraryItem>(Token);

        var response = await client.PostAsJsonAsync(ApiRoutes.LibraryPlay(created!.Id), new PlayItemRequest(), Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task InvalidRequestIsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(ApiRoutes.Library, new AddToLibraryRequest(MediaKind.Movie, "Dune", "not a link"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnavailableTorrentClientIsServiceUnavailable()
    {
        _qbit.Unavailable = true;
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(ApiRoutes.Library, new AddToLibraryRequest(MediaKind.Movie, "Dune", Magnet), Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task PosterIsStoredWithTheItemAndDeletedWithIt()
    {
        using var client = _factory.CreateClient();
        var created = await (await client.PostAsJsonAsync(
                ApiRoutes.Library, new AddToLibraryRequest(MediaKind.Movie, "Dune", Magnet, PosterUrl: PosterUrl), Token))
            .Content.ReadFromJsonAsync<LibraryItem>(Token);
        var stored = Path.Combine(_factory.MediaRoot, created!.Id.ToString("N"), PosterStore.FileName);

        var first = await client.GetByteArrayAsync(ApiRoutes.LibraryPoster(created.Id), Token);
        var second = await client.GetByteArrayAsync(ApiRoutes.LibraryPoster(created.Id), Token);

        Assert.Equal(FakeTmdbImages.Image, first);
        Assert.Equal(FakeTmdbImages.Image, second);
        Assert.Equal([PosterUrl], _tmdb.Requests);
        Assert.True(File.Exists(stored));

        await client.DeleteAsync(ApiRoutes.LibraryItem(created.Id), Token);

        Assert.False(File.Exists(stored));
    }

    [Fact]
    public async Task ItemWithoutPosterHasNone()
    {
        using var client = _factory.CreateClient();
        var created = await (await client.PostAsJsonAsync(ApiRoutes.Library, new AddToLibraryRequest(MediaKind.Movie, "Dune", Magnet), Token))
            .Content.ReadFromJsonAsync<LibraryItem>(Token);

        var response = await client.GetAsync(ApiRoutes.LibraryPoster(created!.Id), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class FakeTmdbImages : HttpMessageHandler
    {
        public static readonly byte[] Image = [0xFF, 0xD8, 0xFF, 0xE0];

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Image) });
        }
    }
}

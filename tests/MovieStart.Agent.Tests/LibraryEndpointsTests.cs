using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MovieStart.Agent.Downloads;
using MovieStart.Shared;
using MovieStart.Shared.Library;

namespace MovieStart.Agent.Tests;

public sealed class LibraryEndpointsTests : IDisposable
{
    private const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

    private readonly FakeQbitClient _qbit = new();
    private readonly AgentFactory _factory;

    public LibraryEndpointsTests()
    {
        _factory = new AgentFactory(services => services.AddSingleton<IQbitClient>(_qbit));
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
}

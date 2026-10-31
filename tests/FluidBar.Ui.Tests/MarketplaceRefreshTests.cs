using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FluidBar.App.Views.Market;
using FluidBar.Marketplace.Catalog;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Signing;

namespace FluidBar.Ui.Tests;

public sealed class MarketplaceRefreshTests
{
    [Fact]
    public async Task Browse_refreshes_the_signed_catalog_and_keeps_conditional_requests()
    {
        await AvaloniaTest.RunAsync(async () =>
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "../.."));
            var index = MarketplaceIndexParser.Parse(File.ReadAllText(Path.Combine(root, "marketplace/index.json")));
            var first = index with { Plugins = [index.Plugins![0]] };
            var firstJson = IndexSigner.CreateSignedJson(first, [IndexSigner.Sign(first, key, "test-market")]);
            var secondJson = IndexSigner.CreateSignedJson(index, [IndexSigner.Sign(index, key, "test-market")]);
            using var handler = new CatalogHandler(firstJson, secondJson);
            using var client = new MarketplaceCatalogClient(new MarketplaceCatalogOptions
            {
                IndexUrl = new Uri("https://market.example/index.json"),
                Trust = MarketplaceTrustOptions.ForMarketplaceKeys(TrustedKey.FromPublicKey(key, "test-market", SignatureRole.Marketplace)),
            }, handler);
            using var service = new MarketplaceServiceAdapter(TestFixtures.CreateLocalizer("en-US"), "stable", client);
            var query = new MarketplaceQuery(null, "all", "stable");
            Assert.Single(await service.BrowseAsync(query, CancellationToken.None));
            Assert.Equal(2, (await service.BrowseAsync(query, CancellationToken.None)).Count);
            Assert.Equal(2, (await service.BrowseAsync(query, CancellationToken.None)).Count);
            Assert.Equal(3, handler.Requests);
            Assert.Equal([null, "\"old\"", "\"new\""], handler.ETags);
        });
    }

    private static string Source([System.Runtime.CompilerServices.CallerFilePath] string source = "") => source;

    private sealed class CatalogHandler(string first, string second) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public List<string?> ETags { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ETags.Add(request.Headers.IfNoneMatch.FirstOrDefault()?.Tag);
            Requests++;
            var response = new HttpResponseMessage(Requests >= 3 ? HttpStatusCode.NotModified : HttpStatusCode.OK);
            if (Requests < 3)
            {
                response.Content = new StringContent(Requests == 1 ? first : second, Encoding.UTF8, "application/json");
                response.Headers.ETag = new EntityTagHeaderValue(Requests == 1 ? "\"old\"" : "\"new\"");
            }
            return Task.FromResult(response);
        }
    }
}

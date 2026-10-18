using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Signing;

namespace FluidBar.Sdk.Tests;

/// <summary>
/// Guards the committed sample marketplace index: it must stay schema-valid and signature-valid against the
/// committed sample public key, so the App/UI can be developed against a realistic signed document.
/// </summary>
public sealed class SampleMarketplaceIndexTests
{
    [Fact]
    public void Committed_sample_index_verifies_against_committed_keys()
    {
        var repositoryRoot = TestPaths.RepoRoot();
        var indexPath = Path.Combine(repositoryRoot, "marketplace", "index.json");
        var keysPath = Path.Combine(repositoryRoot, "marketplace", "keys.json");
        Assert.True(File.Exists(indexPath), $"Sample index missing at '{indexPath}'.");
        Assert.True(File.Exists(keysPath), $"Sample keys missing at '{keysPath}'.");

        var indexJson = File.ReadAllText(indexPath);
        var document = MarketplaceIndexParser.Parse(indexJson);
        var keys = ReadKeys(File.ReadAllText(keysPath));
        Assert.NotEmpty(keys);

        var trust = new MarketplaceTrustOptions { MarketplaceKeys = keys };
        var verifier = new IndexSignatureVerifier(trust);
        var result = verifier.Verify(indexJson);

        Assert.True(result.Verified, string.Join("; ", result.Errors));
        Assert.Equal(2, document.Plugins!.Count);
    }

    private static List<TrustedKey> ReadKeys(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var keys = new List<TrustedKey>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            keys.Add(new TrustedKey
            {
                KeyId = element.GetProperty("keyId").GetString() ?? throw new InvalidOperationException("keyId missing"),
                Role = SignatureRole.Marketplace,
                Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
                PublicKeySpkiBase64 = element.GetProperty("publicKey").GetString() ?? throw new InvalidOperationException("publicKey missing"),
                NotBefore = element.TryGetProperty("notBefore", out var notBefore) ? notBefore.GetDateTimeOffset() : null,
                NotAfter = element.TryGetProperty("notAfter", out var notAfter) ? notAfter.GetDateTimeOffset() : null,
            });
        }

        return keys;
    }
}

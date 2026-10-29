using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Manifest;

namespace FluidBar.App.Plugins;

/// <summary>Production marketplace location and the public key trusted to sign its catalog.</summary>
/// <remarks>
/// The private key is deliberately absent from source control. It is kept offline and supplied to the protected
/// marketplace-publish CI environment as MARKETPLACE_SIGNING_KEY_PEM. Rotating this public key without shipping
/// a client update would make every previously shipped build reject the catalog, so rotate by adding a new key.
/// </remarks>
internal static class MarketplaceReleaseConfiguration
{
    /// <summary>Canonical signed marketplace index.</summary>
    public const string IndexUrl = "https://fluidbar.doulor.cn/market/index.json";

    /// <summary>Key identifier embedded in signed catalogs.</summary>
    public const string KeyId = "fluidbar-marketplace-2026";

    /// <summary>SPKI/PKIX public key for the offline ECDSA P-256 signing key generated on 2026-10-30.</summary>
    public const string PublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAErQ4ZzxaXz6R9J6Z+/E+DG5BTv6ufj92H1RQ+KgdTeoxvpP4D//yGgez0HWmIIG8SC0y9MKYpcbRVlPuyr82aUA==";

    /// <summary>Trust used by production builds; environment variables can override it for development.</summary>
    public static MarketplaceTrustOptions Trust => MarketplaceTrustOptions.ForMarketplaceKeys(new TrustedKey
    {
        KeyId = KeyId,
        Role = SignatureRole.Marketplace,
        PublicKeySpkiBase64 = PublicKeySpkiBase64,
    });
}

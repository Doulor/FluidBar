using Windows.Devices.Geolocation;

namespace FluidBar.Platform.Windows.Weather;

/// <summary>
/// Real device location through the Windows location service (Wi-Fi/cell/GPS), used before the
/// public-IP geolocation fallback. Unlike an IP lookup this reflects the machine's physical
/// surroundings and is not changed by a VPN/proxy exit node (report 2026-11-01: Tokyo IP while the
/// desktop is in China).
/// </summary>
public static class WindowsLocationProvider
{
    private static readonly TimeSpan AccessTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PositionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Asks Windows for a coarse position. Returns null when the user/device policy denies location,
    /// the service is unavailable, or the request times out; callers then fall back to IP geolocation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<GeoLocation?> TryGetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(AccessTimeout);
            var access = await Geolocator.RequestAccessAsync().AsTask(timeout.Token).ConfigureAwait(false);
            if (access != GeolocationAccessStatus.Allowed)
            {
                return null;
            }

            timeout.CancelAfter(PositionTimeout);
            var locator = new Geolocator
            {
                // Weather only needs a city-sized fix; a coarse request is faster and avoids turning on
                // GPS hardware when Wi-Fi/network positioning is enough.
                DesiredAccuracyInMeters = 1000,
            };
            var position = await locator
                .GetGeopositionAsync(TimeSpan.FromMinutes(10), PositionTimeout)
                .AsTask(timeout.Token)
                .ConfigureAwait(false);
            var point = position.Coordinate?.Point;
            if (point is null)
            {
                return null;
            }

            var coordinate = point.Position;
            if (!double.IsFinite(coordinate.Latitude) || !double.IsFinite(coordinate.Longitude))
            {
                return null;
            }

            var address = position.CivicAddress;
            var city = address?.City?.Trim();
            var country = address?.Country?.Trim();
            var label = string.IsNullOrEmpty(city)
                ? null
                : string.IsNullOrEmpty(country) ? city : $"{city}, {country}";
            return new GeoLocation(coordinate.Latitude, coordinate.Longitude, label);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null; // our own access/position timeout
        }
        catch (Exception)
        {
            // UnauthorizedAccessException, COM exceptions, location disabled: the IP fallback remains.
            return null;
        }
    }
}

using System.Net;
using FluidBar.Platform.Windows.Telemetry;

namespace FluidBar.Platform.Tests.Telemetry;

public class HttpTelemetrySinkTests
{
    private static readonly byte[] Body = "{\"schema\":1}"u8.ToArray();

    [Fact]
    public async Task AcceptedBatch_ReportsSent()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var sink = new HttpTelemetrySink(
            new Uri("https://t.example.test/v1/batch"), new HttpClient(handler), attempts: 1);

        var result = await sink.SendAsync(Body, new string('a', 32), "4.0.0-preview.4");

        Assert.Equal(TelemetrySendStatus.Sent, result.Status);
        Assert.Equal(202, result.StatusCode);
        Assert.Single(handler.Requests);
        Assert.Equal("application/json", handler.Requests[0].MediaType);
    }

    [Fact]
    public async Task ServerError_IsRetriedThenReportedAsFailed()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var sink = new HttpTelemetrySink(
            new Uri("https://t.example.test/v1/batch"), new HttpClient(handler),
            attempts: 2, retryDelay: TimeSpan.FromMilliseconds(1));

        var result = await sink.SendAsync(Body, new string('a', 32), "4.0.0-preview.4");

        Assert.Equal(TelemetrySendStatus.Failed, result.Status);
        Assert.True(result.ShouldRetry);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RejectedBatch_IsNotRetried()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var sink = new HttpTelemetrySink(
            new Uri("https://t.example.test/v1/batch"), new HttpClient(handler),
            attempts: 3, retryDelay: TimeSpan.FromMilliseconds(1));

        var result = await sink.SendAsync(Body, new string('a', 32), "4.0.0-preview.4");

        Assert.Equal(TelemetrySendStatus.Rejected, result.Status);
        Assert.False(result.ShouldRetry);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MissingEndpoint_ReportsUnavailable()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var sink = new HttpTelemetrySink(
            new Uri("https://t.example.test/v1/batch"), new HttpClient(handler), attempts: 1);

        var result = await sink.SendAsync(Body, new string('a', 32), "4.0.0-preview.4");

        Assert.Equal(TelemetrySendStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task NetworkFailure_IsSwallowedAsFailed()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("offline"));
        using var sink = new HttpTelemetrySink(
            new Uri("https://t.example.test/v1/batch"), new HttpClient(handler),
            attempts: 2, retryDelay: TimeSpan.FromMilliseconds(1));

        var result = await sink.SendAsync(Body, new string('a', 32), "4.0.0-preview.4");

        Assert.Equal(TelemetrySendStatus.Failed, result.Status);
        Assert.Equal("HttpRequestException", result.Detail);
    }

    [Fact]
    public async Task EmptyPayload_IsRejectedWithoutNetwork()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var sink = new HttpTelemetrySink(
            new Uri("https://t.example.test/v1/batch"), new HttpClient(handler), attempts: 1);

        var result = await sink.SendAsync(ReadOnlyMemory<byte>.Empty, new string('a', 32), "4.0.0-preview.4");

        Assert.Equal(TelemetrySendStatus.Rejected, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("4.0.0-preview.4", "preview")]
    [InlineData("4.0.0", "stable")]
    [InlineData("3.2.0-rc.1", "preview")]
    public void Channel_IsDerivedFromTheVersionStamp(string version, string expected)
    {
        Assert.Equal(expected, WindowsTelemetryEnvironment.ChannelOf(version));
    }

    [Fact]
    public void Environment_ReportsOsAndArchWithoutIdentifiers()
    {
        var environment = WindowsTelemetryEnvironment.Create("4.0.0-preview.4", selfContained: true, "zh-Hans");

        Assert.Equal("4.0.0-preview.4", environment.AppVersion);
        Assert.Equal("preview", environment.Channel);
        Assert.True(environment.SelfContained);
        Assert.True(environment.OsRelease.Split('.').Length >= 3);
        Assert.False(string.IsNullOrWhiteSpace(environment.Arch));
        Assert.Equal("zh-Hans", environment.Locale);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<(string? MediaType, string? UserAgent)> Requests { get; } = [];

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Content?.Headers.ContentType?.MediaType, request.Headers.UserAgent.ToString()));
            return Task.FromResult(_respond(request));
        }
    }
}

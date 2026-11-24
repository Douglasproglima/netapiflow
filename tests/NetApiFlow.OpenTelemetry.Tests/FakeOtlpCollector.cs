using System.Collections.Concurrent;
using System.Net;
using System.Text;
using OpenTelemetry.Exporter;

namespace NetApiFlow.OpenTelemetry.Tests;

internal sealed class FakeOtlpCollector
{
    private readonly ConcurrentQueue<byte[]> _requests = new();

    public IReadOnlyList<byte[]> Requests => [.. _requests];

    public void Configure(OtlpExporterOptions options)
    {
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
        options.Endpoint = new Uri("http://collector.test/v1/traces");
        options.HttpClientFactory = () => new HttpClient(new RecordingHandler(_requests));
    }

    public bool ReceivedTheScope(string scopeName)
    {
        var name = Encoding.UTF8.GetBytes(scopeName);
        return _requests.Any(body => body.AsSpan().IndexOf(name) >= 0);
    }

    private sealed class RecordingHandler(ConcurrentQueue<byte[]> requests) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = new MemoryStream();
            request.Content?.CopyTo(body, null, cancellationToken);
            return Record(body.ToArray());
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Record(request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken));

        private HttpResponseMessage Record(byte[] body)
        {
            requests.Enqueue(body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        }
    }
}

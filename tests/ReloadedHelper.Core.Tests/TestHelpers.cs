using System.Net;
using System.Net.Http;

namespace ReloadedHelper.Core.Tests;

internal sealed class FakeHttpMessageHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    public string? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequestUri = request.RequestUri?.ToString();
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        });
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TeachWell.Application;

namespace TeachWell.Infrastructure.Canvas;

public sealed partial class CanvasPatClient(HttpClient http, CanvasHostPolicy policy) : ICanvasClient
{
    public async Task<CanvasValidation> ValidateConnectionAsync(Uri origin, string token, CancellationToken ct)
    {
        if (policy.ParseOrigin(origin.AbsoluteUri) is null) return new(null, CanvasError.Unavailable);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "api/v1/users/self/profile"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return new(null, CanvasError.InvalidToken);
            if (response.StatusCode == HttpStatusCode.Forbidden) return new(null, CanvasError.PermissionDenied);
            if ((int)response.StatusCode == 429) return new(null, CanvasError.RateLimited);
            if ((int)response.StatusCode is >= 300 and < 400) return new(null, CanvasError.RedirectBlocked);
            if (!response.IsSuccessStatusCode) return new(null, CanvasError.Unavailable);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), new JsonDocumentOptions { MaxDepth = 16 }, ct);
            if (!json.RootElement.TryGetProperty("id", out var id) || id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)) return new(null, CanvasError.UnexpectedResponse);
            var value = id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText();
            if (string.IsNullOrEmpty(value) || value.Length > 80 || value[0] == '0' || value.Any(c => c is < '0' or > '9')) return new(null, CanvasError.UnexpectedResponse);
            return new(value, CanvasError.None);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(null, CanvasError.Timeout); }
        catch (HttpRequestException) { return new(null, CanvasError.Unavailable); }
        catch (JsonException) { return new(null, CanvasError.UnexpectedResponse); }
        catch (InvalidOperationException) { return new(null, CanvasError.UnexpectedResponse); }
    }
}

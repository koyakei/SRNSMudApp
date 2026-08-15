#pragma warning disable CA1848

using System.Net.Http.Headers;
using System.Text.Json;

using Google.Apis.Auth;

namespace SRNSMudApp.Services.Auth;

public class ExternalTokenVerificationService(HttpClient httpClient, ILogger<ExternalTokenVerificationService> logger) : IExternalTokenVerificationService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<ExternalTokenVerificationService> _logger = logger;

    public async Task<(string? Email, string? ProviderKey)> VerifyTokenAsync(string provider, string token)
    {
        return provider.ToUpperInvariant() switch
        {
            "GOOGLE" => await VerifyGoogleTokenAsync(token),
            "LINE" => await VerifyLineTokenAsync(token),
            "GITHUB" => await VerifyGithubTokenAsync(token),
            _ => (null, null)
        };
    }

    private async Task<(string? Email, string? ProviderKey)> VerifyGoogleTokenAsync(string idToken)
    {
        try
        {
            GoogleJsonWebSignature.Payload payload = await GoogleJsonWebSignature.ValidateAsync(idToken);
            return (payload.Email, payload.Subject);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Invalid Google ID token");
            return (null, null);
        }
    }

    private async Task<(string? Email, string? ProviderKey)> VerifyLineTokenAsync(string idToken)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            HttpResponseMessage response = await _httpClient.GetAsync(new Uri("https://api.line.me/v2/profile"));

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Invalid LINE token");
                return (null, null);
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var userId = doc.RootElement.GetProperty("userId").GetString();
            return (null, userId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP Error verifying LINE token");
            return (null, null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON Error verifying LINE token");
            return (null, null);
        }
    }

    private async Task<(string? Email, string? ProviderKey)> VerifyGithubTokenAsync(string codeOrToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com/user"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", codeOrToken);
            request.Headers.UserAgent.ParseAdd("SRNSMudApp");

            HttpResponseMessage response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Invalid GitHub token");
                return (null, null);
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var id = doc.RootElement.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var email = doc.RootElement.TryGetProperty("email", out JsonElement emailProp) && emailProp.ValueKind != JsonValueKind.Null
                ? emailProp.GetString()
                : null;

            return (email, id);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP Error verifying GitHub token");
            return (null, null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON Error verifying GitHub token");
            return (null, null);
        }
    }
}
#pragma warning restore CA1848
#region

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

#endregion

namespace SRNSMudApp.Tests;

public class LoginIntegrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PostLogin_WithoutFormName_ShouldFailWithAntiforgeryOrRoutingError()
    {
        // Arrange
        HttpClient client = factory.CreateClient();

        // Generate a valid Antiforgery token using the DI container
        using IServiceScope scope = factory.Services.CreateScope();
        IAntiforgery antiforgery = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var httpContext = new DefaultHttpContext();
        AntiforgeryTokenSet tokenSet = antiforgery.GetAndStoreTokens(httpContext);

        // Prepare POST data without specifying the form name (_handler)
        // This simulates what happened when `this.internals.form.submit()` was called in PasskeySubmit.razor.js
        var postData = new Dictionary<string, string>
        {
            { tokenSet.FormFieldName, tokenSet.RequestToken },
            { "Input.Email", "test@example.com" },
            { "Input.Password", "Password123!" },
            { "__passkeySubmit", "" } // Simulating the passkey submit button
        };

        var content = new FormUrlEncodedContent(postData);

        // Add the antiforgery cookie to the request
        if (httpContext.Response.Headers.TryGetValue("Set-Cookie", out StringValues cookieValues))
        {
            // Extract just the cookie value before the first semicolon
            var cookie = cookieValues.ToString().Split(';')[0];
            client.DefaultRequestHeaders.Add("Cookie", cookie);
        }

        // Act
        // We POST to /Account/Login without "_handler" field
        HttpResponseMessage postResponse = await client.PostAsync("/Account/Login", content);

        // Assert
        var postResponseString = await postResponse.Content.ReadAsStringAsync();

        // The test passes if it fails (not success status code), and the error message contains the expected string.
        Assert.False(postResponse.IsSuccessStatusCode, "Expected the POST request to fail, but it succeeded.");

        // Assert that the error contains "does not specify which form is being submitted"
        Assert.Contains("does not specify which form is being submitted", postResponseString);
    }
}
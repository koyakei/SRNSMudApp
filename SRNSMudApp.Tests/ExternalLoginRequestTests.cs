using System.ComponentModel.DataAnnotations;

using SRNSMudApp.Models;

namespace SRNSMudApp.Tests;

public class ExternalLoginRequestTests
{
    [Fact]
    public void ExternalLoginRequest_Requires_Provider_And_Token()
    {
        var request = new ExternalLoginRequest { Provider = string.Empty, Token = string.Empty };
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result => result.MemberNames.Contains(nameof(ExternalLoginRequest.Provider)));
        Assert.Contains(validationResults, result => result.MemberNames.Contains(nameof(ExternalLoginRequest.Token)));
    }
}

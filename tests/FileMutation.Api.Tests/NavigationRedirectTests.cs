using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FileMutation.Api.Tests;

/// <summary>
/// The root and the habitual /swagger path lead to the Scalar UI, so the first URL
/// anyone types works instead of 404.
/// </summary>
public sealed class NavigationRedirectTests(FileMutationApiFactory factory)
    : IClassFixture<FileMutationApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/")]
    [InlineData("/swagger")]
    public async Task Landing_paths_redirect_to_the_Scalar_UI(string path)
    {
        var response = await _client.GetAsync(path).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/scalar/", response.Headers.Location?.ToString());
    }
}

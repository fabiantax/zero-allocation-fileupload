using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FileMutation.Api.Tests;

public sealed class FileMutateEndpointTests(FileMutationApiFactory factory) : IClassFixture<FileMutationApiFactory>
{
    private readonly HttpClient _client = CreateClient(factory);

    [Fact]
    public async Task Valid_text_upload_returns_mutated_content_and_original_file_name()
    {
        using var content = CreateUpload("original content"u8.ToArray(), "original-name.txt", "text/plain");

        using var response = await PostAsync(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("original-name.txt", response.Content.Headers.ContentDisposition?.FileNameStar ??
            response.Content.Headers.ContentDisposition?.FileName?.Trim('\"'));
        Assert.Equal("original content\n2026-09-22:Ab9Cd8Ef7Gh6Ij5K", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_with_invalid_final_utf8_bytes_returns_problem_before_a_file_response_starts()
    {
        using var content = CreateUpload([0x61, 0xC3], "invalid.txt", "text/plain");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Json_upload_with_valid_utf8_is_rejected_as_unsupported_media_type()
    {
        using var content = CreateUpload("{\"ready\":true}"u8.ToArray(), "payload.json", "text/plain");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Upload_larger_than_the_configured_limit_returns_problem()
    {
        using var content = CreateUpload(Encoding.UTF8.GetBytes("0123456789abcdefg"), "large.txt", "text/plain");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_non_file_file_field_returns_problem(bool includeEmptyField)
    {
        using var content = new MultipartFormDataContent();
        if (includeEmptyField)
        {
            content.Add(new StringContent(string.Empty), "file");
        }

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Empty_text_file_is_rejected()
    {
        // Contract: an empty upload is a client mistake, not a file to mutate.
        // docs/architecture.md, state machine: "missing or empty file field" -> 400.
        using var content = CreateUpload([], "empty.txt", "text/plain");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    private static MultipartFormDataContent CreateUpload(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        return content;
    }

    private static HttpClient CreateClient(FileMutationApiFactory factory)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        return client;
    }

    private Task<HttpResponseMessage> PostAsync(HttpContent content) =>
        _client.PostAsync("/files/mutate", content).WaitAsync(TimeSpan.FromSeconds(5));

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatusCode, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }
}

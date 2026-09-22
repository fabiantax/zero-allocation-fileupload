using System.Net;
using System.Net.Http.Json;
using System.Text;
using FileMutation.Domain.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
        // SetHttpFileName emits both the ASCII-only fallback and filename* for every name.
        Assert.Equal(
            "attachment; filename=original-name.txt; filename*=UTF-8''original-name.txt",
            response.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal("original-name.txt", response.Content.Headers.ContentDisposition?.FileNameStar ??
            response.Content.Headers.ContentDisposition?.FileName?.Trim('\"'));
        Assert.Equal("original content\n2026-09-22:Ab9Cd8Ef7Gh6Ij5K", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Non_ascii_file_name_is_returned_with_rfc5987_filename_star_encoding()
    {
        // SetHttpFileName must emit filename* (RFC 5987) for non-ASCII names. The plain
        // filename= fallback is deliberately lossy ('_' substitution); only filename* is exact.
        using var content = CreateUpload("unicode content"u8.ToArray(), "résumé.txt", "text/plain");

        using var response = await PostAsync(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "attachment; filename=r_sum_.txt; filename*=UTF-8''r%C3%A9sum%C3%A9.txt",
            response.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal("résumé.txt", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("r_sum_.txt", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("unicode content\n2026-09-22:Ab9Cd8Ef7Gh6Ij5K", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_with_invalid_final_utf8_bytes_returns_problem_before_a_file_response_starts()
    {
        using var content = CreateUpload([0x61, 0xC3], "invalid.txt", "text/plain");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task Unsafe_file_names_are_rejected(string fileName)
    {
        // Path fragments and dot-names must never round-trip into Content-Disposition.
        using var content = CreateUpload("safe bytes"u8.ToArray(), fileName, "text/plain");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "The file name is missing or not a valid file name.");
    }

    [Fact]
    public async Task Json_upload_with_valid_utf8_is_rejected_as_unsupported_media_type()
    {
        using var content = CreateUpload("{\"ready\":true}"u8.ToArray(), "payload.json", "text/plain");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Text_file_declared_as_non_text_content_type_returns_problem()
    {
        using var content = CreateUpload("plain bytes"u8.ToArray(), "notes.txt", "application/json");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "Only text/plain content is accepted.");
    }

    [Fact]
    public async Task Non_multipart_request_is_rejected_by_the_accepts_contract()
    {
        // The Accepts metadata rejects non-multipart requests in the framework layer, before
        // the endpoint runs; this pins that contract rather than the endpoint's own branch.
        using var content = new StringContent("{\"not\":\"multipart\"}");
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var response = await PostAsync(content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Multipart_request_without_boundary_returns_problem()
    {
        using var content = new ByteArrayContent("ignored"u8.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "The request must be multipart/form-data.");
    }

    [Fact]
    public async Task Second_file_part_is_rejected()
    {
        using var content = new MultipartFormDataContent();
        var first = new ByteArrayContent("first file"u8.ToArray());
        first.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        var second = new ByteArrayContent("second file"u8.ToArray());
        second.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(first, "file", "first.txt");
        content.Add(second, "file", "second.txt");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Supply exactly one file part named 'file'.");
    }

    [Fact]
    public async Task File_part_with_the_wrong_field_name_is_ignored()
    {
        // Only parts named 'file' count; a well-formed upload under any other field name is a
        // missing-file request, not a file to mutate.
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("valid content"u8.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(file, "attachment", "notes.txt");

        using var response = await PostAsync(content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Supply exactly one non-empty file part named 'file'.");
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

    [Fact]
    public async Task Unhandled_mutation_failure_returns_problem_details()
    {
        // The mutator runs after acceptance has passed but before anything is flushed, so a
        // failure there must surface as RFC 7807 through the IExceptionHandler, not an empty 500.
        using var failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileMutator>();
                services.AddSingleton<IFileMutator>(new ThrowingFileMutator());
            }));
        using var client = failingFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        using var content = CreateUpload("will fail"u8.ToArray(), "boom.txt", "text/plain");

        using var response = await client
            .PostAsync("/files/mutate", content)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("An unexpected error occurred.", problem.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    private static MultipartFormDataContent CreateUpload(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        return content;
    }

    private sealed class ThrowingFileMutator : IFileMutator
    {
        public ValueTask MutateAsync(Stream source, Stream destination, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Deliberate failure for the exception-handling test.");
    }

    private static HttpClient CreateClient(FileMutationApiFactory factory)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        return client;
    }

    private Task<HttpResponseMessage> PostAsync(HttpContent content) =>
        _client.PostAsync("/files/mutate", content).WaitAsync(TimeSpan.FromSeconds(5));

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string? expectedDetail = null)
    {
        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatusCode, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        if (expectedDetail is not null)
        {
            Assert.Equal(expectedDetail, problem.Detail);
        }
    }
}

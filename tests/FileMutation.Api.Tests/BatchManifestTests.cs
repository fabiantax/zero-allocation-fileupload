using System.Text.Json;
using System.Text.Json.Nodes;
using FileMutation.Api.Batches;
using FileMutation.Domain.Batches;
using Xunit;

namespace FileMutation.Api.Tests;

/// <summary>
/// Tests for <c>manifest.json</c>: the manifest and its source-generated context are public so these
/// call them directly. Each test pins one part of the contract (contracts.md, ADR 0011), so a break
/// names the part that moved.
/// </summary>
public sealed class BatchManifestTests
{
    private static readonly BatchId Id = ParseId("3f9c00112233445566778899aabbccdd");

    private static readonly BatchFileOutcome[] Rows =
    [
        new(0, "a.txt", "a.txt", BatchFileStatus.Mutated, null),
        new(1, "a.txt", "a (2).txt", BatchFileStatus.Mutated, null),
        new(2, "b.json", null, BatchFileStatus.Rejected, "UnsupportedFileExtension"),
    ];

    [Fact]
    public void The_manifest_serializes_to_exactly_the_contract_json()
    {
        const string expected =
            """{"batchId":"3f9c00112233445566778899aabbccdd","complete":true,"mutated":2,"rejected":1,"files":[""" +
            """{"index":0,"declaredFileName":"a.txt","entryName":"a.txt","status":"mutated","reasonCode":null},""" +
            """{"index":1,"declaredFileName":"a.txt","entryName":"a (2).txt","status":"mutated","reasonCode":null},""" +
            """{"index":2,"declaredFileName":"b.json","entryName":null,"status":"rejected","reasonCode":"UnsupportedFileExtension"}]}""";

        Assert.Equal(expected, Serialize(complete: true, Rows));
    }

    [Fact]
    public void Property_names_are_camelCase_and_in_contract_order_for_the_manifest_and_every_row()
    {
        var manifest = Parse(complete: true, Rows);

        Assert.Equal(["batchId", "complete", "mutated", "rejected", "files"], Names(manifest));
        Assert.All(
            manifest["files"]!.AsArray(),
            row => Assert.Equal(["index", "declaredFileName", "entryName", "status", "reasonCode"], Names(row!)));
    }

    [Fact]
    public void Status_is_camelCase_text_not_the_member_name_or_a_number()
    {
        var files = Parse(complete: true, Rows)["files"]!.AsArray();

        Assert.Equal(["mutated", "mutated", "rejected"], files.Select(row => row!["status"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(0, 0)]
    public void Counts_are_derived_from_the_rows(int mutated, int rejected)
    {
        var rows = Enumerable.Range(0, mutated + rejected)
            .Select(index => index < mutated
                ? new BatchFileOutcome(index, "a.txt", $"a{index}.txt", BatchFileStatus.Mutated, null)
                : new BatchFileOutcome(index, "b.json", null, BatchFileStatus.Rejected, "UnsupportedFileExtension"))
            .ToArray();

        var manifest = Parse(complete: true, rows);

        Assert.Equal((mutated, rejected), (manifest["mutated"]!.GetValue<int>(), manifest["rejected"]!.GetValue<int>()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Complete_says_whether_the_batch_ran_to_its_end(bool complete)
    {
        Assert.Equal(complete, Parse(complete, Rows)["complete"]!.GetValue<bool>());
    }

    [Fact]
    public void ReasonCode_is_an_explicit_null_for_a_mutated_row_and_the_code_for_a_rejected_one()
    {
        var files = Parse(complete: true, Rows)["files"]!.AsArray();

        Assert.True(files[0]!.AsObject().ContainsKey("reasonCode"));
        Assert.Null(files[0]!["reasonCode"]);
        Assert.Equal("UnsupportedFileExtension", files[2]!["reasonCode"]!.GetValue<string>());
    }

    private static string Serialize(bool complete, BatchFileOutcome[] rows) =>
        JsonSerializer.Serialize(BatchManifest.From(Id, rows, complete), BatchManifestJsonContext.Default.BatchManifest);

    private static JsonNode Parse(bool complete, BatchFileOutcome[] rows) => JsonNode.Parse(Serialize(complete, rows))!;

    private static string[] Names(JsonNode node) => node.AsObject().Select(property => property.Key).ToArray();

    private static BatchId ParseId(string text)
    {
        Assert.True(BatchId.TryParse(text, out var id));
        return id;
    }
}

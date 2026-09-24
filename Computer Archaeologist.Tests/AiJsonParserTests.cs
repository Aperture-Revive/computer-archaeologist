using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Models;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>
/// The AI must never be trusted to return clean JSON. Every shape a real endpoint produces is
/// covered here, and none of them may throw.
/// </summary>
public sealed class AiJsonParserTests
{
    private const string ValidJson = """
        {
          "interestingness": 87,
          "confidence": 74,
          "category": "forgotten_project",
          "summary": "Appears to be an abandoned game prototype from 2017.",
          "reasons": ["old creation date", "looks like a project folder"],
          "historical_significance": 70,
          "personal_significance": 88,
          "technical_significance": 65,
          "rarity": 60,
          "unusualness": 55,
          "story_potential": 80,
          "recommended_action": "include"
        }
        """;

    [Fact]
    public void Valid_json_is_parsed_completely()
    {
        var parsed = AiJsonParser.ParseAnalysis(ValidJson, "test-model");

        Assert.True(parsed.Success);
        var result = parsed.Result!;
        Assert.Equal(87, result.Interestingness, 3);
        Assert.Equal(74, result.Confidence, 3);
        Assert.Equal(ArtifactCategories.ForgottenProject, result.Category);
        Assert.Equal(RecommendedActions.Include, result.RecommendedAction);
        Assert.Equal(2, result.Reasons.Count);
        Assert.Equal("test-model", result.Model);
        Assert.True(result.Succeeded);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void Json_inside_a_markdown_fence_is_parsed()
    {
        var raw = "```json\n" + ValidJson + "\n```";
        var parsed = AiJsonParser.ParseAnalysis(raw);

        Assert.True(parsed.Success);
        Assert.Equal(87, parsed.Result!.Interestingness, 3);
    }

    [Fact]
    public void Json_surrounded_by_prose_is_parsed()
    {
        var raw = "Sure! Here is my analysis:\n\n" + ValidJson + "\n\nLet me know if you need more.";
        var parsed = AiJsonParser.ParseAnalysis(raw);

        Assert.True(parsed.Success);
        Assert.Equal(ArtifactCategories.ForgottenProject, parsed.Result!.Category);
    }

    [Fact]
    public void Numbers_sent_as_strings_are_coerced_and_reported()
    {
        var raw = ValidJson
            .Replace("\"interestingness\": 87", "\"interestingness\": \"87\"")
            .Replace("\"confidence\": 74", "\"confidence\": \"74\"");

        var parsed = AiJsonParser.ParseAnalysis(raw);

        Assert.True(parsed.Success);
        Assert.Equal(87, parsed.Result!.Interestingness, 3);
        Assert.Contains(parsed.Warnings, w => w.Contains("coerced-string", StringComparison.Ordinal));
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var raw = ValidJson
            .Replace("\"interestingness\": 87", "\"interestingness\": 9400")
            .Replace("\"confidence\": 74", "\"confidence\": -30");

        var parsed = AiJsonParser.ParseAnalysis(raw);

        Assert.True(parsed.Success);
        Assert.Equal(100, parsed.Result!.Interestingness, 3);
        Assert.Equal(0, parsed.Result.Confidence, 3);
    }

    [Fact]
    public void Missing_required_fields_are_reported_but_still_produce_a_result()
    {
        var parsed = AiJsonParser.ParseAnalysis("""{ "interestingness": 50 }""");

        Assert.True(parsed.Success);
        Assert.Contains("missing:confidence", parsed.Warnings);
        Assert.Contains("missing:category", parsed.Warnings);
        Assert.Contains("missing:summary", parsed.Warnings);
        Assert.Contains("missing:recommended_action", parsed.Warnings);
        Assert.Equal(ArtifactCategories.Unknown, parsed.Result!.Category);
        Assert.Equal(RecommendedActions.Maybe, parsed.Result.RecommendedAction);
    }

    [Theory]
    [InlineData("I cannot help with that.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ this is not json at all }")]
    [InlineData("[1, 2, 3]")]
    public void Unusable_responses_fail_without_throwing(string raw)
    {
        var parsed = AiJsonParser.ParseAnalysis(raw);

        Assert.False(parsed.Success);
        Assert.NotNull(parsed.Error);
        Assert.Null(parsed.Result);
    }

    [Fact]
    public void Unknown_category_falls_back_to_unknown_and_is_reported()
    {
        var raw = ValidJson.Replace("\"forgotten_project\"", "\"alien_artifact\"");
        var parsed = AiJsonParser.ParseAnalysis(raw);

        Assert.True(parsed.Success);
        Assert.Equal(ArtifactCategories.Unknown, parsed.Result!.Category);
        Assert.Contains("category-out-of-catalogue", parsed.Warnings);
    }

    [Fact]
    public void Invalid_recommended_action_falls_back_to_maybe()
    {
        var raw = ValidJson.Replace("\"include\"", "\"definitely keep this one\"");
        var parsed = AiJsonParser.ParseAnalysis(raw);

        Assert.True(parsed.Success);
        Assert.Equal(RecommendedActions.Maybe, parsed.Result!.RecommendedAction);
        Assert.Contains("recommended_action-invalid", parsed.Warnings);
    }

    [Fact]
    public void Reasons_may_arrive_as_a_single_string_or_a_mixed_array()
    {
        var single = AiJsonParser.ParseAnalysis(ValidJson.Replace(
            "\"reasons\": [\"old creation date\", \"looks like a project folder\"]",
            "\"reasons\": \"only one reason\""));
        Assert.True(single.Success);
        Assert.Single(single.Result!.Reasons);

        var mixed = AiJsonParser.ParseAnalysis(ValidJson.Replace(
            "\"reasons\": [\"old creation date\", \"looks like a project folder\"]",
            "\"reasons\": [\"text\", 42, null]"));
        Assert.True(mixed.Success);
        Assert.Equal(2, mixed.Result!.Reasons.Count);
    }

    [Fact]
    public void Nested_braces_inside_strings_do_not_break_extraction()
    {
        var raw = """
            {
              "interestingness": 40,
              "confidence": 50,
              "category": "configuration",
              "summary": "A config containing the literal text {\"nested\": true} in a comment.",
              "recommended_action": "maybe"
            }
            """;

        var parsed = AiJsonParser.ParseAnalysis(raw);
        Assert.True(parsed.Success);
        Assert.Contains("nested", parsed.Result!.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Unavailable_results_are_never_treated_as_scored()
    {
        var unavailable = AiAnalysisResult.Unavailable("Ai_Error_Timeout");
        Assert.False(unavailable.Succeeded);
        Assert.Equal("Ai_Error_Timeout", unavailable.Error);
        Assert.Equal(0, unavailable.Confidence);
    }

    [Fact]
    public void Report_narrative_is_parsed_with_highlights()
    {
        var raw = """
            {
              "overview": "We examined a lot.",
              "observations": "Mostly old projects.",
              "final_summary": "Nothing was modified.",
              "highlights": [
                { "path": "D:\\Old\\main.cs", "note": "An abandoned prototype." },
                { "path": "", "note": "ignored" }
              ]
            }
            """;

        var narrative = AiPrompts.ParseReportNarrative(raw);

        Assert.NotNull(narrative);
        Assert.Equal("We examined a lot.", narrative!.Overview);
        Assert.Single(narrative.Highlights);
        Assert.Equal("An abandoned prototype.", narrative.Highlights[@"D:\Old\main.cs"]);
    }

    [Fact]
    public void Broken_report_narrative_returns_null_instead_of_throwing()
    {
        Assert.Null(AiPrompts.ParseReportNarrative("not json"));
        Assert.Null(AiPrompts.ParseReportNarrative(null));
        Assert.Null(AiPrompts.ParseReportNarrative("[1,2]"));
    }
}

using Gwendoline.Core;

namespace Gwendoline.Core.Tests;

public class ClaudeTextTests
{
    [Fact]
    public void DoneGivesALineThenTheProject()
    {
        var lines = ClaudeText.Done("pauline-windows", new Random(1)).Split('\n');
        Assert.Contains(lines[0], ClaudeText.DoneLines);
        Assert.Equal("📁 pauline-windows", lines[1]);
    }

    [Fact]
    public void AskShowsTheToolAndTheCommand()
    {
        var lines = ClaudeText.Ask("Bash", "npm run build", "app", new Random(1)).Split('\n');
        Assert.Contains(lines[0], ClaudeText.AskLines);
        Assert.Equal("Bash: npm run build", lines[1]);
        Assert.Equal("📁 app", lines[2]);
    }

    [Fact]
    public void AskWithoutDetailShowsTheTool() =>
        Assert.Equal("WebSearch", ClaudeText.Ask("WebSearch", null, "app", new Random(1)).Split('\n')[1]);

    [Fact]
    public void LongCommandsAreCut()
    {
        var shown = ClaudeText.Shorten(new string('x', 1000));
        Assert.Equal(ClaudeText.DetailLimit, shown.Length);
        Assert.EndsWith("…", shown);
    }

    [Theory]
    [InlineData("oui")]
    [InlineData("Oui!")]
    [InlineData(" ok ")]
    [InlineData("allow")]
    [InlineData("vas-y")]
    [InlineData("yes")]
    [InlineData("👍")]
    public void ReadsAYes(string text) => Assert.True(ClaudeText.ParseAnswer(text));

    [Theory]
    [InlineData("non")]
    [InlineData("NON.")]
    [InlineData("deny")]
    [InlineData("no")]
    public void ReadsANo(string text) => Assert.False(ClaudeText.ParseAnswer(text));

    [Theory]
    [InlineData("/status")]
    [InlineData("oui mais non")]
    [InlineData("")]
    public void IgnoresAnythingElse(string text) => Assert.Null(ClaudeText.ParseAnswer(text));

    [Fact]
    public void EveryRandomPickIsALine()
    {
        var random = new Random(7);
        for (var i = 0; i < 100; i++)
        {
            Assert.Contains(ClaudeText.Answered(i % 2 == 0, random), ClaudeText.AllowedLines.Concat(ClaudeText.DeniedLines));
        }
    }
}

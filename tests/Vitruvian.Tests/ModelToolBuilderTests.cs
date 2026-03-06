using VitruvianAbstractions.Interfaces;
using Xunit;

namespace VitruvianTests;

public sealed class ModelToolBuilderTests
{
    [Fact]
    public void Build_WithNameAndDescription_CreatesToolWithoutParameters()
    {
        var tool = new ModelToolBuilder("search", "Search the web").Build();

        Assert.Equal("search", tool.Name);
        Assert.Equal("Search the web", tool.Description);
        Assert.Null(tool.Parameters);
    }

    [Fact]
    public void Build_WithParameters_CreatesToolWithAllParameters()
    {
        var tool = new ModelToolBuilder("search", "Search the web")
            .AddParameter("query", "Search query text")
            .AddParameter("maxResults", "Maximum results to return")
            .Build();

        Assert.Equal("search", tool.Name);
        Assert.NotNull(tool.Parameters);
        Assert.Equal(2, tool.Parameters.Count);
        Assert.Equal("Search query text", tool.Parameters["query"]);
        Assert.Equal("Maximum results to return", tool.Parameters["maxResults"]);
    }

    [Fact]
    public void Build_SupportsChainingMultipleParameters()
    {
        var builder = new ModelToolBuilder("test", "Test tool");

        var result = builder
            .AddParameter("a", "First")
            .AddParameter("b", "Second")
            .AddParameter("c", "Third");

        Assert.Same(builder, result);
        var tool = result.Build();
        Assert.Equal(3, tool.Parameters!.Count);
    }

    [Fact]
    public void Build_OverwritesDuplicateParameterNames()
    {
        var tool = new ModelToolBuilder("test", "Test tool")
            .AddParameter("query", "Original")
            .AddParameter("query", "Updated")
            .Build();

        Assert.Single(tool.Parameters!);
        Assert.Equal("Updated", tool.Parameters!["query"]);
    }

    [Fact]
    public void Constructor_ThrowsOnNullOrWhitespaceName()
    {
        Assert.Throws<ArgumentException>(() => new ModelToolBuilder("", "desc"));
        Assert.Throws<ArgumentException>(() => new ModelToolBuilder("  ", "desc"));
    }

    [Fact]
    public void Constructor_ThrowsOnNullDescription()
    {
        Assert.Throws<ArgumentNullException>(() => new ModelToolBuilder("name", null!));
    }

    [Fact]
    public void AddParameter_ThrowsOnNullOrWhitespaceName()
    {
        var builder = new ModelToolBuilder("test", "Test");

        Assert.Throws<ArgumentException>(() => builder.AddParameter("", "desc"));
        Assert.Throws<ArgumentException>(() => builder.AddParameter("  ", "desc"));
    }
}

using Assistent.Core.Tools;
using ExampleToolkit;

namespace Assistent.Tests;

[TestClass]
public class ToolCatalogTests
{
    private sealed class FakeTool(string name) : IAssistantTool
    {
        public string Name { get; } = name;
        public string Description => "fake";
        public Task<object?> InvokeAsync(IDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);
    }

    [TestMethod]
    public void Register_IgnoresDuplicateNames_CaseInsensitive()
    {
        var catalog = new ToolCatalog();
        catalog.Register(new FakeTool("abc"));
        catalog.Register(new FakeTool("ABC"));

        Assert.HasCount(1, catalog.DescribeTools().Split(Environment.NewLine));
    }

    [TestMethod]
    public void DescribeTools_ListsNameAndDescription()
    {
        var catalog = new ToolCatalog();
        catalog.Register(new FakeTool("abc"));

        StringAssert.Contains(catalog.DescribeTools(), "- abc: fake");
    }

    [TestMethod]
    public async Task CurrentDateTimeTool_ReturnsResult()
    {
        var tool = new CurrentDateTimeTool();
        var result = await tool.InvokeAsync(new Dictionary<string, object?>());

        Assert.AreEqual("current_datetime", tool.Name);
        Assert.IsNotNull(result);
    }
}

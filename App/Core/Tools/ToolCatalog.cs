using System.IO;
using System.Reflection;
using Microsoft.SemanticKernel;

namespace Assistent.Core.Tools;

public sealed class ToolCatalog
{
    private readonly List<IAssistantTool> _tools = new();

    public void Register(IAssistantTool tool)
    {
        if (_tools.Any(existing => existing.Name.Equals(tool.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _tools.Add(tool);
    }

    public void DiscoverToolsFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t =>
                     typeof(IAssistantTool).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
        {
            if (Activator.CreateInstance(type) is IAssistantTool tool)
            {
                Register(tool);
            }
        }
    }

    public void LoadFromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var assembly = Assembly.LoadFrom(file);
                DiscoverToolsFromAssembly(assembly);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to load plugin {file}: {ex.Message}");
            }
        }
    }

    public void DiscoverToolsFromCurrentAssembly()
    {
        DiscoverToolsFromAssembly(typeof(ToolCatalog).Assembly);
    }

    public ToolExecutor BuildExecutor() => new(_tools);

    public Kernel CreateKernel()
    {
        var builder = Kernel.CreateBuilder();
        return builder.Build();
    }
}

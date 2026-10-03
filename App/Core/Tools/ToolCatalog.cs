using System.IO;
using System.Reflection;

namespace Assistent.Core.Tools;

public sealed class ToolCatalog
{
    private readonly List<IAssistantTool> _tools = new();
    private readonly Dictionary<string, string> _instructions = new(StringComparer.OrdinalIgnoreCase);

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

    public void LoadFromDirectory(string directory, IEnumerable<string> allowedPlugins)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            Console.WriteLine($"Pasta de plugins não encontrada: {directory}");
            Console.WriteLine("Nenhum mapeador Markdown foi encontrado nos plugins.");
            return;
        }

        var allowlist = allowedPlugins
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(file => allowlist.Contains(Path.GetFileName(file)))
            .ToArray();
        Console.WriteLine($"Pasta de plugins encontrada: {directory} ({files.Length} DLL(s) permitida(s)).");

        foreach (var file in files)
        {
            try
            {
                var assembly = Assembly.LoadFrom(file);
                DiscoverToolsFromAssembly(assembly);
                LoadInstructionsForAssembly(assembly, Path.GetDirectoryName(file) ?? directory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Não foi possível carregar o plugin {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        var markdownMapper = _tools.FirstOrDefault(tool =>
            tool.Name.Contains("markdown", StringComparison.OrdinalIgnoreCase)
            || tool.Name.Contains("marqdown", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine(markdownMapper is null
            ? "Nenhum mapeador Markdown foi encontrado nos plugins."
            : $"Mapeador Markdown encontrado: {markdownMapper.Name}.");
    }

    public void DiscoverToolsFromCurrentAssembly()
    {
        DiscoverToolsFromAssembly(typeof(ToolCatalog).Assembly);
    }

    public string DescribeTools()
    {
        return string.Join(
            Environment.NewLine,
            _tools.Select(tool =>
            {
                var description = $"- {tool.Name}: {tool.Description}";
                return _instructions.TryGetValue(tool.Name, out var instructions)
                    ? $"{description}{Environment.NewLine}  Instruções: {instructions}"
                    : description;
            }));
    }

    private void LoadInstructionsForAssembly(Assembly assembly, string directory)
    {
        var assemblyTools = _tools.Where(tool =>
            assembly.GetTypes().Any(type =>
                typeof(IAssistantTool).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.IsInterface
                && type.Name.Equals(tool.GetType().Name, StringComparison.Ordinal)));

        foreach (var tool in assemblyTools)
        {
            var instructionPath = Path.Combine(directory, $"{tool.Name}.md");
            if (File.Exists(instructionPath))
            {
                _instructions[tool.Name] = File.ReadAllText(instructionPath).Trim();
            }
        }
    }

    public ToolExecutor BuildExecutor(IToolApprovalService approvalService) => new(_tools, approvalService);
}

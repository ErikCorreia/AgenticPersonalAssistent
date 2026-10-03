using System.Text.Json;
using Assistent.Core.Tools;
using FileManagerToolkit;

namespace Assistent.Tests;

[TestClass]
public class ToolExecutionTests
{
    private sealed class ApprovalService(bool approved) : IToolApprovalService
    {
        public int Requests { get; private set; }

        public Task<bool> ApproveAsync(ToolExecutionIntent intent, CancellationToken cancellationToken = default)
        {
            Requests++;
            return Task.FromResult(approved);
        }
    }

    private sealed class RecycleBinService : IRecycleBinService
    {
        public int EmptyCalls { get; private set; }

        public IReadOnlyList<RecycleBinItem> ListItems() => Array.Empty<RecycleBinItem>();

        public void Empty() => EmptyCalls++;
    }

    private sealed class MutatingTool : IAssistantTool
    {
        public string Name => "mutate";
        public string Description => "test mutation";
        public int Invocations { get; private set; }

        public ToolExecutionIntent GetExecutionIntent(IDictionary<string, object?> arguments) =>
            new(true, false, "Alterar um arquivo de teste.");

        public Task<object?> InvokeAsync(
            IDictionary<string, object?> arguments,
            CancellationToken cancellationToken = default)
        {
            Invocations++;
            return Task.FromResult<object?>("done");
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_DeniedAction_DoesNotInvokeTool()
    {
        var tool = new MutatingTool();
        var approval = new ApprovalService(false);
        var executor = new ToolExecutor(new[] { tool }, approval);
        using var arguments = JsonDocument.Parse("{}");

        var result = await executor.ExecuteAsync("mutate", arguments.RootElement);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, tool.Invocations);
        Assert.AreEqual(1, approval.Requests);
    }

    [TestMethod]
    public void FileManager_ReadIntent_RequiresProviderConsent()
    {
        var fileManager = new FileManager(new[] { Path.GetTempPath() });
        var intent = fileManager.GetExecutionIntent(new Dictionary<string, object?>
        {
            ["action"] = "read",
            ["path"] = Path.Combine(Path.GetTempPath(), "private.txt")
        });

        Assert.IsTrue(intent.SendsLocalDataToProvider);
    }

    [TestMethod]
    public void FileManager_ReadOnlyFolder_ReadIntentWarnsAboutCloudTransfer()
    {
        var writableRoot = Path.Combine(Path.GetTempPath(), "assistant-writable");
        var readOnlyRoot = Path.Combine(Path.GetTempPath(), "assistant-readonly");
        var fileManager = new FileManager(
            new[] { writableRoot },
            new[] { readOnlyRoot },
            new RecycleBinService());
        var intent = fileManager.GetExecutionIntent(new Dictionary<string, object?>
        {
            ["action"] = "read",
            ["path"] = Path.Combine(readOnlyRoot, "system.ini")
        });

        Assert.IsTrue(intent.SendsLocalDataToProvider);
        Assert.IsFalse(intent.RequiresApproval);
    }

    [TestMethod]
    public void FileManager_ListIntentRequiresConsentForMetadata()
    {
        var fileManager = new FileManager(new[] { Path.GetTempPath() });
        var intent = fileManager.GetExecutionIntent(new Dictionary<string, object?>
        {
            ["action"] = "list",
            ["path"] = Path.GetTempPath()
        });

        Assert.IsTrue(intent.SendsLocalDataToProvider);
    }

    [TestMethod]
    public void FileManager_SystemInfoIntentRequiresConsentWithoutMutationApproval()
    {
        var fileManager = new FileManager(new[] { Path.GetTempPath() });

        var intent = fileManager.GetExecutionIntent(new Dictionary<string, object?>
        {
            ["action"] = "system_info"
        });

        Assert.IsFalse(intent.RequiresApproval);
        Assert.IsTrue(intent.SendsLocalDataToProvider);
    }

    [TestMethod]
    public async Task FileManager_SystemInfo_ReturnsMemoryAndDriveUsage()
    {
        var fileManager = new FileManager(new[] { Path.GetTempPath() });

        var result = await fileManager.InvokeAsync(new Dictionary<string, object?>
        {
            ["action"] = "system_info"
        });

        using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var root = response.RootElement;
        Assert.IsTrue(root.GetProperty("Success").GetBoolean());
        Assert.AreEqual("system_info", root.GetProperty("Action").GetString());

        var memory = root.GetProperty("Memory");
        Assert.IsGreaterThan(0L, memory.GetProperty("TotalBytes").GetInt64());
        Assert.IsGreaterThanOrEqualTo(0L, memory.GetProperty("UsedBytes").GetInt64());
        Assert.IsGreaterThanOrEqualTo(0L, memory.GetProperty("AvailableBytes").GetInt64());
        Assert.IsTrue(memory.GetProperty("UsagePercentage").GetDouble() is >= 0 and <= 100);

        var drives = root.GetProperty("Drives");
        Assert.IsGreaterThan(0, drives.GetArrayLength());
        var drive = drives[0];
        Assert.IsGreaterThan(0L, drive.GetProperty("TotalBytes").GetInt64());
        Assert.IsGreaterThanOrEqualTo(0L, drive.GetProperty("UsedBytes").GetInt64());
        Assert.IsGreaterThanOrEqualTo(0L, drive.GetProperty("AvailableBytes").GetInt64());
        Assert.IsTrue(drive.GetProperty("UsagePercentage").GetDouble() is >= 0 and <= 100);
    }

    [TestMethod]
    public async Task FileManager_WriteOutsideAllowedRoot_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.GetFullPath(root + "-outside");
        var fileManager = new FileManager(new[] { root });
        var result = await fileManager.InvokeAsync(new Dictionary<string, object?>
        {
            ["action"] = "write",
            ["path"] = Path.Combine(outsideRoot, "blocked.txt"),
            ["content"] = "not written"
        });

        using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.IsFalse(response.RootElement.GetProperty("Success").GetBoolean());
        Assert.IsFalse(File.Exists(Path.Combine(outsideRoot, "blocked.txt")));
    }

    [TestMethod]
    public async Task FileManager_RelativePath_ResolvesWithinAllowedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "note.txt");
        try
        {
            var fileManager = new FileManager(new[] { root });
            await fileManager.InvokeAsync(new Dictionary<string, object?>
            {
                ["action"] = "write",
                ["path"] = "note.txt",
                ["content"] = "safe"
            });

            Assert.AreEqual("safe", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task FileManager_DeleteAllowedRoot_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "keep.txt");
        await File.WriteAllTextAsync(filePath, "keep");
        try
        {
            var fileManager = new FileManager(new[] { root });
            var result = await fileManager.InvokeAsync(new Dictionary<string, object?>
            {
                ["action"] = "delete",
                ["path"] = root
            });

            using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.IsFalse(response.RootElement.GetProperty("Success").GetBoolean());
            Assert.IsTrue(File.Exists(filePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task FileManager_WriteToReadOnlyRoot_IsRejected()
    {
        var writableRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var readOnlyRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(readOnlyRoot);
        var target = Path.Combine(readOnlyRoot, "blocked.txt");
        try
        {
            var fileManager = new FileManager(
                new[] { writableRoot },
                new[] { readOnlyRoot },
                new RecycleBinService());
            var result = await fileManager.InvokeAsync(new Dictionary<string, object?>
            {
                ["action"] = "write",
                ["path"] = target,
                ["content"] = "must not be written"
            });

            using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.IsFalse(response.RootElement.GetProperty("Success").GetBoolean());
            Assert.IsFalse(File.Exists(target));
        }
        finally
        {
            Directory.Delete(readOnlyRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task FileManager_ReadFromReadOnlyRoot_IsAllowed()
    {
        var writableRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var readOnlyRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(readOnlyRoot);
        var filePath = Path.Combine(readOnlyRoot, "system.ini");
        await File.WriteAllTextAsync(filePath, "setting=value");
        try
        {
            var fileManager = new FileManager(
                new[] { writableRoot },
                new[] { readOnlyRoot },
                new RecycleBinService());
            var result = await fileManager.InvokeAsync(new Dictionary<string, object?>
            {
                ["action"] = "read",
                ["path"] = filePath
            });

            using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.IsTrue(response.RootElement.GetProperty("Success").GetBoolean());
            Assert.AreEqual("setting=value", response.RootElement.GetProperty("Content").GetString());
        }
        finally
        {
            Directory.Delete(readOnlyRoot, recursive: true);
        }
    }

    [TestMethod]
    public void FileManager_EmptyRecycleBin_RequiresExplicitConfirmationPhrase()
    {
        var fileManager = new FileManager(
            new[] { Path.GetTempPath() },
            Array.Empty<string>(),
            new RecycleBinService());
        var intent = fileManager.GetExecutionIntent(new Dictionary<string, object?>
        {
            ["action"] = "empty_recycle_bin"
        });

        Assert.IsTrue(intent.RequiresApproval);
        Assert.IsFalse(intent.SendsLocalDataToProvider);
        Assert.AreEqual("ESVAZIAR LIXEIRA", intent.RequiredConfirmationPhrase);
    }

    [TestMethod]
    public async Task ConsoleApprovalService_EmptyRecycleBin_RequiresExactPhrase()
    {
        var originalInput = Console.In;
        try
        {
            var approval = new ConsoleToolApprovalService();
            var intent = new ToolExecutionIntent(
                true,
                false,
                "Esvaziar a Lixeira.",
                "ESVAZIAR LIXEIRA");

            Console.SetIn(new StringReader("esvaziar lixeira"));
            Assert.IsFalse(await approval.ApproveAsync(intent));

            Console.SetIn(new StringReader("ESVAZIAR LIXEIRA"));
            Assert.IsTrue(await approval.ApproveAsync(intent));
        }
        finally
        {
            Console.SetIn(originalInput);
        }
    }

    [TestMethod]
    public async Task ToolExecutor_DeniedRecycleBinCleanup_DoesNotEmptyBin()
    {
        var recycleBin = new RecycleBinService();
        var fileManager = new FileManager(
            new[] { Path.GetTempPath() },
            Array.Empty<string>(),
            recycleBin);
        var executor = new ToolExecutor(new[] { fileManager }, new ApprovalService(false));
        using var arguments = JsonDocument.Parse("""{"action":"empty_recycle_bin"}""");

        var result = await executor.ExecuteAsync("file_manager", arguments.RootElement);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, recycleBin.EmptyCalls);
    }
}

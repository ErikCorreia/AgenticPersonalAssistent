using System.Net;
using System.Text;
using System.Text.Json;
using Assistent;
using Assistent.Core.Tools;
using Assistent.services;

namespace Assistent.Tests;

[TestClass]
public class OllamaProviderTests
{
    private sealed class QueueHandler(IEnumerable<string> responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public List<string> RequestUris { get; } = new();
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!.ToString());
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ToolApproval : IToolApprovalService
    {
        public Task<bool> ApproveAsync(ToolExecutionIntent intent, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class SampleTool : IAssistantTool
    {
        public string Name => "sample_tool";
        public string Description => "Returns a sample value.";
        public object ParameterSchema => new
        {
            type = "OBJECT",
            properties = new { query = new { type = "STRING" } },
            required = new[] { "query" }
        };
        public ToolExecutionIntent GetExecutionIntent(IDictionary<string, object?> arguments) =>
            ToolExecutionIntent.Safe;
        public Task<object?> InvokeAsync(
            IDictionary<string, object?> arguments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(new { value = $"result for {arguments["query"]}" });
    }

    [TestMethod]
    public async Task AskAsync_UsesLocalOllamaToolCallingWithoutGeminiKey()
    {
        var handler = new QueueHandler(new[]
        {
            """{"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"sample_tool","arguments":{"query":"hello"}}}]}}""",
            """{"message":{"role":"assistant","content":"Resultado local pronto."}}"""
        });
        using var httpClient = new HttpClient(handler);
        var options = new AIOptions
        {
            Provider = "Ollama",
            OllamaBaseUrl = "http://localhost:11434",
            OllamaModel = "qwen3:8b",
            SystemPrompt = "Responda em português."
        };
        var provider = new OllamaProvider(httpClient, options);
        var executor = new ToolExecutor(new[] { new SampleTool() }, new ToolApproval());
        var service = new AIService(provider, executor, options);

        var response = await service.AskAsync("Use a ferramenta local.");

        Assert.AreEqual("Resultado local pronto.", response);
        Assert.HasCount(2, handler.RequestUris);
        Assert.AreEqual("http://localhost:11434/api/chat", handler.RequestUris[0]);
        using var firstRequest = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.AreEqual("qwen3:8b", firstRequest.RootElement.GetProperty("model").GetString());
        Assert.AreEqual("object", firstRequest.RootElement
            .GetProperty("tools")[0]
            .GetProperty("function")
            .GetProperty("parameters")
            .GetProperty("type").GetString());

        using var followUpRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = followUpRequest.RootElement.GetProperty("messages");
        Assert.AreEqual("assistant", messages[2].GetProperty("role").GetString());
        Assert.AreEqual("tool", messages[3].GetProperty("role").GetString());
        Assert.AreEqual("sample_tool", messages[3].GetProperty("tool_name").GetString());
        Assert.IsTrue(messages[3].GetProperty("content").GetString()!.Contains("result for hello", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GenerateAsync_ServerUnavailable_ReportsLocalOllamaError()
    {
        using var httpClient = new HttpClient(new HttpStatusHandler(HttpStatusCode.ServiceUnavailable));
        var provider = new OllamaProvider(httpClient, new AIOptions
        {
            OllamaModel = "qwen3:8b"
        });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GenerateAsync("system", Array.Empty<AssistantMessage>(), Array.Empty<IAssistantTool>()));

        StringAssert.Contains(exception.Message, "Verifique se o Ollama está iniciado");
    }

    private sealed class HttpStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}

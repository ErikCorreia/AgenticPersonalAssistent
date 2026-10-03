using System.Net;
using System.Text;
using System.Text.Json;
using Assistent;
using Assistent.Core.Tools;
using Assistent.services;

namespace Assistent.Tests;

[TestClass]
public class GeminiConversationTests
{
    private sealed class QueueHandler(IEnumerable<string> responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public List<string> Requests { get; } = new();
        public List<string?> RequestUris { get; } = new();
        public List<string?> ApiKeys { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.ToString());
            ApiKeys.Add(request.Headers.GetValues("x-goog-api-key").Single());
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ReadOnlyTool : IAssistantTool
    {
        public string Name => "sample_tool";
        public string Description => "Returns a sample value.";
        public ToolExecutionIntent GetExecutionIntent(IDictionary<string, object?> arguments) => ToolExecutionIntent.Safe;
        public Task<object?> InvokeAsync(IDictionary<string, object?> arguments, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(new { value = "observed" });
    }

    private sealed class ApprovalService : IToolApprovalService
    {
        public Task<bool> ApproveAsync(ToolExecutionIntent intent, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    [TestMethod]
    public async Task AskAsync_UsesStructuredFunctionCallsAndReturnsFinalText()
    {
        var handler = new QueueHandler(new[]
        {
            """{"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"sample_tool","args":{}}}]}}]}""",
            """{"candidates":[{"content":{"role":"model","parts":[{"text":"Resultado verificado."}]}}]}"""
        });
        using var httpClient = new HttpClient(handler);
        var options = new AIOptions { GoogleApiKey = "test-key", Model = "test-model", SystemPrompt = "Responda em português." };
        var provider = new GoogleGeminiProvider(httpClient, options);
        var executor = new ToolExecutor(new[] { new ReadOnlyTool() }, new ApprovalService());
        var service = new AIService(provider, executor, options);

        var response = await service.AskAsync("Use a ferramenta de teste.");

        Assert.AreEqual("Resultado verificado.", response);
        Assert.HasCount(2, handler.Requests);
        Assert.IsTrue(handler.Requests[0].Contains("functionDeclarations", StringComparison.Ordinal));
        Assert.IsTrue(handler.Requests[1].Contains("functionResponse", StringComparison.Ordinal));
        Assert.AreEqual("test-key", handler.ApiKeys[0]);
        Assert.IsTrue(handler.RequestUris[0]!.EndsWith(":generateContent", StringComparison.Ordinal));
        Assert.IsFalse(handler.RequestUris[0]!.Contains("key=", StringComparison.OrdinalIgnoreCase));
    }
}

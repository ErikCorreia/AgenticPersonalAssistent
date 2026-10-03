using System.Net;
using System.Text;
using Assistent.Core.Tools;
using WebSearchToolkit;

namespace Assistent.Tests;

[TestClass]
public class WebSearchTests
{
	private sealed class FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		public List<Uri> Uris { get; } = new();

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Uris.Add(request.RequestUri!);
			return Task.FromResult(respond(request));
		}
	}

	private static HttpResponseMessage Ok(string body, string type) =>
		new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };

	private static WebSearch Create(FuncHandler handler, string? key = "k", IPAddress[]? ip = null) =>
		new(new HttpClient(handler), () => key, (_, _) => Task.FromResult(ip ?? [IPAddress.Parse("93.184.216.34")]));

	private static Dictionary<string, object?> Args(params (string, object?)[] items) =>
		items.ToDictionary(i => i.Item1, i => i.Item2);

	private static string Error(object? result) =>
		(string)result!.GetType().GetProperty("Error")!.GetValue(result)!;

	[TestMethod]
	public async Task Search_ParsesBraveResults()
	{
		var handler = new FuncHandler(_ => Ok("""{"web":{"results":[{"title":"A <b>x</b>","url":"https://a.com","description":"desc","age":"1 day"}]}}""", "application/json"));
		var result = await Create(handler).InvokeAsync(Args(("action", "search"), ("query", "teste")), CancellationToken.None);
		var results = (System.Collections.IEnumerable)result!.GetType().GetProperty("Results")!.GetValue(result)!;
		Assert.HasCount(1, results.Cast<object>().ToList());
		StringAssert.Contains(handler.Uris[0].ToString(), "q=teste");
	}

	[TestMethod]
	public async Task Search_WithoutKey_ReturnsError()
	{
		var result = await Create(new FuncHandler(_ => Ok("{}", "application/json")), key: null)
			.InvokeAsync(Args(("action", "search"), ("query", "x")), CancellationToken.None);
		StringAssert.Contains(Error(result), "BRAVE_API_KEY");
	}

	[TestMethod]
	public async Task Fetch_RejectsInvalidSchemeAndLocalTargets()
	{
		var tool = Create(new FuncHandler(_ => Ok("x", "text/plain")));
		foreach (var url in new[] { "file:///c:/x.txt", "http://localhost/x", "http://127.0.0.1/", "http://192.168.0.1/", "https://user:p@a.com/" })
		{
			var result = await tool.InvokeAsync(Args(("action", "fetch"), ("url", url)), CancellationToken.None);
			Assert.IsNotNull(Error(result), url);
		}
	}

	[TestMethod]
	public async Task Fetch_RejectsHostResolvingToPrivateIp()
	{
		var tool = Create(new FuncHandler(_ => Ok("x", "text/plain")), ip: [IPAddress.Parse("10.0.0.5")]);
		var result = await tool.InvokeAsync(Args(("action", "fetch"), ("url", "https://evil.example/")), CancellationToken.None);
		StringAssert.Contains(Error(result), "privados");
	}

	[TestMethod]
	public async Task Fetch_RedirectToPrivateIp_IsBlocked()
	{
		var handler = new FuncHandler(r => r.RequestUri!.Host == "a.com"
			? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://169.254.169.254/") } }
			: Ok("secret", "text/plain"));
		var result = await Create(handler).InvokeAsync(Args(("action", "fetch"), ("url", "https://a.com/")), CancellationToken.None);
		StringAssert.Contains(Error(result), "privados");
		Assert.HasCount(1, handler.Uris);
	}

	[TestMethod]
	public async Task Fetch_ConvertsHtmlAndTruncates()
	{
		var html = "<html><head><title>T</title></head><body><script>bad()</script><p>" + new string('a', 800) + "</p></body></html>";
		var result = await Create(new FuncHandler(_ => Ok(html, "text/html")))
			.InvokeAsync(Args(("action", "fetch"), ("url", "https://a.com/"), ("maxChars", 500)), CancellationToken.None);
		var text = (string)result!.GetType().GetProperty("Text")!.GetValue(result)!;
		Assert.AreEqual(500, text.Length);
		Assert.DoesNotContain("bad()", text);
	}

	[TestMethod]
	public async Task Fetch_RejectsBinaryContent()
	{
		var result = await Create(new FuncHandler(_ => Ok("x", "application/zip")))
			.InvokeAsync(Args(("action", "fetch"), ("url", "https://a.com/f")), CancellationToken.None);
		StringAssert.Contains(Error(result), "não suportado");
	}

	[TestMethod]
	public void IsPrivate_ClassifiesAddresses()
	{
		Assert.IsTrue(WebSearch.IsPrivate(IPAddress.Parse("::1")));
		Assert.IsTrue(WebSearch.IsPrivate(IPAddress.Parse("::ffff:10.0.0.1")));
		Assert.IsTrue(WebSearch.IsPrivate(IPAddress.Parse("100.64.0.1")));
		Assert.IsTrue(WebSearch.IsPrivate(IPAddress.Parse("fd00::1")));
		Assert.IsFalse(WebSearch.IsPrivate(IPAddress.Parse("8.8.8.8")));
	}

	[TestMethod]
	public void Intent_RequiresApprovalAndSendsData()
	{
		var intent = new WebSearch().GetExecutionIntent(Args(("action", "search"), ("query", "x")));
		Assert.IsTrue(intent.RequiresApproval);
		Assert.IsTrue(intent.SendsLocalDataToProvider);
	}
}

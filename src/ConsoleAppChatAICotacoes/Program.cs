using Azure.AI.OpenAI;
using Azure.Monitor.OpenTelemetry.Exporter;
using ConsoleAppChatAICotacoes.Tracing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using OpenAI.Chat;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.ClientModel;

Console.WriteLine("***** Testes com Agent Framework + Microsoft Foundry + MCP Server +" +
    "OpenTelemetry + Application Insights *****");
Console.WriteLine();

var oldForegroundColor = Console.ForegroundColor;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService(OpenTelemetryExtensions.ServiceName);
var traceProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource(OpenTelemetryExtensions.ServiceName)
    .AddHttpClientInstrumentation()
    .AddAzureMonitorTraceExporter(options =>
    {
        options.ConnectionString = configuration["MCP:AppInsights"];
    })
    .Build();

var mcpName = configuration["MCP:Name"]!;
await using var mcpClient = await McpClient.CreateAsync(new HttpClientTransport(new()
{
    Name = mcpName,
    Endpoint = new Uri(configuration["MCP:Endpoint"]!),
    AdditionalHeaders = new Dictionary<string, string>()
    {
        { "Ocp-Apim-Subscription-Key", configuration["MCP:SubscriptionKey"]! }
    }
}));
Console.WriteLine($"Ferramentas do MCP:");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"***** {mcpName} *****");
var mcpTools = await mcpClient.ListToolsAsync().ConfigureAwait(false);
Console.WriteLine($"Quantidade de ferramentas disponiveis = {mcpTools.Count}");
Console.WriteLine();
foreach (var tool in mcpTools)
{
    Console.WriteLine($"* {tool.Name}: {tool.Description}");
}
Console.ForegroundColor = oldForegroundColor;
Console.WriteLine();

var agent = new AzureOpenAIClient(endpoint: new Uri(configuration["MicrosoftFoundry:Endpoint"]!),
        credential: new ApiKeyCredential(configuration["MicrosoftFoundry:ApiKey"]!))
    .GetChatClient(configuration["MicrosoftFoundry:DeploymentName"]!)
    .AsAIAgent(
        instructions: "Você é um assistente de IA que ajuda o usuario a consultar informações" +
            "sobre cotações de moedas estrangeiras, as quais estão disponíveis através das" +
            "ferramentas do MCP Server de Cotações.",
        tools: [.. mcpTools])
    .AsBuilder()
    .UseOpenTelemetry(sourceName: OpenTelemetryExtensions.ServiceName,
        configure: (cfg) => cfg.EnableSensitiveData = true)
    .Build();
while (true)
{
    Console.WriteLine("Sua pergunta:");
    var userPrompt = Console.ReadLine();

    using var activity1 = OpenTelemetryExtensions.ActivitySource
        .StartActivity("PerguntaChatIACotacoes")!;

    var result = await agent.RunAsync(userPrompt!);

    Console.WriteLine();
    Console.WriteLine("Resposta da IA:");
    Console.WriteLine();

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    var textReponse = result.AsChatResponse().Messages.Last().Text;
    Console.WriteLine(textReponse);
    Console.ForegroundColor = oldForegroundColor;

    Console.WriteLine();
    Console.WriteLine();

    activity1.Stop();
}
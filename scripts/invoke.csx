#load "lib/common.csx"
// Cross-platform replacement for scripts/invoke.ps1.
// POSTs a sample approval request to the Logic App's HTTP trigger.
//
// Usage:
//   dotnet script scripts/invoke.csx -- [--environment dev|prod] [--amount 2500]
//                                       [--trigger-url '<url>'] [--request-id REQ-1]
//                                       [--requester foo@bar] [--description "..."]
//                                       [--trigger-name When_an_approval_request_is_received]
//
// If --trigger-url is omitted, the script asks Azure for the workflow's callback URL,
// avoiding the unquoted '&' pitfall when copying URLs on the command line.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

var parsed = Common.ParseArgs(Args);
var environment = Common.Get(parsed, "environment", "dev");
var amount = int.Parse(Common.Get(parsed, "amount", "2500"));
var requestId = Common.Get(parsed, "request-id", $"REQ-{Random.Shared.Next(1000, 9999)}");
var requester = Common.Get(parsed, "requester", "alice@contoso.com");
var description = Common.Get(parsed, "description", "Demo approval request");
var triggerName = Common.Get(parsed, "trigger-name", "When_an_approval_request_is_received");
string triggerUrl = parsed.TryGetValue("trigger-url", out var u) ? u : null;

if (environment != "dev" && environment != "prod")
{
    Common.WriteError($"--environment must be 'dev' or 'prod' (got '{environment}').");
    return 1;
}

if (string.IsNullOrEmpty(triggerUrl))
{
    triggerUrl = FetchTriggerUrl(environment, triggerName);
    if (triggerUrl is null) return 1;
}

if (triggerUrl.IndexOf("sig=", StringComparison.OrdinalIgnoreCase) < 0)
{
    Common.WriteWarn("TriggerUrl is missing the 'sig=' query parameter.");
    Common.WriteWarn("If you pasted it on the command line, wrap it in single quotes — some shells");
    Common.WriteWarn("(PowerShell, bash) treat '&' as a separator and silently truncate the URL.");
    Common.WriteWarn($"Received: {triggerUrl}");
}

var body = JsonSerializer.Serialize(new
{
    requestId,
    requester,
    amount,
    description,
});

Common.WriteHeading($"POST {triggerUrl}");
Console.WriteLine(body);

try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    using var request = new HttpRequestMessage(HttpMethod.Post, triggerUrl)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
    using var response = await http.SendAsync(request);
    var responseBody = await response.Content.ReadAsStringAsync();

    Console.WriteLine();
    var status = (int)response.StatusCode;
    if (response.IsSuccessStatusCode)
    {
        Common.WriteSuccess($"HTTP {status} {response.ReasonPhrase}");
        if (!string.IsNullOrEmpty(responseBody)) Console.WriteLine(responseBody);
        return 0;
    }

    Common.WriteError($"HTTP {status} {response.ReasonPhrase}");
    if (!string.IsNullOrEmpty(responseBody)) Common.WriteError(responseBody);
    var hints = new Dictionary<int, string>
    {
        [401] = "Hint: SAS signature mismatch. If you passed --trigger-url, wrap it in single quotes or omit it to let the script fetch it.",
        [403] = "Hint: Access denied. Check IP restrictions on the workflow.",
        [404] = "Hint: Workflow or trigger not found. Confirm the workflow is deployed and enabled.",
        [500] = "Hint: The run started but an action failed, often because the Office 365 connection is not authorized in the portal.",
        [502] = "Hint: 502 means a downstream connector returned an error. Usually the Office 365 connection is not authorized. Open the Logic App in the portal, then API connections, office365, Edit API connection, Authorize. Or run: dotnet script scripts/invoke.csx -- --amount 100 to skip the connector.",
    };
    if (hints.TryGetValue(status, out var hint)) Common.WriteWarn(hint);
    return 1;
}
catch (Exception ex)
{
    Console.WriteLine();
    Common.WriteError(ex.Message);
    return 1;
}

string FetchTriggerUrl(string env, string trigger)
{
    var rg = $"rg-ghcp-logicapp-{env}";
    var workflow = $"la-approval-{env}";

    Console.WriteLine($"Fetching trigger URL for {workflow} in {rg}...");

    var sub = Common.Capture("az", new[] { "account", "show", "--query", "id", "-o", "tsv" });
    if (sub.ExitCode != 0)
    {
        Common.WriteError("az account show failed. Run 'az login' first.");
        if (!string.IsNullOrWhiteSpace(sub.Stderr)) Common.WriteError(sub.Stderr);
        return null;
    }
    var subId = sub.Stdout.Trim();

    var uri = $"https://management.azure.com/subscriptions/{subId}/resourceGroups/{rg}" +
              $"/providers/Microsoft.Logic/workflows/{workflow}/triggers/{trigger}" +
              "/listCallbackUrl?api-version=2016-06-01";

    var rest = Common.Capture("az", new[] { "rest", "--method", "post", "--uri", uri });
    if (rest.ExitCode != 0 || string.IsNullOrWhiteSpace(rest.Stdout))
    {
        Common.WriteError($"Could not retrieve trigger URL. Is the workflow deployed? (rg={rg}, workflow={workflow})");
        if (!string.IsNullOrWhiteSpace(rest.Stderr)) Common.WriteError(rest.Stderr);
        return null;
    }

    try
    {
        using var doc = JsonDocument.Parse(rest.Stdout);
        return doc.RootElement.GetProperty("value").GetString();
    }
    catch (Exception ex)
    {
        Common.WriteError($"Could not parse listCallbackUrl response: {ex.Message}");
        return null;
    }
}

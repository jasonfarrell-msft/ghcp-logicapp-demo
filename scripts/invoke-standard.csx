#load "lib/common.csx"
// POSTs a sample approval request to the Logic Apps Standard workflow.
//
// Usage:
//   dotnet script scripts/invoke-standard.csx -- [--environment dev|prod] [--amount 2500]
//                                                  [--trigger-url '<url>'] [--request-id REQ-1]
//                                                  [--requester foo@bar] [--description "..."]
//                                                  [--timeout <seconds>]   default: 300
//
// If --trigger-url is omitted, the script fetches the callback URL via the
// hostruntime management API (Standard path), not the Consumption path.

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
var timeoutSeconds = int.Parse(Common.Get(parsed, "timeout", "300"));
string triggerUrl = parsed.TryGetValue("trigger-url", out var u) ? u : null;

if (environment != "dev" && environment != "prod")
{
    Common.WriteError($"--environment must be 'dev' or 'prod' (got '{environment}').");
    return 1;
}

if (string.IsNullOrEmpty(triggerUrl))
{
    triggerUrl = FetchTriggerUrl(environment);
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

if (timeoutSeconds < 90)
    Common.WriteWarn($"--timeout {timeoutSeconds}s is under the 90 s HTTP response window — the workflow must complete (or time out) before then to return a synchronous response.");
if (timeoutSeconds >= 90)
    Common.WriteWarn($"Waiting up to {timeoutSeconds}s for a synchronous response. If RequestApproval has a limit.timeout < {timeoutSeconds}s it will time out and HandleFailure should return HTTP 502.");

Common.WriteHeading($"POST {triggerUrl}");
Console.WriteLine(body);

try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
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
        [403] = "Hint: 403 — likely the V2 Office 365 connection is not yet authorized.\n" +
                "  Open the Azure portal: Logic App → API connections → con-office365-std-{env} → Edit → Authorize → Save.\n" +
                "  Below-threshold invokes (amount <= threshold) do not use the connector and should succeed.",
        [404] = "Hint: Workflow or trigger not found. Confirm the workflow is deployed and enabled.",
        [500] = "Hint: The run started but an action failed, often because the Office 365 connection is not authorized in the portal.",
        [502] = "HTTP 502 — two possible causes:\n" +
                "  (a) HandleFailure path triggered: RequestApproval timed out or failed (expected in the error-handling demo).\n" +
                "  (b) Office 365 connection not authorized: open the portal → API connections → Edit → Authorize.\n" +
                "  To demo the timeout path: set RequestApproval limit.timeout to PT1M, redeploy, then run with --timeout 90.",
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

string FetchTriggerUrl(string env)
{
    var rg = $"rg-ghcp-logicapp-{env}";
    var siteName = $"la-approval-std-{env}";
    var triggerName = "When_an_approval_request_is_received";

    Console.WriteLine($"Fetching trigger URL for {siteName} in {rg}...");

    var sub = Common.Capture("az", new[] { "account", "show", "--query", "id", "-o", "tsv" });
    if (sub.ExitCode != 0)
    {
        Common.WriteError("az account show failed. Run 'az login' first.");
        if (!string.IsNullOrWhiteSpace(sub.Stderr)) Common.WriteError(sub.Stderr);
        return null;
    }
    var subId = sub.Stdout.Trim();

    // Standard uses the hostruntime management path — NOT the Consumption
    // Microsoft.Logic/workflows path (which returns 404 for Standard sites).
    var uri = $"https://management.azure.com/subscriptions/{subId}/resourceGroups/{rg}" +
              $"/providers/Microsoft.Web/sites/{siteName}" +
              $"/hostruntime/runtime/webhooks/workflow/api/management/workflows/Approval" +
              $"/triggers/{triggerName}/listCallbackUrl" +
              "?api-version=2024-04-01";

    var rest = Common.Capture("az", new[] { "rest", "--method", "post", "--uri", uri });
    if (rest.ExitCode != 0 || string.IsNullOrWhiteSpace(rest.Stdout))
    {
        Common.WriteError($"Could not retrieve trigger URL. Is the workflow deployed? (rg={rg}, site={siteName})");
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

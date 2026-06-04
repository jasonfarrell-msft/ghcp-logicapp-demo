#load "lib/common.csx"
// Deploy Logic Apps Standard infrastructure and workflow content.
// Usage: dotnet script scripts/deploy-standard.csx -- [--environment dev|prod] [--skip-content]

using System;
using System.IO;
using System.Text.Json;

var parsed = Common.ParseArgs(Args);
var environment = Common.Get(parsed, "environment", "dev");
var location = Common.Get(parsed, "location", "swedencentral");
var skipContent = parsed.ContainsKey("skip-content");

if (environment != "dev" && environment != "prod")
{
    Common.WriteError($"--environment must be 'dev' or 'prod' (got '{environment}').");
    return 1;
}

var root = Common.RepoRoot();
var paramFile = Path.Combine(root, "infra-standard", "parameters", $"{environment}.bicepparam");
var mainFile = Path.Combine(root, "infra-standard", "main.bicep");
var standardDir = Path.Combine(root, "standard");

// ──────────────────────────────────────────────────────────────
// Phase 1: Build Bicep
// ──────────────────────────────────────────────────────────────
Common.WriteHeading("Building Bicep template");
var buildResult = Common.Run("az", new[] { "bicep", "build", "--file", mainFile });
if (buildResult != 0)
{
    Common.WriteError("Bicep build failed.");
    return buildResult;
}

// ──────────────────────────────────────────────────────────────
// Phase 2: Deploy infrastructure
// ──────────────────────────────────────────────────────────────
Common.WriteHeading($"Deploying Standard infrastructure ({environment})");

var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var deploymentName = $"ghcp-logicapp-standard-{environment}-{stamp}";

var deployResult = Common.Run("az", new[]
{
    "deployment", "sub", "create",
    "--name", deploymentName,
    "--location", location,
    "--template-file", mainFile,
    "--parameters", paramFile,
    "--parameters", $"location={location}",
});

if (deployResult != 0)
{
    Common.WriteError("Infrastructure deployment failed.");
    return deployResult;
}

// ──────────────────────────────────────────────────────────────
// Phase 3: Retrieve deployment outputs
// ──────────────────────────────────────────────────────────────
Common.WriteHeading("Retrieving deployment outputs");

var showCmd = Common.Capture("az", new[]
{
    "deployment", "sub", "show",
    "--name", deploymentName,
    "--query", "properties.outputs",
    "-o", "json",
});

if (showCmd.ExitCode != 0 || string.IsNullOrWhiteSpace(showCmd.Stdout))
{
    Common.WriteError("Could not retrieve deployment outputs.");
    if (!string.IsNullOrWhiteSpace(showCmd.Stderr)) Common.WriteError(showCmd.Stderr);
    return 1;
}

string workflowAppName, resourceGroupName;
try
{
    using var doc = JsonDocument.Parse(showCmd.Stdout);
    workflowAppName = doc.RootElement.GetProperty("workflowAppName").GetProperty("value").GetString();
    resourceGroupName = doc.RootElement.GetProperty("resourceGroupName").GetProperty("value").GetString();
    Console.WriteLine($"  Workflow App: {workflowAppName}");
    Console.WriteLine($"  Resource Group: {resourceGroupName}");
}
catch (Exception ex)
{
    Common.WriteError($"Could not parse deployment outputs: {ex.Message}");
    return 1;
}

// ──────────────────────────────────────────────────────────────
// Phase 4: Publish workflow content
// ──────────────────────────────────────────────────────────────
if (skipContent)
{
    Common.WriteWarn("Skipping workflow content publish (--skip-content).");
}
else
{
    Common.WriteHeading("Publishing workflows...");
    Console.WriteLine($"Zip-deploying {standardDir} to {workflowAppName}");

    var shell = Environment.OSVersion.Platform == PlatformID.Unix ? "/bin/zsh" : "cmd.exe";
    var shellArg = Environment.OSVersion.Platform == PlatformID.Unix ? "-c" : "/c";

    // Build a zip that includes the workflow folder, host.json, connections.json, parameters.json
    // (excludes local.settings.json — runtime app settings come from Azure)
    var zipPath = Path.Combine(Path.GetTempPath(), $"logicapp-{workflowAppName}-{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
    var zipCommand = $"cd '{standardDir}' && zip -r '{zipPath}' . -x 'local.settings.json' '.git/*' '.vscode/*' 'node_modules/*'";
    var zipResult = Common.Run(shell, new[] { shellArg, zipCommand });
    if (zipResult != 0)
    {
        Common.WriteError("Failed to create deployment zip.");
        return zipResult;
    }
    Console.WriteLine($"Created zip: {zipPath}");

    // az webapp deploy uses Kudu zip-deploy and works correctly for Logic Apps Standard
    var publishResult = Common.Run("az", new[]
    {
        "webapp", "deploy",
        "--resource-group", resourceGroupName,
        "--name", workflowAppName,
        "--src-path", zipPath,
        "--type", "zip",
        "-o", "none"
    });

    File.Delete(zipPath);

    if (publishResult != 0)
    {
        Common.WriteError("Workflow content publish failed.");
        return publishResult;
    }
}

// ──────────────────────────────────────────────────────────────
// Phase 4.5: Verify V2 connection authorization
// ──────────────────────────────────────────────────────────────
// V2 connections are created in an Unauthorized state and require a one-time
// interactive OAuth sign-in before the workflow runtime will accept them.
// We check the office365 connection and print the portal URL if not Connected.
Common.WriteHeading("Verifying connection authorization");

var subIdCmd = Common.Capture("az", new[] { "account", "show", "--query", "id", "-o", "tsv" });
if (subIdCmd.ExitCode != 0)
{
    Common.WriteError("Could not retrieve subscription ID.");
    return 1;
}
var subscriptionId = subIdCmd.Stdout.Trim();

// Connections to verify: name -> display label
var connectionsToCheck = new[] {
    ($"con-office365-std-{environment}", "Office 365"),
};

bool allConnectionsAuthorized = true;
foreach (var (connName, connLabel) in connectionsToCheck)
{
    var connCmd = Common.Capture("az", new[]
    {
        "rest", "--method", "get",
        "--url", $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Web/connections/{connName}?api-version=2018-07-01-preview",
        "--query", "{statuses:properties.statuses,runtimeUrl:properties.connectionRuntimeUrl}",
        "-o", "json"
    });

    bool authorized = false;
    string runtimeUrl = null;
    if (connCmd.ExitCode == 0 && !string.IsNullOrWhiteSpace(connCmd.Stdout))
    {
        try
        {
            using var connDoc = JsonDocument.Parse(connCmd.Stdout);
            runtimeUrl = connDoc.RootElement.TryGetProperty("runtimeUrl", out var rt) ? rt.GetString() : null;
            if (connDoc.RootElement.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in statuses.EnumerateArray())
                {
                    if (s.TryGetProperty("status", out var st) &&
                        string.Equals(st.GetString(), "Connected", StringComparison.OrdinalIgnoreCase))
                    {
                        authorized = true;
                        break;
                    }
                }
            }
        }
        catch { /* non-fatal */ }
    }

    Console.WriteLine($"  {connLabel} ({connName}): {(authorized ? "Connected" : "NOT AUTHORIZED")}");
    if (runtimeUrl != null) Console.WriteLine($"    Runtime URL: {runtimeUrl}");

    if (!authorized)
    {
        allConnectionsAuthorized = false;
        Console.WriteLine();
        Common.WriteWarn($"⚠️  {connLabel} connection needs authorization.");
        Console.WriteLine($"    1. Open  https://portal.azure.com/#@/resource/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Web/connections/{connName}/edit");
        Console.WriteLine("    2. Click  'Authorize'  and sign in.");
        Console.WriteLine("    3. Click  'Save'.");
        Console.WriteLine();
    }
}

if (!allConnectionsAuthorized)
{
    Common.WriteWarn("One or more connections need authorization. Authorize them in the portal (URLs above),");
    Common.WriteWarn("then re-run this script. The runtime will not start until all connections are authorized.");
    return 0;
}

// All connections authorized — restart so the runtime picks up any new app settings.
Console.WriteLine();
Console.WriteLine("All connections authorized. Restarting workflow app...");
Common.Run("az", new[]
{
    "webapp", "restart",
    "--resource-group", resourceGroupName,
    "--name", workflowAppName,
    "-o", "none"
});

// ──────────────────────────────────────────────────────────────
// Phase 4.6: Wait for workflow runtime to be ready
// ──────────────────────────────────────────────────────────────
// After a zip deploy + restart the WS1 host can take 60-180s to load the
// extension bundle, mount the content share, and register workflows.
// Poll the management endpoint rather than using a fixed sleep.
Common.WriteHeading("Waiting for workflow runtime to be ready...");
var pollUri = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}" +
    $"/providers/Microsoft.Web/sites/{workflowAppName}/hostruntime/runtime/webhooks/workflow" +
    "/api/management/workflows?api-version=2024-04-01";
var runtimeReady = false;
for (int attempt = 1; attempt <= 18; attempt++) // 18 x 10s = 3 min
{
    System.Threading.Thread.Sleep(10000);
    var probe = Common.Capture("az", new[] { "rest", "--method", "get", "--uri", pollUri, "-o", "none" });
    if (probe.ExitCode == 0)
    {
        runtimeReady = true;
        Console.WriteLine($"  ✓ Runtime ready after ~{attempt * 10}s.");
        break;
    }
    Console.Write($"  [{attempt,2}/18] starting");
    for (int dot = 0; dot < (attempt % 4) + 1; dot++) Console.Write(".");
    Console.WriteLine();
}
if (!runtimeReady)
{
    Common.WriteWarn("Runtime did not become ready within 3 minutes.");
    Common.WriteWarn("The workflow may still be starting. Wait a moment and then invoke.");
}

// ──────────────────────────────────────────────────────────────
// Phase 5: Retrieve trigger callback URL
// ──────────────────────────────────────────────────────────────
Common.WriteHeading("Retrieving trigger callback URL");

var subId = subscriptionId; // already resolved in Phase 4.5

var workflowName = "Approval"; // Folder name in standard/Approval/
var triggerName = "When_an_approval_request_is_received";

var uri = $"https://management.azure.com/subscriptions/{subId}/resourceGroups/{resourceGroupName}" +
          $"/providers/Microsoft.Web/sites/{workflowAppName}/hostruntime/runtime/webhooks/workflow" +
          $"/api/management/workflows/{workflowName}/triggers/{triggerName}" +
          "/listCallbackUrl?api-version=2023-12-01";

var restCmd = Common.Capture("az", new[] { "rest", "--method", "post", "--uri", uri });
if (restCmd.ExitCode != 0 || string.IsNullOrWhiteSpace(restCmd.Stdout))
{
    Common.WriteWarn("Could not retrieve trigger callback URL.");
    if (!string.IsNullOrWhiteSpace(restCmd.Stderr)) Common.WriteError(restCmd.Stderr);
    Common.WriteWarn($"Is the workflow deployed? (rg={resourceGroupName}, site={workflowAppName}, workflow={workflowName})");
}
else
{
    try
    {
        using var urlDoc = JsonDocument.Parse(restCmd.Stdout);
        var triggerUrl = urlDoc.RootElement.GetProperty("value").GetString();
        Console.WriteLine();
        Common.WriteSuccess("✅ Deployment complete!");
        Console.WriteLine();
        Console.WriteLine("Trigger URL:");
        Console.WriteLine(triggerUrl);
        Console.WriteLine();
        Console.WriteLine("Test with:");
        Console.WriteLine($"  dotnet script scripts/invoke-standard.csx -- --environment {environment} --amount 500");
        Console.WriteLine($"  dotnet script scripts/invoke-standard.csx -- --environment {environment} --amount 2500");
    }
    catch (Exception ex)
    {
        Common.WriteWarn($"Could not parse trigger URL: {ex.Message}");
    }
}

// ──────────────────────────────────────────────────────────────
// Phase 6: Recent run history (smoke check)
// ──────────────────────────────────────────────────────────────
if (runtimeReady)
{
    Common.WriteHeading("Recent workflow runs");
    var runsUri = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}" +
        $"/providers/Microsoft.Web/sites/{workflowAppName}/hostruntime/runtime/webhooks/workflow" +
        $"/api/management/workflows/{workflowName}/runs?api-version=2024-04-01&$top=5";
    var runsCmd = Common.Capture("az", new[] { "rest", "--method", "get", "--uri", runsUri, "-o", "json" });
    if (runsCmd.ExitCode == 0 && !string.IsNullOrWhiteSpace(runsCmd.Stdout))
    {
        try
        {
            using var runsDoc = JsonDocument.Parse(runsCmd.Stdout);
            var runs = runsDoc.RootElement.GetProperty("value");
            if (runs.GetArrayLength() == 0)
            {
                Console.WriteLine("  No runs yet — invoke the workflow to create the first run.");
            }
            else
            {
                Console.WriteLine($"  {"Status",-12}  {"Start time",-26}  Run ID");
                Console.WriteLine($"  {new string('-', 12)}  {new string('-', 26)}  {new string('-', 34)}");
                foreach (var run in runs.EnumerateArray())
                {
                    var props = run.GetProperty("properties");
                    var runStatus = props.TryGetProperty("status", out var s) ? s.GetString() : "?";
                    var startTime = props.TryGetProperty("startTime", out var t) ? t.GetString() : "";
                    var runId = run.GetProperty("name").GetString();
                    var prev = Console.ForegroundColor;
                    Console.ForegroundColor = runStatus == "Succeeded" ? ConsoleColor.Green
                                            : runStatus == "Failed"    ? ConsoleColor.Red
                                            : ConsoleColor.Yellow;
                    Console.Write($"  {runStatus,-12}");
                    Console.ForegroundColor = prev;
                    Console.WriteLine($"  {startTime,-26}  {runId}");

                    // For any failed run, print action-level status so the cause is immediately visible.
                    if (runStatus == "Failed")
                    {
                        var actionsUri = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}" +
                            $"/providers/Microsoft.Web/sites/{workflowAppName}/hostruntime/runtime/webhooks/workflow" +
                            $"/api/management/workflows/{workflowName}/runs/{runId}/actions?api-version=2024-04-01";
                        var actionsCmd = Common.Capture("az", new[] { "rest", "--method", "get", "--uri", actionsUri, "-o", "json" });
                        if (actionsCmd.ExitCode == 0 && !string.IsNullOrWhiteSpace(actionsCmd.Stdout))
                        {
                            try
                            {
                                using var actDoc = JsonDocument.Parse(actionsCmd.Stdout);
                                foreach (var action in actDoc.RootElement.GetProperty("value").EnumerateArray())
                                {
                                    var ap = action.GetProperty("properties");
                                    var aStatus = ap.TryGetProperty("status", out var as_) ? as_.GetString() : "?";
                                    var aCode = ap.TryGetProperty("code", out var ac) ? ac.GetString() : "";
                                    var aName = action.GetProperty("name").GetString();
                                    if (aStatus == "Failed")
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.Write($"    ✗ {aName,-40}");
                                        Console.ForegroundColor = prev;
                                        Console.WriteLine($"  {aCode}");
                                        if (ap.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
                                            Console.WriteLine($"      {err}");
                                    }
                                }
                            }
                            catch { /* non-fatal */ }
                        }
                    }
                }
            }
        }
        catch { /* non-fatal */ }
    }
}

return 0;

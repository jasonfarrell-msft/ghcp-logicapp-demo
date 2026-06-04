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
// Phase 4.5: Verify V2 connection runtime URL and authorization
// ──────────────────────────────────────────────────────────────
// The Bicep already wires `office365-ConnectionRuntimeUrl` and
// `office365-ConnectionName` into the workflow app from the V2 connection's
// `properties.connectionRuntimeUrl`. The remaining gap is the OAuth consent —
// V2 connections are created in an *Unauthorized* state and require a one-time
// interactive sign-in in the Azure portal before the workflow can send mail.
Common.WriteHeading("Verifying Office 365 connection authorization");

var subIdCmd = Common.Capture("az", new[] { "account", "show", "--query", "id", "-o", "tsv" });
if (subIdCmd.ExitCode != 0)
{
    Common.WriteError("Could not retrieve subscription ID.");
    return 1;
}
var subscriptionId = subIdCmd.Stdout.Trim();
var connectionName = $"con-office365-std-{environment}";

var connStatusCmd = Common.Capture("az", new[]
{
    "rest", "--method", "get",
    "--url", $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Web/connections/{connectionName}?api-version=2018-07-01-preview",
    "--query", "{statuses:properties.statuses,runtimeUrl:properties.connectionRuntimeUrl}",
    "-o", "json"
});

bool connectionAuthorized = false;
string runtimeUrl = null;
if (connStatusCmd.ExitCode == 0 && !string.IsNullOrWhiteSpace(connStatusCmd.Stdout))
{
    try
    {
        using var connDoc = JsonDocument.Parse(connStatusCmd.Stdout);
        runtimeUrl = connDoc.RootElement.TryGetProperty("runtimeUrl", out var rt) ? rt.GetString() : null;
        if (connDoc.RootElement.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in statuses.EnumerateArray())
            {
                if (s.TryGetProperty("status", out var st) &&
                    string.Equals(st.GetString(), "Connected", StringComparison.OrdinalIgnoreCase))
                {
                    connectionAuthorized = true;
                    break;
                }
            }
        }
    }
    catch (Exception ex)
    {
        Common.WriteWarn($"Could not parse connection status: {ex.Message}");
    }
}
else if (!string.IsNullOrWhiteSpace(connStatusCmd.Stderr))
{
    Common.WriteWarn(connStatusCmd.Stderr);
}

Console.WriteLine($"  Connection: {connectionName}");
Console.WriteLine($"  Runtime URL: {runtimeUrl ?? "(not yet available)"}");
Console.WriteLine($"  Authorized: {(connectionAuthorized ? "yes" : "NO — manual step required")}");

if (!connectionAuthorized)
{
    Console.WriteLine();
    Common.WriteWarn("⚠️  The Office 365 connection has not been authorized yet.");
    Common.WriteWarn("    Until you authorize it, the Send_approval_email step will fail at runtime.");
    Console.WriteLine();
    Console.WriteLine("    Authorize it in the Azure portal:");
    Console.WriteLine($"      1. Open  https://portal.azure.com/#@/resource/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Web/connections/{connectionName}/edit");
    Console.WriteLine("      2. Click  'Authorize'  and sign in as the approver mailbox.");
    Console.WriteLine("      3. Click  'Save'.");
    Console.WriteLine();
    Console.WriteLine("    Then re-run this deploy script (or just invoke the workflow).");
}
else
{
    Console.WriteLine("Restarting workflow app to pick up the latest connection runtime URL...");
    Common.Run("az", new[]
    {
        "webapp", "restart",
        "--resource-group", resourceGroupName,
        "--name", workflowAppName,
        "-o", "none"
    });
    Console.WriteLine("Waiting 30 seconds for workflow runtime to register workflows...");
    System.Threading.Thread.Sleep(30000);
}

// ──────────────────────────────────────────────────────────────
// Phase 5: Retrieve trigger callback URL
// ──────────────────────────────────────────────────────────────
Common.WriteHeading("Retrieving trigger callback URL");

var subCmd = Common.Capture("az", new[] { "account", "show", "--query", "id", "-o", "tsv" });
if (subCmd.ExitCode != 0)
{
    Common.WriteError("Could not retrieve subscription ID.");
    return 1;
}
var subId = subCmd.Stdout.Trim();

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
    Common.WriteWarn("You may need to authorize the Office 365 connection in the portal before invoking.");
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
        if (!connectionAuthorized)
        {
            Common.WriteWarn("Authorize the Office 365 connection (see instructions above) BEFORE invoking,");
            Common.WriteWarn("otherwise Send_approval_email will fail with 403 'missing connection ACL'.");
            Console.WriteLine();
        }
        Console.WriteLine("Test with:");
        Console.WriteLine($"  dotnet script scripts/invoke-standard.csx -- --environment {environment} --amount 2500");
    }
    catch (Exception ex)
    {
        Common.WriteWarn($"Could not parse trigger URL: {ex.Message}");
    }
}

return 0;

#load "lib/common.csx"
// Cross-platform deployment script for Logic Apps Standard.
// Usage: dotnet script scripts/deploy-standard.csx -- [--environment dev|prod] [--location eastus]

using System;
using System.IO;
using System.Text.Json;

var parsed = Common.ParseArgs(Args);
var environment = Common.Get(parsed, "environment", "dev");
var location = Common.Get(parsed, "location", "swedencentral");

if (environment != "dev" && environment != "prod")
{
    Common.WriteError($"--environment must be 'dev' or 'prod' (got '{environment}').");
    return 1;
}

var root = Common.RepoRoot();
var paramFile = Path.Combine(root, "infra-standard", "parameters", $"{environment}.bicepparam");
var mainFile = Path.Combine(root, "infra-standard", "main.bicep");
var standardDir = Path.Combine(root, "standard");

Common.WriteHeading($"▶ Building Bicep template");
var buildExit = Common.Run("az", new[] { "bicep", "build", "--file", mainFile });
if (buildExit != 0)
{
    Common.WriteError("Bicep build failed.");
    return buildExit;
}

Common.WriteHeading($"▶ Deploying {environment} infrastructure from {mainFile}");
var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var deploymentName = $"ghcp-logicapp-standard-{environment}-{stamp}";

var deployExit = Common.Run("az", new[]
{
    "deployment", "sub", "create",
    "--name", deploymentName,
    "--location", location,
    "--template-file", mainFile,
    "--parameters", paramFile,
});

if (deployExit != 0)
{
    Common.WriteError("Deployment failed.");
    return deployExit;
}

Common.WriteSuccess("✓ Infrastructure deployed");

// Get outputs from deployment
var outputResult = Common.Capture("az", new[]
{
    "deployment", "sub", "show",
    "--name", deploymentName,
    "--query", "properties.outputs",
    "-o", "json"
});

if (outputResult.ExitCode != 0)
{
    Common.WriteError("Failed to retrieve deployment outputs.");
    return outputResult.ExitCode;
}

var outputs = JsonDocument.Parse(outputResult.Stdout);
var workflowAppName = outputs.RootElement.GetProperty("workflowAppName").GetProperty("value").GetString();
var resourceGroupName = outputs.RootElement.GetProperty("resourceGroupName").GetProperty("value").GetString();

Common.WriteHeading($"▶ Publishing workflows to {workflowAppName}...");

var publishExit = Common.Run("func", new[]
{
    "azure", "functionapp", "publish", workflowAppName
}, standardDir);

if (publishExit != 0)
{
    Common.WriteError("Workflow publish failed.");
    return publishExit;
}

Common.WriteSuccess("✓ Workflows published");

// Get subscription ID
var subResult = Common.Capture("az", new[] { "account", "show", "--query", "id", "-o", "tsv" });
if (subResult.ExitCode != 0)
{
    Common.WriteError("Failed to get subscription ID.");
    return subResult.ExitCode;
}
var subscriptionId = subResult.Stdout.Trim();

// Get callback URL
Common.WriteHeading($"▶ Fetching trigger callback URL...");

var urlPath = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Web/sites/{workflowAppName}/hostruntime/runtime/webhooks/workflow/api/management/workflows/Approval/triggers/When_an_approval_request_is_received/listCallbackUrl?api-version=2022-03-01";

var urlResult = Common.Capture("az", new[]
{
    "rest",
    "--method", "post",
    "--uri", urlPath,
    "--query", "value",
    "-o", "tsv"
});

if (urlResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(urlResult.Stdout))
{
    Common.WriteSuccess($"\n✓ Trigger URL:\n{urlResult.Stdout.Trim()}\n");
}
else
{
    Common.WriteWarn("Could not retrieve trigger URL. Check workflow deployment in portal.");
}

Common.WriteSuccess($"✓ Deployment complete for {environment}");
return 0;

#load "lib/common.csx"
// Cross-platform replacement for scripts/deploy-standard.ps1.
// Usage: dotnet script scripts/deploy-standard.csx -- [--environment dev|prod]
//                                                     [--location eastus] [--skip-content]

using System;
using System.IO;
using System.Text.Json;

var parsed = Common.ParseArgs(Args);
var environment = Common.Get(parsed, "environment", "dev");
var location = Common.Get(parsed, "location", "eastus");
var skipContent = Common.GetFlag(parsed, "skip-content");

if (environment != "dev" && environment != "prod")
{
    Common.WriteError($"--environment must be 'dev' or 'prod' (got '{environment}').");
    return 1;
}

var root = Common.RepoRoot();
var paramFile = Path.Combine(root, "infra-standard", "parameters", $"{environment}.bicepparam");
var mainFile = Path.Combine(root, "infra-standard", "main.bicep");
var projectDir = Path.Combine(root, "standard");

Common.WriteHeading($"Deploying Logic Apps Standard infra ({environment}) from {mainFile}");

var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var deploymentName = $"ghcp-logicapp-std-{environment}-{stamp}";

var result = Common.Capture("az", new[]
{
    "deployment", "sub", "create",
    "--name", deploymentName,
    "--location", location,
    "--template-file", mainFile,
    "--parameters", paramFile,
    "--query", "properties.outputs",
    "-o", "json",
});

if (result.ExitCode != 0)
{
    if (!string.IsNullOrWhiteSpace(result.Stderr)) Console.Error.Write(result.Stderr);
    Common.WriteError("Bicep deployment failed.");
    return result.ExitCode;
}

string siteName, rgName;
try
{
    using var doc = JsonDocument.Parse(result.Stdout);
    rgName = doc.RootElement.GetProperty("resourceGroupName").GetProperty("value").GetString();
    siteName = doc.RootElement.GetProperty("siteName").GetProperty("value").GetString();
}
catch (Exception ex)
{
    Common.WriteError($"Could not parse deployment outputs: {ex.Message}");
    Console.WriteLine(result.Stdout);
    return 1;
}

if (skipContent)
{
    Common.WriteWarn("Infra deployed. Skipping content publish.");
    return 0;
}

if (!Common.CommandExists("func"))
{
    Common.WriteWarn("Azure Functions Core Tools (func) not found on PATH.");
    Common.WriteWarn("Install from https://learn.microsoft.com/azure/azure-functions/functions-run-local then run:");
    Common.WriteWarn($"    cd \"{projectDir}\" && func azure functionapp publish {siteName}");
    return 0;
}

Common.WriteHeading($"Publishing {projectDir} to {siteName}...");
var publishExit = Common.Run("func",
    new[] { "azure", "functionapp", "publish", siteName },
    workingDir: projectDir);

if (publishExit != 0)
{
    Common.WriteError($"func publish failed with exit code {publishExit}.");
    return publishExit;
}

Common.WriteSuccess($"Done. Site: {siteName} in {rgName}");
return 0;

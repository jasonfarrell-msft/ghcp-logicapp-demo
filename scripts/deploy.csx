#load "lib/common.csx"
// Cross-platform replacement for scripts/deploy.ps1.
// Usage: dotnet script scripts/deploy.csx -- [--environment dev|prod] [--location swedencentral]

using System;
using System.IO;

var parsed = Common.ParseArgs(Args);
var environment = Common.Get(parsed, "environment", "dev");
var location = Common.Get(parsed, "location", "swedencentral");

if (environment != "dev" && environment != "prod")
{
    Common.WriteError($"--environment must be 'dev' or 'prod' (got '{environment}').");
    return 1;
}

var root = Common.RepoRoot();
var paramFile = Path.Combine(root, "infra", "parameters", $"{environment}.bicepparam");
var mainFile = Path.Combine(root, "infra", "main.bicep");

Common.WriteHeading($"Deploying {environment} from {mainFile}");

var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var deploymentName = $"ghcp-logicapp-{environment}-{stamp}";

return Common.Run("az", new[]
{
    "deployment", "sub", "create",
    "--name", deploymentName,
    "--location", location,
    "--template-file", mainFile,
    "--parameters", paramFile,
});

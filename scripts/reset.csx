#load "lib/common.csx"
// Cross-platform replacement for scripts/reset.ps1.
// Usage: dotnet script scripts/reset.csx -- [--environment dev|prod]

using System;

var parsed = Common.ParseArgs(Args);
var environment = Common.Get(parsed, "environment", "dev");

if (environment != "dev" && environment != "prod")
{
    Common.WriteError($"--environment must be 'dev' or 'prod' (got '{environment}').");
    return 1;
}

var root = Common.RepoRoot();

Common.WriteWarn("Resetting working tree to baseline...");
var restoreExit = Common.Run("git", new[] { "restore", "." }, workingDir: root);
if (restoreExit != 0)
{
    Common.WriteError($"git restore failed (exit {restoreExit}).");
    return restoreExit;
}
// git clean output is intentionally discarded to mirror the original behavior.
Common.Capture("git", new[] { "clean", "-fd", "scenarios", "docs" }, workingDir: root);

var rgName = $"rg-ghcp-logicapp-{environment}";
Common.WriteHeading($"Deleting resource group {rgName}...");
var delExit = Common.Run("az", new[] { "group", "delete", "--name", rgName, "--yes", "--no-wait" });
if (delExit != 0)
{
    Common.WriteError($"az group delete failed (exit {delExit}).");
    return delExit;
}

Common.WriteSuccess($"Resource group {rgName} deletion initiated.");
return 0;

#load "lib/common.csx"
// Deploy the Logic Apps Standard side-by-side project.
// Usage: dotnet script scripts/deploy-standard.csx -- [--environment dev|prod] [--location swedencentral] [--skip-content]

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

var parsed = Common.ParseArgs(Args);
var environment = Common.Get(parsed, "environment", "dev");
var location = Common.Get(parsed, "location", "swedencentral");
var skipContent = Common.GetFlag(parsed, "skip-content");
// --content-only: skip bicep build + ARM deployment, only re-zip and re-deploy
// the workflow content. Useful for tight iteration loops while debugging
// workflow.json. Each invocation increments scripts/.build-version and stamps
// it into the workflow's `definition.description` so you can confirm the
// runtime is loading your latest edit.
var contentOnly = Common.GetFlag(parsed, "content-only");

if (environment != "dev" && environment != "prod")
{
    Common.WriteError($"--environment must be 'dev' or 'prod' (got '{environment}').");
    return 1;
}

var root = Common.RepoRoot();
var mainFile  = Path.Combine(root, "infra-standard", "main.bicep");
var paramFile = Path.Combine(root, "infra-standard", "parameters", $"{environment}.bicepparam");
var standardDir = Path.Combine(root, "standard");
var zipPath = Path.Combine(root, $"standard-{environment}.zip");
var buildVersionFile = Path.Combine(root, "scripts", ".build-version");

string rgName, workflowAppName, office365ConnectionName;

if (contentOnly)
{
    // Skip bicep + ARM. Resolve names from convention (matches main.bicep).
    rgName                  = $"rg-ghcp-logicapp-{environment}";
    workflowAppName         = $"la-approval-std-{environment}";
    office365ConnectionName = $"con-office365-std-{environment}";
    Common.WriteWarn($"--content-only: skipping Phases 1+2 (bicep build + ARM deploy)");
    Common.WriteWarn($"   targeting rg={rgName}, site={workflowAppName}");
}
else
{
    // --- Phase 1: bicep build -----------------------------------------------
    Common.WriteHeading($"[1/7] Building Bicep template {mainFile}");
    var build = Common.Run("az", new[] { "bicep", "build", "--file", mainFile });
    if (build != 0) { Common.WriteError("Bicep build failed."); return build; }

    // --- Phase 2: subscription-scoped deployment ----------------------------
    var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var deploymentName = $"ghcp-logicapp-std-{environment}-{stamp}";
    Common.WriteHeading($"[2/7] az deployment sub create --location {location}");

    var deploy = Common.Capture("az", new[]
    {
        "deployment", "sub", "create",
        "--name", deploymentName,
        "--location", location,
        "--template-file", mainFile,
        "--parameters", paramFile,
        "-o", "json",
    });
    if (deploy.ExitCode != 0)
    {
        Common.WriteError("Deployment failed.");
        if (!string.IsNullOrWhiteSpace(deploy.Stderr)) Common.WriteError(deploy.Stderr);
        if (!string.IsNullOrWhiteSpace(deploy.Stdout)) Console.WriteLine(deploy.Stdout);
        return deploy.ExitCode;
    }

    try
    {
        using var doc = JsonDocument.Parse(deploy.Stdout);
        var outputs = doc.RootElement.GetProperty("properties").GetProperty("outputs");
        rgName                  = outputs.GetProperty("resourceGroupName").GetProperty("value").GetString();
        workflowAppName         = outputs.GetProperty("workflowAppName").GetProperty("value").GetString();
        office365ConnectionName = outputs.GetProperty("office365ConnectionName").GetProperty("value").GetString();
    }
    catch (Exception ex)
    {
        Common.WriteError($"Could not read deployment outputs: {ex.Message}");
        return 1;
    }
    Common.WriteSuccess($"Deployed: rg={rgName}, site={workflowAppName}, connection={office365ConnectionName}");
}

// --- Phase 3: zip + deploy standard/ content -------------------------------
int buildNumber = 0;
if (skipContent)
{
    Common.WriteWarn("[3/7] --skip-content set; skipping zip-deploy.");
}
else
{
    // Increment build counter and stamp it into the workflow description so
    // we can verify the runtime picked up our edit.
    buildNumber = ReadBuildNumber(buildVersionFile) + 1;
    File.WriteAllText(buildVersionFile, buildNumber.ToString());
    StampBuildIntoWorkflow(Path.Combine(standardDir, "Approval", "workflow.json"), buildNumber);

    Common.WriteHeading($"[3/7] Packaging {standardDir} → {zipPath}  (build #{buildNumber})");
    if (File.Exists(zipPath)) File.Delete(zipPath);
    ZipFile.CreateFromDirectory(standardDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

    // Wait for the SCM endpoint to be ready before zip-deploying. On first
    // deploy the site was just created by Bicep and the worker/Kudu may not
    // be listening yet — hitting it too early returns HTTP 502.
    Common.WriteHeading($"      Waiting for SCM endpoint to be ready...");
    for (var attempt = 1; attempt <= 18; attempt++)
    {
        var probe = Common.Capture("az", new[]
        {
            "webapp", "show",
            "--resource-group", rgName,
            "--name", workflowAppName,
            "--query", "state",
            "-o", "tsv",
        });
        var state = probe.Stdout?.Trim();
        if (probe.ExitCode == 0 && string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
        {
            // Site reports Running — give SCM a few more seconds on first deploy
            if (attempt == 1) System.Threading.Thread.Sleep(5_000);
            Common.WriteSuccess($"      Site state: {state}");
            break;
        }
        Console.WriteLine($"      [attempt {attempt}/18] site state={state ?? "unknown"}, waiting...");
        System.Threading.Thread.Sleep(10_000);
        if (attempt == 18)
        {
            Common.WriteWarn("Site did not reach Running state after 3 minutes — attempting zip deploy anyway.");
        }
    }

    Common.WriteHeading($"      az webapp deploy --type zip --src-path {Path.GetFileName(zipPath)}");
    var publish = Common.Run("az", new[]
    {
        "webapp", "deploy",
        "--resource-group", rgName,
        "--name", workflowAppName,
        "--src-path", zipPath,
        "--type", "zip",
    });
    if (publish != 0) { Common.WriteError("Zip-deploy failed."); return publish; }
    Common.WriteSuccess($"Workflow content deployed (build #{buildNumber}).");
}

// --- Phase 4: workflow runtime health -------------------------------------
// This catches schema errors in workflow.json (e.g. wrong key shape, hyphenated
// Switch case object keys, V1 'name' vs Standard 'referenceName') BEFORE we
// hand the user a trigger URL that would only fail at invoke time.
var subId = GetSubscriptionId();
if (subId is null) return 1;

Common.WriteHeading("[4/7] Checking workflow runtime health");
var healthUri = $"https://management.azure.com/subscriptions/{subId}/resourceGroups/{rgName}" +
                $"/providers/Microsoft.Web/sites/{workflowAppName}" +
                $"/hostruntime/runtime/webhooks/workflow/api/management/workflows/Approval" +
                $"?api-version=2024-04-01";

string healthState = null;
string healthError = null;
for (var attempt = 1; attempt <= 18; attempt++)
{
    var h = Common.Capture("az", new[] { "rest", "--method", "get", "--uri", healthUri });
    if (h.ExitCode == 0 && !string.IsNullOrWhiteSpace(h.Stdout))
    {
        try
        {
            using var doc = JsonDocument.Parse(h.Stdout);
            if (doc.RootElement.TryGetProperty("health", out var health))
            {
                healthState = health.TryGetProperty("state", out var s) ? s.GetString() : null;
                if (health.TryGetProperty("errorMessage", out var em))
                    healthError = em.ToString();
                if (!string.IsNullOrEmpty(healthState)) break;
            }
        }
        catch { /* runtime still starting */ }
    }
    Console.Write($"  [attempt {attempt}/18] runtime starting...");
    System.Threading.Thread.Sleep(10_000);
    Console.WriteLine();
}

if (string.Equals(healthState, "Healthy", StringComparison.OrdinalIgnoreCase))
{
    Common.WriteSuccess("Workflow is healthy.");
    if (buildNumber > 0) Common.WriteSuccess($"  === Build #{buildNumber} loaded ===");
}
else if (string.Equals(healthState, "Unhealthy", StringComparison.OrdinalIgnoreCase))
{
    Common.WriteError($"Workflow loaded but is Unhealthy. Schema validation failed:");
    if (!string.IsNullOrEmpty(healthError)) Common.WriteError("  " + healthError);
    if (buildNumber > 0) Common.WriteError($"  === Build #{buildNumber} REJECTED ===");
    Common.WriteWarn("Common Standard gotchas:");
    Common.WriteWarn("  - host.connection uses 'referenceName', NOT 'name' (Consumption uses 'name').");
    Common.WriteWarn("  - Switch case object keys must be valid identifiers (no hyphens).");
    Common.WriteWarn("    Use 'Case_escalation_denied' as the key; the 'case' value can be 'escalation-denied'.");
    Common.WriteWarn("  - $connections workflow parameter must be removed (it's Consumption-only).");
    return 1;
}
else
{
    Common.WriteWarn($"Could not determine workflow health after 3 minutes (state={healthState ?? "unknown"}).");
    Common.WriteWarn("Continuing anyway — the runtime may still be starting.");
}

// --- Phase 5: fetch trigger URL via hostruntime ---------------------------
// This succeeds independent of connection authorization — the URL is just a
// SAS-signed callback and is valid as soon as the workflow loads.
Common.WriteHeading("[5/7] Fetching trigger URL (hostruntime path)");
var callbackUri = $"https://management.azure.com/subscriptions/{subId}/resourceGroups/{rgName}" +
                  $"/providers/Microsoft.Web/sites/{workflowAppName}" +
                  $"/hostruntime/runtime/webhooks/workflow/api/management/workflows/Approval" +
                  $"/triggers/When_an_approval_request_is_received/listCallbackUrl" +
                  $"?api-version=2024-04-01";

string triggerUrl = null;
for (var attempt = 1; attempt <= 6; attempt++)
{
    var cb = Common.Capture("az", new[] { "rest", "--method", "post", "--uri", callbackUri });
    if (cb.ExitCode == 0 && !string.IsNullOrWhiteSpace(cb.Stdout))
    {
        try
        {
            using var doc = JsonDocument.Parse(cb.Stdout);
            triggerUrl = doc.RootElement.GetProperty("value").GetString();
            if (!string.IsNullOrWhiteSpace(triggerUrl)) break;
        }
        catch { /* try again */ }
    }
    if (attempt < 6) System.Threading.Thread.Sleep(5_000);
}

if (string.IsNullOrWhiteSpace(triggerUrl))
{
    Common.WriteError("Could not retrieve trigger URL. Re-run the script in a minute.");
    return 1;
}
Common.WriteSuccess("Trigger URL:");
Console.WriteLine(triggerUrl);

// --- Phase 6: connection authorization advisory (informational only) ------
Common.WriteHeading("[6/7] Office 365 connection authorization");
var connectionResourceId = $"/subscriptions/{subId}/resourceGroups/{rgName}/providers/Microsoft.Web/connections/{office365ConnectionName}";
var probe = Common.Capture("az", new[]
{
    "resource", "show",
    "--ids", connectionResourceId,
    "--api-version", "2016-06-01",
    "-o", "json",
});

var portalUrl = $"https://portal.azure.com/#@/resource/subscriptions/{subId}/resourceGroups/{rgName}/providers/Microsoft.Web/connections/{office365ConnectionName}/edit";
var isConnected = false;
if (probe.ExitCode == 0 && !string.IsNullOrWhiteSpace(probe.Stdout))
{
    try
    {
        using var doc = JsonDocument.Parse(probe.Stdout);
        if (doc.RootElement.GetProperty("properties").TryGetProperty("statuses", out var statuses) &&
            statuses.ValueKind == JsonValueKind.Array)
        {
            isConnected = statuses.EnumerateArray()
                .Any(s => s.TryGetProperty("status", out var st) &&
                          string.Equals(st.GetString(), "Connected", StringComparison.OrdinalIgnoreCase));
        }
    }
    catch { /* treat as unauthorized */ }
}

if (isConnected)
{
    Common.WriteSuccess("V2 connection is authorized (Connected). Invokes will reach Office 365.");
}
else
{
    Common.WriteWarn("V2 connection is NOT yet authorized — this is expected on first deploy.");
    Common.WriteWarn("OAuth consent is a one-time human gate; it cannot be automated.");
    Common.WriteWarn("");
    Common.WriteWarn("  → Below-threshold invokes (e.g. --amount 100) will succeed without auth.");
    Common.WriteWarn("  → Above-threshold invokes need this URL clicked once:");
    Common.WriteWarn($"     {portalUrl}");
    Common.WriteWarn("     Click Authorize → sign in → Save. Then no further redeploy is needed.");
}

Console.WriteLine();
Console.WriteLine("Invoke (sample):");
Console.WriteLine($"  dotnet script scripts/invoke-standard.csx -- --environment {environment} --amount 100");
Console.WriteLine($"  dotnet script scripts/invoke-standard.csx -- --environment {environment} --amount 2500");
Console.WriteLine($"  dotnet script scripts/invoke-standard.csx -- --environment {environment} --amount 15000");

// --- Phase 7: run history smoke check --------------------------------------
Common.WriteHeading("[7/7] Recent run history (last 5)");
var runsUri = $"https://management.azure.com/subscriptions/{subId}/resourceGroups/{rgName}" +
              $"/providers/Microsoft.Web/sites/{workflowAppName}" +
              $"/hostruntime/runtime/webhooks/workflow/api/management/workflows/Approval/runs" +
              $"?api-version=2024-04-01&%24top=5";

var runs = Common.Capture("az", new[] { "rest", "--method", "get", "--uri", runsUri });
if (runs.ExitCode != 0 || string.IsNullOrWhiteSpace(runs.Stdout))
{
    Common.WriteWarn("No run history yet (workflow not yet invoked).");
    return 0;
}

try
{
    using var doc = JsonDocument.Parse(runs.Stdout);
    if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.GetArrayLength() == 0)
    {
        Common.WriteWarn("No run history yet.");
        return 0;
    }
    foreach (var run in arr.EnumerateArray())
    {
        var name   = run.GetProperty("name").GetString();
        var status = run.GetProperty("properties").GetProperty("status").GetString();
        var start  = run.GetProperty("properties").TryGetProperty("startTime", out var s) ? s.GetString() : "";
        switch (status)
        {
            case "Succeeded": Common.WriteSuccess($"  {start}  {status,-10}  {name}"); break;
            case "Failed":    Common.WriteError(  $"  {start}  {status,-10}  {name}"); break;
            default:          Common.WriteWarn(   $"  {start}  {status,-10}  {name}"); break;
        }

        if (status != "Failed") continue;

        // For failed runs, list failed actions + error codes
        var actionsUri = $"https://management.azure.com/subscriptions/{subId}/resourceGroups/{rgName}" +
                         $"/providers/Microsoft.Web/sites/{workflowAppName}" +
                         $"/hostruntime/runtime/webhooks/workflow/api/management/workflows/Approval/runs/{name}/actions" +
                         $"?api-version=2024-04-01";
        var acts = Common.Capture("az", new[] { "rest", "--method", "get", "--uri", actionsUri });
        if (acts.ExitCode != 0 || string.IsNullOrWhiteSpace(acts.Stdout)) continue;
        try
        {
            using var actDoc = JsonDocument.Parse(acts.Stdout);
            if (!actDoc.RootElement.TryGetProperty("value", out var actArr)) continue;
            foreach (var a in actArr.EnumerateArray())
            {
                var aStatus = a.GetProperty("properties").GetProperty("status").GetString();
                if (aStatus != "Failed") continue;
                var aName = a.GetProperty("name").GetString();
                var code = a.GetProperty("properties").TryGetProperty("code", out var c) ? c.GetString() : "";
                Common.WriteError($"      ↳ failed action: {aName}  code={code}");
            }
        }
        catch { /* best-effort diagnostics */ }
    }
}
catch (Exception ex)
{
    Common.WriteWarn($"Could not parse run history: {ex.Message}");
}

return 0;

string GetSubscriptionId()
{
    var r = Common.Capture("az", new[] { "account", "show", "--query", "id", "-o", "tsv" });
    if (r.ExitCode != 0)
    {
        Common.WriteError("az account show failed. Run 'az login' first.");
        if (!string.IsNullOrWhiteSpace(r.Stderr)) Common.WriteError(r.Stderr);
        return null;
    }
    return r.Stdout.Trim();
}

int ReadBuildNumber(string path)
{
    try
    {
        if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var n))
            return n;
    }
    catch { /* fall through */ }
    return 0;
}

void StampBuildIntoWorkflow(string workflowPath, int buildNumber)
{
    // Inserts/updates "definition.description" with a build stamp. This field
    // is non-functional at runtime — purely a sentinel so the user can confirm
    // their edit reached the runtime (visible via the workflow GET endpoint).
    try
    {
        var raw = File.ReadAllText(workflowPath);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var stamp = $"build #{buildNumber} @ {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}";

        // Walk the tree manually so we preserve formatting on the rest of the file.
        // Simplest approach: serialize via JsonNode-equivalent (Utf8JsonWriter).
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("definition"))
                {
                    writer.WritePropertyName("definition");
                    writer.WriteStartObject();
                    var wroteDescription = false;
                    foreach (var dprop in prop.Value.EnumerateObject())
                    {
                        if (dprop.NameEquals("description"))
                        {
                            writer.WriteString("description", stamp);
                            wroteDescription = true;
                        }
                        else
                        {
                            dprop.WriteTo(writer);
                        }
                    }
                    if (!wroteDescription) writer.WriteString("description", stamp);
                    writer.WriteEndObject();
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }
        File.WriteAllBytes(workflowPath, ms.ToArray());
    }
    catch (Exception ex)
    {
        Common.WriteWarn($"Could not stamp build number into workflow.json: {ex.Message}");
    }
}

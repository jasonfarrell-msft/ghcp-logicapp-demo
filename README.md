# GitHub Copilot × Azure Logic Apps — Demo Set

A small, opinionated repo for **showcasing the value of GitHub Copilot when
authoring and maintaining Azure Logic Apps (Consumption)**. It contains a
working skeleton workflow, the Bicep that deploys it, and a set of
pre-scripted scenarios you can run live.

> **Why this exists.** Logic App definitions are JSON-heavy, full of subtle
> conventions (`runAfter`, `$connections`, scope semantics), and changes often
> need to be mirrored across IaC, the workflow definition, and parameter
> files. This is exactly the kind of cross-cutting, schema-bound work where
> Copilot shines.

## Running the demo

👉 **See [`DEMO.md`](./DEMO.md).** It contains the presenter script:
pre-flight checklist, scenario order with talking points, and an "if
something breaks" guide. This README only covers what the repo *is* and how
to set it up.

## Repo layout

```
infra/
├── main.bicep                       # subscription-scoped entry point
├── modules/logicApp.bicep           # Logic App + connectors + inline definition
├── parameters/{dev,prod}.bicepparam
└── workflows/approval.workflow.json # standalone copy of the definition
samples/approval-request.http        # ready-to-send sample payloads
scripts/deploy.csx                   # az deployment wrapper (cross-platform, dotnet-script)
scripts/invoke.csx                   # POST a sample to the trigger URL (auto-fetches the URL)
scripts/reset.csx                    # git restore + delete the resource group
scripts/lib/common.csx               # shared helpers (#load'd by the other scripts)
scenarios/                           # one markdown file per scenario
DEMO.md                              # presenter script
```

## The skeleton workflow

A single Logic App, **`la-approval-<env>`**, with an HTTP-triggered approval
flow:

1. HTTP trigger receives `{ requestId, requester, amount, description }`.
2. Initialize `approverEmail` and `threshold` (intentionally hard-coded).
3. If `amount > threshold` → send an Office 365 approval email.
4. Switch on the approver's selection → respond `approved` or `rejected`.
5. Otherwise → respond `auto-approved`.

The skeleton **deliberately** has gaps: no scopes, hard-coded values, no
retries, no escalation, no Teams notification, no docs. **Each scenario fixes
one of those gaps live.**

## Prerequisites

- Azure CLI ≥ 2.50 with the Bicep extension (`az bicep install`)
- An Azure subscription you can deploy to (`az login` first)
- **.NET SDK 8.0 (LTS)** — <https://dotnet.microsoft.com/download/dotnet/8.0>
- **`dotnet-script` global tool** — install once with `dotnet tool install -g dotnet-script`
  (and make sure `~/.dotnet/tools` is on your `PATH`)
- VS Code with the lab extensions listed below
- GitHub Copilot Chat / CLI

The helper scripts under `scripts/` are `.csx` files that run cross-platform
(Windows, macOS, Linux) via `dotnet script`. There is no PowerShell dependency.

## VS Code environment setup

Install the recommended extensions before running the labs. They cover the
Bicep files, Logic Apps Standard project, REST sample requests, and the
Mermaid diagram generated in Scenario 01.

```bash
code --install-extension GitHub.copilot
code --install-extension GitHub.copilot-chat
code --install-extension ms-azuretools.vscode-bicep
code --install-extension bierner.markdown-mermaid
```

You can confirm the environment with (works on bash, zsh, and PowerShell):

```bash
code --list-extensions | grep -E 'GitHub.copilot|GitHub.copilot-chat|ms-azuretools.vscode-bicep|ms-azuretools.vscode-azurelogicapps|humao.rest-client|bierner.markdown-mermaid'
```

When VS Code opens this workspace, it also prompts for these extensions from
`.vscode/extensions.json` if any are missing.

## One-time setup

```bash
# Install the script runner once (skip if already installed)
dotnet tool install -g dotnet-script

# Validate Bicep
az bicep build --file infra/main.bicep

# Deploy dev baseline
dotnet script scripts/deploy.csx -- --environment dev

# Authorize the deployed API connections in the portal before invoking:
# Logic App -> API connections -> con-office365-dev -> Edit API connection -> Authorize

# Smoke test after connection authorization:
dotnet script scripts/invoke.csx -- --environment dev --amount 100
```

✅ Expect `HTTP 200 OK` with `"status":"auto-approved"`.

To exercise the full approval path (`--amount 2500` and above), use the Office
365 connection authorized during setup. The invoke call returns `HTTP 202
Accepted` immediately while the approval continues asynchronously. The
authorization survives redeploys.

## Invoke

```bash
dotnet script scripts/invoke.csx -- --environment dev --amount 2500
```

The script fetches the trigger URL from Azure for you — no copy/paste.

If you do pass a URL explicitly, **wrap it in single quotes** — `&` is a
command separator in PowerShell and most POSIX shells, and an unquoted URL
gets silently truncated, causing 401s:

```bash
dotnet script scripts/invoke.csx -- --trigger-url 'https://...&sig=...'
```

…or use `samples/approval-request.http` in VS Code.

## Reset

⚠️ `dotnet script scripts/reset.csx -- --environment dev` runs `git restore .`
**and deletes the resource group**. It does **not** redeploy. Use it at the
end of the demo, not between scenarios. To start fresh after a reset, redeploy:

```bash
dotnet script scripts/deploy.csx -- --environment dev
```

## Scenarios at a glance

| # | Scenario | What it shows |
|---|---|---|
| 01 | Explain & document | Mermaid + plain-English from JSON |
| 02 | Refactor: extract sub-flows | Copilot reasons over `Scope`/`runAfter` |
| 03 | Add error handling | `retryPolicy`, failure scopes, dead-letter |
| 04 | Add Teams notification | Wiring a new connector end-to-end |
| 05 | Add an escalation branch | Cross-file IaC consistency |
| 06 | Migrate Consumption → Standard | Cross-cutting refactor across IaC, schema, connections, local dev |

Scenarios are numbered in presentation order. See [`DEMO.md`](./DEMO.md) for
the full narrative arc, talking points, and per-beat narration.

## Standard (Logic Apps Standard) — side-by-side

Scenario 06 introduces a **side-by-side Logic Apps Standard** project. The
original Consumption deployment under `infra/` stays in place so both
runtimes can be compared live.

```
standard/
├── host.json
├── connections.json
├── parameters.json
├── local.settings.json   # local-only — replace placeholders before `func start`
└── Approval/
    └── workflow.json
infra-standard/
├── main.bicep
├── modules/logicAppStandard.bicep
└── parameters/{dev,prod}.bicepparam
scripts/deploy-standard.csx
```

### Prerequisites (Standard only)

- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azure Logic Apps (Standard) VS Code extension](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-azurelogicapps)
- (Optional, for full local dev) Azurite for local storage emulation

### Deploy

```bash
az bicep build --file infra-standard/main.bicep
dotnet script scripts/deploy-standard.csx -- --environment dev
```

The script provisions the Storage Account, WS1 plan, and `workflowapp` site,
then runs `func azure functionapp publish` to deploy the contents of
`standard/` to the new site. Pass `--skip-content` to provision infra only.

### Run locally

```bash
cd standard
func start
```

POST the same approval payload to the local trigger URL printed by `func`.

See [`docs/migration-consumption-to-standard.md`](./docs/migration-consumption-to-standard.md)
for the full migration plan.

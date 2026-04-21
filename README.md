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
scripts/deploy.ps1                   # az deployment wrapper
scripts/invoke.ps1                   # POST a sample to the trigger URL (auto-fetches the URL)
scripts/reset.ps1                    # git restore + delete the resource group
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
- VS Code with the **REST Client** extension (optional, for `samples/*.http`)
- GitHub Copilot Chat / CLI

## One-time setup

```powershell
# Validate Bicep
az bicep build --file infra/main.bicep

# Deploy dev baseline
./scripts/deploy.ps1 -Environment dev

# Smoke test (skips the Office 365 connector — no auth required yet):
./scripts/invoke.ps1 -Environment dev -Amount 100
```

✅ Expect `HTTP 200 OK` with `"status":"auto-approved"`.

To exercise the full approval path (`-Amount 2500` and above), authorize the
Office 365 connection **once** in the portal: Logic App → API connections →
`con-office365-dev` → **Edit API connection** → **Authorize**. The
authorization survives redeploys.

## Invoke

```powershell
./scripts/invoke.ps1 -Environment dev -Amount 2500
```

The script fetches the trigger URL from Azure for you — no copy/paste.

If you do pass a URL explicitly, **wrap it in single quotes** — `&` is a
command separator in PowerShell and an unquoted URL gets silently truncated,
causing 401s:

```powershell
./scripts/invoke.ps1 -TriggerUrl 'https://...&sig=...'
```

…or use `samples/approval-request.http` in VS Code.

## Reset

⚠️ `./scripts/reset.ps1 -Environment dev` runs `git restore .` **and deletes
the resource group**. It does **not** redeploy. Use it at the end of the
demo, not between scenarios. To start fresh after a reset, redeploy:

```powershell
./scripts/deploy.ps1 -Environment dev
```

## Scenarios at a glance

| # | Scenario | What it shows |
|---|---|---|
| 01 | Refactor: extract sub-flows | Copilot reasons over `Scope`/`runAfter` |
| 02 | Parameterize hard-coded values | Workflow `parameters` vs `variables` |
| 03 | Add error handling | `retryPolicy`, failure scopes, dead-letter |
| 04 | Add an escalation branch | Cross-file IaC consistency |
| 05 | Explain & document | Mermaid + plain-English from JSON |
| 06 | Add Teams notification | Wiring a new connector end-to-end |
| 07 | Migrate Consumption → Standard | Cross-cutting refactor across IaC, schema, connections, local dev |

The presenter order in [`DEMO.md`](./DEMO.md) is **not** `01 → 07` — it
opens with `05` (explain) and ends with `07` (migrate). See that file for the
narrative arc.

## Standard (Logic Apps Standard) — side-by-side

Scenario 07 introduces a **side-by-side Logic Apps Standard** project. The
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
scripts/deploy-standard.ps1
```

### Prerequisites (Standard only)

- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azure Logic Apps (Standard) VS Code extension](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-azurelogicapps)
- (Optional, for full local dev) Azurite for local storage emulation

### Deploy

```powershell
az bicep build --file infra-standard/main.bicep
./scripts/deploy-standard.ps1 -Environment dev
```

The script provisions the Storage Account, WS1 plan, and `workflowapp` site,
then runs `func azure functionapp publish` to deploy the contents of
`standard/` to the new site. Pass `-SkipContent` to provision infra only.

### Run locally

```powershell
cd standard
func start
```

POST the same approval payload to the local trigger URL printed by `func`.

See [`docs/migration-consumption-to-standard.md`](./docs/migration-consumption-to-standard.md)
for the full migration plan.

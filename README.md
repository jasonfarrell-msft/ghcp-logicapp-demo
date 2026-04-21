# GitHub Copilot × Azure Logic Apps — Demo Set

A small, opinionated repo for **showcasing the value of GitHub Copilot when
authoring and maintaining Azure Logic Apps (Consumption)**. It contains a
working skeleton workflow plus a set of pre-scripted "scenarios" you can run
live in front of an audience.

> **Why this exists.** Logic App definitions are JSON-heavy, full of subtle
> conventions (`runAfter`, `$connections`, scope semantics), and changes often
> need to be mirrored across IaC, the workflow definition, and parameter
> files. This is exactly the kind of cross-cutting, schema-bound work where
> Copilot shines.

## Repo layout

```
infra/
├── main.bicep                   # subscription-scoped entry point
├── modules/logicApp.bicep       # Logic App + connectors + inline definition
├── parameters/{dev,prod}.bicepparam
└── workflows/approval.workflow.json   # standalone copy of the definition
samples/approval-request.http    # ready-to-send sample payloads
scripts/deploy.ps1               # az deployment wrapper
scripts/invoke.ps1               # POST a sample to the trigger URL (auto-fetches the URL)
scripts/reset.ps1                # git restore + redeploy baseline (between scenarios)
scenarios/                       # demo scripts (1 per markdown file)
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

## Deploy

```powershell
# Validate
az bicep build --file infra/main.bicep

# Deploy dev
./scripts/deploy.ps1 -Environment dev
```

After deployment, open the Logic App in the Azure Portal and **authorize the
Office 365 / Teams connections** (the demo deploys them un-authorized to keep
deployments fast and credential-free).

> **Smoke test without authorizing the connector:** invoke with an amount
> below the threshold — it skips the approval-email branch entirely and
> returns `auto-approved`. Great first sanity check after deploy:
> ```powershell
> ./scripts/invoke.ps1 -Environment dev -Amount 100
> ```
> Above-threshold invokes will return **502** until the Office 365 connection
> is authorized in the portal.

## Invoke

Just run:

```powershell
./scripts/invoke.ps1 -Environment dev -Amount 2500
```

The script fetches the trigger URL from Azure for you — **no copy/paste**.

If you do want to pass a URL explicitly (e.g., from another subscription),
**always wrap it in single quotes** — `&` is a command separator in
PowerShell and an unquoted URL gets silently truncated, causing 401s:

```powershell
./scripts/invoke.ps1 -TriggerUrl 'https://...&sig=...'
```

…or use `samples/approval-request.http` in VS Code.

## Demo scenarios

Each file under `scenarios/` is a self-contained ~5-minute demo. Recommended
order:

| # | Scenario | What it shows |
|---|---|---|
| 01 | Refactor: extract sub-flows **(primary)** | Copilot reasons over `Scope`/`runAfter` |
| 02 | Parameterize hard-coded values | Workflow `parameters` vs `variables` |
| 03 | Add error handling | `retryPolicy`, failure scopes, dead-letter |
| 04 | Add an escalation branch | Cross-file IaC consistency |
| 05 | Explain & document | Mermaid + plain-English from JSON |
| 06 | Add Teams notification | Wiring a new connector end-to-end |
| 07 | Migrate Consumption → Standard | Cross-cutting refactor: IaC, workflow schema, connections, local dev |

## The demo loop (every scenario)

Each scenario follows the same **deploy → refactor → redeploy → verify**
loop. This is the core narrative: Copilot doesn't just edit text, it
changes what's running in Azure.

```
1. Deploy baseline       ./scripts/deploy.ps1 -Environment dev
2. Show current behavior ./scripts/invoke.ps1 -Environment dev
3. Refactor with Copilot (paste the scenario's prompt)
4. Validate locally       az bicep build --file infra/main.bicep
5. Redeploy (in-place)    ./scripts/deploy.ps1 -Environment dev
6. Re-invoke              ./scripts/invoke.ps1 -Environment dev
7. Reset for next demo    ./scripts/reset.ps1 -Environment dev
```

Notes:
- **Bicep is idempotent** — redeploying to the same RG updates the workflow
  in place. The trigger URL stays the same across redeploys.
- **`scripts/reset.ps1`** restores the working tree to baseline and
  redeploys, so each scenario starts clean.
- **Connector authorization** is a one-time per-environment step done in the
  portal — it survives redeploys.

## Demo tips

- **Open both files** before each scenario: `infra/workflows/approval.workflow.json`
  and `infra/modules/logicApp.bicep`. The "keep them in sync" angle is one of
  Copilot's strongest stories here.
- **Run `az bicep build` after every scenario** as the validation gate — it's
  a clean way to show the change is real and well-formed.
- Reset between demos with `./scripts/reset.ps1`.

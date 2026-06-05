# Scenario 05 — Migrate Consumption → Standard

**Goal:** Side-by-side migration to a Logic Apps **Standard** project, deployed to Azure. The flagship cross-cutting demo: different Azure resources, different on-disk layout, different connection model — all preserving the **escalation-branch behavior** from Scenario 04 byte-for-byte. **Azure-only — no local runtime.**

The Consumption Logic App stays deployed so the audience can compare the two live in the portal.

## Why this is a great Copilot demo

A Consumption → Standard migration is **not** a 1:1 copy. It requires:

- Different Azure resources (`Microsoft.Web/sites` kind `functionapp,workflowapp` + WS1 plan + Storage, instead of `Microsoft.Logic/workflows`).
- A different on-disk layout (`workflow.json` per workflow + `host.json` + `connections.json` + `parameters.json`).
- A different connection model: `$connections` in the workflow definition is replaced by a top-level `connections.json`, and the Office 365 connection itself must be a fresh **V2** connection authorized for the workflow app's **Managed Identity** via `accessPolicies`.

By hand from docs this takes hours. With Copilot and the existing Consumption workflow as input, minutes — and that's the point of this beat.

## Model guidance (largest scenario — Opus only)

- Use **VS Code Agent mode** with **Claude Opus 4.6 or higher**. Sonnet is *not* recommended here: the migration touches Bicep + JSON + connection schema simultaneously and Sonnet routinely loses track of `accessPolicies`, V2 vs V1 distinctions, and `connections.json` app-setting bindings across the 6+ files involved.
- Run this scenario in **two prompts only** — generate a plan, review it, then hand the plan back as the implementation brief.

## Prerequisites

| Tool | Why |
|---|---|
| `az` (Azure CLI) ≥ 2.60 with Bicep | Already required by Scenario 01. |
| `dotnet-script` | Already required by Scenario 01. |
| Zip utility on `PATH` | `zip` (macOS/Linux) or `Compress-Archive` / `tar -a -c -f` (Windows). The deploy script bundles `standard/` into a zip. |

You **do not** need Azure Functions Core Tools (`func`).

## Demo setup

Keep `infra/workflows/approval.workflow.json` and `infra/modules/logicApp.bicep` open in editor tabs the entire scenario. They anchor Copilot to the existing Consumption shape — including the escalation branch from Scenario 04 — which the Standard version must preserve.

## Step 1 — Generate the migration plan

> Read `infra/workflows/approval.workflow.json`, `infra/modules/logicApp.bicep`, `infra/main.bicep`, and both files under `infra/parameters/`. Produce a migration plan to move this Consumption Logic App to a Logic Apps **Standard** (single-tenant) project, preserving the approval workflow's behavior **exactly**, including the escalation-branch logic, the `skipApproval` gate, the four `responseStatus` values (`auto-approved`, `approved`, `rejected`, `escalation-denied`), retry policies, and the `HandleFailure` scope. Deployment target is **Azure only** — no local runtime, no `func start`.
>
> Save the plan to `docs/migration-consumption-to-standard.md` with these sections, in this order:
>
> 1. **Resource diff** — table mapping each Consumption resource to its Standard equivalent: `Microsoft.Logic/workflows` → `Microsoft.Web/sites` (`kind: 'functionapp,workflowapp'`), plus the **new** required resources (Storage Account, App Service Plan SKU `WS1` / `kind: elastic`, V2 Office 365 connection, `accessPolicies` child). The Storage Account **must** set `allowSharedKeyAccess: true` and `publicNetworkAccess: 'Enabled'` (App Service creates the Azure Files content share via shared key). **Every taggable resource** (resource group, storage, plan, site, V2 connection) **must** carry the tag `SecurityControl: Ignore` — this tenant's Azure Policy otherwise auto-remediates the storage account back to `allowSharedKeyAccess: false`, causing content-share creation to fail with `403 Forbidden` (`ExtendedCode 99022`). The tag key is `SecurityControl` and the value is `Ignore` (this is tenant-specific; optional for non-Microsoft environments).
> 2. **On-disk layout** — exact file tree for `standard/` (`host.json`, `connections.json`, `parameters.json`, `Approval/workflow.json`) and `infra-standard/` (`main.bicep`, `modules/logicAppStandard.bicep`, `parameters/dev.bicepparam`, `parameters/prod.bicepparam`).
> 3. **Connection model change** — explain how `$connections` in Consumption becomes `connections.json` at the project root, with `managedApiConnections` referencing `connectionRuntimeUrl` and an `authentication` block of type `ManagedServiceIdentity`. Connection name and runtime URL come from app settings (`office365-ConnectionName`, `office365-ConnectionRuntimeUrl`). **Critical: the workflow's `host.connection` uses `referenceName` (not `name` like Consumption)**; using `name` produces an opaque runtime error `'Value cannot be null. (Parameter 'key')'` and the workflow loads as Unhealthy.
> 4. **Workflow translation** — show the before/after of the workflow `parameters` block (Consumption uses `$connections`; Standard drops it). Confirm the trigger schema is preserved byte-for-byte and that **all escalation logic** (`escalationApproverEmail`, `escalationThreshold`, escalation branch, `skipApproval` gating, four-state response) is carried over. Resolve workflow inputs via `InitializeVariable` actions at the start of the workflow rather than workflow parameters. **Standard runtime gotchas the plan must call out explicitly:** (a) `host.connection` uses `referenceName` not `name`; (b) `Switch` case **object keys** must be valid identifiers — no hyphens (use `Case_escalation_denied` as the key, with `"case": "escalation-denied"` as the value); (c) the `$connections` parameter must be removed.
> 5. **What does NOT carry over** — call out explicitly: run history is not migrated, the trigger URL changes, and the Office 365 connection is **replaced**, not reused. Standard requires a *new* **V2** API connection (`Microsoft.Web/connections@2016-06-01` with `kind: 'V2'`) authorized for the workflow app's MSI via `accessPolicies`. V1 connections reject `accessPolicies` (`InvalidApiConnectionAccessPolicy`); V1 without an access policy fails at runtime with `403 missing connection ACL`. Use `con-office365-std-${environmentName}` so the Consumption V1 connection stays untouched. Plan for a **one-time interactive OAuth consent** in the portal — the consent itself cannot be automated.
> 6. **Required Bicep app settings** — list every app setting the workflow app site must have (`AzureWebJobsStorage`, `FUNCTIONS_EXTENSION_VERSION=~4`, `FUNCTIONS_WORKER_RUNTIME=node`, `WEBSITE_NODE_DEFAULT_VERSION=~20`, `APP_KIND=workflowApp`, `AzureFunctionsJobHost__extensionBundle__id`, `AzureFunctionsJobHost__extensionBundle__version=[1.*, 2.0.0)`, `AzureWebJobsSecretStorageType=Files`, `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING`, `WEBSITE_CONTENTSHARE`, `WORKFLOWS_SUBSCRIPTION_ID`, `WORKFLOWS_RESOURCE_GROUP_NAME`, `WORKFLOWS_LOCATION_NAME`, `office365-ConnectionName`, `office365-ConnectionRuntimeUrl`).
> 7. **Deploy + invoke scripts** — describe `scripts/deploy-standard.csx` (bicep build → deploy → zip-deploy via `az webapp deploy --type zip` → **workflow runtime health probe (Unhealthy = exit non-zero)** → `listCallbackUrl` via the `hostruntime` path → connection authorization advisory **(informational only — never blocks deploy)**) and `scripts/invoke-standard.csx` (mirrors `invoke.csx` against the Standard `hostruntime` URL). The deploy script must complete end-to-end on first run *even when the V2 connection is unauthorized* — OAuth consent is a one-time human gate that cannot be automated, and below-threshold invokes work without it. `infra-standard/main.bicep` is **subscription-scoped** (`targetScope = 'subscription'`, it creates the resource group), so the deploy script **must** use **`az deployment sub create --location ${location}`** — not `az deployment group create` — and read the RG name back from the `resourceGroupName` deployment output.
>
> Use the **simplest valid approach, no alternatives**.

> **Review checkpoint:** open `docs/migration-consumption-to-standard.md` and skim it. If a section is wrong or missing, fix the plan before Step 2 — it's cheaper to fix the plan than the code.
>
> **Note:** This file is throwaway. It is git-ignored and gets regenerated on every run.

## Step 2 — Implement the plan

> Implement the migration plan in `docs/migration-consumption-to-standard.md` exactly as written. Create every file the plan specifies under `standard/` and `infra-standard/`, plus `scripts/deploy-standard.csx` and `scripts/invoke-standard.csx`. Reuse helpers from `scripts/lib/common.csx` (`ParseArgs`, `Get`, `RepoRoot`, `Run`, `Capture`, `WriteHeading`, `WriteWarn`, `WriteError`, `WriteSuccess`).
>
> Hard requirements (do not skip any):
> - The **escalation logic** from `infra/workflows/approval.workflow.json` must appear in `standard/Approval/workflow.json` with identical action names, identical `runAfter` shapes, and identical `responseStatus` values.
> - **Workflow JSON — Standard schema rules (these break silently if missed):**
>   - **`host.connection` uses `referenceName`, not `name`.** Consumption: `"connection": { "name": "@parameters('$connections')['office365']['connectionId']" }`. Standard: `"connection": { "referenceName": "office365" }`. Wrong key produces `'Value cannot be null. (Parameter 'key')'` at runtime load.
>   - **`Switch` case object keys must be valid identifiers** — no hyphens. Use `Case_escalation_denied` as the object key with `"case": "escalation-denied"` inside it. The `case` *value* (matched against the expression) is unrestricted.
>   - **`$connections` parameter must be removed** from the workflow's `parameters` block. Standard's resolver fails on its presence.
> - The Office 365 connection is `Microsoft.Web/connections@2016-06-01` with `kind: 'V2'`, named `con-office365-std-${environmentName}`. Add a `Microsoft.Web/connections/accessPolicies@2016-06-01` child with `principal.identity.tenantId = workflowApp.identity.tenantId` and `principal.identity.objectId = workflowApp.identity.principalId`.
> - The site has `identity: { type: 'SystemAssigned' }` and `dependsOn: [ fileService ]`.
> - The Storage Account sets `allowSharedKeyAccess: true` and `publicNetworkAccess: 'Enabled'`, and **every taggable resource** (resource group, storage, plan, site, V2 connection) carries the tag `SecurityControl: Ignore`. Without it, this tenant's Azure Policy re-disables shared-key access and content-share creation fails with `403 Forbidden` (`ExtendedCode 99022`).
> - `infra-standard/main.bicep` is `targetScope = 'subscription'` and creates the resource group; `deploy-standard.csx` deploys with **`az deployment sub create --location ${location}`** (not `az deployment group create`) and reads the RG name from the `resourceGroupName` output.
> - `connections.json` uses **`@appsetting('...')`** references (not literal IDs) so the file is environment-portable.
> - `deploy-standard.csx` must (a) zip-deploy the `standard/` directory via `az webapp deploy --type zip`, (b) **probe the workflow runtime health endpoint** (`Microsoft.Web/sites/.../hostruntime/runtime/webhooks/workflow/api/management/workflows/Approval`) and **exit non-zero on `health.state = 'Unhealthy'`** (this catches `referenceName`/case-key/`$connections` mistakes before handing the user a trigger URL), (c) probe the V2 connection's `properties.statuses[].status` for `Connected` **as informational advisory only** — it must **not** exit non-zero when unauthorized, since OAuth consent is a one-time human gate, (d) print the **exact portal URL** `https://portal.azure.com/#@/resource/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/connections/con-office365-std-{env}/edit` when unauthorized, and (e) call `listCallbackUrl` via the **`Microsoft.Web/sites/.../hostruntime/runtime/webhooks/workflow/api/management/...`** path — *not* the Consumption `Microsoft.Logic/workflows` path.
> - Original Consumption assets (`infra/`, `scripts/deploy.csx`, `scripts/invoke.csx`) are **untouched** — this is additive, side-by-side.
>
> Use the simplest valid approach, no alternatives. Single consolidated diff across all new files.

## Step 3 — Deploy and authorize

```bash
az bicep build --file infra-standard/main.bicep
dotnet script scripts/deploy-standard.csx -- --environment dev
```

The deploy script ends by either:
- ✅ Detecting the V2 connection is `Connected` and printing the trigger URL, OR
- ⚠️ Detecting it isn't, and printing a portal URL.

**If the script printed a portal URL:** click it, click **Authorize**, sign in as the approver mailbox, click **Save**, then re-run the deploy script. It will detect the change, restart the app, and print the trigger URL.

## Step 4 — Verify (all four escalation outcomes)

```bash
# Auto-approve
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 250

# Standard approval path
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 2500

# Escalation path
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 15000
```

✅ **Success indicators:**
- `250` → `"status": "auto-approved"`
- `2500` → email to standard approver → `"status": "approved"` or `"rejected"`
- `15000` → email to escalation approver → either `"status": "escalation-denied"` or, if approved, falls through to the standard approver
- All four `responseStatus` values from Scenario 04 are reachable on the Standard app

## What changes
- New `standard/` folder (host.json, connections.json, parameters.json, Approval/workflow.json).
- New `infra-standard/` Bicep (Storage + WS1 plan + workflowapp site + V2 Office 365 connection + access policy).
- New `scripts/deploy-standard.csx` and `scripts/invoke-standard.csx`.
- Original Consumption assets unchanged.
- Throwaway `docs/migration-consumption-to-standard.md` (git-ignored).

## If something goes wrong

- **`The target scope "subscription" does not match the deployment scope "resourceGroup"`** — `deploy-standard.csx` is using `az deployment group create`. `infra-standard/main.bicep` is subscription-scoped (it creates the RG); deploy with `az deployment sub create --location {location}` and read the RG from the `resourceGroupName` output.
- **`Creation of storage file share failed with ... (403) Forbidden` (`ExtendedCode 99022`)** — Azure Policy re-disabled `allowSharedKeyAccess` on the storage account. Ensure the storage account has `allowSharedKeyAccess: true`, `publicNetworkAccess: 'Enabled'`, **and** the tag `SecurityControl: Ignore` so policy does not remediate it. Tag all resources, not just storage.
- **`InternalSubscriptionIsOverQuotaForSku`** — subscription has zero `WorkflowStandard` (WS1) quota. Request via Portal → Subscriptions → Usage + Quotas, or use a different subscription. Until quota is available, treat this scenario as a code walk-through.
- **`InvalidApiConnectionAccessPolicy`** — Copilot created the connection without `kind: 'V2'`. Tell it: "The Office 365 connection must be `kind: 'V2'`. V1 rejects access policies."
- **`403 missing connection ACL` on first invoke** — the OAuth consent step in the portal hasn't been completed. Re-open the portal URL printed by the deploy script, Authorize, Save, then re-invoke.
- **`listCallbackUrl` returns 404** — `deploy-standard.csx` is using the Consumption URL shape. The Standard URL is under `Microsoft.Web/sites/{site}/hostruntime/runtime/webhooks/workflow/api/management/workflows/Approval/triggers/...`.

## Talking points

- **Plan-first with Opus.** Generating the plan, reviewing it, then handing it back as the implementation brief is dramatically more reliable than five sequential prompts. Opus holds the schema in mind across Bicep + workflow JSON + `connections.json` + scripts in a single execution.
- **The connection model is the trap.** V2 + MSI + `accessPolicies` is the only path that works for Standard with managed identity. V1 silently fails at runtime.
- **Side-by-side, not destructive.** Both Logic Apps stay deployed and visible in the portal. The audience can click through both at the end.
- **Hours by hand, minutes with Copilot.** This is the change that justifies the workshop title.

---
**Redeploy:** `dotnet script scripts/deploy-standard.csx -- --environment dev` (then re-run Verify).

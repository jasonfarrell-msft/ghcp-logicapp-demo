# Scenario 06 — Migrate Consumption → Standard

**Goal:** Side-by-side migration to a Logic Apps **Standard** project. IaC shape, on-disk layout, connection model, and local dev all change at once. The flagship cross-cutting demo.

## Why this is a great Copilot demo

A Consumption → Standard migration is **not** a 1:1 copy. It requires:

- Different Azure resources (`Microsoft.Web/sites` kind `workflowapp` + WS1 plan + Storage, instead of `Microsoft.Logic/workflows`).
- A different on-disk layout (`workflow.json` per workflow + `host.json` + `connections.json` + `parameters.json`).
- Managed API connections become **API connections referenced via `connections.json`** with `connectionRuntimeUrl` + auth, or are replaced by **built-in service-provider connectors** (preferred where available).
- Workflow `parameters` (`$connections`) shape changes — built-in connectors don't use `$connections` at all.
- Local dev becomes possible via the **Azure Logic Apps (Standard) VS Code extension** + Azure Functions Core Tools.

By hand from docs this takes hours. With Copilot and the existing Consumption workflow as input, minutes.

This scenario is **additive** — the original Consumption Logic App stays deployed so the audience can compare the two live.

## Prompts (run in order)

### Step 1 — Plan the migration

> Read `infra/workflows/approval.workflow.json` and
> `infra/modules/logicApp.bicep`. Produce a migration plan to move this
> Consumption Logic App to a Logic Apps **Standard** (single-tenant) project,
> preserving the approval workflow's behavior. Cover: (a) new Azure resources
> required, (b) on-disk file layout for the Standard project, (c) how the
> Office 365 approval-email action should be handled (managed API connection
> vs built-in connector), (d) how `$connections` and workflow `parameters`
> change, (e) what local dev looks like. Save the plan to
> `docs/migration-consumption-to-standard.md`.

### Step 2 — Scaffold the Standard project

> Create a new top-level folder `standard/` that holds a Logic Apps Standard
> project:
> - `standard/host.json`
> - `standard/connections.json` (referencing an Office 365 managed API
>   connection placeholder)
> - `standard/parameters.json`
> - `standard/local.settings.json` (with `AzureWebJobsStorage` and
>   `WORKFLOWS_SUBSCRIPTION_ID` placeholders, marked
>   `"IsEncrypted": false`)
> - `standard/Approval/workflow.json` — the approval workflow translated to
>   the Standard schema (stateful), reusing the trigger / actions /
>   conditions from `infra/workflows/approval.workflow.json`. Keep the
>   approval email step using the Office 365 managed API connection (do not
>   switch connectors yet — that's a follow-up).

### Step 3 — Author Standard infra

> Add `infra-standard/main.bicep` and `infra-standard/modules/logicAppStandard.bicep`
> that deploy:
> - A Storage Account (`Standard_LRS`)
> - An App Service Plan (`WS1`, `kind: elastic`)
> - A `Microsoft.Web/sites` resource with `kind: 'functionapp,workflowapp'`
>   and the required app settings (`AzureWebJobsStorage`,
>   `FUNCTIONS_EXTENSION_VERSION=~4`,
>   `FUNCTIONS_WORKER_RUNTIME=node` (or `dotnet` — pick one and explain),
>   `APP_KIND=workflowApp`)
> - Reuse the existing Office 365 `Microsoft.Web/connections` resource
>   pattern from `infra/modules/logicApp.bicep`.
> Add `infra-standard/parameters/{dev,prod}.bicepparam` mirroring the
> Consumption ones.

### Step 4 — (Optional) Replace managed connector with built-in

> In `standard/Approval/workflow.json`, replace the Office 365 managed API
> approval-email action with the **built-in Office 365** service-provider
> connector if available, otherwise leave a TODO comment explaining the
> trade-off (cost / cold start / VNet support). Update `connections.json`
> accordingly.

### Step 5 — Update tooling

> Add a new deploy script `scripts/deploy-standard.csx` modeled on
> `scripts/deploy.csx`. Update the root `README.md` with a new "Standard"
> section: prerequisites (Azure Functions Core Tools v4, VS Code Logic Apps
> Standard extension), how to run locally (`func start` from `standard/`),
> and how to deploy.

## What changes
- New `docs/migration-consumption-to-standard.md` plan.
- New `standard/` folder with a runnable Standard project layout.
- New `infra-standard/` Bicep deploying the App Service Plan + workflowapp site + storage.
- New `scripts/deploy-standard.csx`.
- Original Consumption assets unchanged — side-by-side, not destructive.

## Verify

```bash
az bicep build --file infra-standard/main.bicep
dotnet script scripts/deploy-standard.csx -- --environment dev
# Optional local dev (requires Azure Functions Core Tools v4):
cd standard; func start
```

POST the same payload to the Standard workflow's trigger URL — same behaviour, different runtime. Open both Logic Apps in the portal to compare run history, pricing, and scaling.

## Talking points
- **Schema differences are subtle** but Copilot navigates them with both definitions in context.
- **`connections.json` is the new `$connections`** — Copilot translates the binding cleanly.
- **Side-by-side, not destructive.** Original `infra/` stays untouched — diff Consumption vs Standard live.
- **Local dev unlocks tests.** `func start` + POST to the local trigger.
- **Cost & operational story.** Standard = predictable pricing, VNet, stateful + stateless mix, private endpoints.

## Stretch
- Have Copilot generate a **GitHub Actions** workflow to deploy the Standard project on push (`azure/login` + `azure/functions-action` with `package: standard/`).
- Ask Copilot to add a **second workflow** (e.g., a stateless reminder) into the same Standard project — multi-workflow is a key Standard advantage.

---
**Redeploy:** `dotnet script scripts/deploy-standard.csx -- --environment dev` (Consumption stays deployed).

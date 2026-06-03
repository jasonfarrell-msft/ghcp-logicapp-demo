# Scenario 06 — Migrate Consumption → Standard

**Goal:** Side-by-side migration to a Logic Apps **Standard** project. IaC shape, on-disk layout, connection model, and local dev all change at once. The flagship cross-cutting demo.

## Model guidance (largest scenario)

- Use **VS Code Agent mode**.
- Start with **Claude Sonnet 4.5** and keep prompts tightly scoped by step with exact file outputs.
- Add "simplest valid approach, no alternatives" to reduce unnecessary reasoning/output.

## Why this is a great Copilot demo

A Consumption → Standard migration is **not** a 1:1 copy. It requires:

- Different Azure resources (`Microsoft.Web/sites` kind `workflowapp` + WS1 plan + Storage, instead of `Microsoft.Logic/workflows`).
- A different on-disk layout (`workflow.json` per workflow + `host.json` + `connections.json` + `parameters.json`).
- Managed API connections become **API connections referenced via `connections.json`** with `connectionRuntimeUrl` + auth, or are replaced by **built-in service-provider connectors** (preferred where available).
- Workflow `parameters` (`$connections`) shape changes — built-in connectors don't use `$connections` at all.

By hand from docs this takes hours. With Copilot and the existing Consumption workflow as input, minutes.

This scenario is **additive** — the original Consumption Logic App stays deployed so the audience can compare the two live.

## Prompts (run in order)

> **Demo tip:** Keep `infra/workflows/approval.workflow.json` and
> `infra/modules/logicApp.bicep` open in editor tabs the entire scenario.
> Every prompt below references them, and visible tabs strongly anchor
> Copilot to the existing Consumption shape.

### Step 1 — Plan the migration

> Read `infra/workflows/approval.workflow.json` and
> `infra/modules/logicApp.bicep`. Produce a migration plan to move this
> Consumption Logic App to a Logic Apps **Standard** (single-tenant)
> project, preserving the approval workflow's behavior **exactly** (same
> trigger schema, same approver email, same threshold, same auto-approve
> branch, same Office 365 approval-email step, same 200 response shape).
>
> Save the plan to `docs/migration-consumption-to-standard.md` with these
> sections, in this order:
> 1. **Resource diff** — table mapping each Consumption resource to its
>    Standard equivalent: `Microsoft.Logic/workflows` →
>    `Microsoft.Web/sites` (`kind: 'functionapp,workflowapp'`), plus the
>    **new** required resources (Storage Account, App Service Plan SKU
>    `WS1` / `kind: elastic`). Keep the Office 365
>    `Microsoft.Web/connections` resource as-is.
> 2. **On-disk layout** — exact file tree for `standard/` (host.json,
>    connections.json, parameters.json,
>    `Approval/workflow.json`) and for `infra-standard/` (main.bicep,
>    modules/logicAppStandard.bicep, parameters/dev.bicepparam,
>    parameters/prod.bicepparam).
> 3. **Connection model change** — explain how `$connections` in
>    Consumption becomes a `connections.json` file at the project root in
>    Standard, with `managedApiConnections` referencing
>    `connectionRuntimeUrl` and an `authentication` block using
>    `@appsetting('...')`.
> 4. **Workflow parameters change** — show the before/after of the
>    workflow `parameters` block (Consumption uses `$connections`;
>    Standard's stateful workflow drops it entirely when only
>    `connections.json` is used).
> 5. **Runtime choice** — recommend `FUNCTIONS_WORKER_RUNTIME=node` for
>    this demo (faster cold start, smaller image, no compile step) and
>    note when `dotnet-isolated` would be chosen instead (custom code
>    extensions).
> 6. **What does NOT carry over** — explicitly call out: run history is
>    not migrated, the trigger URL changes, the managed API connection
>    must be re-authorized after deploy.

### Step 2 — Scaffold the Standard project files

> Create a new top-level folder `standard/` containing exactly these files
> with these exact contents:
>
> **`standard/host.json`** — minimum viable host config:
> ```json
> {
>   "version": "2.0",
>   "extensionBundle": {
>     "id": "Microsoft.Azure.Functions.ExtensionBundle.Workflows",
>     "version": "[1.*, 2.0.0)"
>   }
> }
> ```
>
> **`standard/connections.json`** — exactly one `managedApiConnections`
> entry for Office 365, using app-setting references so the file can be
> committed:
> ```json
> {
>   "managedApiConnections": {
>     "office365": {
>       "api": {
>         "id": "/subscriptions/@{appsetting('WORKFLOWS_SUBSCRIPTION_ID')}/providers/Microsoft.Web/locations/@{appsetting('WORKFLOWS_LOCATION_NAME')}/managedApis/office365"
>       },
>       "connection": {
>         "id": "/subscriptions/@{appsetting('WORKFLOWS_SUBSCRIPTION_ID')}/resourceGroups/@{appsetting('WORKFLOWS_RESOURCE_GROUP_NAME')}/providers/Microsoft.Web/connections/con-office365-dev"
>       },
>       "connectionRuntimeUrl": "@appsetting('office365-ConnectionRuntimeUrl')",
>       "authentication": {
>         "type": "ManagedServiceIdentity"
>       }
>     }
>   }
> }
> ```
>
> **`standard/parameters.json`** — empty workflow parameters object:
> ```json
> {}
> ```
>
> **`standard/Approval/workflow.json`** — translate the workflow from
> `infra/workflows/approval.workflow.json` into the Standard schema:
> - Set `"kind": "Stateful"`.
> - **Remove** the top-level `parameters.$connections` block entirely.
> - Keep the `Request` trigger schema **byte-for-byte identical**.
> - Keep the `Condition_threshold`, `Send_approval_email`,
>   `Condition_approved`, and `Response` actions with **the same names
>   and the same logic**.
> - For `Send_approval_email`, change the action `type` from
>   `ApiConnectionWebhook` to `ApiConnectionWebhook` (same) but update
>   the `inputs.host` to reference the Standard connection model:
>   ```json
>   "host": {
>     "connection": {
>       "referenceName": "office365"
>     }
>   }
>   ```
> - Do **not** use `@parameters('$connections')` anywhere — that's
>   Consumption-only.

### Step 3 — Author Standard Bicep infrastructure

> Add `infra-standard/main.bicep` and
> `infra-standard/modules/logicAppStandard.bicep` that deploy a complete
> Standard environment. Match the parameter shape of
> `infra/main.bicep` (location, environmentName, approverEmail,
> threshold) so the dev/prod `.bicepparam` files can mirror the
> Consumption ones.
>
> **`infra-standard/modules/logicAppStandard.bicep` must include:**
> 1. **Storage Account** — `Microsoft.Storage/storageAccounts@2023-01-01`,
>    SKU `Standard_LRS`, kind `StorageV2`, name
>    `st${replace(environmentName,'-','')}laappr` (must be globally unique
>    and ≤24 chars lowercase).
> 2. **App Service Plan** — `Microsoft.Web/serverfarms@2023-12-01`, SKU
>    name `WS1`, tier `WorkflowStandard`, `kind: 'elastic'`, name
>    `asp-la-standard-${environmentName}`.
> 3. **Workflow App site** — `Microsoft.Web/sites@2023-12-01`,
>    `kind: 'functionapp,workflowapp'`, name
>    `la-approval-std-${environmentName}`, `httpsOnly: true`,
>    `serverFarmId` pointing at the plan, with these **exact app
>    settings** (every one of these is required — missing any will cause
>    the site to deploy but fail to load workflows):
>    | Name | Value |
>    |---|---|
>    | `AzureWebJobsStorage` | full connection string from the storage account using `listKeys()` |
>    | `FUNCTIONS_EXTENSION_VERSION` | `~4` |
>    | `FUNCTIONS_WORKER_RUNTIME` | `node` |
>    | `WEBSITE_NODE_DEFAULT_VERSION` | `~20` |
>    | `APP_KIND` | `workflowApp` |
>    | `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING` | same as `AzureWebJobsStorage` |
>    | `WEBSITE_CONTENTSHARE` | `la-approval-std-${environmentName}-content` |
>    | `WORKFLOWS_SUBSCRIPTION_ID` | `subscription().subscriptionId` |
>    | `WORKFLOWS_RESOURCE_GROUP_NAME` | `resourceGroup().name` |
>    | `WORKFLOWS_LOCATION_NAME` | `location` |
> 4. **Office 365 managed connection** — re-declare the same
>    `Microsoft.Web/connections@2016-06-01` resource pattern that exists
>    in `infra/modules/logicApp.bicep` (name `con-office365-${environmentName}`).
>    Do **not** try to share the Consumption connection — keep them
>    independent so the Consumption demo stays intact.
>
> **`infra-standard/main.bicep`** should be a thin wrapper that calls the
> module, taking the same four parameters as `infra/main.bicep`.
>
> **`infra-standard/parameters/dev.bicepparam`** and
> **`infra-standard/parameters/prod.bicepparam`** should mirror the
> matching files under `infra/parameters/`.
>
> Add `@description` decorators on every parameter (match the style of
> `infra/modules/logicApp.bicep`). Run `az bicep build --file
> infra-standard/main.bicep` mentally and make sure it would compile.

### Step 4 — (Optional, advanced) Built-in connector swap

> **Skip this step if running short on time.** In
> `standard/Approval/workflow.json`, replace the Office 365 managed API
> approval-email action with the **built-in Office 365** service-provider
> connector if available in the current extension bundle, otherwise leave
> a `// TODO` comment in `connections.json` explaining the trade-off:
> - **Managed API connection** (what we have): runs out-of-process, OAuth
>   delegated, requires a `Microsoft.Web/connections` resource.
> - **Built-in connector**: runs in-process inside the workflow runtime,
>   lower latency, no separate connection resource, supports VNet
>   integration directly, but fewer auth options.
>
> If swapped, remove the matching entry from
> `connections.json.managedApiConnections` and add a
> `serviceProviderConnections` block instead.

### Step 5 — Deploy script and README

> Add `scripts/deploy-standard.csx` modeled on `scripts/deploy.csx`.
> The script must:
> 1. Parse `--environment dev|prod` exactly like `scripts/deploy.csx`.
> 2. Run `az bicep build --file infra-standard/main.bicep`.
> 3. Run `az deployment group create` against
>    `infra-standard/main.bicep` with the matching `.bicepparam`.
> 4. **After** the Bicep deploys, run `func azure functionapp publish
>    la-approval-std-{env}` from inside the `standard/` directory to push
>    the workflow content. Print a clear `▶ Publishing workflows...`
>    banner before that step.
> 5. Print the trigger callback URL at the end using
>    `az rest --method post` against the workflow's `listCallbackUrl`
>    action so the demo can immediately POST to it.
>
> Then update the root `README.md`: add a new top-level **"Standard
> migration (Scenario 06)"** section after the existing setup, listing:
> - Prerequisites: Azure Functions Core Tools v4
>   (`brew install azure-functions-core-tools@4` or
>   `npm i -g azure-functions-core-tools@4`) for deployment.
> - Deploy: `dotnet script scripts/deploy-standard.csx -- --environment dev`.
> - Post-deploy: re-authorize `con-office365-dev` in the portal (separate
>   connection from the Consumption one — same name pattern, different
>   resource).

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
```

POST the same payload to the Standard workflow's trigger URL using the
Standard-specific invoke script:

```bash
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 2500
```

Same behaviour, different runtime. Open both Logic Apps in the portal to
compare run history, pricing, and scaling.

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

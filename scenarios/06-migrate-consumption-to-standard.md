# Scenario 06 — Migrate Consumption → Standard

**Goal:** Side-by-side migration to a Logic Apps **Standard** project, deployed to Azure. IaC shape, on-disk layout, and the connection model all change at once. The flagship cross-cutting demo. **Azure-only — no local runtime.**

## Model guidance (largest scenario)

- Use **VS Code Agent mode**.
- Start with **Claude Sonnet 4.5** and keep prompts tightly scoped by step with exact file outputs.
- Add "simplest valid approach, no alternatives" to reduce unnecessary reasoning/output.

## Prerequisites

Everything else in this repo's setup already covers Consumption. Standard adds **two** small things — both are CLI-only, no IDE plugins:

| Tool | Why | macOS install |
|---|---|---|
| `az` (Azure CLI) ≥ 2.60 with Bicep | Deploy Bicep + call ARM REST | already required by Scenario 01 |
| `dotnet-script` | Runs `scripts/deploy-standard.csx` and `scripts/invoke-standard.csx` | already required by Scenario 01 |
| `zip` | The deploy script bundles `standard/` into a zip and pushes it via `az webapp deploy` (no Functions Core Tools needed) | preinstalled on macOS / Linux |

You **do not** need Azure Functions Core Tools (`func`) for this scenario. Deployment is pure `az` + zip.

## Why this is a great Copilot demo

A Consumption → Standard migration is **not** a 1:1 copy. It requires:

- Different Azure resources (`Microsoft.Web/sites` kind `functionapp,workflowapp` + WS1 plan + Storage, instead of `Microsoft.Logic/workflows`).
- A different on-disk layout (`workflow.json` per workflow + `host.json` + `connections.json` + `parameters.json`).
- A different connection model: `$connections` in the workflow definition is replaced by a top-level `connections.json`, and the Office 365 API connection itself must be a **V2** connection authorized for the workflow app's **Managed Identity** via `accessPolicies` — V1 (the Consumption flavor) does not support MSI access policies.

By hand from docs this takes hours. With Copilot and the existing Consumption workflow as input, minutes.

This scenario is **additive** — the original Consumption Logic App stays deployed so the audience can compare the two live in the portal.

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
> The deployment target is **Azure only** — do not include any
> `func start` / local runtime guidance.
>
> Save the plan to `docs/migration-consumption-to-standard.md` with these
> sections, in this order:
> 1. **Resource diff** — table mapping each Consumption resource to its
>    Standard equivalent: `Microsoft.Logic/workflows` →
>    `Microsoft.Web/sites` (`kind: 'functionapp,workflowapp'`), plus the
>    **new** required resources (Storage Account, App Service Plan SKU
>    `WS1` / `kind: elastic`). The Office 365 connection is **replaced**,
>    not reused — see (6) below.
> 2. **On-disk layout** — exact file tree for `standard/` (host.json,
>    connections.json, parameters.json, `Approval/workflow.json`) and for
>    `infra-standard/` (main.bicep, modules/logicAppStandard.bicep,
>    parameters/dev.bicepparam, parameters/prod.bicepparam).
> 3. **Connection model change** — explain how `$connections` in
>    Consumption becomes a `connections.json` file at the project root in
>    Standard, with `managedApiConnections` referencing
>    `connectionRuntimeUrl` and an `authentication` block of type
>    `ManagedServiceIdentity`. Connection name and runtime URL come from
>    workflow app settings (`office365-ConnectionName`,
>    `office365-ConnectionRuntimeUrl`).
> 4. **Workflow parameters change** — show the before/after of the
>    workflow `parameters` block: Consumption uses `$connections`;
>    Standard's stateful workflow drops it entirely because
>    `connections.json` resolves the binding by `referenceName`.
> 5. **Runtime choice** — recommend `FUNCTIONS_WORKER_RUNTIME=node` for
>    this demo (faster cold start, smaller image, no compile step).
> 6. **What does NOT carry over** — explicitly call out: run history is
>    not migrated, the trigger URL changes, and **the Office 365
>    connection does not carry over either**. Standard requires a *new*
>    **V2** API connection (`Microsoft.Web/connections@2016-06-01` with
>    `kind: 'V2'`) so it can be authorized for the workflow app's
>    **Managed Identity** via `accessPolicies`. V1 connections (the
>    Consumption flavor) reject `accessPolicies` with
>    `InvalidApiConnectionAccessPolicy`, and a V1 connection without an
>    access policy fails at runtime with `403 missing connection ACL`
>    during MSI token exchange. Use a distinct name
>    `con-office365-std-${environmentName}` so the Consumption V1
>    connection (`con-office365-${environmentName}`) stays untouched.
>    Plan for a **one-time interactive OAuth consent** in the portal
>    after deploy — the consent itself cannot be automated.
>
> Use the **simplest valid approach, no alternatives** in the plan.

> **Note:** This `docs/migration-consumption-to-standard.md` is a
> throwaway artifact produced by the prompt — it is not committed to
> the repo. Re-running this step regenerates it.

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
>     "version": "[4.*, 5.0.0)"
>   }
> }
> ```
>
> **`standard/connections.json`** — exactly one `managedApiConnections`
> entry for Office 365, **using app-setting references** so the file can
> be committed and works in any environment:
> ```json
> {
>   "managedApiConnections": {
>     "office365": {
>       "api": {
>         "id": "/subscriptions/@{appsetting('WORKFLOWS_SUBSCRIPTION_ID')}/providers/Microsoft.Web/locations/@{appsetting('WORKFLOWS_LOCATION_NAME')}/managedApis/office365"
>       },
>       "connection": {
>         "id": "/subscriptions/@{appsetting('WORKFLOWS_SUBSCRIPTION_ID')}/resourceGroups/@{appsetting('WORKFLOWS_RESOURCE_GROUP_NAME')}/providers/Microsoft.Web/connections/@{appsetting('office365-ConnectionName')}"
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
> Note we keep the **managed API** connection (not the built-in
> service-provider Office 365 connector). The
> `ApiConnectionWebhook` action `/approvalmail/$subscriptions` (Send
> approval email with callback) is only available via the managed API —
> the built-in connector supports `Send Email (V2)` but not the
> approval-with-callback pattern this workflow needs.
>
> **`standard/parameters.json`** — empty workflow parameters object:
> ```json
> {}
> ```
>
> **`standard/Approval/workflow.json`** — translate
> `infra/workflows/approval.workflow.json` into the Standard schema:
> - Set top-level `"kind": "Stateful"`.
> - **Remove** the workflow `parameters.$connections` block entirely.
> - Keep the `When_an_approval_request_is_received` `Request` trigger
>   schema **byte-for-byte identical**.
> - Keep `Condition_threshold`, `Send_approval_email`,
>   `Condition_approved`, and the response actions with **the same names
>   and the same logic**.
> - For `Send_approval_email`, keep `type: "ApiConnectionWebhook"` and
>   change the host binding to the Standard reference style:
>   ```json
>   "host": {
>     "connection": {
>       "referenceName": "office365"
>     }
>   }
>   ```
> - Do **not** use `@parameters('$connections')` anywhere — that's
>   Consumption-only. Resolve the approver email and threshold inside
>   the workflow with `InitializeVariable` actions
>   (`Initialize_approverEmail`, `Initialize_threshold`) at the start —
>   the deploy is Azure-only and there is no plan to expose them as
>   workflow parameters in this scenario.

### Step 3 — Author Standard Bicep infrastructure

> Add `infra-standard/main.bicep` and
> `infra-standard/modules/logicAppStandard.bicep` that deploy a complete
> Standard environment to Azure. Match the parameter shape of
> `infra/main.bicep` (location, environmentName, approverEmail,
> threshold, escalationApproverEmail, escalationThreshold, teamsGroupId,
> teamsChannelId) so the dev/prod `.bicepparam` files can mirror the
> Consumption ones.
>
> **`infra-standard/modules/logicAppStandard.bicep` must include:**
>
> 1. **Storage Account** — `Microsoft.Storage/storageAccounts@2023-01-01`,
>    SKU `Standard_LRS`, kind `StorageV2`, name
>    `st${replace(environmentName, '-', '')}lastd` (must be globally
>    unique and ≤24 chars lowercase). Add a
>    `Microsoft.Storage/storageAccounts/fileServices` child with a
>    7-day share-delete retention policy.
> 2. **App Service Plan** — `Microsoft.Web/serverfarms@2023-12-01`, SKU
>    name `WS1`, tier `WorkflowStandard`, `kind: 'elastic'`, name
>    `asp-la-standard-${environmentName}`,
>    `properties.maximumElasticWorkerCount: 20`.
> 3. **V2 Office 365 connection** — declare a **new** V2 API connection
>    (`Microsoft.Web/connections@2016-06-01`, **`kind: 'V2'`**) named
>    `con-office365-std-${environmentName}`. This is **not** the same
>    resource as the Consumption V1 connection
>    (`con-office365-${environmentName}`); keep them independent so the
>    Consumption demo stays intact. Set:
>    ```bicep
>    properties: {
>      displayName: 'Office 365 Outlook (Standard ${environmentName})'
>      api: {
>        id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
>      }
>    }
>    ```
>    V2 is required because only V2 connections expose
>    `properties.connectionRuntimeUrl` (used by `connections.json`) and
>    only V2 connections accept `accessPolicies` children.
> 4. **Workflow App site** — `Microsoft.Web/sites@2023-12-01`,
>    `kind: 'functionapp,workflowapp'`, name
>    `la-approval-std-${environmentName}`, `httpsOnly: true`,
>    `identity: { type: 'SystemAssigned' }` (mandatory — the connection
>    access policy depends on this principal),
>    `serverFarmId` pointing at the plan, with these **exact app
>    settings** (every one is required — missing any will deploy the
>    site but it will fail to load workflows or authenticate to Office
>    365):
>    | Name | Value |
>    |---|---|
>    | `AzureWebJobsStorage` | full connection string built from `storageAccount.listKeys()` |
>    | `FUNCTIONS_EXTENSION_VERSION` | `~4` |
>    | `FUNCTIONS_WORKER_RUNTIME` | `node` |
>    | `WEBSITE_NODE_DEFAULT_VERSION` | `~20` |
>    | `APP_KIND` | `workflowApp` |
>    | `AzureFunctionsJobHost__extensionBundle__id` | `Microsoft.Azure.Functions.ExtensionBundle.Workflows` |
>    | `AzureFunctionsJobHost__extensionBundle__version` | `[1.*, 2.0.0)` |
>    | `AzureWebJobsSecretStorageType` | `Files` |
>    | `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING` | same as `AzureWebJobsStorage` |
>    | `WEBSITE_CONTENTSHARE` | `${workflowAppName}-content` |
>    | `WORKFLOWS_SUBSCRIPTION_ID` | `subscription().subscriptionId` |
>    | `WORKFLOWS_RESOURCE_GROUP_NAME` | `resourceGroup().name` |
>    | `WORKFLOWS_LOCATION_NAME` | `location` |
>    | `office365-ConnectionName` | `office365Connection.name` |
>    | `office365-ConnectionRuntimeUrl` | `office365Connection.properties.connectionRuntimeUrl` |
>
>    Add `dependsOn: [ fileService ]` on the site so the file share is
>    ready before the runtime starts.
>
> 5. **Connection access policy** — declare a child resource
>    `Microsoft.Web/connections/accessPolicies@2016-06-01` with:
>    - `parent: office365Connection`
>    - `name: workflowApp.name`
>    - `properties.principal.type: 'ActiveDirectory'`
>    - `properties.principal.identity.tenantId: workflowApp.identity.tenantId`
>    - `properties.principal.identity.objectId: workflowApp.identity.principalId`
>
>    Without this child, the workflow runtime gets `403 missing
>    connection ACL` on every Send-approval-email call, even though the
>    workflow shows as Healthy in the portal.
>
>    > ⚠️ **V1 will not work.** A V1 connection
>    > (`Microsoft.Web/connections` with no `kind` field, the
>    > Consumption flavor) rejects the `accessPolicies` child with
>    > `InvalidApiConnectionAccessPolicy: Access policies are not
>    > supported in 'V1' api connection`. And a V1 connection **without**
>    > an access policy fails at runtime with the same 403. There is no
>    > V1 path that works for Standard + MSI. Always create a fresh V2
>    > connection.
>
> 6. **Outputs** — `workflowAppName`, `workflowAppId`,
>    `storageAccountName`, `office365ConnectionName`.
>
> **`infra-standard/main.bicep`** is a thin wrapper that calls the module
> with the eight parameters above. Output `resourceGroupName`,
> `workflowAppName`, `storageAccountName`.
>
> **`infra-standard/parameters/dev.bicepparam`** and
> **`infra-standard/parameters/prod.bicepparam`** mirror the matching
> files under `infra/parameters/`.
>
> Add `@description` decorators on every parameter (match the style of
> `infra/modules/logicApp.bicep`). Run `az bicep build --file
> infra-standard/main.bicep` mentally and make sure it would compile.

### Step 4 — Deploy script (`scripts/deploy-standard.csx`)

> Add `scripts/deploy-standard.csx` modeled on `scripts/deploy.csx` and
> reusing helpers from `scripts/lib/common.csx` (`ParseArgs`, `Get`,
> `RepoRoot`, `Run`, `Capture`, `WriteHeading`, `WriteWarn`, `WriteError`,
> `WriteSuccess`). The script must run **end-to-end against Azure** with
> no local runtime steps.
>
> Phases (in order):
>
> 1. **Args** — parse `--environment dev|prod` (default `dev`),
>    `--location` (default `swedencentral`), and `--skip-content`.
>    Validate environment.
> 2. **Bicep build** — `az bicep build --file
>    infra-standard/main.bicep`.
> 3. **Deploy** — `az deployment group create` against
>    `rg-ghcp-logicapp-{env}` with the matching `.bicepparam` and
>    `--parameters location=...`. Use a timestamped deployment name.
> 4. **Read outputs** — `az deployment group show ... --query
>    properties.outputs` and parse `workflowAppName` and
>    `resourceGroupName`.
> 5. **Publish workflows** — unless `--skip-content` is passed:
>    - Build a zip of the `standard/` directory (excluding
>      `local.settings.json`, `.git/*`, `.vscode/*`, `node_modules/*`)
>      using the `zip` CLI on macOS/Linux.
>    - Push it via `az webapp deploy --src-path <zip> --type zip` (this
>      uses the Kudu zip-deploy endpoint and is the simplest path — no
>      Functions Core Tools required).
>    - Delete the temp zip on success.
> 6. **Verify the V2 connection authorization** (this is the critical
>    new piece). The OAuth consent cannot be automated, so the script
>    must detect whether the connection is `Connected` and prompt the
>    operator if not:
>    - Get the subscription ID from `az account show`.
>    - GET
>      `/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/connections/con-office365-std-{env}?api-version=2018-07-01-preview`
>      via `az rest`, projecting
>      `{statuses: properties.statuses, runtimeUrl: properties.connectionRuntimeUrl}`.
>    - Mark `connectionAuthorized = true` if any
>      `statuses[].status == "Connected"` (case-insensitive).
>    - **If not authorized**, print a yellow warning and the *exact*
>      portal URL to consent at:
>      `https://portal.azure.com/#@/resource/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/connections/con-office365-std-{env}/edit`,
>      with the click-path "Authorize → sign in as the approver mailbox →
>      Save", and **do not restart** the app.
>    - **If authorized**, run `az webapp restart` and sleep 30s so the
>      runtime picks up the latest connection runtime URL and re-loads
>      the workflow.
> 7. **Print the trigger callback URL** by POSTing to
>    `https://management.azure.com/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/sites/{workflowApp}/hostruntime/runtime/webhooks/workflow/api/management/workflows/Approval/triggers/When_an_approval_request_is_received/listCallbackUrl?api-version=2023-12-01`
>    via `az rest --method post --uri ...` (note: Standard uses the
>    **hostruntime** path under `Microsoft.Web/sites`, not the
>    Consumption `Microsoft.Logic/workflows` path).
>    - On success, print the URL plus a "Test with: `dotnet script
>      scripts/invoke-standard.csx -- --environment {env} --amount
>      2500`" hint.
>    - If the connection was **not** authorized in phase 6, print a
>      yellow warning that authorization must happen *before* invoking
>      or `Send_approval_email` will fail with `403 missing connection
>      ACL`.
>
> Use the simplest valid approach, no alternatives.

### Step 5 — Invoke script (`scripts/invoke-standard.csx`)

> Add `scripts/invoke-standard.csx` for testing the deployed Standard
> workflow. Model it on `scripts/invoke.csx` and reuse `common.csx`.
>
> Required behavior:
> 1. Parse `--environment dev|prod` (default `dev`), `--amount` (default
>    2500), `--request-id`, `--requester`, `--description`, optional
>    `--trigger-url`, optional `--trigger-name` (default
>    `When_an_approval_request_is_received`).
> 2. If `--trigger-url` is omitted, fetch it by POSTing to the workflow
>    site's `listCallbackUrl` action (same Microsoft.Web/sites
>    `hostruntime` URL shape as the deploy script). This avoids the
>    classic "unquoted `&` truncated my URL on the command line" bug.
> 3. Warn loudly if the trigger URL is missing the `sig=` parameter.
> 4. POST a `{ requestId, requester, amount, description }` JSON body
>    with a 5-minute HTTP timeout.
> 5. On non-2xx, print colored hints keyed by status code. Specifically:
>    - **502** — "Office 365 connection not authorized. Open the Logic
>      App in the portal → Workflows → Approval → connection → Authorize.
>      Or run with `--amount 100` to skip the connector branch."
>    - **500** — "An action failed at runtime, often the Office 365
>      connection."
>    - 401 / 403 / 404 — SAS / IP restriction / workflow-not-found
>      hints.

### Step 6 — Authorize the Standard V2 connection (mandatory before first invoke)

> The Bicep creates `con-office365-std-${environmentName}` as a V2
> connection wired to the workflow app's MSI via `accessPolicies`, but
> the **OAuth consent itself cannot be automated** — it requires a
> human sign-in. Until you complete it, every run fails at
> `Send_approval_email` (the API connection is wired, but no user has
> consented to the connector calling Office 365 on its behalf).
>
> The deploy script's Phase 6 (above) detects this state and prints a
> direct portal URL. Click that URL, then:
>
> 1. The portal opens the API connection's *Edit API connection* blade.
> 2. Click **Authorize**.
> 3. Sign in as the approver mailbox (`approverEmail` from
>    `dev.bicepparam`).
> 4. Consent to the Office 365 permissions Microsoft prompts for.
> 5. Click **Save** at the top of the blade.
>
> After Save, the connection's `properties.statuses[0].status` flips to
> `Connected`. Re-run the deploy script (it will detect the change and
> restart the app) or proceed straight to invoke.

## What changes
- New `standard/` folder with the Standard project layout (host.json, connections.json, parameters.json, Approval/workflow.json).
- New `infra-standard/` Bicep deploying the App Service Plan + workflowapp site + storage + V2 Office 365 connection + access policy.
- New `scripts/deploy-standard.csx` and `scripts/invoke-standard.csx`.
- Original Consumption assets unchanged — side-by-side, not destructive.
- Throwaway `docs/migration-consumption-to-standard.md` produced by Step 1's prompt (not committed).

## Verify

End-to-end the verification flow is **deploy → authorize → invoke**.

### 1. Deploy

```bash
dotnet script scripts/deploy-standard.csx -- --environment dev
```

This builds Bicep, deploys the resource group, zips and pushes the
`standard/` content via `az webapp deploy`, then checks whether the
new V2 connection `con-office365-std-dev` is authorized. On a fresh
deploy it is **not**, and the script prints a yellow warning plus a
direct portal URL.

### 2. Authorize the V2 Office 365 connection (one-time, manual)

Click the portal URL the script printed → **Authorize** → sign in as
the approver mailbox → **Save**. Until this is done, the workflow
validates as Healthy but every run fails at `Send_approval_email` with
`403 missing connection ACL`.

Re-run the deploy script once more (optional) — it will now detect
`Connected` and restart the workflow app to pick up the runtime URL.

### 3. Invoke and expect the email

```bash
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 2500
```

The script POSTs the sample payload, the workflow runs, and the
approver mailbox receives an Office 365 approval email with Approve /
Reject buttons. Click one, and the workflow's response action returns
the matching `approved` / `rejected` body.

Open both Logic Apps in the portal side-by-side to compare run history,
pricing, and scaling between Consumption and Standard.

## Talking points
- **Schema differences are subtle** but Copilot navigates them with both definitions in context.
- **`connections.json` is the new `$connections`** — Copilot translates the binding cleanly, and the app-setting indirection (`@appsetting('office365-ConnectionName')`) makes the file environment-agnostic.
- **V2 vs V1 is the gotcha.** Copilot's first instinct is to reuse the V1 connection from Consumption. The Bicep prompt explicitly disallows that and the failure mode (`403 missing connection ACL`) is documented inline.
- **Side-by-side, not destructive.** Original `infra/` stays untouched — diff Consumption vs Standard live.
- **Cost & operational story.** Standard = predictable pricing, VNet, stateful + stateless mix, private endpoints.

## Stretch
- Have Copilot generate a **GitHub Actions** workflow to deploy the Standard project on push (`azure/login` + `azure/functions-action` with `package: standard/`).
- Ask Copilot to add a **second workflow** (e.g., a stateless reminder) into the same Standard project — multi-workflow is a key Standard advantage.

---
**Redeploy:** `dotnet script scripts/deploy-standard.csx -- --environment dev` (Consumption stays deployed).

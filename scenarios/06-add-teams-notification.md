# Scenario 06 — Add a Teams adaptive card (on Standard)

**Goal (cuttable finale):** Wire a Microsoft Teams adaptive card into the **Standard** Logic App you produced in Scenario 05, so every approval outcome posts a status card to a Teams channel.

> 💡 **Why cuttable:** This is a bonus payoff that demonstrates how clean the V2-connection model is on Standard. If you're short on time, **cut this one first** — the migration in Scenario 05 is the climax of the workshop. Teams notification is the encore.

## Why Standard is the right place for Teams

Adding a Teams adaptive card on Standard is clean because Standard already speaks the modern connection model:

- Standard uses **V2 connections + Managed Identity + `accessPolicies`** — the same model you set up for Office 365 in Scenario 05.
- `connections.json` is just another entry in the same file — additive, not invasive.
- No `$connections` workflow-parameter gymnastics. The action references the connection by `referenceName: 'teams'` and the runtime resolves it via app settings.

## Prerequisites

- Scenario 05 deployed and verified (you can invoke the Standard app and see all four `responseStatus` values).
- A Microsoft Teams **channel** you can post to. You'll need its `groupId` (Team) and `channelId`, both extracted from the channel's **Copy Link** URL:
  1. In Teams, right-click (or click ⋯) the channel → **Get link to channel** → **Copy**.
  2. Paste the URL somewhere readable. It looks like:
     ```
     https://teams.microsoft.com/l/channel/19%3A<channelId>%40thread.tacv2/<channelName>?groupId=<groupId>&tenantId=<tenantId>
     ```
  3. **`teamsGroupId`** = the `groupId` query parameter (a UUID, e.g. `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`).
  4. **`teamsChannelId`** = the path segment between `/channel/` and `%40thread`, URL-decoded: `19:<channelId>@thread.tacv2`.

## Step 0 — Populate the param file

Once you have both values, open `infra-standard/parameters/dev.bicepparam` and add them:

```
param teamsGroupId   = '<paste groupId UUID here>'
param teamsChannelId = '<paste decoded 19:...@thread.tacv2 value here>'
```

Leave everything else in that file unchanged. Copilot will add the matching Bicep parameter declarations when you run the prompt in Step 1.

## Model guidance

- Use **VS Code Agent mode** with **Claude Sonnet 4.6 or higher**. V2 connections on Standard are a clean schema — Sonnet is reliable here.
- Ask for one consolidated diff.

## Prompt (copy/paste)

> Add a Microsoft Teams notification to the **Standard** Logic App. After the existing response action runs, post an Adaptive Card to a Teams channel summarizing the outcome. Update the workflow JSON, `connections.json`, `parameters.json` (if needed), the Bicep module, `main.bicep`, and both `.bicepparam` files together.
>
> **New Bicep parameters** (thread through `main.bicep` → module → app settings, just like the Office 365 ones):
> - `teamsChannelId` (string)
> - `teamsGroupId` (string)
>
> **New Azure resources** in the Standard module (mirror the Office 365 V2 pattern):
> - `Microsoft.Web/connections@2016-06-01` named `con-teams-std-${environmentName}` with `kind: 'V2'` and `properties.api.id = subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'teams')`.
> - `Microsoft.Web/connections/accessPolicies@2016-06-01` child granting the workflow app's MSI (`principal.identity.tenantId = workflowApp.identity.tenantId`, `principal.identity.objectId = workflowApp.identity.principalId`).
> - **New app settings on the workflow app site:** `teams-ConnectionName` (= the connection's `name`), `teams-ConnectionRuntimeUrl` (= the connection's `properties.connectionRuntimeUrl`), `teamsChannelId`, `teamsGroupId`. Existing settings stay unchanged.
>
> **`standard/connections.json`** — add a new `managedApiConnections.teams` entry alongside the existing `office365` entry. Same shape: `connection.id` resolves via `@appsetting('teams-ConnectionName')`, `connectionRuntimeUrl` via `@appsetting('teams-ConnectionRuntimeUrl')`, `authentication.type` is `ManagedServiceIdentity`, and `api.id` points at the `teams` managed API in the deploy region.
>
> **`standard/Approval/workflow.json`** — add a `Post_adaptive_card_to_channel` action that runs **after** the existing response action, branching the card content on the four `responseStatus` values:
> - `auto-approved` and `approved` → green card titled **"Approval Granted"**, color `good`.
> - `rejected` → red card titled **"Request Rejected"**, color `attention`.
> - `escalation-denied` → red card titled **"Escalation Denied"**, color `attention`.
>
> Card body: a `TextBlock` title (size large, weight bolder, color reflecting outcome) plus a `FactSet` with: Request ID, Status, Requester, Amount (formatted as currency), Description. Use plain ASCII text only — no emoji or special characters in the JSON.
>
> Reference the Teams connection by `referenceName: 'teams'` (no `$connections` block). Use `@{appsetting('teamsChannelId')}` and `@{appsetting('teamsGroupId')}` (or the workflow inputs you initialized at the top of the workflow) for the channel and group identifiers.
>
> Use the simplest valid approach, no alternatives.

## Step 2 — Deploy and authorize the Teams connection

```bash
az bicep build --file infra-standard/main.bicep
dotnet script scripts/deploy-standard.csx -- --environment dev
```

The deploy script already has a V2 connection authorization probe from Scenario 05. Extend it (or let Copilot extend it) to check **both** `con-office365-std-${env}` *and* `con-teams-std-${env}`. If the Teams connection isn't `Connected` yet, the script prints:

```
https://portal.azure.com/#@/resource/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/connections/con-teams-std-{env}/edit
```

Click it → **Authorize** → sign in → **Save**, then re-run the deploy script. It will detect both connections are authorized and print the trigger URL.

## Step 3 — Verify

```bash
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 250
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 2500
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 15000
```

✅ **Success indicators:**
- `250` → green **"Approval Granted"** card in the channel.
- `2500` (approved by email) → green **"Approval Granted"** card.
- `2500` (rejected by email) → red **"Request Rejected"** card.
- `15000` (escalation rejects) → red **"Escalation Denied"** card.
- `15000` (escalation approves, then standard approves) → green **"Approval Granted"** card.
- All four card states are reachable; the JSON response from `invoke-standard.csx` still carries the same `responseStatus` values.

## If something goes wrong

- **Card posts but body is `null`** — the `body` is a string of escaped JSON. Ask Copilot to "wrap the body construction in `@{json(concat(...))}` so the runtime sends an object, not a string."
- **`403` from the Teams action** — the V2 connection isn't authorized. Click the portal URL the deploy script printed and complete OAuth.
- **`InvalidApiConnectionAccessPolicy`** — Copilot created the connection without `kind: 'V2'`. Tell it: "The Teams connection must be `kind: 'V2'`. V1 rejects access policies."
## Talking points

- **Encore, not exam.** Scenario 05 was the climax. This one demonstrates the *shape* of post-migration work: adding a connector on Standard is the same V2 + MSI + `accessPolicies` pattern Copilot already learned for Office 365 — and it's additive, not invasive.
- **Same prompt vocabulary, second time.** "Add a connector. V2. Managed Identity. App settings." That's the pattern for every future connector: SQL, ServiceBus, SharePoint — same recipe.
- **What you didn't have to write.** Adaptive Card JSON, the Teams managed-API ID expression, the `accessPolicies` child resource shape, the `connections.json` entry. None of it. Copilot wrote it from the existing Office 365 pattern.

---
**Redeploy:** `dotnet script scripts/deploy-standard.csx -- --environment dev` (then re-run Verify).

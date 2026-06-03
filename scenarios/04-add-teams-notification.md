# Scenario 04 — Add Teams notification

**Goal:** Add a Microsoft Teams adaptive card notification that posts on **both** approve AND reject outcomes with dynamic styling and messaging.

## Current state
The workflow only sends an approval email and returns an HTTP response. There is no Teams integration — no Teams connection, no Teams parameters, no card posting.

## Prerequisites

⚠️ **Before running this scenario:**

1. **Get your Teams channel link**:
   - Open Microsoft Teams → Navigate to the channel where you want cards posted
   - Click the **"..."** next to the channel name → **"Copy link"**
   - Copy the full link (example: `https://teams.microsoft.com/l/channel/19%3A...`)

2. **Configure Teams IDs** (let Copilot help!):
   - Paste the link to Copilot and say: "Update dev.bicepparam with these Teams IDs"
   - Copilot will extract the **channel ID** and **group (team) ID** from the link and update the parameter files
   - You only need those two IDs — the Teams connector reads the tenant from the authorized connection itself, so don't add a `teamsTenantId` parameter
   
   > **Note**: This only adds parameters — the Teams connection resource won't exist until you complete the main scenario implementation below.

## Model guidance (fast demo flow)

- Use **VS Code Agent mode**.
- Prefer **Claude Sonnet 4.5** with strict file targeting.
- Ask for "single diff across workflow JSON + Bicep + params files, simplest valid approach."

## Prompt

> Add a Microsoft Teams adaptive card notification to the approval workflow and change it to async (202 Accepted) processing. Update `infra/workflows/approval.workflow.json`, `infra/main.bicep`, `infra/modules/logicApp.bicep`, and the environment parameter files.
>
> **1. Async response pattern**
> - Add a `responseStatus` string variable initialized to `"pending"`.
> - Move the Response action to run immediately after `Initialize_responseStatus`, returning HTTP `202` with body `{ "requestId": ..., "status": "processing", "message": "Approval workflow started" }`.
> - Remove the old `Respond` scope.
> - The `RequestApproval` scope runs in parallel after the immediate response.
> - In the Switch cases, set `responseStatus` to `"approved"` or `"rejected"` accordingly.
>
> **2. Teams connection**
> - Add `teamsChannelId` and `teamsGroupId` parameters threaded from `main.bicep` → `logicApp.bicep` → workflow (no tenant ID needed — the connector reads it from the authorized connection).
> - Add a `teamsConnection` resource (Microsoft Teams managed API, `kind: 'V2'`) in the Bicep module.
> - Wire it into the workflow's `$connections` parameter alongside the existing Office 365 connection.
> - Update `dev.bicepparam` and `prod.bicepparam` with the Teams IDs.
>
> **3. Adaptive card action — use this exact shape**
>
> Add a `Post_adaptive_card` action of type `ApiConnection` that runs after `Switch_on_approver_response` succeeds. Use this exact structure (NOT the Flow bot `messageBody`/`recipient` schema):
>
> - `method`: `post`
> - `host.connection.name`: `@parameters('$connections')['teams']['connectionId']`
> - `path`: `/v3/beta/teams/@{encodeURIComponent(parameters('teamsGroupId'))}/channels/@{encodeURIComponent(parameters('teamsChannelId'))}/messages`
> - `body` shape:
>   ```
>   body: {
>     body: { contentType: 'html', content: '<attachment id="<guid>"></attachment>' }
>     attachments: [{
>       id: '<same-guid>'
>       contentType: 'application/vnd.microsoft.card.adaptive'
>       content: '@{json(concat(...))}'   // must resolve to a JSON OBJECT, not a string
>     }]
>   }
>   ```
>
> **Adaptive card content rules** (these prevent runtime and compile errors):
> - Build the card via `@{json(concat('...', expr, '...'))}` — NOT a nested Bicep/JSON object with inline `@{...}` expressions, and NOT `@{string(json(...))}`.
> - Card body: a `TextBlock` title plus a `FactSet` with Request ID, Status, Requester, Amount, Description.
> - Title text: `if(equals(variables('responseStatus'), 'approved'), 'Approval Granted', 'Request Rejected')` — **plain ASCII only**, no Unicode glyphs like ✓/✗ (Bicep escape rules forbid them and will fail BCP006).
> - Title color: `if(equals(variables('responseStatus'), 'approved'), 'good', 'attention')` — green for approved, red for rejected.
> - Do **not** include `subscription()`, `resourceGroup()`, or `workflow()` functions inside the card content — they fail at runtime in this scope ("subscription is not defined").
> - Inside Bicep single-quoted strings, JSON double quotes are literal (`"`); do **not** escape them as `\"`.
>
> The card must post on **both** approve and reject outcomes. The workflow returns 202 immediately, and Teams shows the final decision when it's made.

## What changes
- **`infra/modules/logicApp.bicep`**: adds `teamsConnection` resource, `teamsName` variable, `teamsChannelId` / `teamsGroupId` parameters, threads Teams into `$connections`, moves Response to return 202 immediately, adds `Set_responseStatus` + `Post_adaptive_card` actions, removes old `Respond` Scope
- **`infra/main.bicep`**: adds `teamsChannelId` / `teamsGroupId` parameters and passes them to the module
- **`infra/parameters/dev.bicepparam`** & **`prod.bicepparam`**: adds Teams channel and group ID values
- **`infra/workflows/approval.workflow.json`**: adds `responseStatus` variable, moves Response to return 202 immediately after initialization, adds the Teams "Post adaptive card" connector action posting on both approve and reject, removes old synchronous response pattern

## Verify

```bash
# Build and validate Bicep
az bicep build --file infra/main.bicep

# Deploy the updated infrastructure
dotnet script scripts/deploy.csx -- --environment dev
```

**Authorize the Teams connection** (required before testing):
- Open Azure Portal → Navigate to Logic App `la-approval-dev`
- Go to **Development Tools** → **API connections**
- Click `con-teams-dev` → **Edit API connection** → **Authorize**
- Sign in with your Microsoft 365 account
- Click **Save**

**Test the workflow:**
```bash
# Test approved flow
dotnet script scripts/invoke.csx -- --environment dev --amount 2500
# Returns immediately: 202 Accepted, status: "processing"
# Approve via email → Green "Approval Granted ✓" card appears in Teams

# Test rejected flow
dotnet script scripts/invoke.csx -- --environment dev --amount 2500
# Returns immediately: 202 Accepted, status: "processing"
# Reject via email → Red "Request Rejected ✗" card appears in Teams
```

✅ HTTP response returns immediately (202), and Teams cards show final outcomes when approval decisions are made.

## Talking points
- Managed API connections (Teams, Office 365) are first-class Azure resources — declared in Bicep, authorized once per environment.
- For Consumption Logic Apps, the Teams "Post adaptive card in a chat or channel" connector action is the right primitive — Copilot wires it up; you don't hand-author connector URLs.
- Logic Apps variables (`responseStatus`) can be used inside adaptive card JSON via expressions.
- Adaptive Cards support `color` property: `"good"` (green), `"attention"` (red), `"warning"` (yellow), `"default"` (gray).
- Conditional expressions in Logic Apps: `@{if(condition, trueValue, falseValue)}` and can be nested.
- This pattern ensures visibility for ALL workflow outcomes, not just successful approvals.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

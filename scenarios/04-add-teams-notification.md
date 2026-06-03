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

> Add a Microsoft Teams adaptive card notification to the approval workflow and change it to async processing.
> Update `infra/workflows/approval.workflow.json`, `infra/main.bicep`, `infra/modules/logicApp.bicep`, and the environment parameter files to:
>
> 1. **Return 202 Accepted immediately** after variable initialization (don't wait for approval):
>    - Move the Response action to run right after `Initialize_responseStatus`
>    - Status code: `202`, body: `{ "requestId": "...", "status": "processing", "message": "Approval workflow started" }`
>    - Remove the old `Respond` Scope that waited for approval completion
>
> 2. **Process approval asynchronously**:
>    - RequestApproval Scope runs after Response (no dependencies)
>    - Add a `teamsConnection` resource (Microsoft Teams managed API) in the Bicep module
>    - Add `teamsChannelId` and `teamsGroupId` parameters threaded from main.bicep → module → workflow (no tenant ID — the connector reads it from the authorized connection)
>    - Wire the Teams connection into the workflow's `$connections` parameter
>    - Add a `responseStatus` variable set after the Switch resolves (approved/rejected)
>
> 3. **Post adaptive card with final outcome**:
>    - This is a Consumption Logic App, so use the **Microsoft Teams connector's "Post adaptive card in a chat or channel"** action. Let Copilot pick the connector operation — don't hand-write connector URLs or swagger paths.
>    - Add it as `Post_adaptive_card` after `Switch_on_approver_response` completes, posting to channel `teamsChannelId` in team `teamsGroupId` as the Flow bot.
>    - Card body: title plus a FactSet with Request ID, Status, Requester, Amount, Description, and a link back to the run.
>    - Title is dynamic via `@{if(equals(variables('responseStatus'), 'approved'), 'Approval Granted ✓', 'Request Rejected ✗')}` (green for approved, red for rejected).
>    - The card must post on **both** approve and reject outcomes.
>
> The workflow now returns immediately, and Teams shows the final decision when it's made.

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

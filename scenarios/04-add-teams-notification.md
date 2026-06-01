# Scenario 04 — Add Teams notification

**Goal:** Add a Microsoft Teams adaptive card notification that posts on **both** approve AND reject outcomes with dynamic styling and messaging.

## Current state
The workflow only sends an approval email and returns an HTTP response. There is no Teams integration — no Teams connection, no Teams parameters, no card posting.

## Prerequisites

⚠️ **Before running this scenario:**

1. **Get your Teams channel link**:
   - Open Microsoft Teams → Navigate to the channel where you want cards posted
   - Click the **"..."** next to the channel name → **"Get link to channel"**
   - Copy the full link (example: `https://teams.microsoft.com/l/channel/19%3A...`)

2. **Configure Teams IDs** (let Copilot help!):
   - Paste the link to Copilot and say: "Update dev.bicepparam with these Teams IDs"
   - Copilot will extract the channel ID, group ID, and tenant ID and update the parameter files
   
   > **Note**: This only adds parameters — the Teams connection resource won't exist until you complete the main scenario implementation below.

## Model guidance (fast demo flow)

- Use **VS Code Agent mode**.
- Prefer **GPT-5.3 or newer** with strict file targeting.
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
>    - Add `teamsChannelId`, `teamsGroupId`, and `teamsTenantId` parameters threaded from main.bicep → module → workflow
>    - Wire the Teams connection into the workflow's `$connections` parameter
>    - Add a `responseStatus` variable set after the Switch resolves (approved/rejected)
>
> 3. **Post adaptive card with final outcome**:
>    - Add `Post_adaptive_card` action after Switch_on_approver_response completes
>    - Use the Microsoft Teams connector's `PostCardToConversation` operation shape, not the Microsoft Graph channel messages path
>    - For a Consumption Logic App `ApiConnection` action, set:
>      - `method`: `post`
>      - `path`: `/v1.0/teams/conversation/adaptivecard/poster/Flow%20bot/location/Channel`
>      - `body.recipient.groupId`: `teamsGroupId`
>      - `body.recipient.channelId`: `teamsChannelId`
>      - `body.messageBody`: the Adaptive Card JSON serialized as a string, e.g. `string(approvalAdaptiveCard)` in Bicep
>    - Do **not** use `/v1.0/teams/{teamId}/channels/{channelId}/messages`; that Graph-style path does not map to a Teams connector swagger operation and causes "Operation Id cannot be determined from definition and swagger"
>    - Title: "Approval Granted ✓" (green/good) for approved, "Request Rejected ✗" (red/attention) for rejected
>    - FactSet with: Request ID, Status (responseStatus variable), Requester, Amount, Description
>    - Include link to the workflow run: `[View Run](@{concat('https://portal.azure.com/#view/...')})`
>    - Use conditional expressions: `@{if(equals(variables('responseStatus'), 'approved'), 'Approval Granted ✓', 'Request Rejected ✗')}`
>    - Card must post on BOTH outcomes (approved and rejected)
>
> The workflow now returns immediately, and Teams shows the final decision when it's made.

## What changes
- **`infra/modules/logicApp.bicep`**: adds `teamsConnection` resource, `teamsName` variable, `teamsChannelId`/`teamsGroupId`/`teamsTenantId` parameters, threads Teams into `$connections`, moves Response to return 202 immediately, adds `Set_responseStatus` + `Post_adaptive_card` actions, defines the Adaptive Card as an object and passes it as `string(approvalAdaptiveCard)`, removes old `Respond` Scope
- **`infra/main.bicep`**: adds `teamsChannelId`/`teamsGroupId`/`teamsTenantId` parameters and passes them to the module
- **`infra/parameters/dev.bicepparam`** & **`prod.bicepparam`**: adds Teams ID values
- **`infra/workflows/approval.workflow.json`**: adds `responseStatus` variable, moves Response to return 202 immediately after initialization, adds dynamic adaptive card action posting on both approve and reject using the `PostCardToConversation` connector path and `recipient`/`messageBody` payload, removes old synchronous response pattern

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
- Teams connector actions must use connector swagger paths. `PostCardToConversation` uses `/v1.0/teams/conversation/adaptivecard/poster/Flow%20bot/location/Channel`; Graph-style channel message URLs are not valid `ApiConnection` paths.
- Logic Apps variables (`responseStatus`) can be used inside adaptive card JSON via expressions.
- Adaptive Cards support `color` property: `"good"` (green), `"attention"` (red), `"warning"` (yellow), `"default"` (gray).
- Conditional expressions in Logic Apps: `@{if(condition, trueValue, falseValue)}` and can be nested.
- This pattern ensures visibility for ALL workflow outcomes, not just successful approvals.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

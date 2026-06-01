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
   - Copilot will extract the channel ID and group ID and update the file automatically
   
3. **Deploy the changes**:
   ```bash
   dotnet script scripts/deploy.csx -- --environment dev
   ```

4. **Authorize the Teams connection** in Azure Portal:
   - Navigate to Logic App `la-approval-dev` → API connections
   - Click `con-teams-dev` → **Edit API connection** → **Authorize**
   - Sign in with your Microsoft 365 account

## Model guidance (fast demo flow)

- Use **VS Code Agent mode**.
- Prefer **GPT-4.1** with strict file targeting.
- Ask for "single diff across workflow JSON + Bicep + params files, simplest valid approach."

## Prompt

> Add a Microsoft Teams adaptive card notification to the approval workflow.
> Update `infra/workflows/approval.workflow.json`, `infra/modules/logicApp.bicep`,
> `infra/main.bicep`, and both `.bicepparam` files to:
>
> 1. Add a `teamsConnection` resource (Microsoft Teams managed API) in the Bicep module
> 2. Add `teamsChannelId` and `teamsGroupId` parameters threaded from main.bicep → module → workflow
> 3. Wire the Teams connection into the workflow's `$connections` parameter
> 4. Add a `responseStatus` variable set after the Switch resolves (approved/rejected)
> 5. Post an adaptive card to Teams after the Switch with dynamic styling:
>    - Title: "Approval Granted ✓" (green) for approved, "Request Rejected ✗" (red/attention) for rejected
>    - A "Status" fact showing the responseStatus variable
>    - Requester, amount, description, request ID, and a link to the run
>    - Use conditional expressions: `@{if(equals(variables('responseStatus'), 'approved'), 'Approval Granted ✓', 'Request Rejected ✗')}`
>
> Card color should be `"good"` for approved, `"attention"` for rejected. The card must post on BOTH outcomes.

## What changes
- **`infra/modules/logicApp.bicep`**: adds `teamsConnection` resource, `teamsName` variable, `teamsChannelId`/`teamsGroupId` parameters, threads Teams into `$connections`, and adds `Set_responseStatus` + `Post_adaptive_card` actions
- **`infra/main.bicep`**: adds `teamsChannelId`/`teamsGroupId` parameters and passes them to the module
- **`infra/parameters/dev.bicepparam`** & **`prod.bicepparam`**: adds Teams ID values
- **`infra/workflows/approval.workflow.json`**: adds `responseStatus` variable + dynamic adaptive card action posting on both approve and reject

## Verify

```bash
az bicep build --file infra/main.bicep
dotnet script scripts/deploy.csx -- --environment dev
```

Then authorize the Teams connection:
- Azure Portal → Logic App `la-approval-dev` → **API connections** → `con-teams-dev` → **Edit API connection** → **Authorize** → sign in with M365 account.

```bash
# Test approved flow
dotnet script scripts/invoke.csx -- --environment dev --amount 2500
# Approve via email → Green "Approval Granted ✓" card appears in Teams

# Test rejected flow
dotnet script scripts/invoke.csx -- --environment dev --amount 2500
# Reject via email → Red "Request Rejected ✗" card appears in Teams
```

✅ Both approve and reject outcomes post adaptive cards with appropriate styling.

## Talking points
- Managed API connections (Teams, Office 365) are first-class Azure resources — declared in Bicep, authorized once per environment.
- Logic Apps variables (`responseStatus`) can be used inside adaptive card JSON via expressions.
- Adaptive Cards support `color` property: `"good"` (green), `"attention"` (red), `"warning"` (yellow), `"default"` (gray).
- Conditional expressions in Logic Apps: `@{if(condition, trueValue, falseValue)}` and can be nested.
- This pattern ensures visibility for ALL workflow outcomes, not just successful approvals.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

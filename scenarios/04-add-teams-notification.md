# Scenario 04 — Enhance Teams notification

**Goal:** Update the Teams notification to post cards for **both** approve AND reject outcomes with dynamic styling and messaging.

## Current state
The workflow already posts a Teams adaptive card when requests are approved, but it only posts on approval. Rejections are silent.

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

> The workflow currently posts Teams adaptive cards only when requests are
> approved. Update both `infra/workflows/approval.workflow.json` and
> `infra/modules/logicApp.bicep` to:
> 
> 1. Set `shouldPostTeams=true` in BOTH the Approve and Reject cases
> 2. Make the adaptive card dynamic based on the outcome:
>    - Title: "Approval Granted ✓" (green) for approved, "Request Rejected ✗" (red/attention) for rejected
>    - Add a "Status" fact showing the responseStatus variable value
>    - Use conditional expressions to set the text and color based on responseStatus
>
> The card should show the requester, amount, description, request ID, status, and link to the run.

## What changes
- `Set_shouldPostTeams_true_reject` action added in the Reject case
- Adaptive card `messageBody` becomes dynamic:
  - Title uses `@{if(equals(variables('responseStatus'), 'approved'), 'Approval Granted ✓', 'Request Rejected ✗')}`
  - Color uses conditional: `"good"` for approved, `"attention"` for rejected
  - New "Status" fact shows the current responseStatus variable
- Both workflow.json and logicApp.bicep updated with matching changes

## Verify

```bash
az bicep build --file infra/main.bicep
dotnet script scripts/deploy.csx -- --environment dev
# Test approved flow
dotnet script scripts/invoke.csx -- --environment dev --amount 2500
# Approve via email → Green "Approval Granted ✓" card appears in Teams

# Test rejected flow
dotnet script scripts/invoke.csx -- --environment dev --amount 2500
# Reject via email → Red "Request Rejected ✗" card appears in Teams
```

✅ Both approve and reject outcomes post adaptive cards with appropriate styling.

## Talking points
- Logic Apps variables (`responseStatus`) can be used inside adaptive card JSON via expressions.
- Adaptive Cards support `color` property: `"good"` (green), `"attention"` (red), `"warning"` (yellow), `"default"` (gray).
- Conditional expressions in Logic Apps: `@{if(condition, trueValue, falseValue)}` and can be nested.
- This pattern ensures visibility for ALL workflow outcomes, not just successful approvals.
- One variable change (`shouldPostTeams`) gates the entire Teams notification - clean separation of concerns.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

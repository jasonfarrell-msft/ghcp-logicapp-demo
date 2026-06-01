# Scenario 05 — Add escalation approval tier

**Goal:** Add a second approval tier for high-value requests. Requests above an escalation threshold go to a senior approver first, then continue to the standard approver if escalation is approved (or stop with 403 if rejected).

## Current state
The workflow has a single approval tier:
- Amount < threshold → auto-approve (200 response)
- Amount ≥ threshold → email to `approverEmail`, respond with approve/reject status

## Success criteria
After this scenario:
- New workflow parameters: `escalationApproverEmail` (string, default 'escalation@contoso.com'), `escalationThreshold` (int, default 10000)
- New logic: If amount > escalationThreshold → send escalation email first
- If escalation approver **rejects** → set responseStatus to `"escalation-denied"`, skipApproval=true, and **post adaptive card to Teams showing escalation denial**
- If escalation approver **approves** → set `skipApproval=false` and continue to standard approval flow
- Standard approval logic runs only if `skipApproval==false` (preserves auto-approve and handles escalation rejection)
- **Teams adaptive card posts for all outcomes:** escalation-denied (red), approved (green), rejected (red), auto-approved (green)
- Test all three paths: auto-approve (< threshold), standard approval (≥ threshold, < escalation), escalation (≥ escalation)

## Model guidance

- Use **VS Code Agent mode** with **GPT-4.1+**.
- Provide full context: reference `infra/workflows/approval.workflow.json` and `infra/modules/logicApp.bicep`.
- This is a **cross-file change**: workflow JSON, Bicep module, main.bicep, and both .bicepparam files.

## Example prompt

> Add a second approval tier for high-value requests:
> 
> 1. Add two new workflow parameters: `escalationApproverEmail` (string, default 'escalation@contoso.com') and `escalationThreshold` (int, default 10000)
> 2. After the existing "Check_amount_against_threshold" If block (that sets skipApproval for auto-approve), add a new If condition "Check_escalation_threshold" that checks if amount > escalationThreshold
> 3. In the TRUE branch of "Check_escalation_threshold":
>    - Add action "Send_escalation_email" (Office 365 ApiConnectionWebhook, "Send approval email", same structure as existing approval email but To: escalationApproverEmail)
>    - Add Switch condition "Switch_on_escalation_response" on the escalation approval response SelectedOption:
>      - Case "Approve": Do nothing (continues to standard flow)
>      - Case "Reject": Set responseStatus='escalation-denied', skipApproval=true
>      - Default: Same as Reject
> 4. The existing standard approval logic (Send_approval_email action) should already be inside a condition that checks skipApproval==false, so it naturally skips after escalation rejection
> 5. **CRITICAL - Fixed Post_adaptive_card Timing:** The existing `Post_adaptive_card` action must run after the ENTIRE approval flow completes. **Set its runAfter to `{ "Check_skipApproval": ["Succeeded"] }`** (NOT Check_escalation_threshold). This ensures the card posts AFTER both the escalation check AND the standard approval flow complete, so responseStatus is properly set before the card displays. Without this, mid-range amounts ($1000-$10000) will post cards with empty responseStatus showing as "Request Rejected ✗" before the approval email is even sent.
> 6. Update the adaptive card title logic to handle the escalation-denied case: Title should be "Request Escalation Denied ✗" with color "attention" when responseStatus equals 'escalation-denied', "Approval Granted ✓" (green/good) when 'approved' or 'auto-approved', and "Request Rejected ✗" (red/attention) for 'rejected'.
> 7. Update main.bicep to add the two new parameters with @description decorators
> 8. Update dev.bicepparam: escalationApproverEmail = 'escalation@contoso.com', escalationThreshold = 10000
> 9. Update prod.bicepparam: escalationApproverEmail = 'cfo-approvals@contoso.com', escalationThreshold = 25000
> 
> Maintain the existing retryPolicy, HandleFailure scope, and all error handling patterns. Ensure Teams notifications work for all outcomes: escalation-denied, approved, rejected, and auto-approved.

## Implementation notes
- The escalation check should come **after** the auto-approve check and **before** the standard approval email
- Both the escalation email and standard approval email need the same retryPolicy configuration
- The `skipApproval` variable acts as a circuit breaker: set to true in three places (auto-approve, escalation rejection, within HandleFailure)
- The escalation Switch should handle Approve/Reject/Default cases explicitly
- **Teams notification positioning:** The `Post_adaptive_card` action must run after the entire approval flow (escalation + standard) completes, not just after the standard approval switch. This ensures it captures escalation rejections.
- **Adaptive card title logic:** Must handle four responseStatus values: 'escalation-denied' (red/attention), 'rejected' (red/attention), 'approved' (green/good), 'auto-approved' (green/good)
- This touches 5 files total: workflow.json, logicApp.bicep, main.bicep, dev.bicepparam, prod.bicepparam

## Verify

```bash
# Rebuild and deploy with new parameters
az bicep build --file infra/main.bicep
dotnet script scripts/deploy.csx -- --environment dev

# Test all three approval paths
dotnet script scripts/invoke.csx -- --environment dev --amount 250     # auto-approve (< 1000)
dotnet script scripts/invoke.csx -- --environment dev --amount 2500    # standard approval (≥ 1000, < 10000)
dotnet script scripts/invoke.csx -- --environment dev --amount 15000   # escalation tier (≥ 10000)
```

✅ **Success indicators:**
- 250: 200 response, `responseStatus: "auto-approved"`, Teams card with green "Approval Granted ✓"
- 2500: Email to approverEmail only (no escalation), Teams card shows approval/rejection outcome
- 15000: Email to escalation@contoso.com first
  - If escalation approver **rejects**: Teams card with red "Request Escalation Denied ✗", no email to standard approver
  - If escalation approver **approves**: Email to approverEmail, Teams card shows final standard approval/rejection outcome
- **Check Teams channel:** All outcomes (escalation-denied, approved, rejected, auto-approved) should post adaptive cards with appropriate colors and titles

## Gotchas
- **Cross-file consistency**: All 5 files must stay in sync. If you add a parameter to main.bicep, it must appear in both .bicepparam files.
- **Switch expressions**: The Switch condition expression is `@body('Send_escalation_email')?['SelectedOption']` (matches the standard approval pattern).
- **Variable state**: The `skipApproval` variable must be checked before the standard approval email to prevent duplicate approvals.
- **Nested conditionals**: You now have three levels: auto-approve check → escalation check → standard approval (only if skipApproval==false).
- **CRITICAL - Teams notification timing:** The `Post_adaptive_card` action must depend on `Check_skipApproval`, NOT `Check_escalation_threshold`. Set runAfter to `{ "Check_skipApproval": ["Succeeded"] }`. This ensures it waits for BOTH the escalation check AND the standard approval flow (including setting responseStatus) to complete before posting the card. If you set it to depend on `Check_escalation_threshold`, it will run in parallel with `Check_skipApproval`, causing the card to post with empty responseStatus (showing as "Request Rejected ✗") before the approval email is sent for mid-range amounts.
- **Adaptive card conditional logic**: The title expression needs to handle 4 states (escalation-denied, rejected, approved, auto-approved). Use nested if() expressions: `@{if(equals(variables('responseStatus'), 'escalation-denied'), 'Request Escalation Denied ✗', if(or(equals(variables('responseStatus'), 'approved'), equals(variables('responseStatus'), 'auto-approved')), 'Approval Granted ✓', 'Request Rejected ✗'))}`

## Talking points
- **Progressive enhancement**: Each scenario adds a new capability without breaking prior work.
- **Cross-file orchestration**: Copilot can coordinate changes across workflow JSON, Bicep, and parameter files in one go.
- **Conditional branching**: The escalation Switch demonstrates approval workflow state machines.
- **Environment-specific thresholds**: Dev uses 10K, prod uses 25K for escalation - same logic, different risk tolerance.
- **Multi-path notifications**: Teams adaptive card posts for all approval outcomes (4 states: escalation-denied, rejected, approved, auto-approved) with dynamic styling and content.
- **Action sequencing (Bug Fix Applied)**: The Post_adaptive_card action MUST depend on Check_skipApproval (which wraps the standard approval flow), NOT Check_escalation_threshold. This prevents the card from posting prematurely with empty responseStatus before the approval flow completes.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

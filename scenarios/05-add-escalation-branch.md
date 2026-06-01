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
- If escalation approver **rejects** → 403 response with `"escalation-denied"` status (no standard approval needed)
- If escalation approver **approves** → set `skipApproval=false` and continue to standard approval flow
- Standard approval logic runs only if `skipApproval==false` (preserves auto-approve and handles escalation rejection)
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
> 5. Update main.bicep to add the two new parameters with @description decorators
> 6. Update dev.bicepparam: escalationApproverEmail = 'escalation@contoso.com', escalationThreshold = 10000
> 7. Update prod.bicepparam: escalationApproverEmail = 'cfo-approvals@contoso.com', escalationThreshold = 25000
> 
> Maintain the existing retryPolicy, HandleFailure scope, and all error handling patterns.

## Implementation notes
- The escalation check should come **after** the auto-approve check and **before** the standard approval email
- Both the escalation email and standard approval email need the same retryPolicy configuration
- The `skipApproval` variable acts as a circuit breaker: set to true in three places (auto-approve, escalation rejection, within HandleFailure)
- The escalation Switch should handle Approve/Reject/Default cases explicitly
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
- 250: 200 response, `responseStatus: "auto-approved"`
- 2500: Email to approverEmail only (no escalation)
- 15000: Email to escalation@contoso.com first, then approverEmail if approved

## Gotchas
- **Cross-file consistency**: All 5 files must stay in sync. If you add a parameter to main.bicep, it must appear in both .bicepparam files.
- **Switch expressions**: The Switch condition expression is `@body('Send_escalation_email')?['SelectedOption']` (matches the standard approval pattern).
- **Variable state**: The `skipApproval` variable must be checked before the standard approval email to prevent duplicate approvals.
- **Nested conditionals**: You now have three levels: auto-approve check → escalation check → standard approval (only if skipApproval==false).

## Talking points
- **Progressive enhancement**: Each scenario adds a new capability without breaking prior work.
- **Cross-file orchestration**: Copilot can coordinate changes across workflow JSON, Bicep, and parameter files in one go.
- **Conditional branching**: The escalation Switch demonstrates approval workflow state machines.
- **Environment-specific thresholds**: Dev uses 10K, prod uses 25K for escalation - same logic, different risk tolerance.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

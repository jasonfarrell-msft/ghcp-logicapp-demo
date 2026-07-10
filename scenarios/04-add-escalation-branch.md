# Scenario 04 — Add an escalation tier for high-value requests

**Goal:** Add a second approval tier so that **really expensive** requests need a senior approver first. This is a *business rule change* — the workflow gets a new branch, new parameters, and a third response status, all observable from the console.

> 💡 **Demo framing:** "The CFO wants a sign-off above $10K." You shouldn't need to know Logic Apps internals to ship it. Let Copilot do the wiring across all five files.

## Current state

After Scenario 03, the workflow:
- Auto-approves below the threshold (`responseStatus: "auto-approved"`)
- Otherwise emails the standard approver and returns `responseStatus: "approved"` or `"rejected"`
- Has retry policies and a `HandleFailure` scope wrapping the approval flow

There is **one** approver and **one** threshold. The console (via `invoke.csx`) prints the final `responseStatus` from the HTTP 200 body — that is your visibility into every outcome.

## What we want

- A new "escalation" tier on top of the existing flow
- Above an **escalation threshold**, a senior approver gets the email *first*
  - If they **reject**, the request stops there and returns `responseStatus: "escalation-denied"`
  - If they **approve**, the workflow continues to the existing standard approval flow as it does today
- Auto-approve below the normal threshold still works exactly as before
- Every outcome — `auto-approved`, `approved`, `rejected`, `escalation-denied` — is visible in the JSON response printed by `invoke.csx`

## Model guidance

- Use **VS Code Agent mode** with **Claude Sonnet 4.6 or higher**.
- Run **two prompts in sequence** — Prompt 1 is the full deployable change (Bicep + params), Prompt 2 syncs the standalone workflow JSON reference file. Only Prompt 1 is required for deployment.
- Context for **Prompt 1**: `infra/main.bicep`, `infra/modules/logicApp.bicep`, `infra/parameters/dev.bicepparam`, `infra/parameters/prod.bicepparam`.
- Context for **Prompt 2**: `infra/workflows/approval.workflow.json` only.

## Prompt 1 — Add escalation to the deployed Bicep (copy/paste)

> Add a CFO escalation tier to the approval workflow. This changes four files. Do not touch `infra/workflows/approval.workflow.json`.
>
> **`infra/main.bicep`:**
> - Declare `param escalationApproverEmail string` and `param escalationThreshold int = 10000`.
> - Pass both into the `logicApp` module call alongside the existing `approverEmail` and `threshold`.
>
> **`infra/modules/logicApp.bicep`:**
> - Add matching `param escalationApproverEmail string` and `param escalationThreshold int = 10000` declarations.
> - Add four `InitializeVariable` actions to the end of the existing root init chain (after `Initialize_responseStatus`). The full chain must be: `Initialize_approverEmail` → `Initialize_threshold` → `Initialize_responseStatus` → `Initialize_escalationApproverEmail` (string, value `escalationApproverEmail`) → `Initialize_escalationThreshold` (integer, value `escalationThreshold`) → `Initialize_skipApproval` (boolean, value `false`) → `Initialize_decision` (string, value `''`). Each runAfter the previous one.
> - Update `RequestApproval`'s `runAfter` to point to `Initialize_decision` (the last init in the chain).
> - Inside the `RequestApproval` scope, add a new `Check_escalation_threshold` action (type `If`, runAfter `{}`) BEFORE `Check_amount_against_threshold`. Expression: `amount > escalationThreshold`. True branch: `Send_escalation_email` (ApiConnectionWebhook to `escalationApproverEmail`, subject prefix `'ESCALATION - '`, same connector/path/retry as `Send_approval_email`), then `Switch_on_escalation_response` on `body('Send_escalation_email')?['SelectedOption']`: Approve case = empty actions, Reject case = `Set_decision_escalation_denied` (SetVariable `decision = 'escalation-denied'`) then `Set_skipApproval_true` (SetVariable `skipApproval = true`, runAfter `Set_decision_escalation_denied`). False/else branch: empty.
> - Update `Check_amount_against_threshold`: set `runAfter: { Check_escalation_threshold: ['Succeeded'] }`, and add `{ equals: ['@variables(\'skipApproval\')', false] }` as a second AND condition to its expression.
> - In the else branch of `Check_amount_against_threshold`, wrap `Set_status_auto_approved` in a new `If` named `Check_skip_before_auto_approve` with expression `equals(skipApproval, false)`. Empty else branch.
> - Replace the `Respond` scope's `Send_response` action with `Respond_result`: type `Response`, HTTP 200, body `{ requestId: '@triggerBody()?[\'requestId\']', responseStatus: '@variables(\'decision\')' }`.
>
> **`infra/parameters/dev.bicepparam`:** add `param escalationApproverEmail = 'escalation@contoso.com'` and `param escalationThreshold = 10000`.
>
> **`infra/parameters/prod.bicepparam`:** add `param escalationApproverEmail = 'cfo-approvals@contoso.com'` and `param escalationThreshold = 25000`.

## Prompt 2 — Sync the reference workflow JSON (copy/paste)

> Update `infra/workflows/approval.workflow.json` to match the escalation logic now in `infra/modules/logicApp.bicep`. Do not change any Bicep files.
>
> Add four `InitializeVariable` actions to the root-level init chain (after `Initialize_responseStatus`):
> 1. `Initialize_escalationApproverEmail` (string, value `"escalation@contoso.com"`) — runAfter `Initialize_responseStatus`.
> 2. `Initialize_escalationThreshold` (integer, value `10000`) — runAfter `Initialize_escalationApproverEmail`.
> 3. `Initialize_skipApproval` (boolean, value `false`) — runAfter `Initialize_escalationThreshold`.
> 4. `Initialize_decision` (string, value `""`) — runAfter `Initialize_skipApproval`.
>
> Update `RequestApproval`'s `runAfter` to point to `Initialize_decision` instead of `Initialize_responseStatus`.
>
> Inside the `RequestApproval` scope, before `Check_amount_against_threshold`:
> 1. Add `Check_escalation_threshold` (type `If`, runAfter `{}`): expression `amount > escalationThreshold`. True branch: `Send_escalation_email` (ApiConnectionWebhook to `@variables('escalationApproverEmail')`, same connector/path/retryPolicy as `Send_approval_email`, subject prefix `"ESCALATION - "`), then `Switch_on_escalation_response` on `body('Send_escalation_email')?['SelectedOption']`: Approve case = empty, Reject case = `Set_decision_escalation_denied` (SetVariable `decision = "escalation-denied"`) then `Set_skipApproval_true` (SetVariable `skipApproval = true`, runAfter it). False/else branch: empty.
> 2. Set `Check_amount_against_threshold` runAfter to `{ "Check_escalation_threshold": ["Succeeded"] }`. Add `{ "equals": ["@variables('skipApproval')", false] }` as a second AND condition on its expression.
> 3. In the else branch of `Check_amount_against_threshold`, wrap `Set_status_auto_approved` in a new `If` named `Check_skip_before_auto_approve` with expression `equals(skipApproval, false)`. Empty else branch.
>
> In the `Respond` scope, replace `Send_response` with `Respond_result`: HTTP 200, body `{ "requestId": "@triggerBody()?['requestId']", "responseStatus": "@variables('decision')" }`.
>
> Keep all retry policies, `HandleFailure` scope, and existing `runAfter` chains intact.

## What should change

**After Prompt 1 (deployable — this is the only prompt needed for `deploy.csx` to work):**
- **`infra/main.bicep`** — two new `param` declarations + both passed into the module call
- **`infra/modules/logicApp.bicep`** — two new `param` declarations + four new root `InitializeVariable` actions (`escalationApproverEmail`, `escalationThreshold`, `skipApproval`, `decision`) + escalation branch inside `RequestApproval` scope + `skipApproval` gating on `Check_amount_against_threshold` + `Check_skip_before_auto_approve` guard in else branch + `Respond` scope uses `Respond_result` reading `decision` variable
- **`infra/parameters/dev.bicepparam`** and **`prod.bicepparam`** — values for the two new params

**After Prompt 2 (reference sync — not required for deployment):**
- **`infra/workflows/approval.workflow.json`** — same init variables and escalation logic, kept in sync with the Bicep for documentation purposes

## Verify

```bash
az bicep build --file infra/main.bicep
dotnet script scripts/deploy.csx -- --environment dev

# Below the standard threshold → auto-approve, no email
dotnet script scripts/invoke.csx -- --environment dev --amount 250

# Above standard, below escalation → goes to standard approver
dotnet script scripts/invoke.csx -- --environment dev --amount 2500

# Above escalation → goes to escalation approver first
dotnet script scripts/invoke.csx -- --environment dev --amount 15000
```

✅ **Success indicators (all visible in the `invoke.csx` console output):**
- `250` → `HTTP 200`, `"status": "auto-approved"`, no email
- `2500` → email to standard approver, response is `"approved"` or `"rejected"`
- `15000` (escalation rejects) → `"status": "escalation-denied"`, no email to standard approver
- `15000` (escalation approves) → email to standard approver next, response carries the final outcome

## If something goes wrong

- **Standard approver gets emailed even after escalation rejected** → tell Copilot: "Use the existing `skipApproval` variable to gate the standard approval, the same way auto-approve does."
- **Bicep build complains about a missing parameter** → one of the five files is out of sync. Ask Copilot to "verify `escalationApproverEmail` and `escalationThreshold` are declared and passed through every layer (`main.bicep` → `logicApp.bicep` → workflow `parameters`) and present in both `.bicepparam` files."
- **`InvalidVariableInitialization` on deploy** → an `InitializeVariable` was placed inside a `Scope`. Move it back to the root `actions` block.

## Talking points

- **Business-rule change, not a rewrite.** A non-Logic-Apps expert can describe the rule ("CFO sign-off above $10K") and Copilot wires up the parameters, branch, and gating — no Logic Apps internals knowledge required.
- **One prompt ships it.** Prompt 1 produces a deployable change across four files. Prompt 2 is optional housekeeping for the reference JSON. No dependency chains, no broken assumptions between prompts.
- **Cross-file coordination.** One prompt keeps `main.bicep`, the module, and both `.bicepparam` files in agreement — params declared, passed, initialized, and used in the workflow logic.
- **Environment-specific thresholds.** Dev escalates at 10K, prod at 25K — same code, different risk posture, controlled by `.bicepparam`.
- **Console as observability.** Every outcome is reflected in the HTTP 200 body, so `invoke.csx` is enough to demo the full state machine — no extra connectors needed.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

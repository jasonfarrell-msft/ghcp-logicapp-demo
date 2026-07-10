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
- Run **two prompts in sequence** — Prompt 1 is infra only (fast), Prompt 2 is workflow logic only (fast). Do not combine them.
- Context for **Prompt 1**: `infra/main.bicep`, `infra/modules/logicApp.bicep`, `infra/parameters/dev.bicepparam`, `infra/parameters/prod.bicepparam`.
- Context for **Prompt 2**: `infra/workflows/approval.workflow.json` only.

## Prompt 1 — Thread parameters through infra (copy/paste)

> Add two new parameters to the infra layer only — do not change any workflow behavior yet.
>
> In `infra/main.bicep`:
> - Declare `param escalationApproverEmail string` and `param escalationThreshold int = 10000`.
> - Pass both into the `logicApp` module call alongside the existing `approverEmail` and `threshold`.
>
> In `infra/modules/logicApp.bicep`:
> - Add matching `param escalationApproverEmail string` and `param escalationThreshold int = 10000` declarations.
> - Add two `InitializeVariable` actions at the end of the root init chain (after `Initialize_decision`): `Initialize_escalationApproverEmail` (string, value `escalationApproverEmail`) and `Initialize_escalationThreshold` (integer, value `escalationThreshold`). Chain them: `Initialize_escalationApproverEmail` runAfter `Initialize_decision`, `Initialize_escalationThreshold` runAfter `Initialize_escalationApproverEmail`.
> - Add a third `InitializeVariable` action `Initialize_skipApproval` (boolean, value `false`) runAfter `Initialize_escalationThreshold`.
> - Update `RequestApproval`'s `runAfter` to point to `Initialize_skipApproval` instead of `Initialize_decision`.
>
> In `infra/parameters/dev.bicepparam`: add `param escalationApproverEmail = 'escalation@contoso.com'` and `param escalationThreshold = 10000`.
>
> In `infra/parameters/prod.bicepparam`: add `param escalationApproverEmail = 'cfo-approvals@contoso.com'` and `param escalationThreshold = 25000`.

## Prompt 2 — Add escalation logic to the workflow (copy/paste)

> Update `infra/workflows/approval.workflow.json` only. Assume `escalationApproverEmail`, `escalationThreshold`, and `skipApproval` variables are already initialized at the root level (added by a previous step).
>
> Inside the `RequestApproval` scope, before `Check_amount_against_threshold`:
> 1. Add `Check_escalation_threshold` (type `If`): expression `amount > escalationThreshold`.
>    - **True branch:** send an approval email to `@variables('escalationApproverEmail')` via the same Office 365 connector used by `Send_approval_email`. Subject prefix `"ESCALATION - "`. Same retry policy (exponential, 4 retries, PT10S). Then `Switch_on_escalation_response` on `body('Send_escalation_email')?['SelectedOption']`:
>      - **Approve case:** empty actions (fall through).
>      - **Reject case:** `Set_decision_escalation_denied` (SetVariable `decision = "escalation-denied"`) then `Set_skipApproval_true` (SetVariable `skipApproval = true`) runAfter it.
>    - **False branch (else):** empty actions.
> 2. `Check_amount_against_threshold` runs `runAfter: { Check_escalation_threshold: ["Succeeded"] }`. Add `{ "equals": ["@variables('skipApproval')", false] }` as a second condition (AND) on its expression.
> 3. In the **false/else** branch of `Check_amount_against_threshold`, wrap `Set_decision_auto_approved` in a new `If` named `Check_skip_before_auto_approve` with expression `skipApproval == false`. Empty else branch.
>
> In the `Respond` scope, replace `Switch_on_decision` and its three cases with a single `Response` action named `Respond_result`: HTTP 200, body `{ "requestId": "@triggerBody()?['requestId']", "responseStatus": "@variables('decision')" }`.
>
> Keep all retry policies, `HandleFailure` scope, and existing `runAfter` chains intact. Use the simplest valid approach, no alternatives.

## What should change

**After Prompt 1:**
- **`infra/main.bicep`** — two new `param` declarations + both passed into the module call
- **`infra/modules/logicApp.bicep`** — two new `param` declarations + three new root `InitializeVariable` actions (`escalationApproverEmail`, `escalationThreshold`, `skipApproval`) + `RequestApproval` `runAfter` updated
- **`infra/parameters/dev.bicepparam`** and **`prod.bicepparam`** — values for the two new params

**After Prompt 2:**
- **`infra/workflows/approval.workflow.json`** — escalation branch inside `RequestApproval` scope, `skipApproval` gating on `Check_amount_against_threshold`, `Check_skip_before_auto_approve` guard in the else branch, `Respond` scope collapsed to a single `Respond_result` action

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
- **Separation of concerns.** Prompt 1 is pure infra plumbing (fast, low risk). Prompt 2 is pure workflow logic (isolated, easier to review). Splitting by concern is faster than one giant prompt and easier to demo live.
- **Cross-file coordination.** Even split across two prompts, one feature request keeps `main.bicep`, the module, both `.bicepparam` files, and the workflow JSON in agreement.
- **Environment-specific thresholds.** Dev escalates at 10K, prod at 25K — same code, different risk posture, controlled by `.bicepparam`.
- **Console as observability.** Every outcome is reflected in the HTTP 200 body, so `invoke.csx` is enough to demo the full state machine — no extra connectors needed.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

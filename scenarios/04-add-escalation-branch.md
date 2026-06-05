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

- Use **VS Code Agent mode** with **Claude Sonnet 4.5 or higher**.
- VSCode Agent Mode will use all files in the solution for context.
- Ask for one consolidated diff across all of them.

> ⏱️ **Note:** Due to the complexity of this scenario, queries can take upwards of 5-10 minutes to complete.

## Prompt (copy/paste)

> I want to add a high-value escalation tier to my approval workflow. Please update the workflow JSON, the Bicep module, `main.bicep`, and both `.bicepparam` files together.
>
> **New parameters** (thread them through `main.bicep` → `logicApp.bicep` → workflow, just like the existing `approverEmail` and `threshold`):
> - `escalationApproverEmail` (string) — dev: `escalation@contoso.com`, prod: `cfo-approvals@contoso.com`
> - `escalationThreshold` (int) — dev: `10000`, prod: `25000`
>
> **New behavior:**
> - If `amount > escalationThreshold`, send the approval email to `escalationApproverEmail` **first**.
>   - If they **reject** → set `responseStatus` to `escalation-denied`, skip the standard approval, and return.
>   - If they **approve** → fall through to the existing standard approval flow unchanged.
> - If `amount <= escalationThreshold`, the workflow behaves exactly like today.
>
> **Response shape:** the existing HTTP 200 response body must include the final `responseStatus`, so the four possible values (`auto-approved`, `approved`, `rejected`, `escalation-denied`) are all visible to the caller. Reuse the same `skipApproval` pattern the workflow already uses for auto-approve — don't invent a new gating mechanism.
>
> Please keep retry policies, the `HandleFailure` scope, and the existing error handling intact. Use the simplest valid approach, no alternatives.

## What should change

- **`infra/main.bicep`** — two new parameters, passed into the module
- **`infra/modules/logicApp.bicep`** — two new parameters, passed into the workflow `parameters`
- **`infra/workflows/approval.workflow.json`** — two new workflow parameters, an escalation branch that reuses the same Office 365 connector, and a final response that surfaces all four status values
- **`infra/parameters/dev.bicepparam`** and **`prod.bicepparam`** — values for the two new params

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

- **Business-rule change, not a rewrite.** A non-Logic-Apps expert can describe the rule ("CFO sign-off above $10K") and Copilot wires up the parameters, branch, and gating across five files in a single pass.
- **Cross-file coordination.** One feature request touches `main.bicep`, the module, both `.bicepparam` files, and the workflow JSON — all of which must stay in agreement.
- **Environment-specific thresholds.** Dev escalates at 10K, prod at 25K — same code, different risk posture, controlled by `.bicepparam`.
- **Console as observability.** Every outcome is reflected in the HTTP 200 body, so `invoke.csx` is enough to demo the full state machine — no extra connectors needed.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

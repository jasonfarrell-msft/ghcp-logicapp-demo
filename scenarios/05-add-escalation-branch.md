# Scenario 05 — Add an escalation tier for high-value requests

**Goal:** Add a second approval tier so that **really expensive** requests need a senior approver first. The Teams card you already built in Scenario 04 should also show the escalation outcome.

> 💡 **Demo framing:** This scenario is meant to feel like a *business rule change* — "the CFO wants a sign-off above $10K." You shouldn't need to know Logic Apps internals to ship it. Let Copilot do the wiring.

## Current state
After Scenario 04, the workflow:
- Auto-approves when amount is below the threshold
- Otherwise emails the standard approver and posts a Teams card with the result

There is **one** approver and **one** threshold.

## What we want
- A new "escalation" tier on top of the existing flow
- Above an **escalation threshold**, a senior approver gets the email *first*
  - If they **reject**, the request stops there and the Teams card shows it was denied at escalation
  - If they **approve**, the workflow continues to the existing standard approval flow as it does today
- Auto-approve below the normal threshold still works exactly as before
- **Every non-auto-approved outcome ends with a Teams card** — approved, rejected, or escalation-denied

## Reusing what's already there (important)
You do **not** need to add a new Teams connection, new channel/group IDs, or touch any of the Teams parameters from Scenario 04. The `Post_adaptive_card` action already exists and is already wired to your channel. We're just going to:

1. Add new parameters for the escalation approver and threshold
2. Add a branch in the workflow that runs *before* the existing approval logic
3. Tweak the existing Teams card title so it has a third state: "escalation-denied"

That's it. No new IDs, no new connections, no re-pasting Teams channel links.

## Model guidance

- Use **VS Code Agent mode** with **Claude Sonnet 4.5**.
- Add these to context: `infra/workflows/approval.workflow.json`, `infra/modules/logicApp.bicep`, `infra/main.bicep`, and both `.bicepparam` files.
- Ask for one consolidated diff across all of them.

## Prompt (copy/paste)

> I want to add a high-value escalation tier to my approval workflow. Please update the workflow JSON, the Bicep module, `main.bicep`, and both `.bicepparam` files together.
>
> **New parameters** (thread them through `main.bicep` → `logicApp.bicep` → workflow, just like the existing `approverEmail` and `threshold`):
> - `escalationApproverEmail` (string) — dev: `escalation@contoso.com`, prod: `cfo-approvals@contoso.com`
> - `escalationThreshold` (int) — dev: `10000`, prod: `25000`
>
> **New behavior:**
> - If `amount > escalationThreshold`, send the approval email to `escalationApproverEmail` **first**.
>   - If they **reject** → set `responseStatus` to `escalation-denied`, skip the standard approval, and let the existing Teams card post.
>   - If they **approve** → fall through to the existing standard approval flow unchanged.
> - If `amount <= escalationThreshold`, the workflow behaves exactly like today.
>
> **Teams card:** keep the existing `Post_adaptive_card` action and its existing connection — do not add a new Teams connection or change the channel/group/tenant parameters. Just update the card's title expression to handle a third state:
> - `approved` or `auto-approved` → "Approval Granted ✓" (good)
> - `rejected` → "Request Rejected ✗" (attention)
> - `escalation-denied` → "Escalation Denied ✗" (attention)
>
> Make sure the Teams card runs after the **whole** approval flow has finished (including the escalation branch), so `responseStatus` is set before the card is built. Reuse the same `skipApproval` pattern the workflow already uses for auto-approve — don't invent a new one.
>
> Please keep retry policies, error handling, and the existing `HandleFailure` scope intact.

## What should change
- **`infra/main.bicep`** — two new parameters, passed into the module
- **`infra/modules/logicApp.bicep`** — two new parameters, passed into the workflow `parameters`
- **`infra/workflows/approval.workflow.json`** — two new workflow parameters, an escalation branch that reuses the same email connector, and an updated card title expression
- **`infra/parameters/dev.bicepparam`** and **`prod.bicepparam`** — values for the two new params

> ✋ **Do not** add or change anything related to `teamsChannelId`, `teamsGroupId`, `teamsTenantId`, or the Teams API connection. They were set up in Scenario 04 and are reused as-is.

## Verify

```bash
az bicep build --file infra/main.bicep
dotnet script scripts/deploy.csx -- --environment dev

# Below the standard threshold → auto-approve, no email, no Teams card
dotnet script scripts/invoke.csx -- --environment dev --amount 250

# Above standard, below escalation → goes to standard approver, Teams card on outcome
dotnet script scripts/invoke.csx -- --environment dev --amount 2500

# Above escalation → goes to escalation approver first
dotnet script scripts/invoke.csx -- --environment dev --amount 15000
```

✅ **Success indicators:**
- `250` → 200 response, `responseStatus: auto-approved`, no Teams card needed
- `2500` → email to standard approver, Teams card shows "Approval Granted ✓" or "Request Rejected ✗"
- `15000` (escalation rejects) → Teams card shows "Escalation Denied ✗", no email to standard approver
- `15000` (escalation approves) → email to standard approver next, Teams card shows that final outcome

## If something goes wrong

- **Teams card shows the wrong title** → tell Copilot: "The card title is being evaluated before `responseStatus` is set. Make `Post_adaptive_card` wait for the entire approval flow (including the escalation branch) to finish."
- **Standard approver gets emailed even after escalation rejected** → tell Copilot: "Use the existing `skipApproval` variable to gate the standard approval, the same way auto-approve does."
- **Bicep build complains about a missing parameter** → one of the five files is out of sync. Ask Copilot to "verify `escalationApproverEmail` and `escalationThreshold` are declared and passed through every layer (`main.bicep` → `logicApp.bicep` → workflow `parameters`) and present in both `.bicepparam` files."

## Talking points
- **Business-rule change, not a rewrite.** A non-Logic-Apps expert can describe the rule ("CFO sign-off above $10K") and Copilot wires up the parameters, branch, and card.
- **Reuse beats re-plumbing.** We deliberately don't touch the Teams connection — the value is showing how new behavior layers on top of existing infrastructure.
- **Environment-specific thresholds.** Dev escalates at 10K, prod at 25K — same code, different risk posture, controlled by `.bicepparam`.
- **One card, three outcomes.** The Teams card now reflects approved, rejected, *and* escalation-denied without adding new actions.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

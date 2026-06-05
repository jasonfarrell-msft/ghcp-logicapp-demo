# Scenario 06 — Externalize config to App Settings

**Goal:** Replace the hard-coded threshold values and email addresses in the Standard workflow with **App Settings** — so business rules can be changed in the Azure portal (or a pipeline variable) without touching workflow JSON or redeploying.

> 💡 **Demo framing:** "After the migration, the logic is right — but the threshold and approver email are still baked into the workflow JSON. In Logic Apps Standard, you can reference App Settings with `@appsetting('...')` directly in any expression. That means the CFO can change the approval threshold from the portal with zero deployment. Let's wire that up."

## Current state

After Scenario 05, `standard/Approval/workflow.json` contains `InitializeVariable` actions with hard-coded values:

```json
"InitializeThreshold":    { "inputs": { "value": 5000 } }
"InitializeApprover":     { "inputs": { "value": "finance@contoso.com" } }
"InitializeEscThreshold": { "inputs": { "value": 10000 } }
"InitializeEscApprover":  { "inputs": { "value": "escalation@contoso.com" } }
```

These values are invisible to operators and require a code change + redeploy to adjust.

## What we want

- Four new **Azure App Settings** on the Standard Logic App:
  - `ThresholdAmount` — normal approval threshold
  - `ApproverEmail` — standard approver address
  - `EscalationThreshold` — senior approver threshold
  - `EscalationApproverEmail` — senior approver address
- The `InitializeVariable` values in the workflow replaced with `@appsetting('...')` references — so the live values are read from the hosting environment at runtime
- The new settings threaded through Bicep (`main.bicep` → `logicAppStandard.bicep` → app settings) and into both `.bicepparam` files
- Workflow behavior **identical** to Scenario 05 — only the source of the values changes

## Model guidance

- Use **VS Code Agent mode** with **Claude Sonnet 4.6 or higher**. This is a targeted cross-file edit; Sonnet handles it well.
- Add to context: `standard/Approval/workflow.json`, `infra-standard/modules/logicAppStandard.bicep`, `infra-standard/main.bicep`, and both `infra-standard/parameters/*.bicepparam` files.
- One consolidated diff across all five files.

## Prompt (copy/paste)

> The Standard Logic App in `standard/Approval/workflow.json` has four `InitializeVariable` actions whose values are hard-coded: the normal threshold, the escalation threshold, the approver email, and the escalation approver email.
>
> Please refactor these so the values come from Azure App Settings instead of being hard-coded in the workflow JSON.
>
> **Changes needed:**
>
> 1. **`standard/Approval/workflow.json`** — replace the `value` in each `InitializeVariable` action with an `@appsetting()` expression:
>    - Threshold → `@appsetting('ThresholdAmount')` (keep the existing variable name and `runAfter`)
>    - Approver email → `@appsetting('ApproverEmail')`
>    - Escalation threshold → `@appsetting('EscalationThreshold')`
>    - Escalation approver email → `@appsetting('EscalationApproverEmail')`
>
> 2. **`infra-standard/modules/logicAppStandard.bicep`** — add four new string parameters (`thresholdAmount`, `approverEmail`, `escalationThreshold`, `escalationApproverEmail`) and include them in the `appSettings` array of the workflow app site resource with the matching key names (`ThresholdAmount`, `ApproverEmail`, `EscalationThreshold`, `EscalationApproverEmail`).
>
> 3. **`infra-standard/main.bicep`** — declare the same four parameters and pass them into the module call.
>
> 4. **`infra-standard/parameters/dev.bicepparam`** — set the values:
>    - `thresholdAmount`: `'5000'`
>    - `approverEmail`: `'finance@contoso.com'`
>    - `escalationThreshold`: `'10000'`
>    - `escalationApproverEmail`: `'escalation@contoso.com'`
>
> 5. **`infra-standard/parameters/prod.bicepparam`** — use production values:
>    - `thresholdAmount`: `'7500'`
>    - `approverEmail`: `'approvals@contoso.com'`
>    - `escalationThreshold`: `'25000'`
>    - `escalationApproverEmail`: `'cfo-approvals@contoso.com'`
>
> Do not change any action names, `runAfter` chains, logic, or response shapes. Use the simplest valid approach, no alternatives. Single consolidated diff across all five files.

## What should change

- **`standard/Approval/workflow.json`** — four `InitializeVariable` `.value` fields replaced with `@appsetting()` expressions; everything else untouched
- **`infra-standard/modules/logicAppStandard.bicep`** — four new params; four new entries in `appSettings`
- **`infra-standard/main.bicep`** — four new params threaded into the module call
- **`infra-standard/parameters/dev.bicepparam`** — dev values for the four new params
- **`infra-standard/parameters/prod.bicepparam`** — prod values for the four new params

## Verify

```bash
az bicep build --file infra-standard/main.bicep
dotnet script scripts/deploy-standard.csx -- --environment dev
```

Re-run all three invoke amounts to confirm behavior is unchanged:

```bash
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 250
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 2500
dotnet script scripts/invoke-standard.csx -- --environment dev --amount 15000
```

✅ **Success indicators:**
- `250` → `"status": "auto-approved"` (no email)
- `2500` → email to approver → `"status": "approved"` or `"rejected"`
- `15000` → email to escalation approver → `"status": "escalation-denied"` or falls through

**Portal confirm (the money shot):** Azure Portal → `la-approval-std-dev` → **Settings** → **Environment variables**. Show `ThresholdAmount`, `ApproverEmail`, `EscalationThreshold`, `EscalationApproverEmail` listed there — and open `standard/Approval/workflow.json` side-by-side to show the values are *gone* from the code.

## If something goes wrong

- **`InvalidTemplate` on `@appsetting()` in workflow JSON** — the expression is not inside a string template. Wrap it: `"@appsetting('ThresholdAmount')"`. The value for a numeric variable should use a conversion: `"@int(appsetting('ThresholdAmount'))"`.
- **Threshold variable type mismatch** — `@appsetting()` always returns a string. If the existing condition compares to an integer, tell Copilot: "The `InitializeVariable` for threshold is `type: Integer`; wrap the `appsetting()` call with `int()`: `@int(appsetting('ThresholdAmount'))`."
- **Bicep build fails with `missing required property`** — one of the five files is out of sync. Ask Copilot to verify that the four new params are declared in `main.bicep`, passed to the module, declared in the module, set in `appSettings`, and present in both `.bicepparam` files.
- **App Settings not visible after deploy** — the deploy script zips and deploys `standard/` content but the app settings come from the Bicep. If you updated the Bicep but skipped `az bicep build`, rebuild and re-run `deploy-standard.csx`.

## Talking points

- **Standard-exclusive capability.** Consumption workflows can't reference App Settings — values must be in parameters in the Bicep or hard-coded. This is a concrete Standard advantage an architect will care about.
- **Operator-friendly.** After this change, a non-engineer can tune approval thresholds via the portal **without touching source code or triggering a deployment**. That changes who owns the business rule.
- **Environment parity.** Dev escalates at $10K, prod at $25K — same workflow JSON, different hosting config. This is the right separation of concerns.
- **Zero behavior change.** All four `responseStatus` outcomes from Scenario 04 are still reachable — the portal App Settings blade is proof the values moved, not the behavior.

---
**Redeploy:** `dotnet script scripts/deploy-standard.csx -- --environment dev` (then re-run Verify).

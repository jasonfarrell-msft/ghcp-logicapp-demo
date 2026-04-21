# Scenario 02 — Parameterize hard-coded values

**Goal:** Move `approverEmail` and `threshold` out of literal `InitializeVariable` blocks into workflow `parameters` fed by Bicep params, so per-environment behaviour comes from `.bicepparam` files alone.

## Prompt

> In `infra/workflows/approval.workflow.json`, replace the literal values for
> `approverEmail` and `threshold` with workflow `parameters` of type `string`
> and `int`. Update all references via `@parameters('...')`. Then update
> `infra/modules/logicApp.bicep` so the workflow `parameters` block supplies
> these values from the existing Bicep params, and remove the now-redundant
> `InitializeVariable` actions.

## What changes
- Workflow `parameters` block gains `approverEmail` (string) and `threshold` (int).
- All references switch from `@variables(...)` to `@parameters(...)`.
- `InitializeVariable` actions removed.
- Bicep wires the values from existing params into the workflow `parameters` property.

## Verify

```powershell
az bicep build --file infra/main.bicep
./scripts/deploy.ps1 -Environment dev
./scripts/invoke.ps1 -Environment dev -Amount 100

./scripts/deploy.ps1 -Environment prod
./scripts/invoke.ps1 -Environment prod -Amount 100
```

✅ Same workflow, different threshold per environment, sourced from `dev.bicepparam` / `prod.bicepparam`. Identical input now produces different branches per env — the tangible payoff of parameterizing.

## Talking points
- Copilot distinguishes workflow `variables` from workflow `parameters` — a common Logic Apps gotcha.
- One prompt → both files updated consistently.
- The dev/prod behaviour split is the concrete IaC payoff.

---
**Redeploy:** `./scripts/deploy.ps1 -Environment dev` (then re-run Verify).

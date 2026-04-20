# Scenario 02 — Parameterize hard-coded values

**Goal:** Move hard-coded values (`approverEmail`, `threshold`, connector
references) out of the workflow body and into Bicep + workflow parameters.

## Demo loop
1. **Deploy baseline:** `./scripts/deploy.ps1 -Environment dev`.
2. **Refactor** with the prompt below.
3. **Validate:** `az bicep build --file infra/main.bicep`.
4. **Redeploy:** `./scripts/deploy.ps1 -Environment dev`. Then redeploy
   `prod` too (`./scripts/deploy.ps1 -Environment prod`) to show the same
   workflow now picks up a different threshold from `prod.bicepparam` — a
   tangible payoff of parameterizing.
5. **Invoke** both envs to show they behave differently with the same input.
6. **Reset:** `./scripts/reset.ps1 -Environment dev`.

## Setup
`infra/workflows/approval.workflow.json` initializes `approverEmail` and
`threshold` as variables with literal values. The Bicep mirror passes them
through Bicep params already, but the standalone JSON does not.

## Prompt to give Copilot
> In `infra/workflows/approval.workflow.json`, replace the literal values for
> `approverEmail` and `threshold` with workflow `parameters` of type `string`
> and `int`. Update all references via `@parameters('...')`. Then update
> `infra/modules/logicApp.bicep` so the workflow `parameters` block supplies
> these values from the existing Bicep params, and remove the now-redundant
> `InitializeVariable` actions.

## Talking points
- Copilot recognizes the difference between workflow `variables` and workflow
  `parameters` — a common Logic Apps gotcha.
- One prompt, two files updated consistently.

## Expected outcome
- Workflow `parameters` now contains `approverEmail` and `threshold`.
- `InitializeVariable` actions removed.
- All references updated to `@parameters('approverEmail')` /
  `@parameters('threshold')`.
- Bicep wires the values into the `parameters` property of the workflow
  resource.

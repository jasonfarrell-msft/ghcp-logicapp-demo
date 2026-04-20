# Scenario 04 — Add an escalation branch

**Goal:** Introduce a second approver tier for high-value requests.

## Demo loop
1. **Deploy baseline:** `./scripts/deploy.ps1 -Environment dev`.
2. **Invoke** with `-Amount 15000` — current workflow treats this the same
   as any above-threshold request. Single approver only.
3. **Refactor** with the prompt below.
4. **Validate:** `az bicep build --file infra/main.bicep`.
5. **Redeploy:** `./scripts/deploy.ps1 -Environment dev`.
6. **Re-invoke** with three amounts to walk all three branches:
   - `-Amount 250` → auto-approved
   - `-Amount 2500` → single approver
   - `-Amount 15000` → escalation approver first
7. **Reset:** `./scripts/reset.ps1 -Environment dev`.

## Setup
Currently the workflow only checks `amount > threshold`. There is no concept of
"this request is so large it needs two approvals."

## Prompt to give Copilot
> Update `infra/workflows/approval.workflow.json` and the matching definition
> in `infra/modules/logicApp.bicep` to add an `escalationThreshold` workflow
> parameter (default `10000`). When `amount > escalationThreshold`, send an
> approval email to a new `escalationApproverEmail` parameter **first**; only
> if they approve should the original `approverEmail` flow run. If the
> escalation approver rejects, respond `403` with status `escalation-denied`.
> Also add `escalationApproverEmail` and `escalationThreshold` as Bicep params
> with sensible defaults, and surface them in `dev.bicepparam` and
> `prod.bicepparam`.

## Talking points
- Copilot threads a new branch through the workflow, the Bicep module, the
  subscription-level `main.bicep`, and both `.bicepparam` files in one go.
- Demonstrates Copilot handling cross-file consistency for IaC.

## Expected outcome
- New nested `If` for `amount > escalationThreshold`.
- Two new Bicep params + parameter file entries.
- Workflow correctly short-circuits when escalation is denied.

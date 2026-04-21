# Scenario 04 — Add an escalation branch

**Goal:** Introduce a second approver tier for high-value requests, threading the change through workflow JSON, the Bicep module, `main.bicep`, and both `.bicepparam` files.

## Prompt

> Update `infra/workflows/approval.workflow.json` and the matching definition
> in `infra/modules/logicApp.bicep` to add an `escalationThreshold` workflow
> parameter (default `10000`). When `amount > escalationThreshold`, send an
> approval email to a new `escalationApproverEmail` parameter **first**; only
> if they approve should the original `approverEmail` flow run. If the
> escalation approver rejects, respond `403` with status `escalation-denied`.
> Also add `escalationApproverEmail` and `escalationThreshold` as Bicep params
> with sensible defaults, and surface them in `dev.bicepparam` and
> `prod.bicepparam`.

## What changes
- New nested `If` for `amount > escalationThreshold` ahead of the existing approval branch.
- New workflow params `escalationApproverEmail` / `escalationThreshold`.
- Same params added to the Bicep module + both `.bicepparam` files.
- Escalation rejection short-circuits with `HTTP 403` `"escalation-denied"`.

## Verify

```powershell
az bicep build --file infra/main.bicep
./scripts/deploy.ps1 -Environment dev
./scripts/invoke.ps1 -Environment dev -Amount 250     # auto-approved
./scripts/invoke.ps1 -Environment dev -Amount 2500    # single approver
./scripts/invoke.ps1 -Environment dev -Amount 15000   # escalation first
```

✅ Three amounts walk all three branches.

## Talking points
- One prompt threads a new branch through workflow JSON, Bicep module, and both parameter files.
- Maximum cross-file fan-out for a single intent — the consistency story at its strongest.

---
**Redeploy:** `./scripts/deploy.ps1 -Environment dev` (then re-run Verify).

# Scenario 05 — Add an escalation branch

**Goal:** Introduce a second approver tier for high-value requests, threading the change through workflow JSON, the Bicep module, `main.bicep`, and both `.bicepparam` files.

## Model guidance (cross-file-heavy scenario)

- Use **VS Code Agent mode**.
- Start with **GPT-4.1** and strict file-targeting prompts.
- Explicitly ask for "one consolidated diff touching workflow, module, main, and both bicepparam files; simplest valid approach."

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

```bash
az bicep build --file infra/main.bicep
dotnet script scripts/deploy.csx -- --environment dev
dotnet script scripts/invoke.csx -- --environment dev --amount 250     # auto-approved
dotnet script scripts/invoke.csx -- --environment dev --amount 2500    # single approver
dotnet script scripts/invoke.csx -- --environment dev --amount 15000   # escalation first
```

✅ Three amounts walk all three branches.

## Talking points
- One prompt threads a new branch through workflow JSON, Bicep module, and both parameter files.
- Maximum cross-file fan-out for a single intent — the consistency story at its strongest.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

# Scenario 05 — Understand and test the escalation branch

**Goal:** Explore the existing two-tier approval system, understand how it threads through multiple files, and test all three approval branches (auto-approve, standard, escalation).

## Current state
The workflow already implements escalation logic:
- Requests > `escalationThreshold` (default 10000) go to `escalationApproverEmail` first
- If escalation approver rejects → 403 response with `"escalation-denied"`  
- If escalation approver approves → continues to standard `approverEmail` flow
- Parameters already defined in `main.bicep`, `logicApp.bicep`, `dev.bicepparam`, and `prod.bicepparam`

This scenario helps you understand the cross-file consistency and test the full behavior.

## Model guidance (exploratory scenario)

- Use **VS Code Agent mode** with **GPT-4.1+**.
- Ask Copilot to **explain** the escalation flow rather than build it.
- Example prompt: "Walk me through how the escalation logic works. Show me all the places where escalationThreshold and escalationApproverEmail are defined and used."

## Understanding prompts

> Explain how the escalation approval branch works in this workflow. Show me:
> 1. Where `escalationThreshold` and `escalationApproverEmail` are defined (params)
> 2. The conditional logic that checks if escalation is needed
> 3. What happens when the escalation approver rejects vs approves
> 4. How the `skipApproval` variable prevents duplicate approval requests
> 5. All files that need to stay synchronized for this feature

## What to explore
- **workflow.json** `Check_escalation_threshold` If block
- **workflow.json** `Switch_on_escalation_response` with Approve/Reject cases
- **logicApp.bicep** inline workflow definition matches workflow.json structure
- **main.bicep** parameter definitions with `@description` decorators
- **dev.bicepparam** / **prod.bicepparam** different threshold values for environments
- Variable flow: `skipApproval` → prevents standard approval after escalation rejection

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
- **Cross-file consistency**: One feature (escalation) touches 5 files that must stay synchronized.
- **Variable state management**: `skipApproval` acts as a circuit breaker after escalation rejection.
- **Nested conditional logic**: `Check_escalation_threshold` AND `Check_amount_against_threshold` work together with `skipApproval==false` check.
- **Environment-specific configuration**: `dev` uses 10000 threshold, `prod` uses 25000 - same logic, different values.
- **Early exit pattern**: 403 response with `escalation-denied` status prevents wasted work (no standard approval email sent).
- Copilot can trace a feature across multiple files and explain the data flow without executing code.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

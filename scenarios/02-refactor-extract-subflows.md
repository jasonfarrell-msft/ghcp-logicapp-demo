# Scenario 02 — Refactor: extract sub-flows

**Goal:** Refactor a flat workflow into well-named `Scope` blocks for readability and testability — keeping the standalone JSON and the Bicep mirror in sync.

## Model guidance (fast demo flow)

- Use **VS Code Agent mode**.
- Prefer **GPT-4.1** with strict file targeting.
- Ask for "one combined diff across both files, minimal explanation, simplest valid approach."

## Prompt

> Open `infra/workflows/approval.workflow.json`. Refactor the workflow so that:
> 1. `InitializeVariable` actions stay at the **root** level of `actions`
>    (Logic Apps forbids `InitializeVariable` inside a `Scope`). Keep them
>    chained at the top.
> 2. The approval-email + switch logic is grouped into a `Scope` named
>    `RequestApproval`, with `runAfter` pointing to the last init variable.
> 3. The HTTP responses are grouped into a `Scope` named `Respond`, with
>    `runAfter` pointing to `RequestApproval`.
> 4. Update `runAfter` chains so the actions run in order.
> 5. Mirror the same change inside `infra/modules/logicApp.bicep` so the inline
>    definition stays in sync.
> Show me a single diff covering both files and explain the trade-offs.

## What changes
- Three root-level actions: the `InitializeVariable` chain, then `Scope: RequestApproval`, then `Scope: Respond`.
- Original child actions move inside the appropriate scope.
- `runAfter` chain becomes `Initialize` → `RequestApproval` → `Respond`.
- The same edits land in the inline definition inside `infra/modules/logicApp.bicep`.

## Verify

```bash
az bicep build --file infra/main.bicep
dotnet script scripts/deploy.csx -- --environment dev
dotnet script scripts/invoke.csx -- --environment dev --amount 100
```

✅ Expected: `HTTP 200 OK` with `"status":"auto-approved"`. Same input as baseline, cleaner shape. In the portal run history the scopes now appear as collapsible groups.

## Talking points
- Copilot understands Logic Apps `Scope` and `runAfter` semantics.
- It edits the JSON and the Bicep mirror **in sync** in one pass — a manual refactor would drift.
- ⚠️ **Consumption gotcha:** `InitializeVariable` is illegal inside a `Scope`. If Copilot nests them you get `InvalidVariableInitialization` at deploy time. Worth calling out — the prompt explicitly steers Copilot around this.
- ⚠️ **`Respond` scope note:** in practice Copilot will fold the `Response` actions into a single `RequestApproval` scope rather than extracting them into a separate `Respond` scope. HTTP `Response` actions must be in the same branch that decides the outcome (they can't converge from separate branches without a result-passing variable). If extracting a separate `Respond` scope is a hard requirement, add a follow-up prompt asking Copilot to use a `Set variable` action to capture the outcome first.
- `az bicep build` is the cheap validation gate before redeploying.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

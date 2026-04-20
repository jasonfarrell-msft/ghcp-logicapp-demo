# Scenario 01 — Refactor: extract sub-flows (PRIMARY)

**Goal:** Show how Copilot can safely refactor a flat workflow into well-named
`Scope` blocks, making the workflow easier to read, test, and extend.

## Demo loop
1. **Deploy baseline** (skip if already deployed):
   ```powershell
   ./scripts/deploy.ps1 -Environment dev
   ```
2. **Smoke test** (no connector auth required — exercises the auto-approved branch):
   ```powershell
   ./scripts/invoke.ps1 -Environment dev -Amount 100
   ```
   Expected: `HTTP 200 OK` with `"status":"auto-approved"`.

   *Optional — full approval path:* requires authorizing the Office 365
   connection once in the portal (Logic App → API connections →
   `con-office365-dev` → Edit API connection → Authorize). Then:
   ```powershell
   ./scripts/invoke.ps1 -Environment dev -Amount 2500
   ```
3. **Refactor** — give Copilot the prompt below.
4. **Validate** locally:
   ```powershell
   az bicep build --file infra/main.bicep
   ```
5. **Redeploy** the same RG — Bicep is idempotent, so this is an in-place
   update of the workflow definition:
   ```powershell
   ./scripts/deploy.ps1 -Environment dev
   ```
6. **Re-invoke** with the same command from step 2 — same input, same output,
   cleaner shape. Show the run history in the portal: scopes are now visible
   as collapsible groups.
7. **Reset** before the next demo: `./scripts/reset.ps1 -Environment dev`.

## Setup
The skeleton in `infra/workflows/approval.workflow.json` has all actions at the
root level. There is no grouping, no naming convention, and the connector call
sits next to the response logic.

## Prompt to give Copilot
> Open `infra/workflows/approval.workflow.json`. Refactor the workflow so that:
> 1. Variable initialization is grouped into a `Scope` named `Initialize`.
> 2. The approval-email + switch logic is grouped into a `Scope` named
>    `RequestApproval`.
> 3. The HTTP responses are grouped into a `Scope` named `Respond`.
> 4. Update `runAfter` chains so the scopes run in order.
> 5. Mirror the same change inside `infra/modules/logicApp.bicep` so the inline
>    definition stays in sync.
> Show me a single diff covering both files and explain the trade-offs.

## Talking points
- Copilot understands Logic Apps' `Scope` action and `runAfter` semantics.
- It keeps the JSON and Bicep mirror **in sync** in one pass — a manual
  refactor would risk drift.
- Reviewer can validate by running `az bicep build infra/main.bicep`.

## Expected outcome
- Three new `Scope` actions at the root of `actions`.
- Original child actions moved inside the appropriate scope.
- `runAfter` chain: `Initialize` → `RequestApproval` → `Respond`.

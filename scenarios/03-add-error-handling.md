# Scenario 03 — Add error handling

**Goal:** Make the workflow resilient to connector failures by adding retry
policies, a failure scope, and a dead-letter HTTP call.

## Demo loop
1. **Deploy baseline:** `./scripts/deploy.ps1 -Environment dev`.
2. **Force a failure first** (optional but powerful) — invoke the trigger
   *before* authorizing the Office 365 connection in the portal. The run
   fails ungracefully. This is your "before" picture.
3. **Refactor** with the prompt below.
4. **Validate:** `az bicep build --file infra/main.bicep`.
5. **Redeploy:** `./scripts/deploy.ps1 -Environment dev`.
6. **Re-invoke** without authorizing the connector — now the run hits
   `HandleFailure`, posts the dead-letter, and returns `502`. Show the
   `HandleFailure` scope in the portal run history.
7. **Reset:** `./scripts/reset.ps1 -Environment dev`.

## Setup
The skeleton has no retry configuration on `Send_approval_email` and no
`runAfter: failed` handler. A transient Office 365 connector failure would
crash the run and return nothing to the caller.

## Prompt to give Copilot
> In `infra/workflows/approval.workflow.json`:
> 1. Add a `retryPolicy` to `Send_approval_email` (exponential, 4 retries,
>    PT10S minimum interval).
> 2. Add a new `Scope` named `HandleFailure` that runs only when
>    `RequestApproval` (or `Send_approval_email` if scopes weren't introduced
>    yet) has `runAfter` status `Failed` or `TimedOut`.
> 3. Inside `HandleFailure`, POST to a placeholder dead-letter URL
>    `https://example.com/dead-letter` with the trigger body and the run id.
> 4. Add a final `Response` returning `502` so the caller sees the failure.
> Mirror the same changes in `infra/modules/logicApp.bicep`.

## Talking points
- Copilot correctly expresses `runAfter` with multiple statuses
  (`["Failed","TimedOut","Skipped"]`) — easy to get wrong by hand.
- `retryPolicy` JSON structure is fully generated.
- The dead-letter pattern transfers cleanly to other workflows.

## Expected outcome
- `Send_approval_email` has a `retryPolicy` of type `exponential`.
- New `HandleFailure` scope with proper `runAfter`.
- Dead-letter HTTP action + 502 response.

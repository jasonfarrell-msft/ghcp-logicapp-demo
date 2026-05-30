# Scenario 03 — Add error handling

**Goal:** Make the workflow resilient: retry policy on the connector call, a `HandleFailure` scope, a dead-letter POST, and a 502 response so callers see the failure.

## Prompt

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

## What changes
- `Send_approval_email` gains an exponential `retryPolicy`.
- New `HandleFailure` scope wired with `runAfter` on `["Failed","TimedOut"]`.
- Dead-letter HTTP POST + 502 `Response` action inside the failure scope.
- Same edits mirrored in the Bicep inline definition.

## Verify

```bash
az bicep build --file infra/main.bicep
dotnet script scripts/deploy.csx -- --environment dev
# Force a failure: invoke BEFORE the Office 365 connection is authorized
# (or temporarily de-authorize it in the portal).
dotnet script scripts/invoke.csx -- --environment dev --amount 2500
```

✅ Expected: `HTTP 502` (graceful) instead of an opaque crash. Portal run history shows `HandleFailure` executed and the dead-letter POST attempted.

## Talking points
- `runAfter` with multiple statuses (`["Failed","TimedOut","Skipped"]`) is easy to mistype by hand — Copilot gets it right.
- The `retryPolicy` JSON shape is fully generated.
- Dead-letter pattern transfers cleanly to other workflows.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

# Scenario 03 — Add error handling

**Goal:** Make the workflow resilient: retry policy on the connector call, a `HandleFailure` scope, a dead-letter POST, and a 502 response so callers see the failure.

## Model guidance (fast demo flow)

- Use **VS Code Agent mode**.
- Prefer **Claude Sonnet 4.5**.
- VSCode Agent Mode will use all files in the solution for context.
- Ask for "single diff only, simplest valid approach" so deployment verification can start quickly.

## Prompt

> Update the workflow to:
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
```

### Timeout / error-handling demo

The `RequestApproval` scope has `limit.timeout = PT1M`. Submit a request
**above** the threshold and simply do not click Approve or Reject in the email:

```bash
dotnet script scripts/invoke.csx -- --environment dev --amount 2500 --timeout 90
```

1. The approval email is sent.
2. After ~60 s with no response, `RequestApproval` times out.
3. `HandleFailure` fires: dead-letter POST is attempted, then `Respond_502`.
4. Your terminal receives **`HTTP 502`** before the 90 s connection limit.

✅ Expected: `HTTP 502` `{ "status": "failed", "runId": "..." }` — graceful failure instead of an opaque crash. Portal run history shows `HandleFailure` executed and `Post_to_dead_letter` attempted.

## Talking points
- `runAfter` with multiple statuses (`["Failed","TimedOut","Skipped"]`) is easy to mistype by hand — Copilot gets it right.
- The `retryPolicy` JSON shape is fully generated.
- Dead-letter pattern transfers cleanly to other workflows.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

# Scenario 03 — Add error handling

**Goal:** Make the workflow resilient: retry policy on the connector call, a `HandleFailure` scope, a dead-letter POST, and a 502 response so callers see the failure.

## Model guidance (fast demo flow)

- Use **VS Code Agent mode**.
- Prefer **Claude Sonnet 4.6 or higher** with strict file targeting.
- Ask for "single diff only, simplest valid approach" so deployment verification can start quickly.

## Prompt

> In `infra/workflows/approval.workflow.json`:
> 1. Add a `retryPolicy` to `Send_approval_email` (exponential, 4 retries,
>    PT10S minimum interval).
> 2. Add a `limit.timeout` of `PT1M` to `Send_approval_email` so the webhook
>    wait fails deterministically when no approver responds. Do **not** put
>    `limit` on the `RequestApproval` scope — the Logic Apps schema rejects
>    `limit` on `Scope` actions (`PropertyNotAllowed`); it is only valid on
>    action types like `ApiConnectionWebhook`, `Http`, and `Until`.
> 3. Add a new `Scope` named `HandleFailure` that runs only when
>    `RequestApproval` has `runAfter` status `Failed` or `TimedOut`. When
>    `Send_approval_email` times out the webhook action fails, which fails
>    the enclosing scope, which satisfies the `Failed` condition.
> 4. Inside `HandleFailure`, POST to a placeholder dead-letter URL
>    `https://example.com/dead-letter` with the trigger body and the run id.
> 5. Add a final `Response` returning `502` so the caller sees the failure.
>    Make it run after `Post_to_dead_letter` on both `Succeeded` and `Failed`
>    so the caller always receives the 502 even if the dead-letter POST fails.
> Mirror the same changes in `infra/modules/logicApp.bicep`.

## What changes
- `Send_approval_email` gains an exponential `retryPolicy` and a `limit.timeout` of `PT1M`.
- New `HandleFailure` scope wired with `runAfter` on `["Failed","TimedOut"]`.
- Dead-letter HTTP POST + 502 `Response` action inside the failure scope.
- Same edits mirrored in the Bicep inline definition.

## Verify

```bash
az bicep build --file infra/main.bicep
dotnet script scripts/deploy.csx -- --environment dev
```

### Timeout / error-handling demo

`Send_approval_email` has `limit.timeout = PT1M`. Submit a request **above**
the threshold and simply do not click Approve or Reject in the email:

```bash
dotnet script scripts/invoke.csx -- --environment dev --amount 2500 --timeout 90
```

1. The approval email is sent.
2. After ~60 s with no response, the webhook wait times out and
   `Send_approval_email` fails, which fails the `RequestApproval` scope.
3. `HandleFailure` fires: dead-letter POST is attempted, then `Respond_502`.
4. Your terminal receives **`HTTP 502`** before the 90 s connection limit.

✅ Expected: `HTTP 502` `{ "status": "failed", "runId": "..." }` — graceful failure instead of an opaque crash. Portal run history shows `HandleFailure` executed and `Post_to_dead_letter` attempted.

## Talking points
- `runAfter` with multiple statuses (`["Failed","TimedOut","Skipped"]`) is easy to mistype by hand — Copilot gets it right.
- The `retryPolicy` JSON shape is fully generated.
- `limit.timeout` belongs on the long-running action (`ApiConnectionWebhook`, `Http`, `Until`), **not** on a `Scope` — a common Logic Apps schema gotcha the prompt calls out explicitly to avoid a `PropertyNotAllowed` deployment failure.
- Dead-letter pattern transfers cleanly to other workflows.

---
**Redeploy:** `dotnet script scripts/deploy.csx -- --environment dev` (then re-run Verify).

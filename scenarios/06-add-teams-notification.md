# Scenario 06 — Add Teams notification

**Goal:** Wire a new connector end-to-end — `$connections` in the workflow, the parameters mapping in Bicep, and the action body — to post an adaptive card on approval.

## Prompt

> Add a Teams notification step to the approval workflow. After
> `Respond_approved` succeeds, post an adaptive card to a Teams channel using
> the existing `teamsConnection` declared in `infra/modules/logicApp.bicep`.
> The card should show the requester, amount, description, and request id, and
> link back to the run. Add `teamsChannelId` and `teamsGroupId` as Bicep
> parameters. Update both `infra/workflows/approval.workflow.json` and the
> inline definition in the Bicep module, and add the `teams` entry to the
> `$connections` workflow parameter so the connector binds correctly.

## What changes
- New action `Post_adaptive_card_to_Teams` after `Respond_approved`.
- `$connections` gains a `teams` mapping in both the workflow JSON and the Bicep `parameters` block.
- New Bicep params `teamsChannelId` / `teamsGroupId` plumbed through `main.bicep` and the parameter files.

## Verify

```powershell
az bicep build --file infra/main.bicep
./scripts/deploy.ps1 -Environment dev
# One-time: portal → Logic App → API connections → teams → Authorize.
./scripts/invoke.ps1 -Environment dev -Amount 2500
```

✅ Approve via the email — adaptive card appears in the configured Teams channel.

## Talking points
- Wiring a new connector touches three places that must agree (`$connections`, workflow `parameters`, action body); easy to forget one by hand.
- Copilot knows the Teams connector's "Post adaptive card" action shape.
- Adaptive card JSON is verbose — Copilot generates a working baseline you can iterate on.

---
**Redeploy:** `./scripts/deploy.ps1 -Environment dev` (then re-run Verify).

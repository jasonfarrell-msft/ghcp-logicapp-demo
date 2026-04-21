# Scenario 06 — Add Teams notification

**Goal:** On approval, post an adaptive card to a Microsoft Teams channel.
Demonstrates Copilot wiring up a new connector end-to-end.

## Demo loop
1. **Deploy baseline:** `./scripts/deploy.ps1 -Environment dev`.
2. **Invoke** and approve via email — confirm: caller gets a response, but
   no Teams notification.
3. **Refactor** with the prompt below.
4. **Validate:** `az bicep build --file infra/main.bicep`.
5. **Redeploy:** `./scripts/deploy.ps1 -Environment dev`. **Authorize the
   Teams connection in the portal** before invoking (one-time per env).
6. **Re-invoke and approve** — adaptive card now appears in the configured
   Teams channel.
7. **Reset:** `./scripts/reset.ps1 -Environment dev`.

## Setup
The Bicep module already declares a `teamsConnection` resource, but the
workflow never uses it.

## Prompt to give Copilot
> Add a Teams notification step to the approval workflow. After
> `Respond_approved` succeeds, post an adaptive card to a Teams channel using
> the existing `teamsConnection` declared in `infra/modules/logicApp.bicep`.
> The card should show the requester, amount, description, and request id, and
> link back to the run. Add `teamsChannelId` and `teamsGroupId` as Bicep
> parameters. Update both `infra/workflows/approval.workflow.json` and the
> inline definition in the Bicep module, and add the `teams` entry to the
> `$connections` workflow parameter so the connector binds correctly.

## Talking points
- Copilot knows the Teams connector's `Post adaptive card in a chat or
  channel` action shape.
- Wiring a new connector touches: `$connections` parameter, the workflow
  `parameters` value mapping in Bicep, and the action body — three places
  that must agree, easy to forget one.
- Adaptive card JSON is verbose; Copilot generates a working baseline.

## Expected outcome
- New action `Post_adaptive_card_to_Teams` after `Respond_approved`.
- `$connections` includes `teams` mapping in the workflow definition and the
  Bicep `parameters` block.
- New Bicep params `teamsChannelId` / `teamsGroupId` plumbed through.

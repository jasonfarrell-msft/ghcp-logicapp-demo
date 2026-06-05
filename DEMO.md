# Demo Presenter Script

**Audience:** cloud architects, platform engineers, dev leads. Familiar with
Azure CLI and Bicep, not necessarily Logic Apps experts.

**Length:** 30–45 minutes.

**Format:** live coding with GitHub Copilot CLI / Chat.

## The story

Logic Apps are powerful but their definitions are JSON-heavy, schema-bound,
and split across three places that must stay in agreement: the workflow
definition, the IaC that deploys it, and the connection wiring. Today we
show how **GitHub Copilot turns Logic App authoring and maintenance from
JSON-wrangling into intent-driven editing**, keeping Bicep, workflow JSON,
and connection bindings in sync in a single pass. We start by asking Copilot
to *explain* what we already have, then evolve it through six concrete
refactors that each touch multiple files — and the climax is the
Consumption → Standard migration, followed by a beat that shows off a
capability Standard has that Consumption simply cannot match.

## The narrative arc

The scenarios are numbered in presentation order — `01` through `06`.

| Beat | Scenario | Why this slot |
|---|---|---|
| 1 | **01 Explain & document** | Open with comprehension. Sets context, surfaces issues that motivate scenarios 03 / 04. |
| 2 | **02 Refactor: extract sub-flows** | First real edit. Shows Copilot navigates Logic Apps schema (`Scope`, `runAfter`) and the Bicep mirror in one pass. |
| 3 | **03 Add error handling** | Harden the workflow. Strong "before/after" failure-mode story. |
| 4 | **04 Add an escalation tier** | Business-rule change. Cross-file feature spanning workflow + 4 Bicep files, with all four outcomes visible in the console — no UI side effects needed. |
| 5 | **05 Migrate Consumption → Standard** | **Climax.** The single biggest cross-cutting change Copilot can do for you here — different resources, different layout, different connection model — preserving the escalation logic from Beat 4 byte-for-byte. |
| 6 | **06 Externalize config to App Settings** | **Landing beat.** Shows a Standard-exclusive capability: business rules in App Settings instead of code. Portal proof: threshold is gone from the JSON, visible in Environment variables. |

## Pre-flight checklist (do this BEFORE the audience walks in)

Run these once. They survive across the whole demo.

- [ ] Install the VS Code lab extensions from the README or accept the
  workspace recommendations: GitHub Copilot, Copilot Chat, Bicep, Azure
  Logic Apps Standard, REST Client, and **Markdown Preview Enhanced** (for Mermaid diagrams; see below).

**Mermaid diagrams: For reliable rendering and export, use the [Markdown Preview Enhanced](https://marketplace.visualstudio.com/items?itemName=shd101wyy.markdown-preview-enhanced) VS Code extension.**

- The built-in Mermaid support in VS Code (v1.121+) is sometimes unreliable for complex diagrams or export.
- Markdown Preview Enhanced provides robust Mermaid rendering, export to PNG/SVG/PDF, and works well for live demos.
- To use: Open your markdown file, then run `Markdown Preview Enhanced: Open Preview to the Side` from the Command Palette (⇧⌘P).

---

- [ ] `az login` and `az account set --subscription <id>`
- [ ] `az bicep install` (or upgrade) — `az bicep version` to confirm
- [ ] `dotnet --version` confirms the .NET 8 SDK (or newer) is on PATH
- [ ] `dotnet tool install -g dotnet-script` (one-time; skip if already installed)
- [ ] Baseline deployed:
  ```bash
  az bicep build --file infra/main.bicep
  dotnet script scripts/deploy.csx -- --environment dev
  ```
- [ ] **Authorize the deployed API connection before running invoke**:
  Logic App `la-approval-dev` → API connections → `con-office365-dev` →
  **Edit API connection** → **Authorize**.
- [ ] Smoke test passes (confirms the trigger and low-amount path):
  ```bash
  dotnet script scripts/invoke.csx -- --environment dev --amount 100
  ```
  ✅ Expect `HTTP 200 OK` with `"status":"auto-approved"`.
- [ ] Full approval path passes:
  ```bash
  dotnet script scripts/invoke.csx -- --environment dev --amount 2500
  ```
  ✅ Expect an approval email; clicking **Approve** returns `HTTP 200 OK`
  with `"status":"approved"`.
- [ ] Open these files in the editor before you begin:
  `infra/workflows/approval.workflow.json`,
  `infra/modules/logicApp.bicep`,
  `infra/parameters/dev.bicepparam`. The "keep them in sync" angle is one
  of Copilot's strongest stories.
- [ ] Open the deployed Logic App in the Azure portal in a side tab.
- [ ] In Copilot, pick your model/mode before starting:
  - Use **VS Code Agent mode** (no Chat mode).
  - **Beats 1–4 and 6:** Claude **Sonnet 4.6 or higher**.
  - **Beat 5 (migration):** Claude **Opus 4.6 or higher**. Sonnet drifts on
    the nested Bicep + V2 connection + `connections.json` schema during the
    migration. Opus is the right tool for the migration — switch deliberately,
    then switch back to Sonnet for Beat 6.
  - Ask for "single diff only" to keep responses short during live narration.
  - Add "use the simplest valid approach, no alternatives" to keep reasoning light.

## Per-scenario beats

For each beat: open the scenario file, paste its **Prompt** verbatim into
Copilot, accept the diff, then run **Verify** from the scenario doc. The
narration below is what to say out loud as you do it.

**Model reminder:** if Copilot starts multi-step back-and-forth, tighten the next prompt with exact files/output and "simplest valid approach."

### Beat 1 — Scenario 01: Explain & document

> **Say this:** "Before we change anything, let's see what we have. This is
> what most engineers actually do on day one with an unfamiliar Logic App —
> and it usually means clicking around the designer. Let's ask Copilot
> instead."

Open [`scenarios/01-explain-and-document.md`](./scenarios/01-explain-and-document.md).
Paste both prompts. Show the generated `docs/approval-workflow.md` next to
the portal designer.

**Why this beat matters:** sets context, builds trust that Copilot reads
the workflow correctly, and the follow-up "review for issues" prompt
naturally cues scenarios 03 and 04.

### Beat 2 — Scenario 02: Refactor: extract sub-flows

> **Say this:** "Copilot identified gaps. Let's start by giving the workflow
> some structure — group actions into named scopes, the way we'd want them
> in code review. Watch how it edits both the standalone JSON and the Bicep
> mirror in a single pass."

Open [`scenarios/02-refactor-extract-subflows.md`](./scenarios/02-refactor-extract-subflows.md).

**Why this beat matters:** first real edit. Shows schema literacy
(`Scope`/`runAfter`) and the mirror-file consistency story. Highlight the
**`InitializeVariable` Consumption gotcha** out loud — it's the kind of
subtle rule that bites people, and it's why the prompt explicitly asks
Copilot to keep init variables at the root.

### Beat 3 — Scenario 03: Add error handling

> **Say this:** "Right now if the connector hiccups, the caller gets nothing
> useful. Let's add a retry policy, a failure scope, and a dead-letter
> handler — then deliberately break the connector to show the new path."

Open [`scenarios/03-add-error-handling.md`](./scenarios/03-add-error-handling.md).
For the strongest "before" picture, **temporarily de-authorize** the Office
365 connection in the portal. Redeploy the new version and re-invoke to show
the graceful 502.

**Why this beat matters:** `runAfter: ["Failed","TimedOut","Skipped"]` and
`retryPolicy` JSON shape are the kinds of things people get subtly wrong by
hand. The dead-letter pattern transfers to other workflows.

### Beat 4 — Scenario 04: Add an escalation tier (business-rule change)

> **Say this:** "The CFO wants a sign-off above $10K. I shouldn't need to
> know Logic Apps internals to ship that. Watch one feature request ripple
> across `main.bicep`, the module, both `.bicepparam` files, and the
> workflow JSON in a single pass — and watch the console as the request
> moves through both tiers."

Open [`scenarios/04-add-escalation-branch.md`](./scenarios/04-add-escalation-branch.md).
After Copilot lands the change, demo all three amounts to prove every
outcome is reachable. Read the `responseStatus` out loud each time so the
audience sees the state machine via the console:

```bash
dotnet script scripts/invoke.csx -- --environment dev --amount 250    # auto-approved
dotnet script scripts/invoke.csx -- --environment dev --amount 2500   # approved or rejected
dotnet script scripts/invoke.csx -- --environment dev --amount 15000  # escalation-denied (or falls through)
```

**Why this beat matters:** demonstrates cross-file IaC orchestration without
needing any new connectors. One business rule, five files, four reachable
outcomes — all observable from the terminal. This sets up Beat 5: the
escalation logic carries over to Standard byte-for-byte.

### Beat 5 — Scenario 05: Migrate Consumption → Standard

> **Say this:** "We've been editing in place. Now the big one — migrate the
> whole thing to Logic Apps Standard, side-by-side. Different Azure
> resources, different on-disk layout, different connection model. And
> the escalation logic we just wrote? It needs to come along
> byte-for-byte. Hours by hand. Minutes with Copilot — but only if you
> use the right tool. I'm switching to Opus for this one."

**Switch the model now:** Agent mode → Claude Opus 4.6+.

Open [`scenarios/05-migrate-consumption-to-standard.md`](./scenarios/05-migrate-consumption-to-standard.md).

**After this beat, switch back to Sonnet** — Beat 6 is a targeted edit.
This scenario is **two prompts**, not five: generate the migration plan,
review it, paste it back as the implementation brief. Walk the audience
through the plan when it lands — this is the moment where they see Copilot
holding the entire migration in mind. Then accept the implementation diff,
deploy, click **Authorize** in the portal for the new V2 connection, and
re-run all three amounts against the Standard app.

Open both Logic Apps in the portal at the end to compare side-by-side.

**Why this beat matters:** the migration is the widest refactor Copilot can do here. Plan-first with Opus is the pattern they should take home.

### Beat 6 — Scenario 06: Externalize config to App Settings (landing beat)

> **Say this:** "We migrated. But look at the workflow JSON — the threshold and approver email are still hard-coded. In Logic Apps Standard, App Settings are first-class citizens in workflow expressions. Let's move those values out of the code and into the hosting environment. After this, a business analyst can change the approval threshold in the portal without touching a single file."

**Switch the model back:** Agent mode → Claude Sonnet 4.6+.

Open [`scenarios/06-externalize-config-to-appsettings.md`](./scenarios/06-externalize-config-to-appsettings.md).
This is a single prompt — Sonnet handles it cleanly. After the redeploy, navigate to Portal → `la-approval-std-dev` → **Settings** → **Environment variables** and show `ThresholdAmount`, `ApproverEmail`, etc. in the list. Open the generated `standard/Approval/workflow.json` side-by-side to show the values are *gone* from the code.

**Why this beat matters:** it answers "what do I get from Standard beyond scalability?" with something concrete and immediately understandable to a non-engineer. The portal proof — values in App Settings, nothing in the JSON — is a memorable close. It also rounds out the story: Beat 1 was comprehension, Beat 5 was the big lift, Beat 6 is the payoff.

## Between scenarios

Bicep is idempotent — every Beat 1–4 redeploy is
`dotnet script scripts/deploy.csx -- --environment dev` against the same RG.
Beats 5 and 6 redeploy with `dotnet script scripts/deploy-standard.csx -- --environment dev`.
The trigger URLs stay the same within each app.

⚠️ **Do not run `scripts/reset.csx` between scenarios.** It deletes the
resource group and re-authorizing the Office 365 connection is a portal
click you don't want to do live. Reset is for end-of-demo cleanup only. If
you must roll back code mid-demo, use `git restore .` and then redeploy.

## If something breaks

The failures most likely to hit you live, in order of probability:

### 1. `HTTP 502` on an above-threshold invoke

**Cause:** the Office 365 connection isn't authorized.

**Fix:** Portal → Logic App → API connections → `con-office365-dev`
(or `con-office365-std-dev` for Standard) → **Edit API connection** →
**Authorize**. Or fall back to `--amount 100` to skip the connector
entirely and keep the demo moving.

`invoke.csx` and `invoke-standard.csx` both print this hint on a 502.

### 2. `InvalidVariableInitialization` on `scripts/deploy.csx` (Consumption)

**Cause:** Copilot moved an `InitializeVariable` action inside a `Scope`.
Consumption Logic Apps forbid this.

**Fix:** tell Copilot:

> `InitializeVariable` actions are not allowed inside a `Scope` in
> Consumption Logic Apps. Move them back to the root `actions` and chain
> them with `runAfter`.

Re-run `az bicep build --file infra/main.bicep` then redeploy.

### 3. `HTTP 401` from a manually-pasted trigger URL

**Cause:** unquoted `&` in PowerShell (or bash without quoting) silently
truncates the URL.

**Fix:** don't pass `--trigger-url` at all — let `invoke.csx` fetch it. If
you must, single-quote the URL.

### 4. Beat 5 deploy fails with `InternalSubscriptionIsOverQuotaForSku`

**Cause:** the subscription has zero `WorkflowStandard` (WS1) VM quota.
Standard Logic Apps run on a dedicated App Service Plan and consume quota
even at 0 instances.

**Fix:** request a quota increase via
**Portal → Subscriptions → Usage + Quotas → WorkflowStandard** or use a
different subscription. Until quota is available, treat Beat 5 as a
**code walk-through** — the Bicep is valid and all files are in place;
only the Azure provisioning step is blocked.

### 5. Beat 5 first invoke returns `403 missing connection ACL`

**Cause:** the V2 connection (`con-office365-std-dev`) is created Unauthorized. OAuth consent cannot be
automated.

**Fix:** open the portal URL the deploy script printed → **Authorize** →
**Save** → re-run the deploy script (it will detect the change and restart
the app) → re-invoke.

### 6. `az deployment sub create` prints `ERROR: The content for this response was already consumed`

**Cause:** known bug in Azure CLI 2.74 where `--query` combined with a
deployment that produces structured output can consume the response body
before the error reporter can read it. The underlying error (e.g. quota)
is still surfaced in the exit code and in `az deployment operation sub list`.

**Fix:** omit `--query` / `-o json` and let `deploy.csx` print the full
ARM response, or use `az deployment sub what-if` first to see quota errors
before the actual deploy.

## Cleanup (after the demo)

```bash
dotnet script scripts/reset.csx -- --environment dev
```

Restores the working tree and deletes `rg-ghcp-logicapp-dev`. If you ran
`prod` in Beat 5, repeat with `--environment prod`.

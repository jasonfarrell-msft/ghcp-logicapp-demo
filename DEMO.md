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
refactors that each touch multiple files — and finish with the high-stakes
one: a full Consumption → Standard migration.

## The narrative arc

The scenarios are numbered in presentation order — `01` through `06` — starting
where a real engineer would: "what is this thing?"

| Beat | Scenario | Why this slot |
|---|---|---|
| 1 | **01 Explain & document** | Open with comprehension. Sets context, surfaces issues that motivate scenarios 03 / 04. |
| 2 | **02 Refactor: extract sub-flows** | First real edit. Shows Copilot navigates Logic Apps schema (`Scope`, `runAfter`) and the Bicep mirror in one pass. |
| 3 | **03 Add error handling** | Harden the workflow. Strong "before/after" failure-mode story. |
| 4 | **04 Teams notification** | New connector end-to-end — `$connections`, Bicep mapping, action body. Demonstrates breadth, not just depth. |
| 5 | **05 Escalation branch** | Explore existing two-tier approval across five files. Shows Copilot can trace and explain cross-file features, not just build them. |
| 6 | **06 Migrate Consumption → Standard** | Finale. The single biggest cross-cutting change Copilot can do for you here. |

If short on time, cut **05** first (closest in shape to **04**).

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
- [ ] **Authorize deployed API connections before running invoke**:
  Logic App `la-approval-dev` → API connections → authorize each deployed
  connection that the workflow uses. For the baseline, authorize:
  - [ ] `con-office365-dev` via **Edit API connection** → **Authorize**
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
- [ ] (Scenario 04 only) After deploying Teams, authorize `con-teams-dev`
  before re-running `invoke.csx`.
- [ ] Open these files in the editor before you begin:
  `infra/workflows/approval.workflow.json`,
  `infra/modules/logicApp.bicep`,
  `infra/parameters/dev.bicepparam`. The "keep them in sync" angle is one
  of Copilot's strongest stories.
- [ ] Open the deployed Logic App in the Azure portal in a side tab.
- [ ] In Copilot, pick your model/mode before starting:
  - Use **VS Code Agent mode** (no Chat mode).
  - **All scenarios (01–06):** use **Claude Sonnet** throughout the demo.
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
Paste both prompts. Show `docs/approval-workflow.md` next to the portal
designer.

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

### Beat 4 — Scenario 04: Teams notification

> **Say this:** "New connector end-to-end. Wiring one in touches three
> places that must agree — `$connections` in the workflow, the parameters
> mapping in Bicep, and the action body. Watch Copilot do all three."

Open [`scenarios/04-add-teams-notification.md`](./scenarios/04-add-teams-notification.md).
Make sure the Teams connection is authorized before you re-invoke.

**Why this beat matters:** breadth. Shows the cross-file consistency story
generalises to a different connector with a more verbose payload (the
adaptive card).

### Beat 5 — Scenario 05: Add escalation branch

> **Say this:** "Almost there. Let's add a second approver tier for really
> high-value requests — above 10K goes to escalation@contoso.com first. If
> they reject, we stop. If they approve, we continue to the standard
> approver. This is the cross-file coordination story at its best — five
> files that all have to agree."

Open [`scenarios/05-add-escalation-branch.md`](./scenarios/05-add-escalation-branch.md).
After Copilot adds the escalation logic, demo all three amounts (`250`,
`2500`, `15000`) to prove all three branches work.

**Why this beat matters:** demonstrates cross-file IaC orchestration. One
feature request touches 5 files (workflow JSON, Bicep module, main.bicep,
dev.bicepparam, prod.bicepparam) with environment-specific thresholds. The
three test amounts prove the three-tier conditional logic (auto-approve <
threshold, standard approval < escalation, escalation approval ≥ escalation)
works correctly.

### Beat 6 — Scenario 06: Migrate Consumption → Standard (finale)

> **Say this:** "We've been editing in place. Now the big one — migrate the
> whole thing to Logic Apps Standard, side-by-side. Different Azure
> resources, different on-disk layout, different connection model, and
> local dev unlocked. Hours by hand. Minutes with Copilot."

Open [`scenarios/06-migrate-consumption-to-standard.md`](./scenarios/06-migrate-consumption-to-standard.md).
Walk the five sub-prompts. Deploy with the new `scripts/deploy-standard.csx`
that Copilot generates. Open both Logic Apps in the portal to compare.

**Why this beat matters:** this is the demo people will remember. It's the
change Copilot is most differentiated at — a wide refactor across
heterogeneous files where the ground truth lives in product docs, not in
the repo.

## Between scenarios

Bicep is idempotent — every scenario's redeploy is
`dotnet script scripts/deploy.csx -- --environment dev` against the same RG.
The trigger URL stays the same.

⚠️ **Do not run `scripts/reset.csx` between scenarios.** It deletes the
resource group and re-authorizing the Office 365 connection is a portal
click you don't want to do live. Reset is for end-of-demo cleanup only. If
you must roll back code mid-demo, use `git restore .` and then
`dotnet script scripts/deploy.csx -- --environment dev`.

## If something breaks

The three failures most likely to hit you live, in order of probability:

### 1. `HTTP 502` on an above-threshold invoke

**Cause:** the Office 365 connection isn't authorized.

**Fix:** Portal → Logic App → API connections → `con-office365-dev` →
**Edit API connection** → **Authorize**. Or fall back to
`dotnet script scripts/invoke.csx -- --environment dev --amount 100` to skip
the connector entirely and keep the demo moving.

`invoke.csx` already prints this hint on a 502.

### 2. `InvalidVariableInitialization` on `scripts/deploy.csx`

**Cause:** Copilot moved an `InitializeVariable` action inside a `Scope`.
Consumption Logic Apps forbid this.

**Fix:** tell Copilot:

> `InitializeVariable` actions are not allowed inside a `Scope` in
> Consumption Logic Apps. Move them back to the root `actions` and chain
> them with `runAfter`.

Re-run `az bicep build --file infra/main.bicep` then redeploy. This is
scenario 02's documented gotcha — keep it as a teaching moment.

### 3. `HTTP 401` from a manually-pasted trigger URL

**Cause:** unquoted `&` in PowerShell (or bash without quoting) silently
truncates the URL.

**Fix:** don't pass `--trigger-url` at all — let `invoke.csx` fetch it. If
you must, single-quote the URL:

```bash
dotnet script scripts/invoke.csx -- --trigger-url 'https://...&sig=...'
```

`invoke.csx` warns when the `sig=` parameter is missing.

### 4. Teams notification doesn't appear (Scenario 04)

**Cause:** Teams connection not authorized OR channel/group IDs not configured.

**Fix:**
- Portal → Logic App → API connections → `con-teams-dev` → **Edit API connection** → **Authorize**
- Verify `infra/parameters/dev.bicepparam` has actual Teams IDs (not `YOUR_CHANNEL_ID_HERE`)
- See README.md \"One-time setup\" section for instructions to get IDs from Teams
- Redeploy after updating IDs: `dotnet script scripts/deploy.csx -- --environment dev`

### 5. Scenario 06 deploy fails with `InternalSubscriptionIsOverQuotaForSku`

**Cause:** the subscription has zero `WorkflowStandard` (WS1) VM quota.
Standard Logic Apps run on a dedicated App Service Plan and consume quota
even at 0 instances.

**Fix:** request a quota increase via
**Portal → Subscriptions → Usage + Quotas → WorkflowStandard** or use a
different subscription. Until quota is available, treat scenario 06 as a
**code walk-through** — the Bicep is valid and all files are in place; only
the Azure provisioning step is blocked.

### 5. `az deployment sub create` prints `ERROR: The content for this response was already consumed`

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
`prod` in scenario 05, repeat with `--environment prod`.

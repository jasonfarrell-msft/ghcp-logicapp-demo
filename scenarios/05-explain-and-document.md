# Scenario 05 — Explain & document

**Goal:** Generate human-readable documentation and a Mermaid diagram from the
workflow JSON — the kind of asset that's painful to maintain manually.

## Demo loop
This scenario doesn't change deployed behavior — it produces docs. The
"deploy" step still matters: it grounds the audience in what actually exists
in Azure before Copilot describes it.

1. **Deploy baseline** (or confirm already deployed):
   `./scripts/deploy.ps1 -Environment dev`.
2. **Show the deployed workflow** in the portal — designer view, point out
   how much clicking it takes to "understand" it.
3. **Generate docs** using the prompts below.
4. **Open `docs/approval-workflow.md`** side-by-side with the portal — same
   information, version-controlled, reviewable in a PR.
5. **No redeploy needed.** Reset with `./scripts/reset.ps1 -Environment dev`
   before the next scenario.

## Setup
No documentation exists for the approval workflow.

## Prompts to give Copilot
> Read `infra/workflows/approval.workflow.json` and produce:
> 1. A concise plain-English summary (max 8 bullets) of what the workflow
>    does, including trigger, branches, and outputs.
> 2. A Mermaid `flowchart TD` diagram showing the actions and their
>    `runAfter` relationships, with conditions on the edges.
> 3. A markdown table of every action with columns: `Name`, `Type`,
>    `Depends on`, `Notes`.
> Save the result to `docs/approval-workflow.md`.

Follow-up prompt:
> Now review the workflow for issues: missing error handling, hard-coded
> values, missing retries, opportunities for parallelism, security concerns
> with the trigger SAS. List findings with severity (high/medium/low) and a
> one-line fix for each.

## Talking points
- Copilot turns opaque JSON into reviewable docs in seconds.
- The "review for issues" prompt seeds the next demo (scenario 03 / 06).
- Mermaid renders inline in GitHub — no extra tooling.

## Expected outcome
- New `docs/approval-workflow.md` with summary, diagram, action table.
- Inline issue list in the chat with prioritized fixes.

# Scenario 01 — Explain & document

**Goal:** Generate a plain-English summary, a Mermaid diagram, and an action table from the workflow JSON — the kind of artefact teams rarely keep current by hand.

## Model guidance (fast demo flow)

- Use **VS Code Agent mode**.
- Prefer **Claude Sonnet 4.5** with a constrained prompt (exact file + exact output).
- Ask for "single markdown output only, simplest valid approach."

## Prompt

> Read `infra/workflows/approval.workflow.json` and produce:
> 1. A concise plain-English summary (max 8 bullets) of what the workflow
>    does, including trigger, branches, and outputs.
> 2. A Mermaid `flowchart TD` diagram showing the actions and their
>    `runAfter` relationships, with conditions on the edges.
> 3. A markdown table of every action with columns: `Name`, `Type`,
>    `Depends on`, `Notes`.
> Save the result to `docs/approval-workflow.md`.

**Follow-up prompt** (seeds the rest of the demo arc):

> Now review the workflow for issues: missing error handling, hard-coded
> values, missing retries, opportunities for parallelism, security concerns
> with the trigger SAS. List findings with severity (high/medium/low) and a
> one-line fix for each.

## What changes
- New `docs/approval-workflow.md` with summary + Mermaid + action table.
- Inline issue list in chat — naturally previews scenarios 02, 03, and 06.
- No deployment change.

## Verify

Open `docs/approval-workflow.md` side-by-side with the portal designer view.
Same information, version-controlled, reviewable in a PR. Mermaid renders
inline on GitHub, and in VS Code when the recommended Markdown Preview
Mermaid Support extension is installed.

## Talking points
- Opaque JSON → reviewable docs in seconds.
- The "review for issues" prompt seeds the rest of the demo arc.
- Mermaid renders inline on GitHub and in the VS Code Markdown preview once the lab extensions are installed.

---
**Redeploy:** not needed (this scenario is docs-only).

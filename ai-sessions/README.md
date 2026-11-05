# AI session archive manifest

**Active Phase 2 records are separate:** [Phase 2 manifest](PHASE-2/README.md). The Phase 1 evidence, historical cutoffs and owner confirmation below are preserved. New Phase 2 exports/results are not implied by Phase 1 completion.

**Final owner confirmation:** real refund manually verified PASSED; all owner export/privacy/repository/reviewer checklist items confirmed complete. [Owner confirmation](OWNER-FINAL-CONFIRMATION.md). Historical snapshot cutoffs below remain accurate; subsequent native-export paths were not supplied to AI and are not invented.

This folder contains the available project records, grouped by provenance. A readable extraction, summary, prompt, report and original raw snapshot are different artifacts; none is silently labelled as a complete native export.

## Start with these records

| Record | What it contains | Completeness |
|---|---|---|
| [CHAT-REFERENCE-LOG.md](CHAT-REFERENCE-LOG.md) | Human-readable chronological decisions, implementation phases, reviews, issues and results | Curated reference summary, not a verbatim transcript |
| [codex-chat/conversation.md](codex-chat/conversation.md) | Visible user/assistant messages from this exact Codex chat, including planning, UI work, reviews, owner-pasted reports and final closeout request | Source-derived snapshot through the timestamp in [metadata](codex-chat/export-metadata.json); the final response generated afterward is necessarily outside it |
| [codex-chat/visible-messages.jsonl](codex-chat/visible-messages.jsonl) | Structured counterpart of those visible messages with source lines/timestamps | Same cutoff as the Markdown record |
| [BUILD-001/readable/conversation.md](BUILD-001/readable/conversation.md) | Readable user/assistant text from the supplied Claude raw snapshot | Ends with source records captured before the final Claude response; see [metadata](BUILD-001/readable/export-metadata.json) |
| [Claude raw JSONL](BUILD-001/raw/dadc0381-3554-5b4b-a537-d3fd6c9ebe70.jsonl) | Original, unmodified project-specific cloud Claude session file, with its tool-result subdirectory | Source timestamp range 9:08:08 p.m.–11:17:22 p.m. Eastern, Nov 1, 2026; a snapshot, not the final full session |
| [claude-reports/](claude-reports/) | The exact report attachments supplied by the owner and later owner-pasted reset/correction reports extracted from this chat | Provided reports, not independent native Claude exports |
| [UI-001/prompt.md](UI-001/prompt.md) / [report](UI-001/completion-report.md) | Exact UI brief and Codex implementation report | Supporting artifacts; full discussion is in the Codex snapshot |
| [archive-inventory.json](archive-inventory.json) | File sizes, SHA-256 hashes and credential-pattern scan results for archive records | Generated during closeout; does not hash itself or closeout metadata |
| [closeout-metadata.json](closeout-metadata.json) | Final capture timestamp, source-cutoff references and documentation validation | Closeout metadata, not a provider result |

Both readable exports exclude private reasoning, system/developer messages, tool arguments/results and binary image payloads. The structured tool-activity indexes retain tool names/types/IDs without payloads. Original requirement images, generated assets, verification screenshots and application files remain in the project. Credential-pattern redactions, if any, are marked and counted in export metadata. Zero matches is not proof of absence of every possible secret. Raw Claude data can contain the owner's account email and embedded assignment images; this is a private submission archive.

## Claude session provenance

The implementation used the cloud Claude session linked to this computer, titled BLEVINS PASS implementation, rather than a local Claude Code shell. The original source was that session's project-specific `~/.claude/projects/-home-claude/` JSONL. Its copied snapshot and tool result are preserved unchanged in BUILD-001/raw. The current local `%USERPROFILE%\.claude\projects` lookup found no additional project-brushfire-specific JSONL to add; unrelated projects were not collected.

The Claude snapshot covers BUILD-001, REVIEW-001 corrections and DEMO-RESET-001 through its copy time. The final 44-test response is not in that snapshot; it was pasted by the owner into this Codex chat and is preserved in the Codex extraction and report supplement. The source-derived readable Claude record is not called `/export` output.

The visible session menu was inspected during closeout: it offered session management/sharing/link actions, but no single-session native download/export was available there. No sharing action, account-wide export or message to Claude was performed. If a native export is available to the owner through their product, add it here and label its actual format/cutoff. For an actual local Claude Code session, retain `/export` output and the applicable project raw JSONL as the assignment requests. Do not fabricate a native export or copy unrelated sessions.

## Supporting prompts and reports

- [BUILD-001 launch](BUILD-001/prompt.md) references the full master prompt retained at the repository root.
- [BUILD-001 completion report](BUILD-001/completion-report.md) is historical and has later review addenda.
- [First correction prompt](BUILD-001/review-001-prompt.md).
- [Reset prompt supplied to Claude](BUILD-001/demo-reset-prompt.md).
- [Final reset review prompt](BUILD-001/demo-reset-review-prompt.md).
- Root handoff, approved plan, master/launch prompts, reset prompts and DESIGN_SYSTEM.md are retained as project context. They do not replace conversation transcripts.

## Historical native-export gaps and subsequent owner completion

At documentation closeout, the supplied raw Claude snapshot and derived text had explicit cutoffs, and native exports were not independently available to AI. The owner has since confirmed all export/submission checklist actions complete. This closes the owner action items by attestation without relabelling derived snapshots as native exports or inventing new file locations. See [completed submission checklist](../docs/SUBMISSION-CHECKLIST.md).

At the original closeout no real refund result had been provided. The subsequent [owner confirmation](OWNER-FINAL-CONFIRMATION.md) records the real refund as manually verified PASSED and all owner submission actions complete. AI did not execute additional refunds or Git operations.

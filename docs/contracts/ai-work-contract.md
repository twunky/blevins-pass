# AI work contract

- The owner launches one implementation session. TASK-001–008 are internal milestones the AI verifies and continues through without routine approval. Internal verification is not owner acceptance.
- The AI never runs Git commands, commits, pushes, or changes history, directly or through scripts. The owner handles the repository and reviewer invitations.
- No deployments, account changes, or extra agents. Secrets stay server-side and out of code, docs, output and archives.
- The AI never fabricates test results, payment success, exports, timings or token usage. Anything unverified is labelled unverified, and missing metrics are recorded as **Not available**.
- Session transcripts and raw session files are kept in `ai-sessions/`. Prompts and reports are supporting records, not transcript substitutes.
- Standards: [AGENTS.md](../../AGENTS.md). Build evidence: [BUILD-001](../tasks/BUILD-001.md).

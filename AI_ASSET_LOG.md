# AI-generated asset log

Generations are appended automatically by `Tools/ImageMCP/server.py` (also recorded in the git-ignored
`Tools/ImageMCP/ledger.jsonl` with token usage). Costs are conservative per-image estimates; the APIs return no price.

## Pipeline status (2026-11-18)
- Local MCP server `karman-images` (official `openai` 3.55 and `google-genai` 3.5 SDKs, MCP Python SDK 2.3),
  registered in the project's `.mcp.json` (keys passed through as `${OPENAI_API_KEY}` / `${GEMINI_API_KEY}`, never stored).
- Tools: `list_image_models`, `generate_image`, `edit_image`, `get_image_metadata`, `spending_status`; provider fallback
  OpenAI ⇄ Gemini on errors or rate limits.
- Verified with free model-listing calls: OpenAI `gpt-image-2.6-sunburst`, `gpt-image-2.6-flare` (also 2.5, 2, 1.5,
  1-mini); Gemini `gemini-3-pro-image`, `gemini-3.1-flash-image`, `gemini-3.1-flash-lite-image`, `gemini-2.5-flash-image`.
- Spending guard: generation is refused unless `KARMAN_IMAGE_BUDGET_USD` (owner-approved) covers the estimate.
  Current budget: **$0 — nothing generated, nothing spent.**

| Date | Provider | Model | Prompt | Size | Cost | Destination |
|---|---|---|---|---|---|---|

"""KÁRMÁN image-generation MCP server (OpenAI + Google Gemini), stdio transport.

Tools: list_image_models, generate_image, edit_image, get_image_metadata, spending_status.

Credentials come only from the environment (OPENAI_API_KEY, GEMINI_API_KEY); they are never logged or written.
Spending guard: every generate/edit call is refused unless KARMAN_IMAGE_BUDGET_USD is set above the running
estimated spend recorded in ledger.jsonl. Per-image cost is estimated conservatively (KARMAN_IMAGE_COST_ESTIMATE_USD,
default 0.30) because the APIs do not return a price; token usage is recorded when the provider reports it.
Every generation is appended to ledger.jsonl and to the project's AI_ASSET_LOG.md table.
"""
from __future__ import annotations

import base64
import datetime as dt
import io
import json
import os
from pathlib import Path

from mcp.server.mcpserver import MCPServer
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]          # repository root
LEDGER = Path(__file__).with_name("ledger.jsonl")
ASSET_LOG = ROOT / "AI_ASSET_LOG.md"
DEFAULT_OUT = ROOT / "Assets" / "Karman" / "Art" / "Generated"

mcp = MCPServer("karman-images")

OPENAI_SIZES = {"1:1": "1024x1024", "3:2": "1536x1024", "16:9": "1536x1024", "2:3": "1024x1536", "9:16": "1024x1536"}


def _budget() -> float:
    try:
        return float(os.environ.get("KARMAN_IMAGE_BUDGET_USD", "0"))
    except ValueError:
        return 0.0


def _estimate() -> float:
    try:
        return float(os.environ.get("KARMAN_IMAGE_COST_ESTIMATE_USD", "0.30"))
    except ValueError:
        return 0.30


def _spent() -> float:
    if not LEDGER.exists():
        return 0.0
    total = 0.0
    for line in LEDGER.read_text().splitlines():
        try:
            total += float(json.loads(line).get("estimated_cost_usd", 0))
        except (ValueError, json.JSONDecodeError):
            pass
    return total


def _guard(n: int = 1) -> None:
    budget, spent, est = _budget(), _spent(), _estimate() * n
    if spent + est > budget + 1e-9:
        raise RuntimeError(
            f"Spending guard: estimated ${spent:.2f} spent + ${est:.2f} for this call exceeds the authorised budget "
            f"${budget:.2f}. Set KARMAN_IMAGE_BUDGET_USD only after the project owner approves spending.")


def _out_path(destination: str | None, stem: str) -> Path:
    if destination:
        p = Path(destination)
        if not p.is_absolute():
            p = ROOT / p
        if p.suffix == "":
            p = p / f"{stem}.png"
    else:
        p = DEFAULT_OUT / f"{stem}.png"
    p.parent.mkdir(parents=True, exist_ok=True)
    return p


def _record(entry: dict) -> None:
    with LEDGER.open("a") as f:
        f.write(json.dumps(entry) + "\n")
    row = (f"| {entry['date']} | {entry['provider']} | {entry['model']} | {entry['prompt'].replace('|', '/')[:180]} | "
           f"{entry['width']}×{entry['height']} | ~${entry['estimated_cost_usd']:.2f} (est.) | {entry['path']} |\n")
    if ASSET_LOG.exists():
        with ASSET_LOG.open("a") as f:
            f.write(row)


def _save(img_bytes: bytes, path: Path) -> tuple[int, int]:
    im = Image.open(io.BytesIO(img_bytes))
    im.save(path, format="PNG")
    return im.size


def _openai_generate(model: str, prompt: str, aspect: str, quality: str, images: list[Path]) -> tuple[bytes, dict]:
    from openai import OpenAI
    client = OpenAI()
    size = OPENAI_SIZES.get(aspect, "auto")
    if images:
        files = [open(p, "rb") for p in images]
        try:
            r = client.images.edit(model=model, image=files, prompt=prompt, size=size, quality=quality)
        finally:
            for f in files:
                f.close()
    else:
        r = client.images.generate(model=model, prompt=prompt, size=size, quality=quality, n=1)
    usage = getattr(r, "usage", None)
    return base64.b64decode(r.data[0].b64_json), (usage.model_dump() if usage is not None and hasattr(usage, "model_dump") else {})


def _gemini_generate(model: str, prompt: str, aspect: str, resolution: str, images: list[Path]) -> tuple[bytes, dict]:
    from google import genai
    from google.genai import types
    client = genai.Client()
    contents: list = [prompt] + [Image.open(p) for p in images]
    try:
        cfg = types.GenerateContentConfig(response_modalities=["IMAGE"],
                                          image_config=types.ImageConfig(aspect_ratio=aspect, image_size=resolution))
    except Exception:  # older SDKs without image_config
        cfg = types.GenerateContentConfig(response_modalities=["IMAGE"])
    r = client.models.generate_content(model=model, contents=contents, config=cfg)
    for part in r.candidates[0].content.parts:
        if getattr(part, "inline_data", None) is not None and part.inline_data.data:
            usage = r.usage_metadata.model_dump() if getattr(r, "usage_metadata", None) is not None else {}
            return part.inline_data.data, usage
    raise RuntimeError("Gemini returned no image part")


def _run(provider: str, model: str, prompt: str, aspect_ratio: str, quality: str, resolution: str,
         destination: str | None, input_images: list[str], fallback: bool) -> dict:
    _guard()
    images = [Path(p) if Path(p).is_absolute() else ROOT / p for p in input_images]
    attempts = [(provider, model)]
    if fallback:
        attempts.append(("gemini", "gemini-3.1-flash-image") if provider == "openai" else ("openai", "gpt-image-2.6-flare"))
    errors = []
    for prov, mdl in attempts:
        try:
            if prov == "openai":
                data, usage = _openai_generate(mdl, prompt, aspect_ratio, quality, images)
            elif prov == "gemini":
                data, usage = _gemini_generate(mdl, prompt, aspect_ratio, resolution, images)
            else:
                raise ValueError(f"unknown provider {prov}")
            stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
            path = _out_path(destination, f"{prov}_{stamp}")
            w, h = _save(data, path)
            entry = {"date": stamp, "provider": prov, "model": mdl, "prompt": prompt, "aspect_ratio": aspect_ratio,
                     "quality": quality, "resolution": resolution, "inputs": [str(p) for p in images],
                     "width": w, "height": h, "path": str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) else str(path),
                     "estimated_cost_usd": _estimate(), "usage": usage}
            _record(entry)
            return entry
        except Exception as e:  # rate limits, unavailable models: try the authorised alternative
            errors.append(f"{prov}/{mdl}: {type(e).__name__}: {str(e)[:300]}")
    raise RuntimeError("All providers failed: " + " | ".join(errors))


@mcp.tool()
def list_image_models() -> dict:
    """List image-capable models currently available to the configured OpenAI and Gemini keys (free API calls)."""
    out: dict = {}
    try:
        from openai import OpenAI
        out["openai"] = sorted(m.id for m in OpenAI().models.list() if "image" in m.id or "dall" in m.id)
    except Exception as e:
        out["openai_error"] = f"{type(e).__name__}: {str(e)[:200]}"
    try:
        from google import genai
        client = genai.Client()  # keep a reference: the pager needs the client open while iterating
        out["gemini"] = sorted(m.name.removeprefix("models/") for m in list(client.models.list())
                               if "image" in m.name.lower() or "imagen" in m.name.lower())
    except Exception as e:
        out["gemini_error"] = f"{type(e).__name__}: {str(e)[:200]}"
    return out


@mcp.tool()
def generate_image(prompt: str, provider: str = "openai", model: str = "gpt-image-2.6-sunburst",
                   aspect_ratio: str = "16:9", quality: str = "high", resolution: str = "2K",
                   destination: str | None = None, fallback: bool = True) -> dict:
    """Generate one image and save it as PNG. provider: "openai" or "gemini". aspect_ratio e.g. 1:1, 16:9, 9:16.
    quality applies to OpenAI (low/medium/high/auto); resolution applies to Gemini (1K/2K/4K).
    destination: file or folder (relative to the repo root); default Assets/Karman/Art/Generated.
    Refused unless KARMAN_IMAGE_BUDGET_USD covers the estimated cost. Logged to AI_ASSET_LOG.md."""
    return _run(provider, model, prompt, aspect_ratio, quality, resolution, destination, [], fallback)


@mcp.tool()
def edit_image(prompt: str, input_images: list[str], provider: str = "openai", model: str = "gpt-image-2.6-sunburst",
               aspect_ratio: str = "16:9", quality: str = "high", resolution: str = "2K",
               destination: str | None = None, fallback: bool = True) -> dict:
    """Edit or compose from one or more input images (paths relative to the repo root or absolute)."""
    return _run(provider, model, prompt, aspect_ratio, quality, resolution, destination, input_images, fallback)


@mcp.tool()
def get_image_metadata(path: str) -> dict:
    """Dimensions and recorded provenance (provider, model, prompt, cost estimate) of a local image."""
    p = Path(path) if Path(path).is_absolute() else ROOT / path
    with Image.open(p) as im:
        info = {"path": str(p), "width": im.size[0], "height": im.size[1], "mode": im.mode, "format": im.format}
    if LEDGER.exists():
        for line in LEDGER.read_text().splitlines():
            e = json.loads(line)
            if Path(e["path"]).name == p.name:
                info["provenance"] = e
    return info


@mcp.tool()
def spending_status() -> dict:
    """Authorised budget, estimated spend so far, and the per-image estimate used by the guard."""
    return {"budget_usd": _budget(), "estimated_spent_usd": round(_spent(), 2), "per_image_estimate_usd": _estimate(),
            "keys": {"openai": bool(os.environ.get("OPENAI_API_KEY")), "gemini": bool(os.environ.get("GEMINI_API_KEY"))}}


if __name__ == "__main__":
    mcp.run()

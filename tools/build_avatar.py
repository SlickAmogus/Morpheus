#!/usr/bin/env python3
"""
Morpheus avatar wizard.

Run from a folder of loose source images. Walks emotions, matches by filename
prefix, classifies open/closed/blink/_N frame parts, resizes & pads images to
the chosen canvas size, copies them into morpheus's avatars folder under a
normalized scheme, and writes manifest.json.

Naming convention output side (what morpheus reads):
    <emotion>_open.png                 single closed-mouth still
    <emotion>_open_1.png, _2.png, ...  animated open-mouth frames
    <emotion>_closed.png               (same idea for closed)
    <emotion>_open_blink.png           single blink overlay during open
    <emotion>_closed_blink.png         single blink overlay during closed
    <emotion>_open_blink_1.png, _2.png animated blink
    tool_<name>.png                    per-tool reaction sprite

Source side: any naming, just pick a prefix per emotion. The script splits each
filename's tokens (by `_`) and decides which output bucket each file belongs to.

Usage:
    cd path/to/loose/images
    python build_avatar.py [--avatars-root <path>]

Requires: Pillow  (pip install Pillow)
"""

from __future__ import annotations
import argparse
import json
import re
import shutil
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

try:
    from PIL import Image, ImageOps
except ImportError:
    sys.stderr.write("Pillow not installed. Run: pip install Pillow\n")
    sys.exit(1)


SUPPORTED_EXTS = {".png", ".jpg", ".jpeg", ".webp", ".bmp"}

DEFAULT_EMOTIONS = [
    "idle", "happy", "sad", "angry", "surprised",
    "thinking", "confused", "smirk", "worried",
]

DEFAULT_TOOLS = ["Bash", "Read", "Edit", "Write", "Glob", "Grep", "Task"]


# ---------- helpers ----------

def ask(prompt: str, default: str | None = None) -> str:
    suffix = f" [{default}]" if default else ""
    raw = input(f"{prompt}{suffix}: ").strip()
    return raw if raw else (default or "")

def ask_yn(prompt: str, default: bool = True) -> bool:
    d = "y" if default else "n"
    a = ask(f"{prompt} (y/n)", d).lower()
    return a.startswith("y")

def ask_int(prompt: str, default: int) -> int:
    while True:
        raw = ask(prompt, str(default))
        try:
            return int(raw)
        except ValueError:
            print("  not an integer")

def ask_float(prompt: str, default: float) -> float:
    while True:
        raw = ask(prompt, str(default))
        try:
            return float(raw)
        except ValueError:
            print("  not a number")

def parse_size(raw: str) -> tuple[int, int]:
    m = re.match(r"^\s*(\d+)\s*[xX]\s*(\d+)\s*$", raw)
    if m: return int(m.group(1)), int(m.group(2))
    n = int(raw)
    return n, n


# ---------- classification ----------

@dataclass
class SourceFile:
    path: Path
    tokens: list[str]            # filename stem split on `_`
    has_open: bool = False
    has_closed: bool = False
    has_blink: bool = False
    frame: int | None = None     # last numeric token, if any

def classify(path: Path, prefix: str) -> SourceFile | None:
    stem = path.stem
    tokens = stem.split("_")
    if not tokens or not tokens[0].lower().startswith(prefix.lower()):
        return None
    sf = SourceFile(path=path, tokens=tokens)
    for t in tokens[1:]:
        tl = t.lower()
        if tl == "open": sf.has_open = True
        elif tl == "closed": sf.has_closed = True
        elif tl == "blink": sf.has_blink = True
        else:
            try: sf.frame = int(t)
            except ValueError: pass
    return sf

@dataclass
class EmotionBucket:
    open_frames: list[Path] = field(default_factory=list)        # animated open-mouth
    closed_frames: list[Path] = field(default_factory=list)
    open_blink_frames: list[Path] = field(default_factory=list)
    closed_blink_frames: list[Path] = field(default_factory=list)

def bucket_files(matches: list[SourceFile]) -> EmotionBucket:
    b = EmotionBucket()
    # group by (mouth, blink)
    groups: dict[tuple[str, bool], list[SourceFile]] = {}
    for sf in matches:
        # Default mouth: closed if neither tag present (single-still emotions).
        if sf.has_open: mouth = "open"
        elif sf.has_closed: mouth = "closed"
        else: mouth = "closed"
        groups.setdefault((mouth, sf.has_blink), []).append(sf)

    def order(items: list[SourceFile]) -> list[Path]:
        items.sort(key=lambda s: (s.frame if s.frame is not None else 0, s.path.name))
        return [s.path for s in items]

    if ("open", False) in groups: b.open_frames = order(groups[("open", False)])
    if ("closed", False) in groups: b.closed_frames = order(groups[("closed", False)])
    if ("open", True) in groups: b.open_blink_frames = order(groups[("open", True)])
    if ("closed", True) in groups: b.closed_blink_frames = order(groups[("closed", True)])
    return b


# ---------- image processing ----------

def process_image(src: Path, dst: Path, size: tuple[int, int]) -> None:
    """Open source, fit-pad to `size` keeping aspect, save as PNG (RGBA)."""
    with Image.open(src) as im:
        if im.mode != "RGBA":
            im = im.convert("RGBA")
        # ImageOps.pad: resize to fit inside, then pad with transparent to exact size.
        out = ImageOps.pad(im, size, method=Image.Resampling.LANCZOS, color=(0, 0, 0, 0))
        dst.parent.mkdir(parents=True, exist_ok=True)
        out.save(dst, format="PNG", optimize=True)

def copy_webp(src: Path, dst: Path) -> None:
    """Copy WebP verbatim — preserves animation frames + per-frame timing."""
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)


def has_frontmatter(text: str) -> bool:
    s = text.lstrip()
    if not s.startswith("---"): return False
    rest = s[3:]
    nl = rest.find("\n")
    if nl < 0: return False
    return "\n---" in rest[nl:]

def default_frontmatter(name: str) -> str:
    return (
        f"---\n"
        f"name: morpheus-{name}\n"
        f"description: morpheus avatar personality for {name} (with [emotion: X] tagging)\n"
        f"---\n\n"
    )

def build_emotion_personality_block(name: str, emotions: list[str]) -> str:
    bullets = "\n".join(f"- `{e}`" for e in emotions)
    return f"""## Emotion tagging (for {name} avatar)

Begin EVERY reply with an emotion tag in the form:

    [emotion: <name>]

Pick the tag whose face best matches the tone of the reply. The literal tag is
stripped before display/TTS, so include it on every message. Use exactly one of
the following emotions (anything else is ignored and the avatar stays neutral):

{bullets}

Examples:

    [emotion: happy] Got it! That worked first try.
    [emotion: thinking] Hmm, give me a moment to check the logs.
    [emotion: surprised] Oh — that test was supposed to fail.

Default to `[emotion: idle]` when nothing else fits.
"""

def write_set(emotion: str, mouth: str, blink: bool,
              frames: list[Path], dst_dir: Path, size: tuple[int, int]) -> str | None:
    """Returns the manifest-relative base path, or None if nothing written."""
    if not frames: return None
    base_stem = f"{emotion}_{mouth}" + ("_blink" if blink else "")
    if len(frames) == 1:
        out = dst_dir / f"{base_stem}.png"
        process_image(frames[0], out, size)
    else:
        for i, f in enumerate(frames, start=1):
            out = dst_dir / f"{base_stem}_{i}.png"
            process_image(f, out, size)
    return f"{base_stem}.png"


# ---------- emotion / tool walks ----------

def walk_emotion(emotion: str, sources: list[Path], dst_dir: Path, size,
                 required: bool) -> dict | None:
    """
    Returns the manifest fragment for an emotion (or None if skipped).
    Manifest fragment shape:
        { "closed": "<file>", "open": "<file>" }   (also writes _blink siblings to disk)
    """
    print(f"\n== emotion: {emotion}{'  (REQUIRED)' if required else ''} ==")
    while True:
        prefix = ask("prefix in source filenames (or 'skip')", "skip" if not required else "")
        if not prefix:
            if required:
                print("  required, please specify a prefix")
                continue
            return None
        if prefix.lower() == "skip":
            if required:
                print("  cannot skip a required emotion")
                continue
            return None
        matches = [c for p in sources if (c := classify(p, prefix)) is not None]
        if not matches:
            print(f"  no files matched prefix '{prefix}'. files in dir starting with '{prefix}':")
            for p in sources:
                if p.stem.lower().startswith(prefix.lower()):
                    print(f"    {p.name}")
            if not ask_yn("try a different prefix?", True): return None
            continue
        bucket = bucket_files(matches)
        print("  matched:")
        if bucket.closed_frames: print(f"    closed: {[p.name for p in bucket.closed_frames]}")
        if bucket.open_frames:   print(f"    open:   {[p.name for p in bucket.open_frames]}")
        if bucket.closed_blink_frames: print(f"    closed_blink: {[p.name for p in bucket.closed_blink_frames]}")
        if bucket.open_blink_frames:   print(f"    open_blink:   {[p.name for p in bucket.open_blink_frames]}")
        if not ask_yn("looks right?", True): continue

        closed_base = write_set(emotion, "closed", False, bucket.closed_frames, dst_dir, size)
        open_base   = write_set(emotion, "open",   False, bucket.open_frames,   dst_dir, size)
        write_set(emotion, "closed", True, bucket.closed_blink_frames, dst_dir, size)
        write_set(emotion, "open",   True, bucket.open_blink_frames,   dst_dir, size)

        out = {}
        if closed_base: out["closed"] = closed_base
        if open_base:   out["open"]   = open_base
        return out if out else None


def walk_emotion_webp(emotion: str, sources: list[Path], dst_dir: Path,
                       required: bool) -> dict | None:
    """
    WebP-mode walk. Convention:
        <prefix>.webp                   → closed (default expression)
        <prefix>_talking.webp           → open   (talking)
        <prefix>_<N>.webp               → alt closed pose (variant)
        <prefix>_<N>_talking.webp       → alt talking pose (variant)
    Variants are picked at random when the emotion is entered.
    """
    print(f"\n== emotion: {emotion}{'  (REQUIRED)' if required else ''} ==")
    while True:
        prefix = ask("prefix in source filenames (or 'skip')", "skip" if not required else "")
        if not prefix:
            if required: print("  required, please specify a prefix"); continue
            return None
        if prefix.lower() == "skip":
            if required: print("  cannot skip"); continue
            return None

        pl = prefix.lower()
        closed_src = None
        talking_src = None
        # Map: variant_index → (closed_path, talking_path)
        variants: dict[int, dict[str, Path]] = {}

        for p in sources:
            if p.suffix.lower() != ".webp": continue
            stem = p.stem.lower()
            if stem == pl:
                closed_src = p; continue
            if stem == f"{pl}_talking":
                talking_src = p; continue
            if not stem.startswith(pl + "_"): continue
            tail = stem[len(pl) + 1:]
            # variant patterns: "<N>" or "<N>_talking"
            if tail.isdigit():
                variants.setdefault(int(tail), {})["closed"] = p
            elif tail.endswith("_talking"):
                head = tail[:-len("_talking")]
                if head.isdigit():
                    variants.setdefault(int(head), {})["talking"] = p

        if not closed_src and not talking_src and not variants:
            print(f"  no .webp matched '{prefix}', '{prefix}_talking', or '{prefix}_<N>.webp'")
            print("  webp files in dir starting with that prefix:")
            for p in sources:
                if p.suffix.lower() == ".webp" and p.stem.lower().startswith(pl):
                    print(f"    {p.name}")
            if not ask_yn("try a different prefix?", True): return None
            continue

        print(f"  matched closed:  {closed_src.name if closed_src else '(none)'}")
        print(f"  matched talking: {talking_src.name if talking_src else '(none)'}")
        if variants:
            print(f"  variants: {len(variants)}")
            for n in sorted(variants):
                v = variants[n]
                print(f"    [{n}] closed={v.get('closed').name if v.get('closed') else '(none)'}"
                      f" talking={v.get('talking').name if v.get('talking') else '(none)'}")
        if not ask_yn("looks right?", True): continue

        out: dict = {}
        if closed_src:
            dst = dst_dir / f"{emotion}.webp"
            copy_webp(closed_src, dst); out["closed"] = dst.name
        if talking_src:
            dst = dst_dir / f"{emotion}_talking.webp"
            copy_webp(talking_src, dst); out["open"] = dst.name

        variant_list = []
        for n in sorted(variants):
            v = variants[n]
            variant_entry: dict = {}
            if "closed" in v:
                dn = f"{emotion}_{n}.webp"
                copy_webp(v["closed"], dst_dir / dn); variant_entry["closed"] = dn
            if "talking" in v:
                dn = f"{emotion}_{n}_talking.webp"
                copy_webp(v["talking"], dst_dir / dn); variant_entry["open"] = dn
            if variant_entry: variant_list.append(variant_entry)
        if variant_list: out["variants"] = variant_list

        return out if out else None


def collect_idle_webp_clips(sources: list[Path], dst_dir: Path) -> list[dict]:
    """Auto-collect any `idle_*.webp` files into IdleClips. Skips `idle.webp` and
    `idle_talking.webp` (those are the base idle expression)."""
    clips: list[dict] = []
    for p in sources:
        if p.suffix.lower() != ".webp": continue
        stem = p.stem.lower()
        if not stem.startswith("idle_"): continue
        if stem in ("idle_talking",): continue
        copy_webp(p, dst_dir / p.name)
        clips.append({
            "file": p.name,
            "minSeconds": 8.0,
            "maxSeconds": 20.0,
            "durationSeconds": 2.0,
            "weight": 1.0,
        })
    return clips


def walk_tools(sources: list[Path], dst_dir: Path, size) -> dict[str, str]:
    out: dict[str, str] = {}
    if not ask_yn("\nadd any tool reaction sprites?", False): return out
    while True:
        name = ask("tool name (e.g. Bash, Read; blank to finish)", "")
        if not name: break
        prefix = ask("prefix in source filenames")
        if not prefix: continue
        matches = [c for p in sources if (c := classify(p, prefix)) is not None]
        if not matches:
            print(f"  no matches for '{prefix}'"); continue
        # tool sprite: take the first matched single image (no mouth handling)
        chosen = matches[0].path
        dst_name = f"tool_{name.lower()}.png"
        process_image(chosen, dst_dir / dst_name, size)
        out[name] = dst_name
        print(f"  → {name}: {dst_name}")
    return out


def walk_idle_animations(emotions_added: list[str]) -> list[dict]:
    out: list[dict] = []
    if not ask_yn("\nschedule random idle expressions (eyebrow flashes, smirks, etc)?", False):
        return out
    while True:
        name = ask("emotion to fire (must be one already added; blank to finish)", "")
        if not name: break
        if name not in emotions_added:
            print(f"  '{name}' not in added emotions: {emotions_added}")
            if not ask_yn("add anyway?", False): continue
        out.append({
            "emotion": name,
            "minSeconds":      ask_float("  min gap seconds", 8),
            "maxSeconds":      ask_float("  max gap seconds", 20),
            "durationSeconds": ask_float("  hold duration seconds", 0.6),
            "weight":          ask_float("  pick weight", 1.0),
        })
    return out


# ---------- main ----------

def main():
    ap = argparse.ArgumentParser(description="Morpheus avatar wizard.")
    ap.add_argument("--avatars-root", help="path to morpheus avatars/ folder. "
                                           "Default: ./avatars next to morpheus.exe if found.")
    ap.add_argument("--source-dir", default=".", help="folder with loose images (default: cwd)")
    args = ap.parse_args()

    src_dir = Path(args.source_dir).resolve()
    if not src_dir.is_dir():
        print(f"source dir not found: {src_dir}"); sys.exit(1)

    print(f"== morpheus avatar wizard ==")
    print(f"   source: {src_dir}")

    sources = sorted([p for p in src_dir.iterdir()
                      if p.is_file() and p.suffix.lower() in SUPPORTED_EXTS])
    if not sources:
        print("no supported image files in source dir"); sys.exit(1)
    print(f"   found {len(sources)} image(s)")

    webp_count = sum(1 for p in sources if p.suffix.lower() == ".webp")
    webp_mode_default = webp_count > 0 and webp_count >= len(sources) // 2
    webp_mode = ask_yn(f"webp mode? ({webp_count}/{len(sources)} files are .webp)", webp_mode_default)

    # avatars root
    avatars_root = args.avatars_root
    if not avatars_root:
        guesses = [
            Path(__file__).resolve().parent.parent / "src" / "bin" / "Debug" / "net8.0" / "avatars",
            Path.cwd() / "avatars",
        ]
        for g in guesses:
            if g.parent.exists(): avatars_root = str(g); break
        if not avatars_root: avatars_root = str(Path.cwd() / "avatars")
    avatars_root = Path(ask("avatars root", avatars_root)).resolve()

    # avatar identity
    name = ""
    while not name:
        name = ask("avatar folder name (no spaces)", "")
    description = ask("description", "")
    author = ask("author", "")

    # canvas size — used for the in-game avatar frame; webp files are copied verbatim,
    # MonoGame scales the texture to fit the frame at draw time.
    size_raw = ask("canvas size (e.g. 256, 320x320)", "320x320")
    size = parse_size(size_raw)

    fps = ask_int("fps for multi-frame PNG sequences (webp uses its own timing)", 8)
    lipsync = ask_float("lipsync threshold (0.0-1.0, lower = more sensitive)", 0.05)
    crop_h = ask_int("horizontal crop in px (trims this many px off BOTH left and right)", 0)
    crop_v = ask_int("vertical crop in px (trims this many px off BOTH top and bottom)", 0)
    transparent_color = ask("transparent color hex (e.g. #00FF00 to key out greenscreen, blank to skip)", "")
    if webp_mode:
        blink_min = blink_max = blink_dur = 0.0  # blinks are inside the webp
    else:
        blink_min = ask_float("blink min seconds", 3.0)
        blink_max = ask_float("blink max seconds", 7.0)
        blink_dur = ask_float("blink duration seconds", 0.18)

    personality_file = ask("personality .md file (optional, will be copied in)", "")
    voice_id = ask("default voice_id (optional)", "")

    dst_dir = avatars_root / name

    # If sources live INSIDE dst_dir we'd nuke them on rmtree. Stage them out first.
    src_in_dst = dst_dir.exists() and any(
        str(p.resolve()).startswith(str(dst_dir.resolve())) for p in sources
    )
    if src_in_dst:
        staging = Path(tempfile.mkdtemp(prefix="morpheus_avatar_stage_"))
        print(f"  source files live inside the destination — staging to {staging}")
        new_sources = []
        for p in sources:
            d = staging / p.name
            shutil.copy2(p, d)
            new_sources.append(d)
        sources = new_sources
        pmd = dst_dir / "personality.md"
        if pmd.is_file():
            shutil.copy2(pmd, staging / "personality.md")
        src_dir = staging

    if dst_dir.exists():
        if not ask_yn(f"{dst_dir} exists. overwrite?", False):
            print("aborted"); sys.exit(0)
        shutil.rmtree(dst_dir)
    dst_dir.mkdir(parents=True)

    walk = walk_emotion_webp if webp_mode else walk_emotion
    walk_args = (sources, dst_dir) if webp_mode else (sources, dst_dir, size)

    emotion_manifests: dict[str, dict] = {}
    idle_pair = walk("idle", *walk_args, required=True)
    if not idle_pair: print("idle is required, aborting"); sys.exit(1)

    emotions_added = ["idle"]
    for emo in DEFAULT_EMOTIONS[1:]:
        frag = walk(emo, *walk_args, required=False)
        if frag:
            emotion_manifests[emo] = frag
            emotions_added.append(emo)

    while ask_yn("\nadd a custom emotion?", False):
        custom = ask("emotion name (lowercase)", "")
        if not custom or custom in emotion_manifests: continue
        frag = walk(custom, *walk_args, required=False)
        if frag:
            emotion_manifests[custom] = frag
            emotions_added.append(custom)

    # generic fallback
    generic = ask("generic-fallback prefix (used when emotion unknown; blank = none)", "")
    generic_path = None
    if generic:
        if webp_mode:
            for p in sources:
                if p.suffix.lower() == ".webp" and p.stem.lower() == generic.lower():
                    copy_webp(p, dst_dir / "generic.webp")
                    generic_path = "generic.webp"
                    break
        else:
            matches = [c for p in sources if (c := classify(p, generic)) is not None]
            if matches:
                process_image(matches[0].path, dst_dir / "generic.png", size)
                generic_path = "generic.png"

    # tools (skipped in webp mode for v1 — could add later via separate convention)
    tools = {} if webp_mode else walk_tools(sources, dst_dir, size)

    # idle animations
    idle_anims = walk_idle_animations(emotions_added)
    idle_clips = collect_idle_webp_clips(sources, dst_dir) if webp_mode else []
    if idle_clips:
        print(f"\n auto-collected {len(idle_clips)} idle webp clip(s):")
        for c in idle_clips: print(f"   - {c['file']}")

    # personality file: copy the user's, then optionally append (or fully generate)
    # the [emotion: X] tagging instructions so Claude will tag every reply.
    personality_out = None
    user_personality_text = ""
    if personality_file:
        src = Path(personality_file).expanduser().resolve()
        if src.is_file():
            user_personality_text = src.read_text(encoding="utf-8")
            print(f"loaded personality source: {src}")
        else:
            print(f"  personality file not found, ignoring: {src}")

    add_emotion_block = ask_yn(
        "auto-generate emotion-tagging instructions for Claude (recommended)?", True)
    # Auto-detect personality.md already in source dir (e.g., committed alongside the
    # WebP frames). Use it as the base unless the user supplied a different one.
    if not user_personality_text:
        candidate = src_dir / "personality.md"
        if candidate.is_file():
            user_personality_text = candidate.read_text(encoding="utf-8")
            print(f"loaded existing personality.md from source dir")

    if add_emotion_block or user_personality_text:
        emotion_keys = ["idle"] + sorted(emotion_manifests.keys())
        body = user_personality_text.rstrip()
        # Only append the auto emotion block if the user-provided text doesn't already
        # contain emotion-tag instructions (avoid duplication).
        already_has_emotion_tag = "[emotion:" in body or "emotion tag" in body.lower()
        if add_emotion_block and not already_has_emotion_tag:
            body += ("\n\n" if body else "") + build_emotion_personality_block(name, emotion_keys)
        # Output styles need YAML frontmatter to show in Claude Code's /config picker.
        if not has_frontmatter(body):
            body = default_frontmatter(name) + body
        personality_out = "personality.md"
        (dst_dir / personality_out).write_text(body, encoding="utf-8")
        print(f"wrote personality: {personality_out} (emotions in tag list: {len(emotion_keys)})")

    manifest = {
        "name": name,
        "description": description,
        "author": author,
        "fps": fps,
        "lipsyncThreshold": lipsync,
        "size": {"width": size[0], "height": size[1]},
        "crop": {"horizontal": crop_h, "vertical": crop_v} if (crop_h or crop_v) else None,
        "transparentColor": transparent_color or None,
        "blinkMinSeconds": blink_min,
        "blinkMaxSeconds": blink_max,
        "blinkDurationSeconds": blink_dur,
        "sprites": {
            "idle": idle_pair,
            "generic": generic_path,
            "emotions": emotion_manifests,
            "tools": tools,
        },
        "idleAnimations": idle_anims,
        "idleClips": idle_clips,
        "personalityFile": personality_out,
    }
    if voice_id:
        # not part of avatar manifest, but record it for the user as a hint file
        (dst_dir / "voice_hint.txt").write_text(
            f"Default voice id for this avatar: {voice_id}\n"
            f"Set this in morpheus's voice dropdown manually.\n",
            encoding="utf-8")

    (dst_dir / "manifest.json").write_text(
        json.dumps(manifest, indent=2), encoding="utf-8")

    print(f"\n done. avatar written to: {dst_dir}")
    print(f" cycle to it in morpheus with F2 (or pick from the avatar dropdown).")


if __name__ == "__main__":
    main()

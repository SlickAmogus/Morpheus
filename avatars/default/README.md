# Default avatar

Template for authoring your own.

## Required files

- `manifest.json` — metadata + sprite map
- `idle_closed.png` — mouth closed, resting expression
- `idle_open.png` — mouth open, resting expression (mid-speech flip target)
- `generic.png` — fallback sprite for any emotion/tool not covered below

## Optional files

- `preview.png` — shown in the avatar picker preview pane
- `personality.md` — Claude Code output style (frontmatter + system-prompt text)
- Emotion pairs, e.g. `happy_closed.png` / `happy_open.png`, `angry_closed.png` / ...
- Tool sprites, e.g. `tool_bash.png`, `tool_edit.png`, `tool_read.png`, `tool_web.png`

## manifest.json shape

```json
{
  "name": "Display name",
  "author": "you",
  "description": "shown in picker",
  "fps": 8,
  "lipsyncThreshold": 0.05,
  "sprites": {
    "idle":     { "closed": "idle_closed.png", "open": "idle_open.png" },
    "generic":  "generic.png",
    "emotions": {
      "happy":    { "closed": "happy_closed.png",  "open": "happy_open.png" },
      "thinking": { "closed": "thinking.png",      "open": "thinking.png"   }
    },
    "tools": {
      "Bash": "tool_bash.png",
      "Edit": "tool_edit.png"
    }
  },
  "personalityFile": "personality.md",
  "previewImage": "preview.png"
}
```

## Fallback cascade

For any (emotion, tool, mouthOpen) lookup, morpheus walks:
1. tool-specific sprite (if current tool matches)
2. emotion sprite for current mouth state
3. idle sprite for current mouth state
4. `generic`

So you only need to ship the sprites you care about; everything else gracefully falls back.

## Distributing

Zip the folder as `myavatar.zip` and drop it into `avatars/`. Morpheus extracts on next launch if a same-named folder doesn't already exist.

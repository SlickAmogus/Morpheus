# Morpheus

Avatar frontend for Claude Code. Morpheus speaks Claude Code's non-code output aloud, animates a 2D avatar while it talks, and switches expressions in response to tool-use events — giving one specific Claude Code session a face and a voice.

Status: **early scaffolding**. Skeleton compiles, no wired features yet.

## What it does (when complete)

- Binds to one chosen Claude Code session via hooks (Stop + PreToolUse + PostToolUse).
- Synthesizes the assistant's text output with TTS (ElevenLabs by default, API key user-supplied).
- Plays audio through NAudio, drives mouth open/closed frames from playback amplitude.
- Switches expression based on tool-use events (e.g. `Bash` → tool_bash sprite, `Read/Grep` → thinking, ...).
- Shows a text box with the message currently being spoken.
- Lets you pick avatars from a dropdown — each avatar is a folder under `avatars/` with a sprite manifest and optional personality file (Claude Code output style).
- Lets you pick UI skins ("templates") the same way from `templates/`.
- Both avatars and templates can be shipped as zip files; morpheus extracts them on launch.

## What it does not do

- Does not run its own LLM. All text comes from Claude Code.
- Does not do speech-to-text. Use Claude Code's input side (your OS dictation, etc.).
- Does not inject commands into your Claude Code session. You activate personalities via `/config → Output style` yourself, after morpheus installs the file.

## Requirements

- .NET 8 SDK
- Windows (Linux/macOS likely work via MonoGame.DesktopGL but untested)

## Build

```bash
dotnet build
dotnet run --project src
```

## Project layout

```
morpheus/
  src/                  C# source (MonoGame + NAudio)
    MorpheusGame.cs     MonoGame entry
    Avatar/             manifest + loader + renderer + state
    Audio/              TTS client + NAudio player + lipsync tap
    Hooks/              HttpListener + bridge + installer
    Ui/                 ImGui overlay + settings store
    Personality/        output-style installer
  avatars/              user-swappable avatar folders (+ *.zip auto-extract)
    default/            placeholder avatar; clone to author your own
  templates/            user-swappable UI skins
    default/            placeholder cyan HUD frames
  tools/                build / asset helper scripts
  reference/            not shipped — source material only
```

## Credits

Spiritual and code ancestor: **[FoxhoundAI](https://github.com/SlickAmogus/FoxhoundAI)** — a MonoGame parody of Metal Gear Solid codec calls driven by GPT-3 and Google TTS. Morpheus reuses FoxhoundAI's audio/sprite pipeline patterns and was built by its author.

Metal Gear Solid is © Konami; no copyrighted assets ship in this repository.

## License

TBD.

# Morpheus

An animated avatar frontend for Claude Code. Morpheus displays a customizable animated avatar that responds to your code work with expressions, emotions, and synthesized voice—giving your Claude Code session a face and personality.

## Features

- **Animated Avatars** — WebP and PNG sprite-based avatars with emotion states and lipsync
- **Claude Code Integration** — Hooks into Claude Code via HTTP to display responses with avatar expressions in real-time
- **Text-to-Speech** — ElevenLabs voice synthesis with customizable voice and speaker settings
- **Session History** — Persistent transcripts with pagination and search
- **Personality System** — Custom avatar personalities with emotion-tagged responses
- **Template System** — Swappable UI frames and layouts
- **Voice Customization** — Custom and shared ElevenLabs voices

## What it does

- Binds to a Claude Code session via HTTP hooks (Stop + Tool events)
- Reads assistant messages and synthesizes them with TTS (ElevenLabs)
- Plays audio via NAudio with mouth-open/closed lipsync from playback amplitude
- Switches avatar expressions based on emotion tags (`[emotion: happy]`, etc.)
- Displays transcripts with message history and navigation
- Supports swappable avatars and UI templates from `avatars/` and `templates/` folders
- Manages voice settings and custom voice library

## What it doesn't do (yet)

- Does not run its own LLM — all text comes from Claude Code
- Does not do speech-to-text input (use Claude Code's native input)
- Does not directly inject text into Claude Code (you control the session)

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

MIT License — see LICENSE file for details.

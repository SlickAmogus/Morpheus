# Default UI template

Swap these files (or clone the folder) to reskin morpheus's window chrome.

## Files

- `manifest.json` — frame paths + default background color
- `UI_avatar.png` — frame drawn around the avatar (transparent center)
- `UI_message.png` — frame drawn around the text readout (transparent center)

## manifest.json shape

```json
{
  "name": "Display name",
  "author": "you",
  "description": "shown in picker",
  "avatarFrame": "UI_avatar.png",
  "messageFrame": "UI_message.png",
  "backgroundColor": "#000000"
}
```

## Distributing

Zip the folder as `mytheme.zip` and drop it into `templates/`. Morpheus extracts on next launch if a same-named folder doesn't already exist.

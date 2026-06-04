# Demo capture (VHS)

The GIFs in the main [README](../README.md) are generated from the `.tape` scripts in this
directory with [VHS](https://github.com/charmbracelet/vhs), so they stay reproducible and can be
re-rendered whenever the CLI's output changes.

## One-time setup

1. **Install VHS** (it bundles `ttyd` + `ffmpeg`):

   ```bash
   brew install vhs          # macOS
   # or: go install github.com/charmbracelet/vhs@latest
   ```

2. **Put `bwsh` on your PATH** so the tapes can call it directly (cleaner than `dotnet run --`):

   ```bash
   # from POC/bwsh/
   dotnet publish Bwsh.csproj -c Release -r "$(dotnet --info | sed -n 's/^ *RID: *//p' | head -1)" \
     --self-contained true -o ./publish
   export PATH="$PWD/publish:$PATH"
   bwsh --help   # verify
   ```

3. **Stand up a stack** (the `status`/`logs` tapes record a real deployment):

   ```bash
   bwsh install --manifest bitwarden.yaml
   ```

## Render the GIFs

Run from `POC/bwsh/` so the `Output docs/images/*.gif` paths resolve:

```bash
vhs docs/status.tape     # docs/images/status.gif
vhs docs/install.tape    # docs/images/install.gif
vhs docs/logs.tape       # docs/images/logs.gif
```

Commit the resulting GIFs under `docs/images/` — that's what GitHub renders in the README.

## Tweaks

- Each tape's `Sleep` values control how long output is shown; bump them if your stack is slow.
- For `install.tape`, pre-pull the images first so the GIF focuses on the orchestration
  animation instead of a long download.
- `Set Theme` accepts any theme from `vhs themes` (default here is `Dracula`).
- `Set Width`/`Set Height` are in pixels — widen if long container names wrap.

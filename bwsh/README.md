# bwsh

`bwsh` is the new installer for Bitwarden self-host. It will replace `bitwarden.sh`/`bitwarden.ps1`,
`run.sh`/`run.ps1` and the Setup container with a single binary for Linux and Windows.

It is in early development (Alpha) and is not yet released. Customers should keep using
`bitwarden.sh` and `bitwarden.ps1`.

## Layout

| Path | Contents |
|---|---|
| `src/Bwsh` | The CLI. Assembly name `bwsh`, root namespace `Bit.SelfHost` |
| `test/Bwsh.Test` | xUnit tests, with NSubstitute and coverlet |
| `Directory.Build.props` | Build settings shared by both projects, and the bwsh version |
| `.editorconfig` | C# code style, mirroring `bitwarden/server` |

## Build and test

Requires the .NET 10 SDK.

```bash
cd bwsh
dotnet test
dotnet format bwsh.slnx --verify-no-changes
```

Publish a self-contained single-file binary (`linux-x64` or `win-x64`). The host it runs on doesn't
need .NET installed:

```bash
dotnet publish src/Bwsh -c Release -r linux-x64 -o publish/linux-x64
./publish/linux-x64/bwsh --version
```

## Versioning

bwsh tracks two versions, and `bwsh --version` prints both:

```
bwsh 0.1.0-alpha.42+50bbd83
core 2026.10.0 · web 2026.10.0 · key-connector 2025.11.0
```

- **bwsh's own version** is set by `VersionPrefix` in `Directory.Build.props`. It is independent of
  `version.json` and the server release train. CI adds the `alpha.<run number>` suffix, and the
  SDK adds the commit after `+`.
- **Stack versions** are the Bitwarden image versions an install deploys. They are baked in from
  the repository's `version.json` at build time, as `bitwarden.sh` does today. The build fails if
  `version.json` is missing or lacks `coreVersion`, `webVersion` or `keyConnectorVersion`.

## CI

`.github/workflows/build-bwsh.yml` runs on pull requests and pushes to `main` that touch `bwsh/`
or `version.json`. It checks formatting, runs the tests on Linux and Windows, then publishes
`linux-x64` and `win-x64` binaries as workflow artifacts, each with a SHA-256 checksum. A smoke
job runs each binary on its own OS.

## Alpha limitations

- Nothing is published to a release. Testers download binaries from the workflow run's artifacts.
- The binaries are not code-signed. Windows SmartScreen and Defender may warn when running
  `bwsh.exe`. Check the download against its `.sha256` file.
- The Linux binary needs glibc. It runs on Debian, Ubuntu, RHEL and similar distributions, but not
  on musl-based ones such as Alpine.

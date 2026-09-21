# Project Overview

## What this is

`kmax-display-example` - a game module hosted inside the ViitorCloud
`unityvc-base-project` Unity template (`D:\Unity\kmax-display-example-base-project`).
It targets Kmax stereoscopic display hardware.

## Repository boundary

Two git repositories are nested:

- **Base project** (`D:\Unity\kmax-display-example-base-project`) - **read-only**.
  Holds `Packages/manifest.json`, `ProjectSettings/`, `Assets/BaseScripts`,
  `Assets/Modules`, the shared build pipeline and the root `docs/`.
- **This module** (`Assets/Games/kmax-display-example`) - **read/write**.
  Everything this module owns lives under this folder.

Because `Packages/` is read-only, third-party UPM packages cannot be installed
through `manifest.json`. They are vendored under `Plugins/` in this module
instead; Unity compiles assembly definitions from `Assets/` identically.

Because the base `docs/` folder is read-only, the AGENTS.md section 16
documentation set for this module lives here
(`Assets/Games/kmax-display-example/docs/`) rather than at the base project root.

## Host environment

- Unity `6000.3.9f1`
- Universal Render Pipeline `17.3.0`
- Input handling: **Both** (legacy Input Manager + Input System package)
- API compatibility level: .NET Standard 2.1

## Kmax SDKs

Two generations of the Kmax SDK are vendored. Exactly one compiles at a time -
see architecture.md for the mechanism and decisions.md for why.

| | Kmax XR Core | Kmax AIO K1 |
|---|---|---|
| Package id | `com.kmax.xr.core` | `com.kmax.xr` |
| Version | 2.5.2 (2025-12-01) | 1.2.0 (2023-03-31) |
| Devices | Kmax M1 / K1 / G1 | Kmax all-in-one K1 |
| Platforms | Windows, Android, Linux, macOS, WebGL | Windows Standalone, WSA |
| Entry point | `XRRig` + `HeadTracker` | `KmaxVR` / `KmaxAR` |
| Native libs | `KmaxStereo`, `kXRCore`, `Dll_dx11` | `KMaxUnity` |
| Status | **default backend** | opt-in backend |
# Project context for AI agents

> Keep it short — this loads into every agent conversation.

## Source of truth
- [App.md](App.md) — product vision (what/why).
- [IMPLEMENTATION.md](IMPLEMENTATION.md) — current version (v1.19 shipped; Everyday coarse probes on Low-movers). **Next: F2 Hot-if-separates.** Heat-ladder locks live in [PATH_B_PHASE1_EXPERIMENT_PLAN.md](PATH_B_PHASE1_EXPERIMENT_PLAN.md) (gap-fill is Later, not Phase 1). Do not skip ahead; update it when a slice finishes or App.md changes. New work is v1.1+, not a new P-phase.
- This file — stack, layout, commands, and hard limits.

## What this project is
AUTO Fan is a Windows desktop app for PC enthusiasts. It measures how a specific machine's fans actually affect temperatures, then builds real multi-point temperature → duty fan curves from data. Like FanControl or similar editors, but the first draft is automatic — and you see the measurement evidence behind it.

## Stack
- Language(s): C# 14 / .NET 10 (Windows, `net10.0-windows`)
- Framework: WPF desktop app (MVVM). Hardware access via LibreHardwareMonitorLib. Vortice Direct3D 12 generates GPU heat (hidden raster burn) for baseline and P5/P6 experiments. NVIDIA GPU fan writes use NvAPIWrapper.Net on Hardware only; LibreHardwareMonitor must not `SetSoftware` on GPU nodes. ADLXWrapper (AMD GPU fans) remains parked. Search stays a discrete pick in Core over an additive model plus measured pair leftovers. No Gaussian process, no Math.NET, no Python sidecar.
- Database / storage: SQLite (`%LocalAppData%\AUTO Fan\autofan.db`) for baseline/experiment runs and later models; JSON files for user settings and cooling profiles. Local only.
- Auth provider: None. This is a local admin Windows app, not a cloud service.
- Hosting / deployment: Unpackaged Win32 installer + portable zip. Requires Administrator. Not Store/MSIX (kernel-level Super I/O access).
- Package manager: NuGet (`dotnet`)

## Commands
- Install: `dotnet restore`
- Run locally: `dotnet run --project src/AutoFan.App` (Administrator + PawnIO are required for real motherboard/CPU sensors; the app still opens and shows what’s missing)
- Run tests: `dotnet test`
- Lint / typecheck: `dotnet format --verify-no-changes` and `dotnet build`
- Build: `dotnet publish src/AutoFan.App -c Release`

## Conventions
Intended projects (do not collapse these into one assembly):
- `src/AutoFan.Hardware` — sensors, fan/pump control, and synthetic CPU/GPU load (no fan writes in the load path)
- `src/AutoFan.Core` — thermal model and optimizer (no hardware I/O; this is what tests hit)
- `src/AutoFan.Storage` — SQLite + JSON persistence
- `src/AutoFan.App` — WPF UI only

## Known gotchas
- Fan/sensor access needs Administrator plus LibreHardwareMonitor’s PawnIO driver. IDE debugging must also be elevated.
- Real sensors use LibreHardwareMonitor (P1). PWM writes start at P3 and stay behind restore-on-exit. `FakeHardwareBackend` remains for tests only.
- Baseline (P4) heats CPU cores and, when a real discrete DX12 GPU exists, a hidden Vortice raster burn: idle, everyday (720p, lighter), then a GPU-work search at ~60 Hz (more passes per Present, **not** uncapped FPS), then Low hold on that **frozen** profile, then **three BIOS settle-holds** (no fan writes) that label CPU and GPU Core STABLE / DRIFTING / NOISY / UNAVAILABLE, then cooldown. Same CPU workers as everyday. Skip WARP / Microsoft Basic Render / iGPU. Do not run an all-core High pass, or extra CPU threads, that hit the 90 °C abort. Stop the load on abort, cancel, crash, GPU device-loss, lost preferred temps, and exit. Baseline must not call `TrySetDuty`. Optional CPU/GPU abort fields may only tighten (CPU 65–90, GPU 65–83); the floor stays 90/83.
- The experiment unit is a writable fan group that reports RPM: a motherboard header or the NVIDIA fans on one card (they move together), not each physical fan on a hub and not each GPU fan. P5 writes one such group at a time through `SafeFanSession`, then restores. P6 writes at most two the same way and does not pair two GPU fans on the same card. Pumps and AMD/Intel GPU fans are not written. NVIDIA GPU fans go through NVAPI. If NVAPI cannot control fans, skip that group; do not crash. The Home detect pass sets each writable motherboard header to 100%, and NVIDIA GPU fans once per card; a header is connected if it has RPM. It does not add heat. BIOS and the NVIDIA driver curve are restored when the pass ends.
- Fan-test dwell waits until the relevant temperatures have stopped moving, with a timeout and abort ceilings. A timeout / still-moving hold is not Measured. Do not add another fixed kitchen-timer hold.
- P5/P6 experiment heat is the locked Low from Watch — everyday CPU workers plus the frozen GPU work-per-frame, stored on the baseline run. A missing lamp refuses heat (no silent `DefaultLow`). Wait until preferred CPU and GPU are ≤ 60 °C before applying load. Rate-of-rise abort (3 °C/s) only arms within 5 °C of the effective ceiling; a mid-range Ryzen spike is not a stop. A GPU reset or missing preferred temperature aborts and restores fans. Aim for about +15 °C GPU Core from idle; a ~2 °C GPU rise is not enough to map GPU fans — store GPU-target influence as Unknown, and do not write NVIDIA GPU fans for mapping, until that rise exists. A Δ below that sensor’s Watch minimum detectable effect is None. A CPU or GPU target that is not STABLE is Unknown. Do not change heater intensity during fan tests. Do not start isolated GPU interleave, A/B/A, named-anchor Hold, planner, extra heater, or GPU generated curves unless the snapshot says so.
- Low-only maps: if live heat or CPU/GPU power exceeds the test load, the control loop may move toward the already-computed cool end. A sustained power rise can raise the matching fans before the temperature spike. Do not raise the default recipe just because confidence is Low. Desktop / gaming / render / mixed are live policies from the same model, not a second experiment tour.
- One **Optimize** action opens a walk window: watch this PC, test fans (screen/refine, then pairs only if those finished), then hold. Extra test buttons are optional. On Stop, abort, crash, or exit, motherboard fans return to BIOS and NVIDIA fans return to the driver. An aborted Watch, empty fan tests, lost temps, or GPU reset retry that step. A thermal abort or cancel during individual tests with a measured group skips pairs and may Hold a quieter conservative policy; confirmation still runs if Hold applies. Do not start a new experiment kind after a thermal abort in Fans.
- Intended starting state for runs: motherboard BIOS fan control + GPU driver curve. Close competing fan apps. While Optimize is holding a policy, motherboard and NVIDIA fans follow that setting.
- An all-core High synthetic load, and a half-thread CPU Low, hit the CPU 90 °C abort on this class of PC. Do not run those. CPU Low matches Everyday. Do not loosen the ceiling.
- On crash or exit, restore motherboard/BIOS fan control and NVIDIA driver fan control, and stop any synthetic load. Never leave PWM stuck at an experiment duty cycle.
- Do not run alongside FanControl, Armoury Crate, iCUE, or similar — they fight over the same headers.
- GPU fan control is vendor-API and driver-version specific (NVAPI vs ADLX), not the same path as motherboard Super I/O. AMD ADLX stays parked.
- Case geometry and manufacturer specs are priors only. Measured thermal response is ground truth.

## Out of bounds
- Safety ceilings (max temp, duty 0–100%, experiment abort) — do not loosen or bypass. User abort fields can only stop sooner. Empty 0 RPM / stalled points are skipped, not fitted.
- Kernel driver / PawnIO setup, code signing, and installer/privileged manifests.
- Uploading sensor logs, hardware IDs, or thermal models anywhere without an explicit product decision.
- Adding a web UI, Electron/Tauri wrapper, cloud backend, or extra paid API.
- Enabling stress tests / fan experiments that ignore thermal abort limits.
- Gaussian process / Expected Improvement / NSGA-II / microphone noise / replacing the Quiet–Cool slider.

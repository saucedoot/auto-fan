# Audit: Path B mental model vs code and experiment

**Date:** 2026-09-21  
**Repo:** https://github.com/saucedoot/auto-fan  
**Code under review:** v1.15 behavior on `cursor/path-b-docs-a4e1` (same C# as master `da6b35f`; Path B exists in docs only)  
**Product truth:** Path B (Danny, 2026-09-21) — Rem0o FanControl-like but automatic: lock heat, measure, build editable multi-point **temperature → duty** curves, show evidence on Home.

This is a read-only investigation. No C# or XAML was changed. Abort floors (CPU 90 °C / GPU 83 °C) were not questioned.

---

## Overall verdict: **Not aligned** with Path B

Docs now describe Path B. The running app is still Path A: one Optimize walk that measures fan effects at **one locked heat**, collapses those effects into a **two-end Quiet–Cool policy**, holds those duties while the window is open, and shows evidence (RPM vs settled °C, labeled claims) on a **What we learned** tab — not editable temperature → duty graphs on Home.

The measurement engine is a strong start for *which fans matter at this heat*. It is not yet an engine that observes a **temperature range** and produces honest FanControl-style curves. Seeding a Home graph from today’s settled points would be a UI over a thin, same-heat isobar, not a measured temp→duty curve.

| Path B promise | Live code |
| --- | --- |
| Editable multi-point **temp → duty** curves on Home | Quiet–Cool **policy** (quiet duty / cool duty / blend). Home tagline: “hold a quieter or cooler fan policy” |
| Curves over a useful temperature range | Fan tests at one frozen Low heat. Points sit in a narrow °C band |
| Quietest curves as the starting point | Quietest **duty pair**, interpolated by slider blend |
| Quiet–Cool secondary until data is good | Quiet–Cool **is** the product; slider locked while holding |
| What we learned = evidence behind the curves | Evidence exists; there are no curves to hang it on |
| BIOS → AUTO → BIOS on locked heat | Not built. Confirmation is vs `Predict()`, after the heater is off |

---

## 1. Blocking gaps (must fix before claiming Path B curves)

### 1.1 The live deliverable is a two-end policy, not editable curves

**Plain language:** After Optimize, AUTO Fan picks a quieter speed and a cooler speed for each fan it measured, then holds a mix of those two. Home does not show a graph you can edit. The Quiet–Cool slider is the main control, not a secondary regenerator.

**What the user sees**

- Home (`src/AutoFan.App/MainWindow.xaml`): Optimize button, Quiet–Cool slider, optional CPU/GPU targets and abort fields, live temps/power, fan group list (duty · RPM). Holding copy is “Holding a policy for this PC.”
- Home tagline in `MainViewModel`: “Test this PC, then hold a quieter or cooler fan policy while this window is open.”
- **What we learned** is a separate tab: `MeasuredRpmChart` (RPM across, settled °C up) plus labeled text rows. Not on Home, not editable, not temp→duty.
- No XAML/control exists for a temperature→duty editor. Grep finds `FanSpeedCurve` only as RPM vs °C evidence.

**What is written to fans**

- `PolicyOptimizer.Recommend` builds a `CoolingPolicy` of `GroupPolicy` records: `QuietDutyPercent`, `CoolDutyPercent`, blended `DutyPercent`, optional `AppliedRpm`.
- Quiet end = BIOS duty at the fan-test reference, or the 1 °C plateau RPM mapped back to duty.
- Cool end = the mapped “useful max” RPM, or the influence `DutyAfter`.
- Blend: `BlendDuty(quiet, cool, QuietCool)` — linear interpolation of two integers.
- Conservative cancel/abort: blend clamped to 0.25.
- `PolicySession.Start` writes each group’s blended duty through `SafeFanSession`. `Tick` walks ±5% between quiet and cool from power, over-target temp, or hotter-than-test. It never looks up duty from a temperature curve.

**Citations:** `PolicyOptimizer.cs`, `CoolingPolicy.cs`, `GroupPolicy.cs`, `PolicySession.cs`, `MainWindow.xaml`, `MainViewModel.cs` (`HomeTagline`, `IsHoldingPolicy`), `OptimizeWalkViewModel.cs` (`HoldDetail`: “Apply the quiet-versus-cool setting chosen on Home”).

### 1.2 Experiments measure duty → ΔT at one heat; they do not observe a temperature range

**Plain language:** During tests the PC is held at one “heat lamp.” AUTO Fan turns one fan faster and waits. Temperature drops a little (or doesn’t). That is “at this heat, more speed → this many degrees cooler.” A FanControl curve answers a different question: “when the CPU is 45 °C, 60 °C, 80 °C, what duty should this fan run?” Those are not the same dataset. You cannot honestly stretch a handful of points around ~60 °C into a full-range curve.

**Technical**

Let heat \(H\) be the frozen Low profile. For group \(g\) at duty \(d_i\), the runner records

\[
T_i = T_{\text{settled}}(d_i \mid H,\ \text{other fans on BIOS}).
\]

`FanSpeedCurveBuilder` stores \((\mathrm{RPM}_i, T_i)\) — an **isobar** (constant heat), X = RPM, Y = temperature.

A Path B curve is \(d = f(T_{\text{sensor}})\) for live control as heat changes. Inverting the isobar would require assuming (a) a unique mapping from \(T\) to \(H\), (b) that BIOS-on-others is the live state, and (c) that \(\Delta T(d)\) measured near 60 °C still holds at 40 °C and at 80 °C. The code does not do that inversion. `PolicyOptimizer` instead picks two duties from BIOS duty + plateau RPM and interpolates.

`ThermalModel.Predict` stores **one** \(\Delta T\) per fan×target — the largest-magnitude duty bucket in `InfluenceMapBuilder.BuildEntry` — then adds pair leftovers for exactly two fans. `PolicyConfirmer.ExpectedSettled` subtracts that full \(\Delta T\) from confirmation-start temperature, **not scaled to the duty actually applied**. Confirmation therefore answers “did the biggest measured step’s cooling appear at whatever heat we have now?” not “does this curve track temperature?”

**Citations:** `FanTestRunner.RunAsync` (`_workload.Set(WorkloadLevel.Low)` once, never changed), `InfluenceMapBuilder.BuildEntry` (best \(\Delta\)), `ThermalModel.Explain` / `Predict`, `PolicyConfirmer.ExpectedSettled`, `FanSpeedCurve` / `FanSpeedPoint` (Rpm, TempCelsius — no duty, no independent temperature axis).

### 1.3 Screen/refine only writes duties **above** current BIOS duty

**Plain language:** Tests never turn a fan down. If BIOS is already loud under the heat lamp, AUTO Fan can only try even louder. The quiet half of a curve is unmeasured.

**Technical:** `FanTestRunner.DutiesAbove` skips every scheduled duty `<= currentDuty`. Screen is `{40, 70, 100}`, refine `{55, 85}` (`FanTestSchedule`). At Low heat, BIOS often already sits high. Live notes: GPU Fan 2 skipped already at 100%; groups at max get `UnknownForGroup(..., AlreadyAtMaxDuty)`.

Consequence for Path B: even a “seed from settled points” graph has no points below BIOS-at-test-load. You cannot claim a quiet starting curve from data that only probed louder-than-BIOS.

**Citation:** `FanTestRunner.DutiesAbove`, `FanTestReasons.AlreadyAtMaxDuty`, `FanTestSchedule.ScreenDuties` / `RefineDuties`.

### 1.4 No curve type, no curve persistence, no Home editor

There is no `TemperatureDutyCurve` (or equivalent) in Core. `FanSpeedCurve` is RPM→°C evidence for diminishing returns. SQLite has no curve table. Settings JSON stores Quiet–Cool blend, targets, abort fields, preferred GPU, hide-unused, connected/empty header ids — not curve points. Hold applies `CoolingPolicy`, which is not reloaded as a curve after restart (restart restores BIOS).

**Citations:** `JsonUserSettingsStore.UserSettingsDto`, SQLite schemas in `SqliteFanTestStore` / `SqliteBaselineStore` / `SqliteInteractionStore` / `SqlitePolicyConfirmationStore`.

### 1.5 BIOS → AUTO → BIOS on locked heat is not built

Path B’s validation bar is: same frozen heat, BIOS settle, apply AUTO, settle, return to BIOS, settle; AUTO should be cooler at similar effort or as warm at less effort; return-to-BIOS should match the first BIOS reading above wander.

What exists: `PolicyConfirmationRunner` after Hold, heater **already stopped** (`FanTestRunner` / `InteractionRunner` call `_workload.Stop()` before return). Expected values come from `Predict()`, floored so GPU cannot go below idle/start. A miss adds 5% duty. Periodic re-confirm while holding (30 min or first sustained gaming/render/mixed) is the same check, still not A/B/A on locked heat.

Live history (docs): GPU 39.0 vs 39.2 was the **floor**, not a measured ~6 °C drop. CPU was cooler than predicted and did not count as a miss (`IsMiss` is only hotter-than-expected).

**Citations:** `PolicyConfirmer.cs`, `PolicyConfirmationRunner.cs`, `MainWindow.ConfirmAppliedAsync`, `App.md` (“It is not built yet”).

### 1.6 Confirmation heat ≠ experiment heat

**Plain language:** Fan tests run with the heat lamp on. Hold and confirmation run with the lamp off. Comparing those temperatures is like checking a recipe in a cold kitchen after you cooked it in a hot one.

**Technical:** Watch calibrates and freezes `HeatProfile` on `IWorkloadActuator.LockedLow`. Fan tests `Set(Low)` which reapplies `LockedLow` in-process. Then workload stops. `ApplyRecommendedPolicyAsync` starts `PolicySession` with no actuator. Confirmation-start GPU in the documented run was ~42 °C vs fan-test GPU ~53 °C. `Predict` still subtracted fan-test \(\Delta T\)s. v1.10 added a GPU floor so the guess cannot go below idle; that hides the mismatch rather than measuring at \(H\).

`HeatProfile` is **not persisted**. After process restart, `Set(Low)` uses `HeatProfile.DefaultLow` (4 GPU passes), not the Watch-calibrated profile. Advanced “Run fan tests” without a same-process Watch has the same problem.

**Citations:** `SyntheticWorkloadActuator.Set` / `ApplyLowLocked`, `SqliteBaselineStore` (no heat-profile columns), `HeatProfile.DefaultLow` vs `StartingLow`.

---

## 2. Experiment design risks (why data might not support full-range curves)

### 2.1 Heat lock — what works, what does not

**Matches Path B**

- Watch is calibrate-then-freeze: idle → everyday → `HeatCalibrator.RunAsync` raises GPU work per frame (`GpuPasses` then `GpuIterations`, cap 16×256, max 6 increases, 30 s/step or settle) until GPU Core is ≥ 15 °C above idle **or** within 8 °C of the GPU abort **or** the cap. CPU workers stay Everyday (`HeatProfile.EverydayCpuWorkers`). No extra CPU threads. No `Present(0)` (hardware: `Present(1)` in `GpuWorkload`).
- If GPU cannot rise 15 °C, Watch still completes. `GpuHeat.IsUseful` is false. Fan tests skip NVIDIA GPU writes (`FanTestReasons.GpuHeatInsufficient`) and mark GPU-target influence Unknown via `InfluenceMapBuilder.WithoutGpuTargets`. CPU-target tests still run.
- Missing idle GPU during calibration is telemetry abort, not a fake rise.
- After freeze, three BIOS settle-holds (`ReferenceStability.HoldCount = 3`) label CPU/GPU STABLE / DRIFTING / NOISY / UNAVAILABLE. One bad sensor does not abort Watch. MDE = `max(0.5 °C, range × 2)`. Non-STABLE targets become Unknown (`ReferenceStability.WithoutUnusableTargets`). Δ below MDE is None.

**Risks**

- Idle / everyday / Low **phases** are fixed kitchen timers (`BaselineSchedule`: 45 / 60 / 60 s), not settle-by-measurement. Only calibration steps and the three reference holds wait for `TemperatureSettle`. Idle GPU used as the +15 °C baseline is the last idle sample, not a settled idle.
- Calibration can freeze because of the **ceiling margin** (abort − 8 °C) without meeting +15 °C. That still finishes Watch; GPU mapping stays Unknown only if stored `GpuRiseCelsius` < 15. If rise is ≥ 15 but the heater is near the abort, later fan tests are fragile.
- Heater is not persisted (see 1.6). Same-process Optimize walk is the only path that reuses the calibrated lamp.
- Isolated GPU interleave (`R → speed → R`) is parked. Causal validity from power/clock is parked. A GPU \(\Delta\) can still be mixed with BIOS fan motion and power wander.

**Citations:** `BaselineRunner.cs`, `HeatCalibrator.cs`, `GpuHeat.cs`, `ReferenceStability.cs`, `BaselineSchedule.cs`.

### 2.2 Settling

**Matches Path B:** Fan-test and pair dwell is settle-by-measurement, not a second kitchen timer. `TemperatureSettle.RelevantTempsSettled`: last 5 samples of preferred CPU and GPU, range ≤ 1.0 °C (`SettleBandCelsius * 2`). Timeout 90 s. Abort ceilings and rate-of-rise still apply. Stalled / 0 RPM points are not fitted (`IsStalled`, `FanSpeedCurveBuilder` drops `Rpm is not > 0`).

**Risks**

- Timeout is treated as success in the runner: `if (settled || now >= deadline) return null`. The sample series is stored either way. There is **no `settled` flag** on `FanTestSample`.
- `FanSpeedCurveBuilder` later drops holds that never settled. **`InfluenceMapBuilder` does not.** Influence \(\Delta\) and the policy’s duty map can be built from a 90 s still-moving hold. Pair `InteractionBuilder.Delta` is the same: tail average vs reference average, no settle gate.
- Reference (BIOS) samples are included in influence \(\Delta\) even though v1.14 removed them from the RPM graph. Mixing BIOS leftover with a perturb is the original spike bug, still possible on the map that Hold uses.
- Settle window is 5 one-second samples (5 s of “not moving by 1 °C”). Slow thermal systems can look settled while still creeping.
- Watch Low phase is 60 s fixed; only the three later holds settle-by-measurement.

**Citations:** `FanTestRunner.SampleAsync` (timeout OR settled), `TemperatureSettle.cs`, `ThermalDynamics.SettleWindowSamples`, `FanSpeedCurveBuilder.Points` (requires settled), `InfluenceMapBuilder.BuildEntry` (no settle check).

### 2.3 Sample design — too few points, all at one heat, only louder than BIOS

At best, a group that moved a temperature is commanded at 40, 70, 100 then 55, 85 — **five duties**, and only those **above BIOS**. `DutiesAbove` plus `testedDuties` also skip duplicates. Plateau analyzer requires **three** valid RPM points (`DiminishingReturnsAnalyzer.MinimumPointCount`). Live v1.14 notes: hub ~0.05 °C; CPU RPM graph went the wrong way. Five isobars near one heat are not a temp-range curve and may not even be a trustworthy RPM plateau if BIOS already ate the lower duties.

`InfluenceMapBuilder` then **throws away the multi-speed information** for the model: one best \(\Delta\), one `DutyAfter`. Pairs use a single +25% step (`FanTestSchedule.DutyStepPercent`), not the refine grid.

Hubs: one header is one group (correct). Flicker: builder starts a new hold on a ≥10% duty jump and skips one-sample stages. That protects the graph, not the influence map.

### 2.4 Pairs / interactions

**When they run:** Only if individual tests `Completed` and `HasUsefulInfluence` (`OptimizeWalkOutcome.AfterFans` → `RunPairs`). Thermal abort or cancel with a measured group → `SkipPairs`; user may Continue to Hold. Lost temps, GPU reset, competing software → Retry, not Hold (`IsStopWalkAbort`). No GPU–GPU pair on the same card (`FanWriteCandidates.AreCoupled`). Max 3 pairs (`InteractionSelector.MaxPairs`), including slight same-target effects. Pumps / AMD-Intel GPU / no-tach skipped.

**Stored:** `interaction_run` / `interaction_sample` / `interaction_entry` (first/second/combined \(\Delta\), residual, evidence, inferred note) / `interaction_skip`. `InteractionBuilder.InferNote` is Inferred airflow guess, not Measured pressure.

**Unknown vs none:** Skipped pairs → writeup `PairsSkipped` = Unknown. Measured residual ≈ 0 → “No leftover” Measured. `ThermalModel.Explain` applies leftover only for **exactly two** fans; three or more sum singles only. Unknown influence contributes nothing (`IsMeasured` requires Measured + delta).

**Risk:** Pair \(\Delta\)s are also one-heat, one +25% step, no settle flag. They do not produce combination curves.

### 2.5 Diminishing returns / 1 °C plateau

`DiminishingReturnsAnalyzer`: quietest measured RPM within 1.0 °C of the coolest point. Needs ≥ 3 finite RPM points. Evidence stays Measured if the curve was Measured; too few points → Unknown. Tests cover the App.md 600–1200 RPM table (recommend 900, never 1200).

**Risk:** Points are RPM vs °C at fixed heat. Plateau means “at this heat, more RPM stops dropping T.” That is useful evidence. It is not “this is the right duty at 80 °C.” Policy uses the plateau only to set the two-end duties (`PolicyOptimizer.TryBuildGroup` / `DutyForRpm`).

Live graph: `MeasuredRpmPlot` + `MeasuredRpmChart` — settled Screen/Refine holds only, no live marker, no `Predict()`, not a BIOS temp→duty graph. Caption: “Each dot is a settled temperature at that fan speed.” Honest as evidence; easy to misread as a fan curve. Path B docs already say it is not.

### 2.6 Trust labels

`MetricEvidence`: Measured, Unknown, Inferred, Modeled. Enforced in Core records (`InfluenceEntry`, `InteractionEntry`, `ExplanationClaim`, `FanSpeedCurve`, `DiminishingReturnsBand`, `BaselineMetric`). `ExplanationReportBuilder` tags paths Inferred, deltas Measured, recommended duties Modeled, RPM-as-noise Inferred, skipped pairs Unknown, insufficient GPU heat Unknown, non-STABLE targets Unknown. UI appends `· {Evidence}` on What we learned rows.

**Gaps**

- Home holding UI does not show labels; it shows policy copy.
- No fourth product-facing “Modeled vs Inferred vs Measured vs Unknown” on a curve, because there is no curve.
- Influence can be tagged Measured even if the hold timed out (2.2).
- Gaming-from-Low is Modeled in writeup; live `PolicySession` still raises toward cool end from power as if that map applies.

### 2.7 Confirmation GPU prediction

Still a floor, not a GPU cooling proof:

- Subtract full modeled \(\Delta\) from **confirmation-start**, which is post-test, heater off.
- `FloorGpu`: expected ≥ idle if start ≥ idle; expected ≥ start if already cooler than idle.
- Extra 5% on miss does not flip GPU policy to trusted (writeup says so; there is no `GpuPolicyTrusted` flag — GPU generated curves are simply not built).
- `ConfirmationDiagnosis` rebuilds the stored miss for debugging; it is not shown in the main UI.

CPU miss is only “hotter than predicted by 2 °C.” Cooler-than-predicted (live: 52 vs 77.8) is silent success.

### 2.8 Safety — mostly intact; a few Hold-path holes

**Keep**

- Floors: CPU 90 / GPU 83 / other 95 / duty 0–100. User abort fields clamp 65–90 / 65–83 (`ThermalAbortLimits.FromUser`). Targets clamp to abort − 1.
- `SafeFanSession`: competing-software block, thermal evaluate, restore on dispose/abort. Dirty file `software-control.active` + `--watchdog` child (`ProcessControlWatchdog` / `WatchdogLoop`) + `CrashRestore` on next launch (`App.OnStartup`). Motherboard `SetDefault`, NVIDIA `RestoreCoolerSettingsToDefault`.
- Fan tests / pairs / detect / window close / dispatcher and domain unhandled: `RestoreDefaults` + `workload.Stop`.
- Rate-of-rise 3 °C/s over 3 s, armed only within 5 °C of the effective ceiling (`ThermalTrend`).
- Experiment 30-minute cap on `FanSessionKind.Experiment`. Policy kind skips that timer (intentional Hold).
- NVIDIA write failures skip the group; they do not throw. LHM must not `SetSoftware` on GPU nodes (GPU path is NVAPI).
- AMD ADLX parked.

**Holes (do not loosen floors; these are detection/restore gaps)**

1. **Hold does not run `EvaluateWhileHeating`.** `SafeFanSession.EvaluateSafety` uses `SafetyLimits.Evaluate` + trend, not telemetry-lost / GPU-device-lost. `PolicySession` has no `IWorkloadActuator`. Preferred CPU/GPU null is treated as “under target” and may **quiet** fans (`NudgeDirection`: `cpu is null || cpu <= target - 5`). Fan tests abort on lost temps; Hold may not.
2. **Confirmation and Hold are not on the locked heat** — not a ceiling hole, but a product-safety/honesty hole: users think the held policy was confirmed under the test load.
3. **Timeout samples in the influence map** can feed Hold as if they were settled.
4. Watchdog restore is best-effort (swallows exceptions per header). Cannot be proven on this Cloud VM.
5. `OnWalkCancel` before tests start restores; after tests start it **applies policy** (`ApplyRecommendedPolicyAsync`) rather than only restoring — matching “cancel still helps,” then confirmation. Close/crash still restore.

---

## 3. What already matches (keep)

These are Path B-compatible building blocks. They should not be ripped out. They are not the Path B product by themselves.

| Piece | Where | Why keep |
| --- | --- | --- |
| Optimize walk: Consent → Watch → Fans (pairs inside) → Hold | `OptimizeWalkViewModel`, `MainWindow.OnWalkPrimary` | Right sequence; extra Advanced buttons are retries |
| Watch calibrate-then-freeze; CPU Low = Everyday workers | `HeatCalibrator`, `HeatProfile`, `SyntheticWorkloadActuator` | Repeatable lamp (in-process) |
| +15 °C GPU gate; Unknown + no NVIDIA mapping if unmet | `GpuHeat`, `FanTestRunner` ctor `gpuHeatUseful` | Honesty |
| 3× BIOS holds, STABLE/DRIFTING/NOISY/UNAVAILABLE, MDE | `BaselineRunner.RunReferenceHoldsAsync`, `ReferenceStability` | Wander gate |
| Settle-by-measurement + 90 s timeout + stall skip | `TemperatureSettle`, `FanTestRunner.SampleAsync` | Right dwell idea (timeout flag missing) |
| Screen then refine; one group at a time; restore between | `FanTestSchedule`, `SafeFanSession` per write | Restore floor |
| Experiment unit = motherboard header or NVIDIA card set | `FanWriteCandidates`, `FanPresenceRunner` | Matches App.md |
| Pairs only after finished individuals; slight effects eligible; skip after thermal abort | `OptimizeWalkOutcome`, `InteractionSelector` | Matches App.md §4 |
| Additive model + measured pair leftover for two fans; Unknown = 0 | `ThermalModel.Explain` | Simple, honest stacking |
| RPM vs settled °C graph from settled holds only | `FanSpeedCurveBuilder` v1.14, `MeasuredRpmPlot` | Evidence graph |
| 1 °C plateau, quietest point | `DiminishingReturnsAnalyzer` | Evidence rule |
| Trust tags on explanation claims | `ExplanationReportBuilder`, `MetricEvidence` | Required for Path B |
| Cancel → quieter conservative policy if something was measured | `PolicyOptimizer` conservative blend 0.25 | “Cancel still helps” |
| Restore BIOS + NVIDIA on Stop/close/abort/crash | `SafeFanSession`, `BiosFanRestorer`, `CrashRestore` | Non-negotiable |
| Case priors only reorder screening | `ScreenOrderer`, `CasePriorCatalog` | Must not skip measurements |
| No GP / Math.NET / mic / planner | Core search is discrete pick | Stay that way |

Walk persistence today:

| Step | Persisted | UI | Fan writes |
| --- | --- | --- | --- |
| Find connected (Home, before Optimize) | `settings.json` connected/empty ids | Checklist | 100% then restore |
| Watch | `baseline_run` / `baseline_sample` / `baseline_metric` | Walk status; Advanced baseline rows | None |
| Individual tests | `fan_test_run` / `fan_test_sample` / `influence_entry` / `fan_test_skip` | Walk status; Advanced fan-test rows | One group at a time; restore after each session dispose |
| Pairs | `interaction_*` | Walk status; Advanced interaction rows | Two groups max; restore after |
| Hold | none as a policy blob; confirmation rows | Home holding panel | Blended duties; Tick nudges |
| Confirmation | `policy_confirmation` (temps, miss, extra airflow, ambient) | What we learned confirmation line | +5% on miss |
| Restore | dirty flag cleared | BIOS caption | `RestoreDefaults` |

---

## 4. Data collection quality

### What is stored

**SQLite** (`%LocalAppData%\AUTO Fan\autofan.db`)

- `baseline_run`: status, abort, ambient, gpu_load_available.
- `baseline_sample`: phase (Idle/Everyday/Low/Reference/Cooldown; High unused in live Watch), **full `HardwareSnapshot` JSON**.
- `baseline_metric`: named metrics with evidence (rises, decay, settle seconds, three holds, range, MDE, state).
- `fan_test_run` / `fan_test_sample` (group, **stage**, snapshot JSON) / `influence_entry` (delta, effect, evidence, duty/rpm before/after, skip) / `fan_test_skip`.
- `interaction_run` / `interaction_sample` (step) / `interaction_entry` / `interaction_skip`.
- `policy_confirmation`: measured/expected CPU/GPU, missed, added_airflow, ambient. **No applied duties, no RPM, no heat profile, no group list.**

**JSON** `settings.json`: QuietCool, targets, abort fields, PreferredGpuId, hide unused, connected/empty header ids.

**Snapshot JSON** can include whatever `SensorTreeMapper` mapped: temps (CPU Tctl/Tdie, GPU Core preferred), power, clocks, load, duty, RPM (`SensorKind` enum). Ambient is first-found or unknown. That is enough raw telemetry **if** later code uses it. Most consumers only read preferred CPU/GPU temps and the tested fan’s duty/RPM.

### Distinguishable settled holds?

Partially.

- Stage enum (`Reference` / `Perturb` / `Screen` / `Refine`) lets the RPM graph ignore BIOS leftovers.
- Settle is recomputed from the snapshot series; timeout vs settled is **not** stored.
- Hub flicker: 10% duty jump splits holds; one-sample Screen snapshots are dropped from the graph, not from influence.
- Influence map still averages reference (possibly unsettled) vs best speed bucket (possibly timed-out).

### Gaps for Path B curves

1. No per-sample settled / timed-out bit.
2. No frozen `HeatProfile` (workers, passes, iterations) on the baseline row.
3. No independent temperature-axis observations (only \(T\) at locked \(H\)).
4. No stored temp→duty polyline.
5. Confirmation row cannot reconstruct applied policy or whether the lamp was on.
6. Clocks/power exist in snapshots but are not used to reject invalid \(\Delta\) (causal validity parked).
7. `GetLatest()` only — no run-to-run join for BIOS A/B/A.
8. Influence collapses multi-duty data to one \(\Delta\).

---

## 5. Tests

Automated tests live in `tests/AutoFan.Core.Tests` (fake hardware, no real fans). They cover a lot of Path A logic. They do not cover Path B curves because those types do not exist.

### Covered (keep)

| Area | Tests |
| --- | --- |
| Settle definition | `TemperatureSettleTests` (moving / flat / too few) |
| Screen writes 40/70/100, restore between groups, skip pump/AMD, one NVIDIA representative | `FanTestRunnerTests` |
| GPU heat insufficient → skip NVIDIA + Unknown GPU targets | `FanTestRunnerTests`, `InfluenceMapBuilderTests` |
| Abort restores partial map | `FanTestRunnerTests` |
| Walk Continue / Retry / SkipPairs; telemetry/GPU-reset retry | `OptimizeWalkOutcomeTests` |
| MDE / STABLE / WithoutUnusableTargets | `ReferenceStabilityTests` |
| Heat calibrate-then-freeze, Everyday CPU workers, cap without abort | `HeatCalibratorTests`, `BaselineRunnerTests` |
| Pairs including slight; coupled GPU skip | `InteractionSelectorTests`, `InteractionRunnerTests` |
| Residual ≠ sum; inferred note not in measured columns | `InteractionBuilderTests` |
| Predict two-fan leftover; 3+ singles only; Unknown = 0; Low-only not High | `ThermalModelFitterTests` |
| Plateau 1 °C; two points Unknown | `DiminishingReturnsAnalyzerTests` |
| Graph: no BIOS leftover, no flicker, no 0 RPM | `FanSpeedCurveBuilderTests` |
| Two-end blend; conservative 0.25 on abort | `PolicyOptimizerTests` |
| Policy tick toward cool from power/temp; restore | `PolicySessionTests` |
| Confirmation miss +5%; GPU floor | `PolicyConfirmerTests` |
| Explanation tags Measured/Modeled/Inferred/Unknown | `ExplanationReportBuilderTests` |
| Restore on dispose/abort/competing software; crash dirty flag | `SafeFanSessionTests`, `CrashRestoreTests` |
| Abort floors cannot be raised | `ThermalAbortLimitsTests`, `SafetyLimitsTests` |
| Rate-of-rise arms near ceiling only | `ThermalTrendTests` |
| SQLite round-trip of samples/influence/confirmation | store tests |

### Untested that Path B needs

- Any temperature→duty curve builder, editor, persistence, or “seed from isobar” honesty check.
- Influence/pairs **rejecting** timed-out unsettled holds (today they accept them).
- `DutiesAbove` when BIOS is already 70–100% (few or zero points).
- Confirming on locked heat vs heater-off (the actual live mismatch).
- BIOS → AUTO → BIOS protocol.
- `Predict` scaled to applied duty (today: full best \(\Delta\)).
- Hold behavior when preferred temps disappear (possible quiet-nudge).
- Persist/reload of `LockedLow` across process restart.
- Editable Home UI (no control to test).

### What only a local Windows PC can show

This Cloud VM cannot run `net10.0-windows` hardware, PawnIO, NVAPI, or a real Optimize. Only a local elevated run can show:

- Whether Watch GPU Core actually rises ~15 °C on this machine (it did on the documented live runs).
- Whether BIOS duty under Low heat leaves any screen points below 100%.
- Whether 90 s is enough to settle this case.
- Whether the RPM graph still goes the wrong way (v1.15: CPU curve wrong; hub ~0.05 °C).
- Watchdog restore after kill.
- BIOS → AUTO → BIOS on the same lamp.
- NVIDIA `RestoreCoolerSettingsToDefault` vs driver version.

---

## A. Mental model vs code (walk map)

```text
Home: Find connected (optional checklist) → Optimize (modal walk)
  1. Consent
  2. Watch this PC     → BaselineRunner
  3. Test fans         → FanTestRunner then maybe InteractionRunner
  4. Hold this setting → PolicyOptimizer + PolicySession + PolicyConfirmationRunner
Home while holding: Stop restores BIOS + NVIDIA driver
What we learned tab: MeasuredRpmChart + ExplanationReport
```

### Watch

- **Types:** `BaselineRunner`, `HeatCalibrator`, `HeatProfile`, `ReferenceStability`, `BaselineRun` / `BaselineSample` / `BaselineMetric`.
- **Persisted:** SQLite baseline tables. Frozen profile only in memory (`LockedLow`).
- **UI:** Walk “Watch this PC”; Advanced “Run baseline.”
- **Writes:** none.

### Individual fan tests

- **Types:** `FanTestRunner`, `FanTestSchedule`, `SafeFanSession`, `InfluenceMapBuilder`, `ScreenOrderer`.
- **Persisted:** samples + influence + skips.
- **UI:** Walk “Test fans”; Advanced retry.
- **Writes:** duties above BIOS on one representative group; restore on session dispose.

### Pairs

- **Types:** `InteractionRunner`, `InteractionSelector`, `InteractionBuilder`.
- **Persisted:** interaction tables. Skipped entirely if individuals did not finish (`_walkPairsSkipped` forces `CurrentInteraction()` null so Hold does not reuse yesterday’s leftovers).
- **Writes:** A, then B, then A+B, +25% vs current; restore after.

### Hold / apply

- **Types:** `ThermalModelFitter`, `FanSpeedCurveBuilder`, `DiminishingReturnsAnalyzer`, `PolicyOptimizer`, `PolicySession`.
- **Persisted:** not the policy. Settings JSON already has the slider.
- **UI:** Home holding panel; slider disabled (`CanEditPriorities = false`).
- **Writes:** blended quiet/cool duties; 5% steps in `Tick`.

### Confirmation

- **Types:** `PolicyConfirmer`, `PolicyConfirmationRunner`, `IPolicyConfirmationStore`.
- **Persisted:** confirmation row.
- **UI:** What we learned “Confirmation” claim.
- **Writes:** +5% all groups on miss.

### Restore

- **Types:** `SafeFanSession.Restore`, `LibreHardwareMonitorBackend.RestoreDefaults`, `NvidiaFanWriter.RestoreAll`, `BiosFanRestorer`, `CrashRestore`, `SoftwareControlLease`.
- **UI:** “Stop, close, or a temperature limit puts fans back on BIOS and the NVIDIA driver.”

**Explicit answer:** The live deliverable today is a Quiet–Cool **policy** (two-end duties / interpolation), not user-visible **editable multi-point temp→duty curves** on Home. Files: `PolicyOptimizer.cs`, `PolicySession.cs`, `GroupPolicy.cs`, `MainWindow.xaml` (no curve editor), `MainViewModel.HomeTagline`, `OptimizeWalkViewModel.HoldDetail`. The RPM chart is evidence, on the What we learned tab (`MeasuredRpmChart.xaml`), axes RPM × °C.

---

## 6. Recommended next proof slices (plans only — do not implement here)

Smallest first. No GP, Bayesian, mic, planner, extra heater, or abort loosening. Isolated GPU only if a later slice proves the lamp cannot map GPU fans honestly.

1. **Honesty gate on stored holds.** When a 90 s timeout fires, mark the hold unsettled. `InfluenceMapBuilder` and `InteractionBuilder` must not emit Measured \(\Delta\) from unsettled series (same rule `FanSpeedCurveBuilder` already uses). Fixture test: still-moving timeout ≠ Measured influence. No UI.

2. **Persist the frozen heat lamp** on `baseline_run` (workers, GPU passes/iterations). Fan tests must refuse to heat, or show Unknown, if `LockedLow` is missing. Process restart must not silently use `DefaultLow`.

3. **Do not claim Path B from a Home graph of today’s points.** If a later slice seeds a picture, label it **Measured RPM → settled °C at locked Low heat** (existing graph), or an **Inferred** temp→duty sketch with almost no points — never Measured full-range curves. Prefer putting that evidence on Home next to Optimize without inventing duties at untested temperatures.

4. **Measure quieter than BIOS at the same lamp** (still one heat). Allow commanded duties below current BIOS during tests (restore-on-exit stays). Until this exists, the quiet side of any curve is a guess.

5. **Scale `Predict` to applied duty** (or stop confirming against the full best \(\Delta\)). Confirmation expected must match what was actually written, at the heat that was actually on.

6. **Confirmation on the locked lamp** (heater stays at `LockedLow` through apply-and-settle). Still not full BIOS A/B/A. This is the smallest check that confirmation means anything.

7. **BIOS → AUTO → BIOS** on that same lamp, using wander/MDE as the match band. This is the Path B validation bar. Do not advertise curves until it exists.

8. **Only then** a reviewed plan for a Home editor that stores user-editable polylines, with trust tags per segment, Quiet–Cool regenerating a first draft, manual edits winning. If data still only covers one heat, segments outside the measured °C band stay Unknown.

Slice 1–2 are data-quality floors. Slice 3 prevents a misleading UI. Slices 4–7 are the experiment proof. Slice 8 is the Path B product.

---

## 7. What this audit did not run

- No live Optimize, Watch, fan writes, or BIOS A/B/A.
- No Windows hardware, PawnIO, NVAPI, or watchdog kill test.
- No `dotnet test` / `dotnet build` on this Linux Cloud VM (`net10.0-windows` WPF). Test **sources** were read; results were not executed here.
- No reading of a live `%LocalAppData%\AUTO Fan\autofan.db` from Danny’s PC (findings about 39.0 vs 39.2, +15.1 °C GPU, CPU curve wrong way come from `IMPLEMENTATION.md` decision log, not a fresh dump).
- No product-feature implementation and no safety-limit changes.

---

## File index (primary)

| Concern | Files |
| --- | --- |
| Walk | `src/AutoFan.App/ViewModels/OptimizeWalkViewModel.cs`, `src/AutoFan.App/MainWindow.xaml.cs`, `src/AutoFan.Core/OptimizeWalkOutcome.cs` |
| Policy vs curves | `PolicyOptimizer.cs`, `PolicySession.cs`, `CoolingPolicy.cs`, `GroupPolicy.cs`, `MainWindow.xaml`, `MainViewModel.cs` |
| Watch / heat | `BaselineRunner.cs`, `HeatCalibrator.cs`, `HeatProfile.cs`, `SyntheticWorkloadActuator.cs`, `GpuHeat.cs`, `ReferenceStability.cs` |
| Fan tests | `FanTestRunner.cs`, `FanTestSchedule.cs`, `InfluenceMapBuilder.cs`, `FanSpeedCurveBuilder.cs` |
| Pairs | `InteractionRunner.cs`, `InteractionSelector.cs`, `InteractionBuilder.cs` |
| Model / confirm | `ThermalModel.cs`, `ThermalModelFitter.cs`, `PolicyConfirmer.cs`, `PolicyConfirmationRunner.cs` |
| Evidence UI | `ExplanationReportBuilder.cs`, `MeasuredRpmPlot.cs`, `MeasuredRpmChart.xaml` |
| Safety | `SafetyLimits.cs`, `SafeFanSession.cs`, `ThermalTrend.cs`, `CrashRestore.cs`, `BiosFanRestorer.cs`, `NvidiaFanWriter.cs` |
| Storage | `Sqlite*.cs`, `JsonUserSettingsStore.cs` |
| Product docs | `App.md`, `IMPLEMENTATION.md` Current snapshot (Path B), `AGENTS.md` |

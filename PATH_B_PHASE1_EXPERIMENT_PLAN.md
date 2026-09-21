# Path B Phase 1 — Experiment foundation plan

**Date:** 2026-09-21  
**For:** Pike review (Ship / Ship after fixes / Do not ship)  
**Status:** Plan only. Do not implement product C#/XAML in this pass.  
**Baseline:** [PATH_B_CODE_AND_EXPERIMENT_AUDIT.md](PATH_B_CODE_AND_EXPERIMENT_AUDIT.md) (PR #2). Product truth: [App.md](App.md) Path B on `master`.  
**Locked (Danny 2026-09-21):** Phase 1 = honesty fixes + multi-heat experiment so data can support real temperature → duty curves. **No Home curve editor.** Phase 2 (editor + evidence-on-curves) is a sequel only.

Abort floors stay **CPU 90 °C / GPU 83 °C / other 95 °C**. Restore-on-exit stays. No GP / Bayesian / mic / planner.

---

## A. Goal of Phase 1

**Plain language:** After Phase 1, Optimize still looks like today’s walk (Watch → test fans → Hold). What changes is the **data it collects**. We will have settled measurements at more than one heat, including fan speeds quieter than BIOS, with honest labels (Measured vs Unknown). That dataset is enough that Phase 2 can draw a real FanControl-style graph — temperature across, duty up — without inventing points for temperatures we never saw.

**“Done” for the experiment/data layer (Phase 2 may start only after this):**

1. Each stored fan hold is either **settled Measured** or **not Measured** (timeout / stall / non-STABLE target). Hold and influence cannot launder a 90 s still-moving series as a fact.
2. The frozen heat lamp(s) are **saved with the run** (CPU workers, GPU work per frame). Fan tests reuse that exact lamp. Restart does not silently fall back to `HeatProfile.DefaultLow`.
3. Optimize observes **at least two frozen heats** (Everyday and Low) plus an **idle BIOS observe** (no fan writes at idle). Preferred CPU (and GPU when the +15 °C gate passes) actually sit in different temperature bands, not a cluster around one load.
4. At those heats, tests may command duties **below and above** BIOS, then restore. Quiet-side points exist or are explicitly Unknown (stall / abort), not guessed.
5. Confirmation runs **with the same Low lamp on**, and the expected temperature matches the **duties actually applied**, not the largest historical Δ.
6. SQLite can answer: for fan group G and sensor S, the Measured `(temperature, duty, RPM, heat id)` points. A small Core builder emits those points. **No Home editor, no live curve actuator.**
7. Cancel still keeps whatever heats/groups finished. Stop / abort / crash still returns motherboard BIOS + NVIDIA driver.
8. Live Hold may still be today’s Quiet–Cool **policy** built from the Low map so the app remains usable. That Hold is not advertised as Path B curves.

Phase 1 does **not** mean the product is validated. BIOS → AUTO → BIOS on locked heat remains the acceptance bar; it is a Pike gate at the end of this sequence, not a Home UI.

---

## B. Current gap (short)

Today Watch freezes one Low heat, then fan tests only write duties **louder than BIOS** at that heat. The app records “at this heat, more speed → this many °C cooler.” That is a **duty → ΔT isobar**.

A FanControl XY plot asks: “when this sensor is 45 °C, 60 °C, 80 °C, what duty should this fan run?” Those temperatures come from **different heat**, not from turning one fan up and down under a single lamp. One isobar’s settled temps sit in a narrow band. Stretching that band into a full-range curve is invention.

Worse: `DutiesAbove` never turns a fan down, `InfluenceMapBuilder` keeps timed-out holds as Measured, `Predict` subtracts the biggest Δ regardless of applied duty, and confirmation runs **after the heater is off**. Even a pretty Home graph of today’s points would be dishonest.

**Files:** `FanTestRunner.DutiesAbove`, `FanTestSchedule.ScreenDuties` `{40,70,100}`, `InfluenceMapBuilder.BuildEntry`, `PolicyConfirmer.ExpectedSettled`, `MainWindow.ConfirmAppliedAsync` (heater already `Stop()`’d).

---

## C. Multi-heat experiment design (core)

### What we are trying to observe

For each writable fan group (motherboard header or one NVIDIA card set) and each usable target (CPU, GPU if the gate passes):

> At heat \(H_k\), with other fans on BIOS/driver, command duty \(d\), wait until preferred temps **stop moving** (or timeout). Store \((T_{\text{cpu}}, T_{\text{gpu}}, d, \mathrm{RPM}, H_k)\) only as **Measured** if it settled.

Idle, Everyday, and Low produce different \(T\) bands on this class of PC (live Watch: idle ~47–49 °C CPU / ~46 °C GPU; Everyday ~58–59; Low ~62 CPU / ~59 GPU when the lamp hit +15 °C). That spread is the **X axis** of a temp→duty plot. Duties at each heat are the **Y axis** candidates.

Phase 1 stores the cloud of Measured points. It does **not** interpolate gaps, pick a polyline, or apply it live. Phase 2 will pick, per heat, a quietest useful duty (1 °C plateau + targets) and connect those few **knots** across heats. Gaps between knots are Modeled, never Measured.

```text
Duty %
 100 │                         ● Low (loud)
  70 │              ● Everyday
  40 │    ● Idle BIOS
  20 │
     └──────────────────────────── Sensor °C
         ~45         ~58        ~65
```

Three Measured knots with a temperature range. That is a real curve seed. One Low isobar is not.

### Heat ladder — three options

#### Option 1 — Three full lamps, full screen/refine at each (reject)

Idle + Everyday + Low, each with 3× BIOS holds, then every header at 5 duties.

- **Honesty:** Highest.  
- **Time:** Roughly 3× today’s fan-test wall clock. Hits `SafetyLimits.MaxExperimentDurationMinutes` (30) on the current `FanTestRunner` clock. First Optimize becomes an ordeal.  
- **Complexity:** High. Quiet probes at Low are most likely to hit 90/83.  
- **Verdict:** Too much for a first Optimize. Do not.

#### Option 2 — Invert one Low isobar into a fake XY plot (reject)

Danny locked this out. Varying duty at one heat does move settled \(T\) a little (quieter → hotter), but that \(T\) is “this heat, this one fan, others BIOS,” not “the PC is in a cooler workload.” Connecting those points as a FanControl curve is the invention we are not doing.

#### Option 3 (recommend) — Two frozen lamps + idle observe; Low-first; slim Everyday

| Heat | Lamp | Fan writes | BIOS wander |
| --- | --- | --- | --- |
| **Idle** | `HeatProfile.Idle` (no synthetic work) | **None.** Store one settled BIOS snapshot: temps, duties, RPMs. | 1 settle-by-measurement (timeout 90 s). Not 3×. Idle is the cool knot, not a characterization of GPU fans. |
| **Everyday** | Existing `HeatProfile.Everyday` (quarter CPU threads, 720p, light GPU work). Not calibrated. | Only groups that **moved a temperature at Low**. Coarse duties including below BIOS. | 1 settled BIOS reference per group (or one shared Everyday BIOS hold at the start of that stage). |
| **Low** | Calibrate-then-freeze as today (`HeatCalibrator` until GPU Core ≥ 15 °C from idle, or cap / 8 °C below GPU abort). CPU workers stay Everyday. | Full screen + refine, **absolute** duties (below and above BIOS). This is still the map that decides who matters. | Keep **3×** BIOS holds (today’s STABLE / DRIFTING / NOISY / UNAVAILABLE + MDE). |

**Calibrate-then-freeze per level:** Only Low is calibrated. Everyday is a fixed second lamp (already in `HeatProfile.Everyday`). Do not add a third GPU-work search. Do not change heater **during** a hold to chase a temperature (App.md already forbids that).

**Order of Optimize “Test fans”:**

1. Cool-down gate (CPU/GPU ≤ 60 °C) as today (`ExperimentStartGate`).  
2. Apply **Low** lamp. Screen/refine all eligible groups (including quieter than BIOS). Restore after each hold.  
3. Stop heat, wait for the 60 °C gate again.  
4. Apply **Everyday** lamp. Probe only groups that moved a temp at Low. Restore after each.  
5. Idle observe can be taken from Watch (already recorded) if that idle window settled; if not, one extra idle settle after tests, still **no writes**.  
6. Pairs: **Low only**, and only if Low individuals **finished**, same selector as today (max 3, slight effects eligible, no GPU–GPU on one card). Do not repeat pairs at Everyday.

Rationale: Low is where effects are visible and where the GPU gate can pass. Everyday is extra **X-axis** coverage on fans we already know matter. Repeating a full screen at Everyday doubles time for little new “who matters” information. Idle writes are a bad deal (little Δ, extra abort risk, little GPU heat).

### Duties at each heat (including below BIOS)

Replace `DutiesAbove` (skip if `duty <= current BIOS`) with an **absolute grid**. Still KISS; not a new planner.

| Pass | Duties | Who |
| --- | --- | --- |
| BIOS reference | No write (or restore, then sample) | Every group we are about to test at this heat |
| Screen | **20, 40, 70, 100** | Low: all eligible groups. Everyday: Low-movers only. Skip 20 if the previous command at 20 stalled (0 RPM). |
| Refine | **55, 85** | Only groups that moved a STABLE target at this heat (existing `MovedATemperature`) |

**Hold order (safety):** BIOS reference first, then **louder** (70, 100), then **quieter** (40, 20), then refine 55/85. If a quiet hold aborts toward 90/83, restore, keep louder Measured points, skip remaining quieter duties for that group. Do not loosen ceilings.

**Per hold:** `SafeFanSession.TrySetDuty` → settle-by-measurement (`TemperatureSettle.RelevantTempsSettled`, 5 samples, 1 °C band) → timeout 90 s. **Timeout ⇒ not Measured.** Stall / 0 RPM ⇒ drop point, do not fit. Duty or RPM did not actually change enough (existing 5% duty / RPM-up rule, generalized to “commanded quieter” as RPM down or duty down ≥ 5%) ⇒ Unknown, not a fake Δ.

NVIDIA GPU fans: still one representative per card; still skipped at a heat where that heat’s GPU rise from idle is **< 15 °C**. Everyday will usually skip NVIDIA writes. Low uses the existing `GpuHeat.IsUseful` on Watch Low rise.

### How points become a temp→duty **candidate** (not a UI)

New Core record (name flexible; keep it boring):

```text
TemperatureDutyPoint(
  FanGroupId, FanGroupName,
  Target,                  // Cpu or Gpu
  TempCelsius,             // settled preferred sensor
  DutyPercent,
  Rpm,
  HeatId,                  // Idle | Everyday | Low
  HeatProfile,             // frozen work
  Settled,                 // bool
  Evidence)                // Measured | Unknown  (Phase 1: no Modeled interpolant)
```

**Measured** only if: hold settled, RPM usable, target STABLE at the heat we use for that claim (Low uses the 3× assessment; Everyday uses Everyday BIOS settle — if Everyday BIOS did not settle, Everyday points are Unknown), GPU points additionally require the +15 °C gate for that heat.

**Unknown:** timeout, stall, non-STABLE, GPU gate fail, skipped quieter abort, no tach.

**Modeled / Inferred:** **not produced in Phase 1.** Phase 2 may linearly connect knots and must tag those segments Modeled.

A `TemperatureDutyPointBuilder` reads stored samples (with heat id + settled bit) and emits the list. Persist the list (new SQLite table or columns on samples — prefer **columns on `fan_test_sample` plus a thin `heat_level` table**, not a new framework). Phase 2 Home editor reads this; Phase 1 tests assert the builder.

Do **not** collapse to one best Δ for this candidate list. Keep `InfluenceMapBuilder` for “who matters” at Low (still needed for refine, pairs, and today’s policy Hold), but **gate it on settled** (honesty slice). Curve candidates are the multi-heat points, not the influence table.

### Pairs / interactions

**Keep at Low only. Do not run across heats in Phase 1.**

Pairs answer “do these two fans add or fight at this heat?” That does not create XY knots. Repeating pairs at Everyday is time we should spend on quieter-than-BIOS and a second heat.

If Low individuals abort/cancel with a partial map: skip pairs (today’s `OptimizeWalkOutcome.SkipPairs`). Pair leftovers stay Unknown. Cancel still helps via the individual Low map + any Everyday points already stored.

### Time budget

Today’s completed live Optimize was ~15 minutes (Watch + Low individuals + 3 pairs + confirmation off-heat).

Rough Phase 1 first Optimize (same PC class, 90 s cap per hold, many holds settle faster):

| Stage | Ballpark |
| --- | --- |
| Watch (unchanged-ish + persist profiles) | ~8–10 min |
| Low screen/refine with extra 20% + below-BIOS | ~8–15 min |
| Everyday slim probes on movers | ~4–8 min |
| Pairs at Low (if individuals finished) | ~4–8 min |
| Confirmation **with Low lamp on** | ~2 min |

**Whole walk may exceed 30 minutes.** That is Optimize wall-clock, not “leave PWM stuck.”

**Do not raise 90/83.** **Do not leave a duty held across heats.**

**Do change how the 30-minute experiment cap applies:** today `FanTestRunner` counts 30 minutes from `startedAt` of the **entire** fan-test run (`SafetyLimits.MaxExperimentDurationMinutes`). A two-heat runner will hit that even though each `SafeFanSession` already restores after a hold.

Phase 1 rule:

- Keep **30 minutes of software control without restore** as a backstop (`SafeFanSession` Experiment kind — already per session, and sessions are per hold).  
- Apply a **per-heat-stage** 30-minute cap, not one cap for Low+Everyday together.  
- Restore between holds stays.  
- If a heat stage hits its cap: persist what settled, skip the rest of that heat, continue to the next heat or to Hold with a partial map (same “cancel still helps” spirit). Thermal abort still does not start a new experiment **kind**; Everyday after a **thermal** abort at Low is a Pike call — **recommend skip Everyday after a thermal abort at Low** (temps already proved unsafe at the hotter lamp). Cancel with a useful Low map may still run Everyday only if the user Continue’s and the 60 °C gate passes; simpler rule for v1: **thermal abort at Low → skip Everyday and pairs, may Hold.** Cancel at Low with measured groups → skip Everyday and pairs unless we have time and the user Continue’s. Prefer **one simple rule:** Everyday runs only if Low individuals **Completed**. Cancel/abort Low → no Everyday, skip pairs, may Hold.

Walk UI: keep one “Test fans” step. Do not add a second walk page. Status text can say which heat is on. No Home editor.

### GPU +15 °C honesty across levels

- Compute rise from **Watch idle GPU Core** to that heat’s BIOS-settled GPU Core (or Low hold).  
- `GpuHeat.IsUseful` today is Watch `GpuRiseCelsius` vs Low. Keep that for **Low NVIDIA writes**.  
- Everyday: almost certainly < 15 °C (live Everyday GPU stayed ~46). **Do not write NVIDIA fans at Everyday. GPU-target Everyday points = Unknown.** CPU-target Everyday points still allowed if CPU is usable.  
- Idle: no NVIDIA writes.  
- If Low never reaches +15 °C: Watch still completes; no NVIDIA mapping; GPU knots stay Unknown. **Do not start Isolated GPU interleave in Phase 1.** Only reopen Isolated GPU if a later live Watch cannot get +15 °C and Phase 2 would otherwise have zero GPU knots — that is a new plan, not this one.

### Safety (non-negotiable)

- Ceilings: CPU 90 / GPU 83 / other 95. User abort fields may only tighten (65–90 / 65–83).  
- Rate-of-rise 3 °C/s, armed within 5 °C of the effective ceiling.  
- Every write through `SafeFanSession`. Dispose/abort/cancel/crash: `RestoreDefaults` (motherboard `SetDefault` + `NvidiaFanWriter.RestoreAll`). Dirty file + `--watchdog` unchanged.  
- Start Everyday/Low fan writes only after the 60 °C gate.  
- Quiet duties can raise temperature; abort restores; partial map kept.  
- Preferred temp lost or GPU device-loss: abort that stage, restore, **retry that walk step** (today’s `IsStopWalkAbort`) — including during Hold (today Hold can treat a missing temp as “under target” and quiet fans; fix that in honesty slices).  
- Heater stops on abort/cancel/exit. Confirmation **holds the Low lamp** until confirm settle finishes, then either stays on for policy Hold-at-heat or stops if we keep today’s unheated Hold — see slice 4. Recommend: confirm with lamp on, then **stop the lamp** and Hold the existing two-end policy as today so we do not invent “live curves” in Phase 1. Restoring fans on Stop still required.

### Persistence

| Data | Where | Notes |
| --- | --- | --- |
| Frozen Everyday + Low `HeatProfile` (CpuWorkers, GpuWidth/Height, GpuPasses, GpuIterations) | New columns or JSON on `baseline_run` | Required. Fan tests load this; missing lamp ⇒ do not heat, do not fake DefaultLow. |
| Idle/Everyday/Low BIOS settle temps + 3× Low holds, MDE, STABLE | Existing `baseline_sample` / `baseline_metric` | Add Everyday GPU/CPU rise metrics if missing. |
| Fan hold: stage, group, **heat id**, **settled bool**, snapshot JSON | `fan_test_sample` extra columns | Snapshot already has sensors/clocks/power/duty/RPM. |
| Influence (Low, settled-only) | `influence_entry` | Still “who matters.” |
| Pairs (Low only) | `interaction_*` | Unchanged shape; skip if Low not Completed. |
| Curve candidates | Derived table `temperature_duty_point` **or** built on read | Prefer build-on-read in Phase 1 (fewer moving parts); add a table only if the builder is expensive. Tests freeze fixtures, not a live DB. |
| Confirmation | `policy_confirmation` | Add: heat profile id, applied duties JSON, lamp-on bool, expected scaled to those duties. |
| Settings JSON | Quiet–Cool, targets, abort, GPU pick, connected headers | Unchanged. **No curve polyline in Phase 1.** |

`IWorkloadActuator.LockedLow` remains the in-process lamp. Watch should `ApplyLow` for Low and also remember Everyday (`LockedEveryday` or a small `IReadOnlyDictionary<HeatId, HeatProfile>`). KISS: two fields on the actuator, not a plugin bus.

---

## D. Honesty prerequisites (must be in Phase 1)

Ordered smallest first. Each is a reviewable code slice. No Home editor.

### D1 — Timeout / still-moving ≠ Measured

- **Goal:** A 90 s timeout is a failed settle, not a fact. `FanSpeedCurveBuilder` already drops unsettled holds; influence and pairs must do the same.  
- **Files:** `FanTestSample` (add `Settled` or infer + persist), `FanTestRunner.SampleAsync` (do not treat `now >= deadline` as success for evidence), `InfluenceMapBuilder.Build`, `InteractionBuilder.Delta`, `SqliteFanTestStore` / `SqliteInteractionStore`, tests `FanTestRunnerTests`, `InfluenceMapBuilderTests`, `FanSpeedCurveBuilderTests`.  
- **Tests:** Fixture still moving at timeout ⇒ influence Evidence Unknown, not a Δ; settled flat window ⇒ Measured.  
- **Done when:** `dotnet test` covers the fixture; no UI change.  
- **Local proof:** Optional. Fake clock is enough.

### D2 — Persist frozen heat lamp; never silent DefaultLow

- **Goal:** `HeatProfile` for Everyday and Low is stored on the baseline run. `FanTestRunner` / `InteractionRunner` apply **that** profile. If missing, do not heat; surface Unknown / retry Watch.  
- **Files:** `HeatProfile`, `BaselineRun`, `SqliteBaselineStore`, `SyntheticWorkloadActuator` (`Set(Low)` today uses `_lockedLow ?? DefaultLow` — delete the silent fallback), `BaselineRunner` persist after `HeatCalibrator`, `MainWindow` pass profile into runners.  
- **Tests:** `SqliteBaselineStoreTests` round-trip profile; `FanTestRunnerTests` asserts `ApplyLow` got the stored profile; missing profile ⇒ no Low heat.  
- **Done when:** Restarting the process cannot run fan tests at `DefaultLow` after a Watch that calibrated 1 pass.  
- **Local proof:** After Watch, kill the app, start fan tests from Advanced — must refuse or re-Watch, not heat with 4 GPU passes by accident.

### D3 — Confirmation expected matches applied duty (still may be off-heat)

- **Goal:** Stop subtracting the full best Δ when Hold applied a quieter blend. Either scale Δ by duty fraction between `DutyBefore` and `DutyAfter`, or confirm only groups we actually wrote and use those duties. Unknown GPU still contributes nothing.  
- **Files:** `PolicyConfirmer.ExpectedSettled`, `ThermalModel.Predict` / `Explain` (optional duty argument — keep it small), `PolicyConfirmerTests`.  
- **Tests:** Blend at quiet end does not predict Low’s full −7 °C GPU drop.  
- **Done when:** Fixture matches.  
- **Local proof:** Not required alone; pairs with D4.

### D4 — Confirmation with the Low lamp on

- **Goal:** After Hold writes duties, keep `LockedLow` applied until settle + miss/extra 5% finishes. Then stop the lamp for Phase 1 live Hold (policy remains two-end, unheated, as today) **or** keep the lamp only for the confirm window. Do not compare fan-test-at-53 °C GPU to idle-ish 40 °C.  
- **Files:** `MainWindow.ApplyRecommendedPolicyAsync` / `ConfirmAppliedAsync`, `PolicyConfirmationRunner`, `IWorkloadActuator`, `SqlitePolicyConfirmationStore` (lamp-on + profile). GPU floor in `FloorGpu` stays as a **sanity clamp**, not the success story.  
- **Tests:** Fake actuator still Low during confirm collect; after confirm, `Stop` is called before returning to idle Hold.  
- **Done when:** Stored confirmation `lamp-on = true` and start GPU is in the fan-test band, not idle.  
- **Local proof:** One Optimize on Danny’s PC: confirmation GPU start ~ fan-test GPU, not ~idle. CPU miss rule unchanged (hotter than expected by 2 °C).

### D5 — Duties below BIOS (still one heat)

- **Goal:** Absolute screen grid `20,40,70,100`; refine `55,85`; louder first then quieter; restore every hold. `AlreadyAtMaxDuty` only if 100 was already commanded and BIOS is 100.  
- **Files:** `FanTestSchedule`, `FanTestRunner.DutiesAbove` (replace), `FanTestRunnerTests` (BIOS at 70 still writes 20/40/100).  
- **Tests:** Fake BIOS duty 70 ⇒ writes include 20, 40, 100, not skip-all-below-70. Stall at 20 dropped.  
- **Done when:** Unit tests green.  
- **Local proof:** On this PC, Low BIOS often high — confirm at least one header records a below-BIOS settled point or a stall Unknown, not silence.

### D6 — Hold: lost temps / GPU reset abort (do not quiet)

- **Goal:** Close the Hold hole where `cpu is null` counts as under-target and fans walk quieter. Align Hold with `EvaluateWhileHeating` (telemetry lost, GPU device lost).  
- **Files:** `PolicySession.NudgeDirection`, `SafeFanSession.EvaluateSafety` or `PolicySession.Tick`, `PolicySessionTests`.  
- **Tests:** Null preferred CPU while holding ⇒ abort + restore, not −5% duty.  
- **Done when:** Unit tests green.  
- **Local proof:** Not required (unsafe to fake on a real PC).

These six are the honesty floor. Multi-heat (section E) sits on top of D1, D2, D5. D3+D4 can ship just before or with the first two-heat slice. D6 is small and should not wait.

---

## E. Phase 1 slice sequence

No Home curve editor in any slice. After each slice: `dotnet test`, `dotnet format --verify-no-changes`, update [IMPLEMENTATION.md](IMPLEMENTATION.md) snapshot in **that** slice’s PR (not this plan PR).

| # | Slice | Intent | In scope | Out of scope | Risks | Pike gate |
| --- | --- | --- | --- | --- | --- | --- |
| **0** | This plan | Agree the ladder (Option 3) and honesty floor | Markdown | Code | — | **Ship / Ship after fixes / Do not ship this plan** |
| **1** | D1 settled flag | Evidence cannot lie about timeout | Sample flag, influence/pairs gate | UI, heats, duties | Older DB rows: treat missing flag as Unknown, not Measured | Tests only |
| **2** | D2 persist lamp | Reproducible heat | `baseline_run` profile(s), actuator fallback removed | Second heat writes | Advanced “Run fan tests” without Watch must refuse | Local: kill after Watch |
| **3** | D6 Hold abort on lost temps | Safety hole | `PolicySession` | Curves | None meaningful | Tests only |
| **4** | D5 below-BIOS grid | Quiet-side data at **Low** | Absolute duties, order loud-then-quiet | Everyday heat, editor | Quiet + Low may abort — that is acceptable | Local: one Low fan-test retry |
| **5** | D3+D4 confirm on lamp, scaled Predict | Confirmation means something | Lamp on during confirm, duty-scaled expected, extra columns | A/B/A, editor | Confirm+lamp may approach abort; restore on miss path must still run | Local: one full Optimize, read confirmation vs fan-test GPU |
| **6** | Two-heat fan tests | **X-axis range** | Idle observe reuse; Everyday slim probes after Low **Completed**; per-heat 30 min cap; GPU gate per heat; walk status copy only | Home editor, pairs at Everyday, Isolated GPU, live curve actuator | Time; Everyday GPU Unknown; thermal abort skip Everyday | Local: Watch temps show two bands; SQLite has Everyday+Low samples |
| **7** | `TemperatureDutyPointBuilder` | Phase 2 can read knots | Core builder + tests; build-on-read from samples | WPF, interpolation, applying points | Mis-tagging BIOS leftover — reuse v1.14 hold splitting | Tests: idle/everyday/low fixtures span T; timeout excluded |
| **8** | BIOS → AUTO → BIOS on **Low lamp** (observe-only protocol) | Validation bar | Same locked Low: settle BIOS, apply current **policy** (not curves), settle, restore BIOS, settle; compare with MDE; persist three legs | Curve editor; claiming Path B product done | Policy A/B/A is not curve A/B/A; still the right heat. Extra time | Local: AUTO cooler at similar RPM **or** as warm at less RPM; return-to-BIOS matches first BIOS within wander |

**“Foundation ready for Phase 2”** = slices 1–7 landed and a local run produced Measured points at **more than one heat** for at least one CPU-linked fan, with settled flags correct. Slice 8 is the honesty check that Hold-at-Low is not worse than BIOS; it does not unlock the editor by itself, but **do not start Phase 2 UI if slice 8 shows AUTO hotter at similar or higher fan effort.**

Phase 1 Hold stays `PolicyOptimizer` + `PolicySession` (two-end). Do not wire `TemperatureDutyPoint` into the actuator yet.

---

## F. Explicitly out of scope (Phase 1)

- Home editable temperature → duty graphs, point dragging, per-fan curve cards.  
- Moving What we learned onto those graphs (Phase 2 sequel: evidence-on-curves).  
- Making Quiet–Cool the primary product, or removing it. Slider may still set today’s policy Hold.  
- Generating or applying live curves from the new points.  
- Isolated GPU interleave (`R → speed → R`), extra heater, named-anchor Hold, experiment planner.  
- Gaussian process / Bayesian / mic / NSGA-II / Math.NET.  
- Loosening CPU 90 / GPU 83 / other 95, or raising user abort above the floor.  
- AMD GPU fan writes, P2 case layout labels.  
- Full factorial pairs, triples, repeating pairs at every heat.  
- Causal validity from power/clock (parked). May be a later honesty add if Δs look like power wander.  
- Inventing Modeled duties at temperatures we never observed.  
- Kitchen-timer dwell replacing settle-by-measurement.

### Phase 2 sequel (mention only)

When Phase 1 data exists: Home shows per-group XY (temp → duty) **seeded from Measured knots** (idle BIOS, Everyday pick, Low pick). Segments between knots Modeled. Outside the measured T range Unknown. User may edit. What we learned is the evidence for those knots (wander, RPM vs °C isobar still useful as supporting chart, pairs, confirmation). Quiet–Cool regenerates a first draft; manual edits win. Do **not** design that editor in this plan.

---

## G. Direction locks (Danny 2026-09-21)

These were open questions. They are **locked**. Slice order in E does not need another design pass.

1. **Wall-clock:** **Yes.** A ~25–40 minute first Optimize is acceptable. Restore after every hold. Abort floors stay 90/83. Everyday probes stay in the default walk (not Advanced-only).
2. **After a thermal abort at Low:** **Yes — skip Everyday and pairs.** May Hold the quieter conservative two-end policy from the partial Low map. Confirmation still runs if Hold applies. Do not start Everyday after a thermal abort in Low.
3. **Phase 1 Hold:** **Keep today’s two-end Quiet–Cool policy.** Do not sit on BIOS until Phase 2. Do not apply temp→duty curves live in Phase 1.

---

## H. What this plan did not verify

- No live Optimize, Watch, or fan writes.  
- No Windows / PawnIO / NVAPI / watchdog kill.  
- No `dotnet test` on this Linux Cloud VM (`net10.0-windows`).  
- No fresh `%LocalAppData%\AUTO Fan\autofan.db` dump; live numbers cited from [IMPLEMENTATION.md](IMPLEMENTATION.md) decision log (GPU +15.1 °C, confirmation 39.0 vs 39.2 floor, CPU isobar wrong way, hub ~0.05 °C).  
- Whether Everyday CPU band is far enough from Low on this PC to be worth the extra time — Watch already showed ~58 vs ~62 °C CPU; GPU Everyday ~ idle. **CPU knots benefit; GPU knots likely only at Low.** That is acceptable if tagged Unknown.  
- Exact settle times for a 20% duty at Low (may timeout more often → Unknown, which is correct).

---

## Recommendation (one sentence)

**Ship Option 3:** honesty slices D1–D6, then Low-first absolute duties (including below BIOS), then Everyday slim probes plus idle BIOS observe, persist lamps and settled `(T, duty)` points, confirm on the Low lamp — **no Home editor until that dataset exists and a Low-lamp BIOS → AUTO → BIOS check is not a loss.**

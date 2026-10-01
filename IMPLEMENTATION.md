# AUTO Fan implementation path

Living tracker for building the product in [App.md](App.md). Rewrite the snapshot whenever a slice finishes or the product direction changes.

## How to use this file

| File | Job |
| --- | --- |
| [App.md](App.md) | What the product is and why. |
| [PRODUCT_DIRECTION.md](PRODUCT_DIRECTION.md) | How that product is built. Controlling method. Historical plans do not override it. |
| [IMPLEMENTATION.md](IMPLEMENTATION.md) (this file) | What we are building *now*, what is done, and what is later. |
| [AGENTS.md](AGENTS.md) | Stack, folder layout, commands, and hard limits. |

Agents: implement **only** the current versioned slice. List later ideas; do not build them. When you finish a slice or [App.md](App.md) changes, update this file in the same change (snapshot, coverage statuses, decision log). Do not invent P14/P15.

Humans: the snapshot below is the “we are here” line. You do not need to read the rest unless you want the full map.

---

## Versioning

**v1.0.0** is the first complete product (P0–P13). New work is **v1.1, v1.2, …** from real use — one slice at a time. Git tag matches the version when a slice ships. Do not add a new P-phase.

---

## Current snapshot

- **Version:** v1.20 measurement prototype, plus Slices 1–10 in the working tree (not tagged). No curve optimizer has shipped.
- **Controlling method:** [PRODUCT_DIRECTION.md](PRODUCT_DIRECTION.md). Joint validated temperature-to-duty curves. The two-speed Quiet–Cool hold is a legacy stand-in, not the product. The old Idle → Everyday → Low → Hot ladder and the independent per-fan 1 °C knot are historical inputs, not the next slice. [PATH_B_PHASE1_EXPERIMENT_PLAN.md](PATH_B_PHASE1_EXPERIMENT_PLAN.md) does not override this snapshot.
- **What the app actually does today:** After Administrator, PawnIO, competing apps, and Find connected, Optimize opens a walk: Watch, test fans, then restore. Watch now stores three heats: CPU-only at the safe worker count with GPU work off, a GPU raster search that uses one CPU worker and stops around a 15 °C GPU Core rise or near the ceiling, and mixed heat that puts the safe CPU count back on that frozen GPU profile. The 15 °C rise only stops the search. It does not make the hold measured. Mixed heat is the lamp fan tests reuse. A hotter CPU pass is not run. Three BIOS holds label CPU and GPU Core STABLE, DRIFTING, NOISY, or UNAVAILABLE. Fan tests reuse the stored lamp. A missing lamp refuses heat. Duties include speeds quieter than BIOS. A 90 s timeout is stored as not settled. Influence and pairs treat that hold as Unknown. A hold is Measured only after about twenty seconds of nearly flat temperature, with CPU power present, GPU power present when GPU temperature is present, load staying put, and no clock drop. Missing power is not treated as stable power. A commanded fan with no RPM is stalled. A timeout, a power jump, or a throttle is stored and is not Measured. The slope and power cutoffs are provisional; they were not locked from a trace on this PC. Five flat seconds are not enough. Optimize's fan step is reference, change, reference on the mixed heat. A step whose temperature does not return is not a fan effect. Those ranks are Modeled, not curve points. The return band is provisional. The separate Fan tests action can still run the older dense grid. That screen does not start the old pair test. After the screen, Optimize tries at most three whole-fan combinations, holds each one, and stops as a draft. Nothing from that search is applied. "Likely quieter" means no observed fan is meaningfully faster and at least one is slower. One fan faster and another slower is not called quieter. A missing fan speed cannot be called quieter. After Low completes, Everyday and Hot-if-separates can add more samples. Hot is kept only when CPU or GPU sits at least 5 °C above Low; otherwise that band is Unknown. NVIDIA GPU fans are not written for mapping until that heat’s GPU rise clears about 15 °C. What we learned shows RPM versus settled °C for one heat at a time. The default heat is Low. An Everyday or Hot point cannot move the Low chart or the old two-speed recommendation. Optimize finishes by restoring motherboard fans to BIOS and NVIDIA fans to the driver. Cancel does the same, and it does not apply a setting. The old two-speed hold remains only as a labeled button under Advanced, and it uses the Low heat only. Confirmation can still run with the heater off if that legacy hold is used. A missing CPU temperature during Hold can count as cool enough to step fans quieter. Close, Stop, abort, or crash restores motherboard fans to BIOS and NVIDIA fans to the driver. A tests-only simulated PC can run one hold in virtual time: a real settle restores, a fought duty is not a pass, a slow creep is not settled in five seconds, drifting power is not called cooling, a missing CPU temperature does not turn a fan down, and cancel or overheat leaves BIOS in control. The live Optimize walk does not use that session yet. Find connected also steps each connected fan down from 100% and records how it spins, then restores. It does not command 0%. A pump that stops reporting speed during synthetic heat aborts the run. NVIDIA fan writes stay on the selected card. The fingerprint does not reapply a profile. A temperature-to-duty draft is saved only when two settled points at different temperatures exist, and that draft is not turned on. The curve controller can look up a duty, take the higher of the CPU and GPU requests, move up faster than it moves down, and give the fans back if a temperature disappears or a fan stays stopped after one spin-up. A draft cannot take control, and startup does not load a curve. A power rise does not spin the fans up. Home shows a draft curve when one exists: measured points, modeled points, the safety point, and unknown fans that stay on BIOS. An edit cannot remove the safety point and marks the draft as needing a check. Startup still does not run the curve. The product is not validated.
- **What today's derived rows are not:** Influence deltas, mixed-heat plateaus, pair leftovers, five-second "settled" flags, and heater-off confirmations are not curve evidence. The raw one-second snapshots may be replayed to design later gates. They are not promoted into validated curve points.
- **What it does not do yet:** Per-anchor uncertainty instead of a fixed 15 °C or 5 °C proof. The screen's return band, the effort band, and the actuator step sizes are still provisional. Session and hold provenance that stops unrelated runs from being joined. BIOS → curves → BIOS. A hardware fingerprint that can safely reapply a profile. The ordered slices are in [PRODUCT_DIRECTION.md](PRODUCT_DIRECTION.md).
- **Next:** Slice 11 only — BIOS, curves, BIOS. The CPU anchor and the GPU or mixed anchor the curves claim. Only a pass becomes Validated and stays in control. Do not load a curve at startup before that proof. Do not treat the provisional cutoffs as validated on this PC.
- **Blocked on:** Nothing in the docs. Do not loosen abort ceilings (CPU 90 / GPU 83 / other 95). Do not claim this PC is validated.

---

## App.md coverage map

Statuses: `not started` · `in progress` · `done` · `needs revision` · `deferred`

### App.md sections after the 2026-09-21 reset

| App.md | Need | Phase | Status |
| --- | --- | --- | --- |
| What one Optimize run does | One consent, then actuation calibration, independent anchors, workload-valid screening, joint candidates, curves, and BIOS → curves → BIOS. Cancel restores. | v1.21+ slices in [PRODUCT_DIRECTION.md](PRODUCT_DIRECTION.md) | needs revision (Optimize now restores on finish and Cancel; the curves are not built) |
| §1 Discover this PC | Sensors, competing-software check, connected groups, then duty-to-RPM and minimum stable duty. Case labels stay parked. Geometry may only order a screen. | P1, P12, Slice 4 | needs revision (the speed sweep and fingerprint exist; a matching fingerprint does not reapply a profile) |
| §2 Establish repeatable heat | Idle, CPU-only, GPU, and mixed frozen anchors. Workload power and clocks stored with the reference. No all-core High. | P4, Slice 5 | needs revision (CPU, GPU, and mixed anchors are stored with a hold assessment; a hotter CPU pass is not built) |
| §3 Learn which fans matter | Reference, step, reference. Ranks stay Modeled. About 15 °C of GPU rise is a calibration aim, not proof of a fan effect. One-fan RPM chart is evidence only. | P5, P8, Slice 6 | needs revision (Optimize uses reference, change, reference and Modeled ranks; the return band is provisional; the older dense grid remains a separate action) |
| §4 Choose combinations | Lowest observed RPM effort across the whole machine, including fans left on BIOS or the driver. "Likely quieter" only if no observed group is meaningfully faster and at least one is slower. Joint vectors only. Pairs only when a joint residual needs them. | Slice 7 | needs revision (a draft search of at most three combinations exists and is not applied; the effort band is provisional; this is not a validated profile) |
| §5 Build the curves | Monotone temperature-to-duty requests from joint points. Max of CPU and GPU requests. Safety extension. Two points or no curve. | Slice 8 | needs revision (draft records and a numbered migration exist; a draft is not applied and is not validated; the live max-of-requests actuator is not built) |
| §6 Run the curves | Lookup, asymmetric hysteresis, RPM feedback, restore on lost authority. Missing temperature aborts. | Slice 9 | needs revision (the controller exists and restores on a lost temperature or a second stall; a draft cannot take control, and startup does not load a curve) |
| §7 Prove it, then reuse it | BIOS → curves → BIOS on each claimed anchor. Fail restores. Reuse only a Validated matching fingerprint. | Slice 11, Slice 12 | not started |
| §8 Show the evidence | Home graphs with Measured, Modeled, Inferred, and Unknown. Per-heat RPM evidence. Draft until the proof passes. | P10, Slice 10 | needs revision (Home shows the draft curve and an edit marks it as needing a check; BIOS is still in control; the BIOS proof is not done) |

### Other App.md promises

| App.md | Need | Phase | Status |
| --- | --- | --- | --- |
| Product / Core Promise | One Optimize produces joint curves or restores the fans. Not validated until BIOS → curves → BIOS. | Slice 11 | needs revision |
| What the user gets | One button, Stop, graphs plus evidence, or an honest return to BIOS. | Slice 1, Slice 10, Slice 11 | needs revision |
| Long-term reuse | A short user-approved check. No background heat. No per-game database. | Slice 12 | needs revision (old heater-off confirmation is not this check) |
| Trust rule | RPM effort is Inferred. Airflow paths are Inferred. Unsettled holds and skipped anchors are Unknown. | P10, Slice 2 | needs revision (a timeout or failed hold stays Unknown; the numeric gates are provisional and were not locked from a trace on this PC) |

### Implied (needed to keep App.md honest)

| Need | Phase | Status |
| --- | --- | --- |
| Solution skeleton, fake hardware, tests that do not touch real fans | P0 | done |
| Hardcoded safety ceilings (max temp, duty 0–100%, experiment abort) — never loosen. User abort fields may only stop sooner. | P0, every later slice | done |
| Live fan writes only behind restore-on-exit, crash recovery, abort, and the competing-software warning. Motherboard BIOS and NVIDIA driver restore. | P3 | done |
| Workload generation only behind those abort limits. A missing stored lamp does not fall back to a default. | P4, v1.17 | done. The shipped lamp is still GPU-weighted. Independent CPU and mixed anchors are not built. |
| A simulated thermal plant and a Core session that can fail closed | Slice 3 | done (tests only; the live Optimize walk still uses the existing runners) |
| Apply a learned curve as the live controller | Slice 9 | not started (the two-speed hold is legacy and must not gain more authority) |
| Distinguish Measured, Modeled, Inferred, and Unknown on every number the person sees | P10, Slice 2 | needs revision (SettledMeasured requires the provisional quiet window and power; Home does not yet show that distinction) |

---

## Phases

> **Historical; Current snapshot wins.** P0–P14 below record what shipped under Path A. They are not the product-direction spec.

Each phase is one logical change for a later chat. Do not skip ahead.

### P0 — Foundation

- **Goal:** A buildable Windows app that cannot hurt hardware yet, with a place for every later layer.
- **App.md:** Enables everything that follows. No user-facing cooling behavior yet.
- **Done when:**
  - Solution exists: `src/AutoFan.Hardware`, `src/AutoFan.Core`, `src/AutoFan.Storage`, `src/AutoFan.App`, plus tests that hit Core (no hardware I/O).
  - WPF shell launches (placeholder screens; Optimize disabled).
  - Fake hardware backend so Core/Storage can be tested without fans.
  - Safety ceilings live as hardcoded limits nothing may loosen: CPU 90 °C / GPU 83 °C / other 95 °C / duty 0–100% ([AGENTS.md](AGENTS.md) out of bounds).
  - `dotnet test` and `dotnet format --verify-no-changes` pass.
- **Out of scope:** LibreHardwareMonitor, PWM writes, real sensors, experiments, installer, PawnIO.
- **Tests:** Core can run a dummy “session” against fake hardware; safety limits reject out-of-range duty cycles in memory.

### P1 — Discover, read-only

- **Goal:** See this PC’s sensors and controllable groups without changing fan speeds.
- **App.md:** §1 hardware discovery (CPU, GPU, motherboard, power, controllers, RPM, pumps, thermals, groups).
- **Done when:** App lists discovered hardware in the UI; admin + PawnIO checks are visible if missing; no PWM writes exist.
- **Out of scope:** User layout labels, case database, fan control, workloads.
- **Tests:** Hardware mapping from a recorded/fake sensor tree; UI can bind to that list without a live machine.

### P2 — User layout labels

- **Goal:** User names what the sensors cannot: case, positions, sizes, orientation, cooler/radiator, intake vs exhaust.
- **App.md:** §1 “The user can also identify…”
- **Done when:** Labels persist (JSON settings); they are stored as user claims, not as proven airflow.
- **Out of scope:** Case manufacturer database (P12). Do not treat labels as ground truth in any model.
- **Tests:** Save/load a layout profile; unlabeled groups still work.

### P3 — Watchdog and restore

- **Goal:** The only phase that is allowed to introduce PWM writes. Gate for every experiment.
- **App.md:** “Safely perturb its cooling system” (What This Product Really Is).
- **Done when:**
  - Setting a duty cycle always registers a restore-to-BIOS (or last-known-safe) path.
  - Process exit, crash, and abort limits restore control.
  - Competing software (FanControl, Armoury Crate, iCUE, etc.) is warned; we do not silently fight headers.
  - Safety ceilings abort the write path.
- **Out of scope:** Optimization, multi-fan experiment planner, GPU-vendor-specific extras beyond what P1 already reads.
- **Tests:** Fake (and, if present, a hardware-free harness) proves restore runs on dispose, cancel, and simulated crash/abort. No test may skip abort limits.
- **Shipped:** Rate-of-rise abort on the safety path (`ThermalTrend`, 3 °C/s over 3 s). Ceilings stay CPU 90 °C / GPU 83 °C / other 95 °C. Stall exclusion lives with the experiment runners, not as a new write API.

### P4 — Baseline

- **Goal:** Understand the machine in its existing state before we change it.
- **App.md:** §2.
- **Done when:** A baseline run records temperatures, RPM, duty, CPU/GPU power, workload level, clocks, rise, decay, settle time; ambient included when a sensor exists. Data lands in SQLite. Workload generation, if any, respects P3 abort limits.
- **Out of scope:** Perturbing fans on purpose (that is P5). Fitting a thermal model (P7). Using baseline settle seconds as the fan-test dwell.
- **Tests:** Parser/aggregates for rise/decay/settle from a fixture time-series; missing ambient is allowed and recorded as unknown.
- **Shipped:** Settle seconds stay a baseline metric computed after fixed idle/everyday/low/high/cooldown phases. Fan-test dwell is settle-by-measurement in P5/P6, not this number.

### P5 — Individual fan tests

- **Goal:** Measure which fan groups actually affect which temperatures.
- **App.md:** §3 (influence map example: front/bottom/rear/top vs CPU/GPU/VRM/case).
- **Done when:** One-group experiments run under P3 safety; results persist as an influence map; UI can show measured effects. Relationships are experimental, not assumed from case labels.
- **Out of scope:** Combination tests (P6), optimizer (P9).
- **Tests:** Influence-map builder from fixture experiment rows; a fan with no effect is stored as low/none, not omitted silently.
- **Shipped:** Settle-by-measurement dwell with a 90 s timeout. Screen at 15/30/45/60/75/90/100%, refine 20/40/50/70/85% only on groups that moved a temperature (v1.18; absolute grid, including below BIOS). After Low Completed, Everyday coarse 20/40/70/100 then 55/85 on those Low-movers (v1.19). After that, Hot-if-separates: extra GPU work from the stored Low lamp, hard stop ~8 °C under abort, keep only if CPU or GPU is ≥ 5 °C above Low; same dense grid on Low-movers; persist Hot only when kept (v1.20). `FanSpeedCurveBuilder` fills live P8 bands from settled Perturb/Screen/Refine holds (v1.14). Stalled / 0 RPM points are dropped. A GPU/CPU Δ is Unknown if duty did not rise by 5% or RPM did not rise (v1.10). Experiment unit is a writable motherboard header with tach, or the NVIDIA fans on one card. Pumps and AMD/Intel GPU fans stay skipped. GPU-target influence is Unknown, and NVIDIA GPU fans are not written for mapping, until Watch GPU Core rose about 15 °C (v1.9); Everyday NVIDIA writes use Everyday GPU rise against that gate; Hot NVIDIA writes use Hot GPU versus Watch idle.

### P6 — Interaction tests

- **Goal:** Find fans that do little alone but matter together (or fight).
- **App.md:** §4.
- **Done when:** Selected combinations are tested; interaction effects persist; UI copy separates **measured** delta from **inferred** airflow path. No air-pressure or airflow-velocity numbers unless a real sensor exists.
- **Out of scope:** Full factorial of every header; three-way search; case-database experiment ordering (P12).
- **Tests:** Combination effect ≠ sum of individuals is detected on fixture data; inferred fields cannot be written into measured columns.
- **Shipped:** `InteractionSelector` picks pairs that affected the same sensor, **including slight effects** (App.md front −1.5 / top −0.7 example stays eligible). Same settle-by-measurement dwell as P5. One confirmation after Optimize applies the pick. Pairs run only if individual tests finished; a thermal abort or cancel with a partial map skips them and does not reuse yesterday’s leftovers (v1.9). No Gaussian process.

### P7 — Thermal model

- **Goal:** A digital thermal profile of *this* PC.
- **App.md:** §5.
- **Done when:** Core can predict likely temp response to a fan-group change from stored experiments; the model is tied to this machine’s data, not a case SKU.
- **Out of scope:** Quiet–Cool search (P9), workload-specific policies (P11), case priors (P12). Gaussian process / Math.NET fit.
- **Tests:** Fit and predict on fixture data; refuse to pretend high confidence with too few experiments.
- **Shipped:** Additive singles plus measured pair leftovers for exactly two fans; three or more sum singles only. Unknown influence contributes nothing (v1.10). Confidence cannot be High for a hotter policy than the experiment load (Low-only maps cap at Medium). No surrogate optimizer.

### P8 — Diminishing returns

- **Goal:** Find the RPM band where more speed stops being worth the noise.
- **App.md:** §8. Philosophy: minimum airflow for the desired thermal result.
- **Done when:** Per-group curves show useful vs wasted RPM; those bands are inputs to the optimizer, not only a chart.
- **Out of scope:** Applying a full policy (P9). Kneedle, slope-threshold knobs, microphone noise.
- **Tests:** Fixture curve like App.md’s 600–1200 RPM example marks the plateau; never recommend the loudest point when the thermal gain is negligible.
- **Shipped:** The 1.0 °C plateau rule stays (quietest point within 1 °C of the coolest measured). Live bands come from the P5 refinement curve. What we learned shows measured RPM → settled °C in a Penpot-matched card (v1.11–v1.14). Points are settled Screen/Refine holds only — BIOS reference and one-sample hub flicker are omitted (v1.14). No live marker, no `Predict()`, no BIOS temp→duty graph. No ΔT/ΔRPM user threshold.

### P9 — Optimizer + simple UX

- **Goal:** User picks priorities; software produces and **applies** a strategy.
- **App.md:** §6, Product, Core Promise, What the User Ultimately Gets.
- **Done when:**
  - Quiet–Cool (and optional detail) in the UI.
  - One **Optimize** action runs the pipeline that exists so far and applies a policy through the P3-safe control loop.
  - Objectives: stay under temp targets, avoid extra RPM, limit spike lag, avoid oscillation.
  - User does not have to know PID, hysteresis, or airflow theory.
- **Out of scope:** Per-workload policy packs (P11), long-term drift (P13). Replacing the Quiet–Cool slider with a ceiling-only UX. Biasing the default louder because confidence is Low.
- **Tests:** Optimizer on fixture model prefers a quieter point with nearly equal temps (App.md §8 example); safety ceilings still abort.
- **Shipped:** Slider + optional CPU/GPU targets stay. One confirmatory apply-and-settle after the pick; a miss adds a little airflow and is reported. Cancel or a thermal abort with a partial map skips pairs and leaves a quieter conservative policy; confirmation still runs if Hold applies (v1.9). Live headroom moves toward the already-computed cool end when observed heat exceeds the test load. One Optimize action runs the characterization, then the policy. RPM remains the noise proxy.

### P10 — Explanations

- **Goal:** Help the user understand *their* computer, not just a curve.
- **App.md:** §10.
- **Done when:** A completion summary includes primary GPU path, primary CPU path, most effective fan, diminishing returns, detected interaction, recommended operating point, expected vs baseline. Every claim is tagged Measured, Modeled, or Inferred.
- **Out of scope:** New experiment types (those belong to P14).
- **Tests:** Report builder never emits an untagged claim; inferred text cannot use measured-only fields.
- **Shipped:** Confirmation line (Measured settled temps vs Modeled prediction). Confirmation GPU expected will not go below idle, or below start if GPU is already cooler than idle; extra airflow on a miss does not make GPU policy trusted (v1.10). RPM-as-noise is Inferred. A gaming result guessed from Low-heat tests is Modeled, not Measured gaming truth. GPU cooling without a ~15 °C Watch rise, and skipped pair tests, are Unknown — not “none” and not “no leftover” (v1.9).

### P11 — Workload policies

- **Goal:** Re-optimize the **same** thermal model for different heat patterns. Do not re-run the experiment tour per activity.
- **App.md:** §7 (rewritten 2026-09-19).
- **Blocked on:** none. P3–P10 App.md revisions shipped. Still do not treat Low-only maps as High-confidence gaming truth.
- **Done when:**
  - Desktop / gaming / render / mixed / sustained / burst are different **policies** from CPU+GPU power, not different experiment passes.
  - Control can raise airflow from a power/workload rise without waiting for the full temp spike.
  - When observed heat exceeds the experiment load, the live loop may move toward the already-computed cool end (P14 headroom). Desktop stays quieter at idle.
- **Out of scope:** A new experiment pass per workload. Per-title game database (P13). Louder default because the confidence label says Low. An all-core High synthetic pass that hits the abort ceiling.
- **Tests:** Fixture “GPU power steps up” produces an earlier fan rise than temp-only control; desktop policy stays quieter at idle; a Low-only model asked for a hotter profile does not claim High confidence and does not raise the recommended point until live temps need the cool end.
- **Shipped:** One Quiet–Cool recipe. Live `PolicySession` classifies desktop / gaming / render / mixed from this PC’s baseline Idle/Everyday/Low watts. A short power spike is Burst (no power-only raise). Sustained gaming/render/mixed raises the matching influence groups toward the already-computed cool end before temperature spikes. Power above the Low test watts, over-target temps, and test-load +3 °C still raise immediately. Desktop idle walks toward the quiet end. Low-only maps do not claim High confidence and do not store a louder recommended point. Writeup says the same tests are reused; a gaming guess from Low heat stays Modeled. No activity picker. No second experiment tour.

### P12 — Case priors

- **Goal:** Use case geometry to test smarter, never to override measurements.
- **App.md:** §1 case database, §9.
- **Done when:** A case catalog can propose likely paths (front→GPU, rear→CPU, …) and **order** the screening pass. A “known” path still gets at least one confirmatory measurement. As data arrives, priors yield to the learned model. Manufacturer specs remain hypotheses. If independently writable groups grow large, cluster by zone at screening, then test the zones — do not bring back a surrogate search.
- **Out of scope:** Treating a case SKU as a finished cooling profile. Skipping a confirmatory point because the case brochure looks obvious. Designing for 8+ independent channels before that hardware shows up.
- **Tests:** Same measurements + different priors do not change the fitted model’s ground-truth relationships; they may change experiment *order* only.
- **Shipped:** `CasePriorCatalog.GenericMidTower` plus `ScreenOrderer` reorder P5 screening from header-name zones (front/bottom → GPU first, rear/top/CPU Fan → CPU). `Fan #1` / `SYS_FAN` stay unknown and are still screened last. A hub is still one group. Priors are not passed to `ThermalModelFitter` or the P10 writeup. No manufacturer SKU list, no case graphic (P2 stays parked). Same-zone headers stay adjacent (cluster by zone without an 8-header search).

### P13 — Continuous learning

- **Goal:** The personal cooling model improves with use instead of a one-shot Optimize.
- **App.md:** Long-term vision; What This Product Really Is item 8.
- **Done when:** Periodic **confirmation** can detect drift (dust, seasonal ambient, new hot game). A failed confirmation may trigger a targeted re-test. The user is not forced to rebuild curves by hand, and the app does not automatically run a full re-characterization every time.
- **Out of scope:** Cloud upload of logs, hardware IDs, or models ([AGENTS.md](AGENTS.md)). Automatic full DOE re-runs. Per-title game database.
- **Tests:** Fixture “ambient shifted” flags the old operating point as stale; no network calls in the learning path.
- **Shipped:** While a policy is held, confirmation repeats every 30 minutes or on the first sustained gaming/render/mixed load. Nudges freeze during settle. A miss still adds 5% duty. `DriftDetector` flags stale from a 3 °C ambient shift, a miss, or extra airflow. The writeup shows the stale line; **Re-check these fans** / Ignore appear only then. Re-check screens/refines only the groups that moved the drifted sensor (including slight), overlays those rows on the last map, re-fits, applies, and confirms once. Confirmations persist in SQLite. No cloud. No automatic full Optimize.

### P14 — Experiment protocol

- **Goal:** Make one Optimize run match [App.md](App.md) “What one Optimize run does.” Revises P3 and P5–P10. Does not erase their v1 history.
- **Status:** done as P3–P10 revisions (2026-09-19). This heading stays so later chats do not revive a second experiment-protocol phase. New work is P11.
- **App.md:** What one Optimize run does; §3–§6, §8, §10; Product / What the User Ultimately Gets (cancel still helps).
- **Done when:**
  - Fan-test dwell waits until the relevant temperatures have stopped moving, with a timeout and existing abort ceilings (no new fixed kitchen timer).
  - Rate-of-rise abort exists on the safety path. Ceilings stay CPU 90 °C / GPU 83 °C / other 95 °C.
  - Screen then refine on writable motherboard groups with tach; pumps, GPU headers, empty 0 RPM, and stalled points excluded.
  - Interaction pairs come from measured same-target effects, including slight effects.
  - One confirmatory apply-and-settle after the policy pick; a miss adds a little airflow and is reported.
  - Cancel leaves a usable partial map and a conservative policy.
  - Live load-range headroom: hotter than the test load may move toward the computed cool end; Low confidence does not raise the default recipe.
  - P8 plateau (1 °C) runs on a real multi-speed refinement curve.
  - Confidence cannot be High for an untested hotter load.
  - P10 writeup includes confirmation, RPM-as-noise as Inferred, and load-range honesty.
- **Out of scope:** Gaussian process / Expected Improvement / NSGA-II / Plackett-Burman / Kneedle / Math.NET search. Microphone noise. Three-way combination search. Replicating every point. Default dual-direction hysteresis. Replacing the Quiet–Cool slider. “Always add airflow when confidence is Low.” P11 policy packs. P12 case catalog. Loosening abort ceilings.
- **Slices (shipped as P3–P10 revisions):**
  1. Settle-by-measurement for P5/P6 dwell + rate-of-rise abort.
  2. Screen then refine (unblocks live P8 bands).
  3. Smart pairs (including slight effects) + one confirmation.
  4. Anytime cancel + live load-range headroom + P7/P10 honesty.
- **Tests:** Fixture series that is still moving is not treated as settled; a stalled 0 RPM perturb is dropped; a slight+slight pair like App.md’s −1.5 / −0.7 example is eligible; confirmation miss raises duty a step and is tagged Measured vs Modeled; cancel after one finished group still yields a policy; Low-only + hotter live load moves toward cool end without changing the stored recommended quiet point.

---

## Direction-change protocol

When [App.md](App.md) or priorities change:

1. Edit App.md first (product truth).
2. Update this file’s coverage map (phase assignment + status). Add, split, defer, or cancel phases here — do not leave a new App.md paragraph unmapped.
3. Add a **Decision log** line: date, what changed, why.
4. Rewrite the **Current snapshot** so the next chat does not follow a stale next-slice.
5. Do not silently skip P3 before any fan write. Do not treat case specs as ground truth. Do not loosen safety ceilings.

If a chat wants to build a later slice “while we’re here,” refuse, note it as a parked v1.x candidate, and stay on the snapshot’s next slice. Do not invent P14/P15.

---

## Decision log

- **2026-09-22** — Slice 10 home graphs. Home shows a saved draft: measured points, modeled points, the safety point, and fans with no curve as unknown and still on BIOS. An edit cannot remove the safety point or command 0%. It keeps duty from falling as temperature rises and marks the draft Edited — check required. It does not mark the draft validated and it does not run the fans. Next is Slice 11, BIOS then curves then BIOS. Ceilings unchanged. This PC is not validated.
- **2026-09-22** — Slice 9 curve actuator. The controller looks up duty from the raw temperature, uses the higher of the CPU and GPU requests, raises duty faster than it lowers it, and limits each step. It respects the minimum stable duty and does not command a stopped fan below its start duty. One stall gets one spin-up. A second stall, a missing temperature, a GPU fault, or Stop restores the fans and can mark the profile invalid. A power rise does not raise the fans. A draft cannot take control, and opening the app does not load a curve. No home graph. Next is Slice 10, home graphs. Ceilings unchanged. This PC is not validated.
- **2026-09-22** — Slice 8 curve records. A draft temperature-to-duty record is saved only when a fan has two settled points at different temperatures. Otherwise the fans stay on BIOS and the driver. The dots are measured. The line between them is modeled. Duty does not fall as temperature rises; if that rule changes a measured dot, the draft needs a check. The coolest request does not go below the minimum stable duty. A safety point reaches 100% no later than 5 °C under the abort limit and is not a measurement. Zero percent is not invented. The record cannot be saved as validated. Curve tables are migration version 1. The draft is not applied. Next is Slice 9, the curve actuator. Ceilings unchanged. This PC is not validated.
- **2026-09-22** — Slice 7 joint candidates. After the screen, Optimize holds the BIOS fan setting, then tries at most three whole-fan combinations. A combination that runs hotter than that BIOS reference, drops a needed temperature, or does not settle is not kept. Effort is observed RPM divided by that fan's measured maximum, including fans left alone and a pump that reports speed. Likely quieter means no observed fan is meaningfully faster and at least one is slower. One faster and one slower is not quieter. A missing speed cannot be called quieter. A pair is added only once, and only when a larger combination misses its prediction. The result is a draft and is not applied. Fans are restored. Next is Slice 8, curve records. Ceilings unchanged. This PC is not validated.
- **2026-09-22** — Slice 6 reference, step, reference. Optimize's fan step holds a reference duty, tries one change, then holds that same reference again. If the temperature does not come back, the step is not a fan effect. Ranks are Modeled. The return band is provisional. Duties are a low, middle, and high from the fan's speed check, or 40, 70, and 100 when that check is missing. The screen does not build a curve and does not start the old pair test. The separate Fan tests action can still run the older dense grid. Next is Slice 7, joint candidates. Ceilings unchanged. This PC is not validated.
- **2026-09-22** — Slice 5 independent anchors. Watch stores a CPU-only heat at the safe worker count, a GPU raster heat calibrated with one CPU worker, and a mixed heat that uses the safe CPU count plus that frozen GPU profile. About 15 °C of GPU rise only stops the GPU search. The hold assessment is separate, so the rise is not proof. Mixed heat remains the lamp fan tests reuse. No hotter CPU pass. Next is Slice 6, reference then step then reference. Ceilings unchanged. This PC is not validated.
- **2026-09-22** — Slice 4 actuation and fingerprint. Find connected still checks RPM at 100%, then steps each connected group downward and records duty-to-RPM, maximum RPM, the lowest duty that keeps spinning, tachometer repeatability, and a start duty only when restarting takes more duty than staying spinning. It does not command 0% and it restores after each group. A topology fingerprint matches the GPU hardware id and the fan group ids. The same display name is not a match. NVIDIA writes use that id and do not fall back to another card. A pump that had RPM and then goes silent during synthetic heat aborts the run and restores. The fingerprint does not reapply a profile. Next is Slice 5, independent anchors. Ceilings unchanged. This PC is not validated.
- **2026-09-22** — Slice 3 session shell and simulated PC. A tests-only plant runs in virtual time with lag, cross-coupling, noise, creep, power drift, a fought duty, a stall, and a missing CPU temperature. One Core session can pass only a settled hold whose commanded duty stuck, and it restores on pass, failure, and cancel. A missing CPU temperature does not write a lower duty. A replay helper runs a recorded trace through the hold check. The live Optimize walk is unchanged. Slope and power cutoffs stay provisional. Next is Slice 4, actuation and fingerprint. Ceilings unchanged. This PC is not validated.
- **2026-09-21** — Slice 2 measurement truth. Fan tests, pair holds, and the three BIOS reference holds use one hold check. A hold is Measured only after a longer quiet window, with power present, load stable, RPM present when a duty was commanded, and no clock drop. Missing power is not stable power. Timeouts and rejected holds stay unsettled, so influence cannot call them a fan effect. Each sample stores the assessment, the commanded duty, and a hold id. The slope and power cutoffs are provisional and were not locked from a trace on this PC. Old stored rows are not reclassified into curve points. Next is Slice 3, the simulated plant and fail-closed session. Ceilings unchanged. This PC is not validated.
- **2026-09-21** — Slice 1 containment. The default Optimize walk finishes by stopping heat and restoring BIOS and the NVIDIA driver. Cancel does the same and does not apply a partial setting. The RPM chart is one heat at a time, and the old two-speed recommendation uses the Low heat only. That hold remains a labeled Advanced button, not the result of Optimize. Next is Slice 2, measurement truth. Ceilings unchanged. This PC is not validated.
- **2026-09-21** — Measurement method lock. Screening for the future collector is reference, step, reference. A fan effect has to beat that anchor's own uncertainty, including a repeated reference and stable power, load, clocks, and RPM. About 15 °C of GPU rise and the old 5 °C Hot gap are not proof. Effort and "likely quieter" use every observed RPM; one fan speeding up while another slows is not called quieter. Samples must share one session before they are joined. Existing influence, plateau, pair, and confirmation rows stay replay-only. Next code is still Slice 1 only. Ceilings unchanged. This PC is not validated.
- **2026-09-21** — Product direction reset. Keep the codebase and the safety floor. The two-speed Quiet–Cool hold and the independent per-fan 1 °C knot are not the optimizer. The controlling method is a hybrid measured joint search: workload-valid anchors, joint duty vectors, monotone CPU/GPU request curves, and BIOS → those curves → BIOS. [PRODUCT_DIRECTION.md](PRODUCT_DIRECTION.md) wins over the Path B ladder, dense-grid ending, and policy proof. Next code is Slice 1 only: stop that legacy hold from being what Optimize applies. This PC is not validated.
- **2026-09-19** — Product is a local Windows thermal diagnostic/optimizer, not a FanControl clone. Fan control is the actuator ([App.md](App.md)).
- **2026-09-19** — Stack: C# / .NET 10 / WPF; LibreHardwareMonitorLib + NvAPIWrapper + ADLXWrapper; SQLite + JSON; no auth; unpackaged admin install. Inspired by FanControl’s hardware layer, not its “draw a curve” product. See [AGENTS.md](AGENTS.md).
- **2026-09-19** — Optimizer stays in-process (Math.NET). No Python sidecar, no web UI, no cloud, no Store/MSIX.
- **2026-09-19** — Implementation is phased P0–P13 in this file. P0 is current. Coding does not start until the user says go.
- **2026-09-19** — Fake hardware in P0; real PWM writes only from P3 onward, always with restore-on-exit.
- **2026-09-19** — Case database deferred to P12. User labels in P2 are claims, not proof.
- **2026-09-19** — This tracker file plus `.cursor/rules/02-implementation-path.mdc` are how later chats stay aligned.
- **2026-09-19** — P0 foundation shipped: `AutoFan.sln` with Core / Hardware / Storage / App / Core.Tests; WPF shell with Optimize disabled; `FakeHardwareBackend` (in-memory only); hardcoded ceilings CPU 90°C / GPU 83°C / other 95°C / duty 0–100%. Tests target `net10.0-windows` so they can reference Hardware. No LibreHardwareMonitor, SQLite, Math.NET, or PWM.
- **2026-09-19** — P1 discovery shipped: `LibreHardwareMonitorLib` 0.9.6 on Hardware only; mapper from a recorded sensor tree; UI lists this PC’s identity, temps, power, fan/pump groups; Administrator + PawnIO checks; `TrySetDuty` still rejects on the real backend. NvAPIWrapper / ADLXWrapper parked until P3 GPU writes. No PawnIO installer.
- **2026-09-19** — P2 parked so P3 could ship first. Layout labels are still not started; they are claims, not airflow proof.
- **2026-09-19** — P3 watchdog/restore shipped: `SafeFanSession` is the production write coordinator; `LibreHardwareMonitorBackend` may `SetSoftware` on motherboard / Super I/O / controller headers only; pumps and GPU headers are refused; restore-to-BIOS on dispose/exit/abort; dirty file + same-exe `--watchdog` child for crash; next launch restores if the dirty file remains; competing software is warned and blocks writes. NvAPIWrapper / ADLXWrapper still parked. No duty sliders, no Optimize, no experiments.
- **2026-09-19** — P4 baseline shipped: observe-only run records temps, RPM, duty, power, clocks, load, rise/decay/settle; ambient unknown when missing. SQLite at `%LocalAppData%\AUTO Fan\autofan.db`. Built-in idle/low/high synthetic heat (CPU threads + ComputeSharp DX12 compute); WARP/software adapters are not counted as GPU load. Not a game replica. No fan writes. Thermal abort and cancel stop the load. Optimize still disabled.
- **2026-09-19** — [AGENTS.md](AGENTS.md) gotchas updated for P4 (ComputeSharp, stop load on exit, SQLite path). NvAPI / ADLX / Math.NET remain planned, not installed. Snapshot advanced to P5.
- **2026-09-19** — P5 individual fan tests shipped: `FanTestRunner` raises one motherboard group at a time through `SafeFanSession`, restores BIOS between groups, and persists an influence map (CPU/GPU/VRM/case) in SQLite. Pumps and GPU headers are skipped, not treated as “no effect.” A measured near-zero delta is stored as **none**. No combination tests, no Optimize. Snapshot advanced to P6.
- **2026-09-19** — First live P4/P5 run on this PC (Nuvoton NCT6687D-R, RTX 5070 Ti). Baseline: idle ~51 °C CPU, low ~79 °C, high aborted at 91.5 °C (90 °C ceiling). No ambient sensor. Fan tests started ~45 s later at 84 °C, aborted in ~5 s during the CPU Fan BIOS hold — no duty writes, no influence map. Pump correctly skipped. Intended everyday state is motherboard BIOS + GPU driver control, other fan apps closed. First `GpuTemperature` on this NVIDIA tree is **GPU VR SoC**, not GPU Core (P4 “GPU rise 6 °C” used VR SoC). Several headers report 0 RPM. Do not loosen abort ceilings.
- **2026-09-19** — P5/P6 experiment heat is `WorkloadLevel.Low` because High already aborted at 90 °C on this PC. High remains a baseline-only characterization. Start gate: wait until preferred CPU and GPU are at or below 60 °C (exactly 60 is ready). Preferred GPU sensor is **GPU Core**, not GPU VR SoC; preferred CPU is Tctl/Tdie when present. Empty 0 RPM headers are skipped, not treated as “no effect.”
- **2026-09-19** — P6 interaction tests shipped: selected motherboard pairs (A, then B, then A+B) through `SafeFanSession`, each with its own BIOS reference; residual vs the sum persists; UI labels Measured deltas and Inferred airflow notes separately. No pressure/flow numbers, no model, no Optimize. Snapshot advanced to P7.
- **2026-09-19** — Run-status copy: each test section keeps a short “what this is” blurb while a separate bold line reports the current step in plain English with live CPU/GPU. Result rows use leftover / possible meaning instead of engineer pair notation. Not a new phase.
- **2026-09-19** — P7 thermal model shipped: `ThermalModelFitter` fits on demand from latest stored baseline / influence / interaction. Predictions are additive singles plus measured pair leftovers for the same experiment duty step. Confidence cannot be High with thin or aborted data. Predictions labeled **Modeled**. No Math.NET, no new SQLite table, no Optimize. Snapshot advanced to P8.
- **2026-09-19** — P8 diminishing returns shipped: `DiminishingReturnsAnalyzer` marks useful vs wasted RPM from a speed-versus-temperature curve. Plateau is hardcoded at 1.0 °C of the coolest measured point; recommended RPM is the quietest plateau point, never the loudest when extra cooling is negligible. Bands are a Core `DiminishingReturnsReport` for P9. No Math.NET, no new SQLite table, no fan writes, no Optimize. Live UI stays unknown until a measured multi-speed curve exists (P5 is still one +25% step). Parked: a later RPM sweep through `SafeFanSession` to fill `FanSpeedCurve` on a real PC. Snapshot advanced to P9.
- **2026-09-19** — P9 optimizer + simple UX shipped: Quiet–Cool slider and optional CPU/GPU targets; one Optimize action applies a discrete `PolicyOptimizer` pick from P7 + P8 through `PolicySession` / `SafeFanSession` (policy mode skips only the 30-minute experiment timer; thermal, competing-software, and restore-on-exit stay). No Math.NET. No per-workload packs. No explanation report. Settings JSON at `%LocalAppData%\AUTO Fan\settings.json`. Live Optimize stays disabled on this PC until fan tests finish. Snapshot advanced to P10.
- **2026-09-19** — P10 explanations shipped: `ExplanationReportBuilder` writes the seven App.md §10 headings from stored tests + P9 policy. Path wording and interaction notes are Inferred and carry no °C/RPM. Influence deltas and residuals are Measured. Recommended duties and test-step `Predict` are Modeled, not interpolated to the Quiet–Cool mix. Missing data is Unknown. No new experiments, no SQLite table, no Math.NET. Snapshot advanced to P11.
- **2026-09-19** — Experiment protocol is now product truth ([App.md](App.md) “What one Optimize run does”). Keep: settle-by-measurement; screen then refine; experiment unit = writable motherboard group with tach; pairs from measured same-target effects including slight; one confirmation (accept pairwise blind spot); one model then re-optimize by workload; Quiet–Cool slider + optional targets; RPM as noise proxy; 1 °C plateau; anytime cancel; Low-only cash-out is live headroom toward the cool end, not a louder default; rate-of-rise + stall exclusion; highest stable experiment load; High stays baseline-only when it hits 90 °C. Reject: GP / Expected Improvement / NSGA-II / Plackett-Burman / Kneedle / Math.NET search; microphone; replace the slider; replicate every point; default dual-direction hysteresis; “always add airflow when confidence is Low”; slope-threshold diminishing returns; three-way search; designing for 8+ independent channels now (P12 clusters by zone if that appears). P3–P10 v1 shipped but are `needs revision`. P11 rewritten (re-optimize, do not re-test) and blocked on P14. P12/P13 updated. P14 added. Snapshot moved off P11 to P14 slice 1 (settle + rate-of-rise). `App direction discussion.md` stays scratch, not a spec.
- **2026-09-19** — P4 Everyday baseline heat: baseline order is idle → everyday (quarter of CPU threads, lighter GPU) → Low → High → cooldown. Everyday rise is Measured. Fan tests stay on Low. High stays the ceiling check and may abort. Everyday is not Measured gaming. Snapshot stays on P14 slice 1.
- **2026-09-19** — P0–P10 brought up to current App.md in place. Fan-test dwell is settle-by-measurement (90 s timeout, no 45/75 s kitchen timer). Rate-of-rise abort (3 °C/s). Screen 40/70/100 then refine 55/85 on groups that moved a temperature; stalled 0 RPM points dropped; live `FanSpeedCurve` feeds P8. Smart pairs include slight same-target effects. One confirmation after apply; miss adds 5% duty. Cancel/abort with a useful group yields a quieter conservative policy. Live headroom toward the cool end when heat exceeds the test load. Low-only maps cannot claim High confidence. Writeup has confirmation, RPM-as-noise Inferred, expected vs baseline, and load-range honesty. One Optimize action runs baseline → fan tests → interactions → policy. Safety wording aligned: duty 0–100%, not invented min/max RPM numbers. P2 stays parked. P14 product rules are absorbed; snapshot moves to P11 (not started). Ceilings unchanged.
- **2026-09-19** — P11 workload policies shipped: same thermal model, live policies from CPU+GPU power (desktop / gaming / render / mixed; burst vs sustained). Sustained power rise raises matching groups toward the cool end before the temperature spike. Burst does not. Desktop idle stays quieter. Low-only recommended `DutyPercent` unchanged until live power or temp needs the cool end. No second experiment tour, no activity picker, no louder default from Low confidence. Snapshot advanced to P12. Ceilings unchanged.
- **2026-09-19** — P12 case priors shipped: generic mid-tower zone table may only **order** P5 screening. A matched name (front/rear/top/CPU Fan) still gets a confirmatory measurement. `Fan #1` / unlabeled SYS headers stay unknown and are still tested. Manufacturer SKU encyclopedia and case-layout graphic stayed parked (P2). Priors do not change the fitted model. Snapshot advanced to P13. Ceilings unchanged.
- **2026-09-19** — P13 continuous learning shipped: periodic observe-only confirmation while holding; persist confirmation locally; ambient-shift / miss marks the operating point stale; user chooses Re-check (targeted groups) or Ignore. No automatic full re-characterization, no cloud, no per-title game list. Snapshot stays on P13 done. P2 remains parked. Ceilings unchanged.
- **2026-09-19** — **v1.0.0:** P0–P13 treated as the first product. New work is versioned (v1.1+) from use, not new P-phases. GPU fan writes and P2 labels stay parked candidates. First git commit + `v1.0.0` tag. Ceilings unchanged.
- **2026-09-19** — Live Temperatures / Power / fan RPM lists were launch-only. They now refresh once a second from `ReadSnapshot`. Found while using v1. Not a new phase. Ceilings unchanged.
- **2026-09-19** — **v1.1:** Baseline GPU compute now targets the discrete card (skip WARP/iGPU) and uses a much heavier Low job so a 5070 Ti actually draws power. Dropped the all-core High baseline pass — it always hit 90 °C here and skipped cooldown. Rise/decay now use Low (old High samples still work). Fan tests stay Low. Ceilings unchanged (CPU 90 / GPU 83).
- **2026-09-19** — Live baseline still showed GPU Core ~38 °C / ~2% / ~33 W. The Low dispatch (16M threads) exceeded the DirectX 65,535 thread-group cap, so the GPU job died; Everyday was legal but too light. Dispatch is now 1,048,576 (within the cap), Low does a long ALU loop, and failed dispatches no longer kill the heater. Ceilings unchanged.
- **2026-09-19** — **v1.2:** GPU heat is a hidden in-process DX12 3D raster burn (Vortice; Everyday 1280×720 / Low 2560×1440; skip WARP/iGPU). ComputeSharp compute heater removed. Optional CPU/GPU abort fields clamp 65–90 / 65–83 and cannot raise the floor. Targets still clamp to abort − 1. Other sensors stay 95. Ceilings unchanged.
- **2026-09-19** — **v1.3:** Product window matches the ChatGPT Home look: Setup when Administrator / PawnIO / competing software fails (Optimize off); Home with one Optimize; Running uses Cancel; Holding uses Stop and What we learned. Extra experiment buttons stay under Advanced. Slider stays locked while running or holding. Penpot file holds the same four screens. Ceilings unchanged.
- **2026-09-19** — Home GPU name is clickable when more than one GPU exists. Default pick is the discrete card (skip UHD/Iris/iGPU). Sensors, identity, and the hidden DX12 heater follow that card. Choice is `PreferredGpuId` in settings.json. Locked while a test or Optimize is running. No GPU fan writes. Ceilings unchanged.
- **2026-09-19** — Home layout follows window size without a NuGet panel library. Below 1000 px: fans move under the main column, hardware is one column, live readings are 2×2, tighter margins. At 1000 px and up: fans stay on the right. Fonts do not zoom. Ceilings unchanged.
- **2026-09-19** — Fan groups box can hide unused headers (0 RPM or no tach), same rule experiments already use. Choice is saved in settings. Does not change which groups get tested. Penpot Home boards show Hide unused; Holding shows Show unused. Ceilings unchanged.
- **2026-09-19** — Hide unused does not hide GPU headers: 0 RPM on the card is often idle, not unplugged. Motherboard empty headers can still be hidden. A FanControl-style spin-up detect pass is not built (GPU writes stay parked). Ceilings unchanged.
- **2026-09-19** — Window has no scrollbars. Home/Learned/Advanced fit the window; the page shrinks only if it would overflow. Advanced uses two columns when wide. Ceilings unchanged.
- **2026-09-19** — Short windows now shrink the page the same way narrow windows do. Each tab is given the leftover height under the title, so it scales down instead of clipping. Ceilings unchanged.
- **2026-09-19** — **v1.4:** Motherboard detect pass on Home (same checklist as PawnIO). Find connected writes one writable motherboard group at a time through `SafeFanSession`, waits for RPM, restores BIOS after each. Same CPU 90 / GPU 83 abort limits. No synthetic heat. GPU and pumps are not written. Result is saved in settings; Optimize stays off until this check is green. Idle-but-connected headers can be tested later; measured empty headers are hidden. Ceilings unchanged.
- **2026-09-19** — Connected-fans check is only “set header to 100%, did RPM rise?”. It does not add CPU/GPU heat. Headers already spinning are also set to 100%. Found fans stay at 100% until the pass ends, then BIOS. Ceilings unchanged.
- **2026-09-19** — Window resize no longer zooms or rearranges the page. Research: a full-window Viewbox scales glyphs and letterboxes; WPF should reflow with Grid and lock MinWidth/MinHeight to the designed layout. Viewbox and the <1000 px compact stack are removed. Window minimum is 1000×720. Ceilings unchanged.
- **2026-09-19** — Fan #1 is not a pump just because a Super I/O flow sensor shares its index. Only names like Pump / AIO Pump / W_PUMP stay skipped. Pump Fan headers stay writable fans. Ceilings unchanged.
- **2026-09-19** — Connected-fans detect keeps a header if it has RPM, including a hub already at 100%. Only 0 RPM after 100% is empty. A spinning fan is not hidden by an old empty mark. Ceilings unchanged.
- **2026-09-19** — Direction: v1.4 is the UI starting point, not a finished first-run. The measurement engine (P0–P13) stays. Next product work is live testing, then a **guided Home path** (v1.5 candidate) so ready → connected fans → Quiet–Cool → Optimize → hold. Do not invent P14/P15. Do not start v1.5 until the user agrees after that conversation. App.md now says the first session is a guided sequence; Advanced stays optional retry. Ceilings unchanged.
- **2026-09-20** — Watch/baseline GPU heat was `Present(0)` with tearing (uncapped frames). That made GPU VRMs coil-whine. Heat now waits for a display refresh (`Present(1)`, no tearing). Same 720p/1440p shader work. Ceilings unchanged.
- **2026-09-20** — **v1.6:** Live Optimize showed Watch Low (half CPU threads) aborting at 90 °C CPU while GPU 1440p stayed ~61 °C. Fan tests then died on rate-of-rise from a ~52 °C start. CPU Low now matches Everyday workers; stronger heat is heavier GPU only. Rate-of-rise (3 °C/s) arms only within 10 °C of the effective abort. Aborted Watch or empty fan tests Fail/retry instead of Continue. Cancel still helps when a group was measured. Ceilings unchanged (CPU 90 / GPU 83). No new user settings.
- **2026-09-20** — **v1.7:** NVIDIA GPU fans on one card are one experiment unit. Detect writes 100% once per GPU, then marks each fan by RPM. Fan tests measure one representative; siblings skip as coupled. Pairs never combine two fans on the same GPU. NVAPI already sets every cooler; FakeHardware mirrors that. Home still lists each GPU fan RPM. Ceilings unchanged. No new user settings.
- **2026-09-20** — Live Optimize after v1.6 (window still pre-v1.7): Watch **Completed** — Idle ~47 °C CPU, Everyday ~58, Low ~69 CPU / ~47 GPU, cooldown ran. GPU rise ~2.6 °C. Fan tests ~5 min then **Aborted** rate-of-rise while refining GPU Fan 3 (CPU ~64 → ~78 °C in ~2 s; GPU Core ~35 °C). Measured: CPU Fan and System Fan #1 (hub) very high on CPU; GPU Fan 1 0→100%; GPU Fan 2 skipped already at 100%; GPU Fan 3 treated as a separate group. Pairs CPU Fan + hub aborted in seconds on the same spike. `policy_confirmation` empty. Do not loosen 90/83. Next work is a behavior discussion, not silent v1.8.
- **2026-09-20** — **v1.8:** Heat lamp is calibrate-then-freeze. Watch raises GPU work per frame at `Present(1)` until GPU Core is about 15 °C above idle (or the work/ceiling cap), then freezes that profile for Low and fan tests. CPU workers stay Everyday. No `Present(0)`. Rate-of-rise arms within 5 °C of the abort. Missing preferred temps and GPU device-loss abort and restore fans. Ceilings unchanged (CPU 90 / GPU 83). No new user settings.
- **2026-09-20** — **v1.9:** Finish the walk honestly. Thermal abort or cancel during individual fan tests with a partial map skips pairs and may Hold (quieter conservative policy; confirmation still runs). Lost temps, GPU reset, or competing software retry Fans, not Hold. GPU-target influence is Unknown, and NVIDIA GPU fans are not written for mapping, until Watch GPU Core rose about 15 °C. No experiment planner, 3×3 pairs, triples, τ settle, or extra heater architecture. Ceilings unchanged. No new user settings. Code shipped.
- **2026-09-20** — Live Optimize on v1.8 heat + v1.9 walk **Completed** through confirmation (~15 min). Watch: Idle ~49 °C CPU / ~46 °C GPU Core; Everyday ~59 CPU / ~46 GPU; Low ~62 CPU / ~59 GPU (GPU rise **+15.1 °C**); cooldown ran. Fan tests **Completed**: CPU Fan, System Fan #1 (hub), GPU Fan 1; GPU Fan 2/3 skipped as coupled. Pairs **Completed** (GPU Fan 1+CPU Fan, CPU Fan+hub, GPU Fan 1+hub). Confirmation **missed**: measured CPU 52 °C vs expected 64 °C (cooler than predicted); measured GPU Core 40 °C vs expected 20 °C (hotter than predicted; 20 °C is below idle). Added 5% airflow. Do not loosen 90/83. Next work is a named call on that GPU miss, not a planner.
- **2026-09-20** — Eventual fan-policy Core spec locked in [Fan Policy Mental Model.md](Fan Policy Mental Model.md): measured RPM → settled °C vs generated per-fan CPU/GPU-demand → duty; power first, temperature corrects; unknown influence stays unknown; GPU-aware generated policy blocked until confirmation is directionally right. Next **code** slice is still that GPU miss (decompose `Predict` vs the stored confirmation). Do not start the measured graph, named-anchor Hold, planner, or extra heater. App.md unchanged. Ceilings unchanged.
- **2026-09-20** — GPU confirmation **diagnosed** (no `Predict` change). `ThermalModel.Explain` + `ConfirmationDiagnosis` rebuild the stored miss from `%LocalAppData%\AUTO Fan\autofan.db`. Record: start GPU 41.7 °C (not idle 45.9); CPU Fan −5.3, hub −5.4, GPU Fan 1 −7.3; three pair leftovers −0.9/−2.4/−1.0 (total −4.3); other corrections none; predicted 19.6 °C vs measured 39.8 °C (error +20.3). Hub GPU Δ used duty 50→52% with RPM 982→915. Fan-test GPU ~53 °C vs confirmation ~40 °C. Next slice is the `Predict` fix, not graphs. App.md unchanged. Ceilings unchanged.
- **2026-09-20** — **v1.10:** GPU `Predict` honesty. Influence GPU/CPU points with no real speed-up (duty rose under 5%, or RPM did not rise) are Unknown. Pair leftovers apply only to exactly two fans; three or more sum singles only; Unknown contributes nothing. Confirmation GPU expected will not go below idle if start is at/above idle, and will not plunge below start if GPU is already cooler than idle. Extra 5% airflow still runs only on a real miss and does not make GPU policy trusted. App.md confirmation wording updated. Next work is a live re-confirm, not graphs. Ceilings unchanged.
- **2026-09-20** — Live re-confirm on v1.10 **Completed**. Watch GPU rise **+18.4 °C**. Fan tests + three pairs Completed. Hub GPU is a real speed-up (50→88%, RPM 979→1377), not 50→52%. Pair leftovers not stacked on three fans. Confirmation: GPU **39.0 vs 39.2** (no miss, no extra airflow). That match is the GPU floor at an already-cool start, not a measured ~6 °C GPU drop. CPU 52.0 vs expected 77.8 (cooler than predicted; not a miss). GPU generated policy stays untrusted. Next work is the measured graph when asked. Ceilings unchanged.
- **2026-09-20** — **v1.11:** Measured RPM → settled °C graph on Home diminishing returns (per fan, CPU/GPU). Points from `FanSpeedCurveBuilder`; 1 °C plateau from `DiminishingReturnsAnalyzer`; consecutive measured RPM only; stalls omitted. No live marker, no `Predict()`, no named-anchor Hold, no demand→duty graph. App.md §8 wording updated. GPU generated policy stays untrusted. Ceilings unchanged.
- **2026-09-20** — **v1.12:** The measured graph is readable: Penpot Charts page is the spec. Axis titles (RPM across, settled °C up), labeled points, a dashed recommended-speed line, a green 1 °C plateau band, and a plain-language caption. Fan and CPU/GPU pickers are labeled. No live marker, no `Predict()`, no named-anchor Hold. GPU generated policy stays untrusted. Ceilings unchanged.
- **2026-09-20** — **v1.13:** App chrome matches Penpot Screens: 1100×720 canvas, 28px page pad, 16px vertical stack, 20px Home/fan-column gap, 300px fan card. What we learned uses the Charts graph card (title, CPU/GPU chips, plot, legend, caption) then the writeup rows. Advanced is one column and does not repeat the graph. No live marker, no `Predict()`, no named-anchor Hold. GPU generated policy stays untrusted. Ceilings unchanged.
- **2026-09-20** — **v1.14:** Graph spikes were leftover BIOS holds and hub duty flicker (one snapshot per reported percent) drawn as if they were settled speeds. `FanSpeedCurveBuilder` now plots only Perturb/Screen/Refine holds that actually settled; a 10% duty jump starts a new hold; stalled 0 RPM stays omitted. No live marker, no `Predict()`, no named-anchor Hold. GPU generated policy stays untrusted. Ceilings unchanged.
- **2026-09-20** — **v1.15:** Live numbers did not prove the product (CPU curve went the wrong way; hub ~0.05 °C; confirmation matched a GPU floor). Watch now takes three BIOS settle-holds after Low, labels CPU/GPU STABLE/DRIFTING/NOISY/UNAVAILABLE, and stores a minimum detectable effect. Fan tests cannot claim a Δ below that MDE, and cannot characterize a target that is not STABLE. One bad sensor does not abort Optimize. Repeating the BIOS control is required; do not replicate every fan speed. Isolated GPU interleave, causal validity, and BIOS A/B/A stay parked as v1.16+. Named-anchor Hold, planner, extra heater, and GPU generated curves stay parked. Ceilings unchanged.
- **2026-09-21** — **Direction B:** AUTO Fan reframed as a FanControl-like measurement-driven curve builder. Home shows editable fan-curve graphs (temperature → duty per fan group) plus "What we learned" evidence visualization. Quiet–Cool slider retained but secondary to manual curve editing until measurement data is better. Trust labels (Measured / Modeled / Inferred / Unknown) stay mandatory. Cancel still helps. Abort floors unchanged (CPU 90 / GPU 83; user fields may only tighten). BIOS → AUTO → BIOS on locked heat remains the validation bar (not built yet). App.md and IMPLEMENTATION.md Current snapshot rewritten for Path B before any curve UI code. Next step after docs is a plan for the smallest Home curve-seed slice for Pike review — not implemented in this PR.
- **2026-09-21** — **Path B doc audit:** Remaining contradictions aligned to Path B. Fan Policy Mental Model, App direction discussion, and Auto Optimize Mental Model are superseded/research-only banners (do not implement from them). README product blurb matches Path B. AGENTS.md next-slice matches this snapshot (curve-seed plan; Isolated GPU only if needed for curve-quality data). App.md no longer names Isolated GPU as the next proof, no longer says the result is “not necessarily a single fan curve,” and treats editable Home curves as the user-facing deliverable (Quiet–Cool secondary; What we learned = evidence). Historical P0–P14 text in this file stays; Current snapshot wins. Still documentation only — no curve-seed code.
- **2026-09-21** — **v1.16:** Phase 1 slice 1 (honesty). Fan-test and pair samples persist `Settled`. `FanTestRunner` / `InteractionRunner` mark a hold settled only when temperatures actually stop moving; a 90 s timeout is not evidence success. `InfluenceMapBuilder`, pair builders, and `FanSpeedCurveBuilder` take Measured Δ only from settled holds; timeout / still-moving series are Unknown. Old SQLite rows default not settled. Heat lamp persist, below-BIOS duties, Everyday heat, confirmation-on-lamp, and Home curve editor stay later slices. Phase 1 Hold remains today’s two-end Quiet–Cool policy (Danny 2026-09-21). Ceilings unchanged (CPU 90 / GPU 83).
- **2026-09-21** — **v1.17:** Phase 1 slice 2 (D2). Everyday and frozen Low `HeatProfile` persist on `baseline_run`. Fan tests and pairs `ApplyLow` that stored lamp. Missing lamp (old row or no Watch) aborts and does not heat with `DefaultLow`. `Set(Low)` no longer falls back to DefaultLow. Below-BIOS duties, Hold lost-temp abort, Everyday heat, confirmation-on-lamp, and Home curve editor stay later slices. Phase 1 Hold remains today’s two-end Quiet–Cool policy. Next slice at ship time was D6 (Hold abort on lost temps); superseded the same day by the locked experiment build order below. Ceilings unchanged (CPU 90 / GPU 83).
- **2026-09-21** — **Path B Phase 1 experiment locks** (plan only, no product code). Danny: heat ladder Idle (BIOS observe) → Everyday → Low → Hot (Hot = game-render-like, calibrate-then-freeze like Low, more GPU work-per-frame not all-core High, hard stop ~8 °C under abort CPU 90/GPU 83 → ~82/75; skip Hot and leave that band Unknown if it cannot separate enough °C from Low — **PLAN DEFAULT (confirm):** neither preferred CPU nor GPU ≥ 5 °C above Low; does not invent temps). Below-BIOS absolute duties required (loud-first then quiet; restore every hold; stall/0 RPM skip). Low+Hot screen 15/30/45/60/75/90/100, refine 20/40/50/70/85 on movers; Everyday stays 20/40/70/100 + refine 55/85. Build order after v1.17: below-BIOS dense Low → Everyday coarse → Hot if separates → point builder / confirm-on-lamp / BIOS→AUTO→BIOS → Phase 2 Home editor only after that. Phase 1 Hold stays two-end Quiet–Cool. ~25–40+ min first Optimize OK. Low thermal abort skips Everyday, pairs, and Hot. Adaptive duty gap-fill is **Later**, not Phase 1 (no 25-point / max-2 defaults locked). D6 Hold abort on lost preferred temps stays Edges / Later as a small optional safety PR — not the next experiment slice. Next **code** slice is below-BIOS + dense Low (D5). Details: [PATH_B_PHASE1_EXPERIMENT_PLAN.md](PATH_B_PHASE1_EXPERIMENT_PLAN.md). Ceilings unchanged (CPU 90 / GPU 83).
- **2026-09-21** — **Pike review of PR #5 (Danny go-ahead):** Two plan corrections, same PR, docs only. (1) Adaptive gap-fill is not Phase 1 — deferred / Later; agent-invented 25-point / max-2 defaults removed from the locked list. (2) Next code slice after this plan merges remains below-BIOS + dense Low grid; D6 stays Edges / Later, not the locked next experiment slice. Ceilings unchanged.
- **2026-09-21** — **v1.18:** Phase 1 slice 3 (D5). Low fan tests replace “duties above BIOS only” with an absolute dense grid: screen 15/30/45/60/75/90/100, refine 20/40/50/70/85 on movers. BIOS reference first, then loud first, then quieter, then refine; restore after every hold. High BIOS duty no longer skips quieter points; `AlreadyAtMaxDuty` only if BIOS is already 100 and nothing remains to write. Stall / 0 RPM is skipped, not fitted. Timeout / unsettled stays not Measured (v1.16). Stored Watch Low lamp still required (v1.17). Everyday coarse probes, Hot, confirmation-on-lamp, gap-fill, Home curve editor, and D6 Hold lost-temp abort stay later. Phase 1 Hold remains today’s two-end Quiet–Cool policy. Next slice is Everyday coarse probes. Ceilings unchanged (CPU 90 / GPU 83).
- **2026-09-21** — **Finish Phase 1 plan** (docs only, no product code). Remaining required work after v1.18 is four small PRs: Everyday coarse → Hot-if-separates (PLAN DEFAULT confirm: skip if neither preferred CPU nor GPU ≥ 5 °C above Low) → temp/duty point builder + confirm-on-lamp + duty-scaled expected → BIOS → AUTO → BIOS on locked Low. Home editor, gap-fill, all-core High, invented Hot, AMD GPU, mic/GP/planner stay out. D6 is optional Edges, not a Phase 1 gate. **No code until Pike Ships the plan.** Details: [PATH_B_PHASE1_FINISH_PLAN.md](PATH_B_PHASE1_FINISH_PLAN.md). Ceilings unchanged (CPU 90 / GPU 83).
- **2026-09-21** — **v1.19:** Phase 1 slice F1. After Low individuals **Completed**, cool-down gate, stored Everyday lamp, coarse probes on Low-movers only (20/40/70/100 then 55/85; loud first; restore every hold; heat id Everyday). Low abort/cancel skips Everyday. Missing Everyday lamp refuses that heat. Per-heat 30 min cap. NVIDIA writes at Everyday follow the +15 °C GPU-rise gate. Walk status copy only. Next is F2 Hot-if-separates. Does not change Low’s dense grid, abort ceilings (CPU 90 / GPU 83), Hot, point builder, confirm-on-lamp, BIOS A/B/A, gap-fill, Home editor, or D6.

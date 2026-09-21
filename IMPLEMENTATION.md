# AUTO Fan implementation path

Living tracker for building the product in [App.md](App.md). Rewrite the snapshot whenever a slice finishes or the product direction changes.

## How to use this file

| File | Job |
| --- | --- |
| [App.md](App.md) | What the product is and why. Do not implement from memory — read the section for the current phase. |
| [IMPLEMENTATION.md](IMPLEMENTATION.md) (this file) | What we are building *now*, what is done, and what is later. |
| [AGENTS.md](AGENTS.md) | Stack, folder layout, commands, and hard limits. |

Agents: implement **only** the current versioned slice. List later ideas; do not build them. When you finish a slice or [App.md](App.md) changes, update this file in the same change (snapshot, coverage statuses, decision log). Do not invent P14/P15.

Humans: the snapshot below is the “we are here” line. You do not need to read the rest unless you want the full map.

---

## Versioning

**v1.0.0** is the first complete product (P0–P13). New work is **v1.1, v1.2, …** from real use — one slice at a time. Git tag matches the version when a slice ships. Do not add a new P-phase.

---

## Current snapshot

- **Version:** v1.17 (Path B Phase 1 slice 2 — persist frozen heat lamp)
- **Status:** Watch saves Everyday and Low `HeatProfile` on the baseline run. Fan tests and pair tests reuse that stored Low lamp. A missing lamp (old database row, or Advanced tests without Watch) does **not** heat with `HeatProfile.DefaultLow` — the run aborts and asks for Watch. A 90 s timeout / still-moving fan-test hold is **not Measured**. GPU generated policy still untrusted. The product is not validated until a BIOS → AUTO → BIOS comparison on the same locked heat. **Path B (2026-09-21) supersedes the policy-only / not-a-curve-utility framing.** Phase 1 Hold stays today’s two-end Quiet–Cool policy (Danny 2026-09-21).
- **What the app actually does today:** After Administrator, PawnIO, competing apps, and Find connected, Optimize opens an owned walk. Watch may raise GPU work per frame at 60 Hz until GPU Core is about 15 °C above idle, then freezes that workload and **stores Everyday + that frozen Low profile with the run**. Still on BIOS/driver fans, it then settles and measures that same condition three times and labels CPU and GPU Core STABLE, DRIFTING, NOISY, or UNAVAILABLE. One bad sensor does not abort Watch. Fan tests apply the **stored** Low profile, not a silent 4-pass default. A fan-test Δ smaller than that sensor’s minimum detectable effect (`max(0.5 °C, range × 2)`) is None. A target that is not STABLE is Unknown. A hold that hits the 90 s timeout while temperatures are still moving is stored as not settled; influence and pairs treat it as Unknown (`Temperatures were still moving at the end of the hold`), not a Measured Δ. Individual fan tests that abort or cancel with a measured group skip pairs and may Continue to Hold (quieter conservative policy; confirmation still runs). Lost temps or a GPU reset retry Fans, not Hold. GPU-target influence stays Unknown, and NVIDIA GPU fans are not written for mapping, until Watch GPU Core rose about 15 °C. A fan-test GPU Δ is Unknown if duty did not rise by 5% or RPM did not rise. `Predict` adds a pair leftover only for exactly two fans; three or more use singles only. Unknown GPU influence contributes nothing. Confirmation will not publish a GPU guess below idle, or below start if GPU is already cooler than idle. What we learned shows the wander section, then the measured RPM versus settled °C graph in one card — only speeds we held until temperature stopped moving, not BIOS leftovers, timeout holds, or one-sample hub flicker — not a BIOS curve, not `Predict()`, no live marker. Advanced stays a single column of optional retries and does not repeat that graph. Close, Stop, abort, GPU reset, lost temps, or crash returns motherboard fans to BIOS and NVIDIA fans to the driver. Latest live confirmation: GPU 39.0 °C vs 39.2 °C expected (no miss, no extra airflow).
- **What the curve-as-product deliverable is (not built yet):** Editable fan-curve graphs (temperature → duty for each controllable fan group) visible on Home, plus "What we learned" evidence visualization. The app computes the quietest curves that meet cooling requirements; user may adjust. Quiet–Cool slider retained but secondary to manual editing until better measurement data exists. Trust labels (Measured / Modeled / Inferred / Unknown) remain mandatory — never sell guesses as Measured.
- **What it does not do yet:** Below-BIOS duties. Everyday / multi-heat mapping. Confirmation-on-lamp. Hold abort when preferred temps go missing. Curve editor UI on Home. Isolated GPU interleave (`R → speed → R`). Causal validity from power/clock. BIOS → AUTO → BIOS validation. CPU characterization when CPU is not STABLE. Case layout labels (P2). AMD GPU fan writes. Live Quiet–Cool slider changes while holding. Named-anchor Hold.
- **Next slice:** Hold abort when preferred temps or GPU telemetry are lost (Phase 1 slice 3 / D6). Do not start Home curve editing, Everyday heat, or below-BIOS duties in that slice.
- **Blocked on:** Pike review of this slice. Do not loosen abort ceilings (CPU 90 / GPU 83).

---

## App.md coverage map

Statuses: `not started` · `in progress` · `done` · `needs revision` · `deferred`

### Numbered “How It Works” sections

| App.md | Need | Phase | Status |
| --- | --- | --- | --- |
| What one Optimize run does | Single characterization checklist: walk window; Watch 3× BIOS reference; persist Everyday + frozen Low heat; settle, screen/refine, pairs only if individuals finished, one confirmation, anytime cancel, live headroom; aborted Watch / empty fan tests / lost temps / GPU reset retry; thermal abort with a partial map skips pairs and may Hold. Timeout holds are not Measured. Missing stored lamp refuses fan tests (no silent DefaultLow). BIOS A/B/A not built. | P9, P10, v1.5, v1.6, v1.9, v1.10, v1.15, v1.16, v1.17 | done (v1.17 stored lamp) |
| §1 Discover hardware | CPU, GPU, motherboard, power, fan controllers, RPM, pumps, thermal sensors, controllable groups; Home detect: 100% + RPM present; NVIDIA GPU fans writable, one probe per card | P1, v1.4, v1.5, v1.7 | done |
| §1 User layout | User identifies case, fan positions/sizes/orientation, cooler/radiator, GPU, radiator locations, intake/exhaust | P2 | not started (parked) |
| §1 Case database | Known case fan locations and approximate airflow geometry as **priors only** | P12 | done (generic mid-tower prior; no SKU catalog) |
| §2 Baseline | Observe without changing fans; idle / everyday / stronger (calibrate GPU work-per-frame at 60 Hz until ~+15 °C from idle, then freeze); persist Everyday + frozen Low `HeatProfile` on the run; three BIOS settle-holds; STABLE / DRIFTING / NOISY / UNAVAILABLE per CPU and GPU Core; MDE = max(0.5 °C, range × 2); record temps, RPM, duty, power, workload, clocks, rise/decay/settle; ambient when available. Settle seconds are a baseline metric, not fan-test dwell. No all-core High abort pass. Hidden DX12 3D GPU heat. | P4, v1.1, v1.2, v1.6, v1.8, v1.15, v1.17 | done |
| §3 Individual fan tests | Screen then refine writable motherboard groups and one NVIDIA GPU fan set per card with tach; reuse the stored Watch Low lamp (refuse if missing); settle-by-measurement; 90 s timeout is not Measured; stall exclusion; influence map; GPU-target Unknown and no NVIDIA GPU writes until Watch GPU Core rose ~15 °C; Δ below MDE is None; non-STABLE CPU/GPU targets are Unknown | P5, v1.5, v1.7, v1.9, v1.10, v1.15, v1.16, v1.17 | done (v1.17 stored lamp) |
| §4 Fan interactions | Pairs from measured same-target effects including slight; only after individuals finished; only settled holds yield a Measured residual; no GPU–GPU pair on the same card; one confirmation; no full factorial; skipped pairs are Unknown | P6, v1.7, v1.9, v1.10, v1.16 | done (live: three pairs completed; pair leftovers only for two fans; timeout holds Unknown) |
| §5 Thermal model | Learned model of *this* machine; additive + measured leftovers; no High confidence for an untested hotter load | P7, v1.10 | done (3+ fans use singles only; Unknown contributes nothing) |
| §6 Priorities | Quiet–Cool slider secondary to Home curve editing; optional targets; optional CPU/GPU stop-test fields that cannot exceed 90/83; RPM as noise proxy; live headroom along generated curves, not a louder default | P9, v1.2 | done (slider shipped; Home curve editor not built) |
| §7 Same curves, different heat | Live control may move along the Home curves as heat changes; no second experiment tour per activity | P11 | done (live heat still reuses the same model; curves not on Home yet) |
| §8 Diminishing returns | 1 °C plateau on a real multi-speed refinement curve; quietest plateau point; measured RPM vs °C graph | P8, v1.11, v1.12, v1.13, v1.14, v1.16 | done (settled holds only, including persisted Settled; Penpot-matched card; no live marker; GPU policy untrusted) |
| §9 Case priors to speed tests | Geometry may **order** screening; must not skip a confirmatory point; many headers → cluster by zone | P12 | done |
| §10 Show what we learned | What we learned is evidence behind Home curves; wander section; confirmation line; RPM-as-noise Inferred; Low-load limits not Measured gaming truth; GPU cooling without ~15 °C rise, non-STABLE targets, and skipped pairs are Unknown | P10, v1.9, v1.10, v1.15 | done (evidence writeup shipped; Home curve editor not built) |

### Other App.md promises

| App.md | Need | Phase | Status |
| --- | --- | --- | --- |
| Product / Core Promise | Install, Optimize, get editable Home curves from measured data; Quiet–Cool secondary; cancel still helps | P9, v1.5 | needs revision (v1.15 still holds a two-end policy; curve UI not built) |
| What the User Ultimately Gets | One **Optimize** walk; editable Home curves + What we learned; Quiet–Cool secondary; cancel still helps | P9, v1.3, v1.5 | needs revision (curves not on Home yet) |
| What This Product Really Is, items 1–3 | Observe; **safely** perturb cooling; measure thermal response (settle + rate-of-rise near the ceiling) | P3, P4, P5, v1.6 | done |
| What This Product Really Is, items 4–6 | Learn fan/workload/temp relationships; interactions; per-machine model | P5–P7 | done |
| What This Product Really Is, items 7–8 | Show editable Home curves + evidence; keep adapting by confirm-and-correct, not full re-test | P9, P11, P13 | needs revision (item 7 curves not built; item 8 confirm-and-correct shipped) |
| Long-term vision | Per-game patterns, seasonal ambient, periodic **confirmation** / drift — not automatic full re-characterization | P13 | done (periodic confirm + ambient/miss drift; targeted re-test only after the user says so. No per-title game database.) |
| §4 / §10 trust rule | Never present inferred airflow/pressure as if it were measured; RPM-as-noise is Inferred; Low-load gaming guesses are not Measured | P6, P10 | done |

### Implied (needed to keep App.md honest, not their own marketing headings)

| Need | Phase | Status |
| --- | --- | --- |
| Solution skeleton, fake hardware, tests that do not touch real fans | P0 | done |
| Hardcoded safety ceilings (max temp, duty 0–100%, experiment abort) — never loosen. Empty 0 RPM / stall is skipped, not a numeric RPM min/max. User abort fields may only stop sooner (v1.2). | P0, enforced every later phase | done |
| Live PWM/fan write only behind restore-on-exit, crash recovery, abort, competing-software warning. Motherboard BIOS + NVIDIA driver restore (v1.5). | P3, v1.5 | done |
| Workload generation for baseline/experiments, only after P3, never without abort limits | P4+ (gated by P3), v1.8, v1.17 | done. Calibrate GPU work per Present at 60 Hz, freeze for Low/fan tests, persist that lamp. CPU workers stay Everyday. Missing stored lamp does not fall back to DefaultLow. Device-loss and missing preferred temps abort. Rate-of-rise arms within 5 °C of the effective abort. Live Watch on this PC: GPU Core +15.1 °C from idle. |
| Apply the learned strategy as a live control loop (v1.15 two-end policy; Path B product is editable Home curves) | P9 | done (actuator still required) |
| Distinguish Measured / Modeled / Inferred on every number shown to the user | P10, v1.16 | done (timeout holds cannot launder as Measured) |

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
- **Shipped:** Settle-by-measurement dwell with a 90 s timeout. Screen at 40/70/100%, refine 55/85% only on groups that moved a temperature. `FanSpeedCurveBuilder` fills live P8 bands from settled Perturb/Screen/Refine holds (v1.14). Stalled / 0 RPM points are dropped. A GPU/CPU Δ is Unknown if duty did not rise by 5% or RPM did not rise (v1.10). Experiment unit is a writable motherboard header with tach, or the NVIDIA fans on one card. Pumps and AMD/Intel GPU fans stay skipped. GPU-target influence is Unknown, and NVIDIA GPU fans are not written for mapping, until Watch GPU Core rose about 15 °C (v1.9).

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
- **2026-09-21** — **v1.17:** Phase 1 slice 2 (D2). Everyday and frozen Low `HeatProfile` persist on `baseline_run`. Fan tests and pairs `ApplyLow` that stored lamp. Missing lamp (old row or no Watch) aborts and does not heat with `DefaultLow`. `Set(Low)` no longer falls back to DefaultLow. Below-BIOS duties, Hold lost-temp abort, Everyday heat, confirmation-on-lamp, and Home curve editor stay later slices. Phase 1 Hold remains today’s two-end Quiet–Cool policy. Next slice is D6 (Hold abort on lost temps). Ceilings unchanged (CPU 90 / GPU 83).

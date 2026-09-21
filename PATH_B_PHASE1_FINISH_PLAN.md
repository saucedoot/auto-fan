# Path B — Finish Phase 1 (remaining slices)

**Date:** 2026-09-21  
**For:** Pike review (Ship / Ship after fixes / Do not ship)  
**Status:** Plan only. **Do not implement product C#/XAML in this PR.** No build agent starts until Pike **Ships** this plan.  
**Parent:** [PATH_B_PHASE1_EXPERIMENT_PLAN.md](PATH_B_PHASE1_EXPERIMENT_PLAN.md) (locked ladder). Audit: [PATH_B_CODE_AND_EXPERIMENT_AUDIT.md](PATH_B_CODE_AND_EXPERIMENT_AUDIT.md). Tracker: [IMPLEMENTATION.md](IMPLEMENTATION.md).  
**Audience:** Danny (what Optimize will do) and Pike (what each PR may touch).

Abort floors stay **CPU 90 °C / GPU 83 °C / other 95 °C**. Restore-on-exit stays. No Home editor. No gap-fill. No all-core High. No invented Hot. No AMD GPU writes. No mic / GP / planner.

---

## Sequential thinking (what this file is answering)

1. **What is already on master?** Honesty timeout flag (v1.16), stored Everyday + Low heat lamps (v1.17), and quieter-than-BIOS dense Low fan tests (v1.18). Hold is still today’s two-end Quiet–Cool policy. Confirmation still runs after the heater is off. Everyday / Hot fan probes, the temp→duty point list, and BIOS → AUTO → BIOS are not built.
2. **What is left before Phase 2?** Four required experiment slices (Everyday, Hot-if-it-separates, point builder + confirm-on-lamp, then BIOS → AUTO → BIOS). Home curve editor is Phase 2. Adaptive duty gap-fill is Later. D6 (Hold abort if temps vanish) is optional Edges only — not required to finish Phase 1.
3. **How to ship it?** Four small PRs in that order. Do not one mega PR.
4. **What must wait on what?** Everyday before Hot. Both heats (or Hot skipped Unknown) before the point builder. Confirm-on-lamp with that builder. BIOS → AUTO → BIOS last. A Low thermal abort skips Everyday, Hot, and pairs. Ceilings stay 90 / 83.
5. **This file** is the remaining-work map. Product code waits for Pike Ship.

---

## 1. Already on master (do not rebuild)

Cited from [IMPLEMENTATION.md](IMPLEMENTATION.md) Current snapshot (v1.18) and [PATH_B_PHASE1_EXPERIMENT_PLAN.md](PATH_B_PHASE1_EXPERIMENT_PLAN.md) sections D1 / D2 / D5 and table E slices 1–3.

| Version | What shipped | Plain language |
| --- | --- | --- |
| **v1.16** | Settled flag; 90 s still-moving holds are **not Measured** | A timeout is “we never saw it stop,” not a temperature fact. |
| **v1.17** | Everyday + frozen Low `HeatProfile` on the Watch run; missing lamp **refuses** to heat (`DefaultLow` silent fallback gone) | Fan tests reuse the same heat Watch froze, even after restart. |
| **v1.18** | Absolute dense **Low** grid, including **below BIOS** | Screen 15/30/45/60/75/90/100; refine 20/40/50/70/85 on movers; loud first then quiet; restore every hold; stall / 0 RPM skipped. |

**Still true today (not a bug in this plan):**

- Watch is Idle → Everyday → Low (3× BIOS labels). No Hot in Watch.
- Fan tests still heat **Low only**. Everyday lamp is stored but not used for probes yet.
- Pairs stay Low-only, and only if Low individuals finished.
- Confirmation still predicts from historical Δs and still settles with the **heater off**.
- Phase 1 Hold is still the two-end Quiet–Cool **policy**, not live curves.
- BIOS → AUTO → BIOS is still the validation bar, not built.

---

## 2. Remaining Phase 1 (foundation ready for Phase 2)

Phase 2 (Home curve editor) may start only after this list is done **and** the last slice’s BIOS → AUTO → BIOS check is not a loss (AUTO hotter at similar or higher fan effort → do **not** start the editor).

### Required (four PRs)

1. **Everyday coarse probes** — extra temperature band on fans that already mattered at Low.
2. **Hot lamp if it separates** — a hotter band via more GPU work-per-frame, or skip and label Unknown.
3. **Temperature/duty point builder + confirm-on-lamp + duty-scaled expected** (honesty D3 + D4).
4. **BIOS → AUTO → BIOS** on the locked Low lamp (observe protocol; still policy Hold, not curves).

**“Foundation ready” means:** SQLite has honest Measured `(temperature, duty, RPM, heat)` points at **more than one heat** for at least one CPU-linked fan; Hot is either Measured or explicitly skipped Unknown; confirmation was taken **with the Low lamp on** and expected temps match **applied** duties; a Low-lamp BIOS → AUTO → BIOS run is stored and is not worse than BIOS at similar fan effort.

### Not required to finish Phase 1

- **D6** Hold abort when preferred temps go missing — **Edges / Later**, optional small safety PR. Mentioned below; **do not schedule it** as a Phase 1 gate.
- Adaptive duty gap-fill — **Later**, not Phase 1.
- Home curve editor, interpolating Modeled segments, live curve actuator — **Phase 2**.

### Explicitly out (every remaining slice)

Home editor · gap-fill · loosening 90/83 · all-core High / extra CPU threads · inventing Hot temperatures · AMD GPU fan writes · Isolated GPU interleave · named-anchor Hold · mic / GP / planner · applying `TemperatureDutyPoint` live.

---

## 3. Ordered slices (small PRs)

Suggested versions are **v1.19–v1.22** after Pike Ships. Each code PR updates [IMPLEMENTATION.md](IMPLEMENTATION.md) in the same change, runs `dotnet test` and `dotnet format --verify-no-changes`, and does **not** start the next slice.

Walk UX stays **one** “Test fans” step. Status text may say which heat is on. No second walk page.

### F1 — Everyday coarse probes (next code after this plan Ships)

**Goal (plain language):** After a **completed** Low fan-test pass, wait until CPU/GPU are cool enough (same ≤ 60 °C gate), turn on the **stored Everyday** lamp (the light everyday heat Watch already saved — not a new calibration), and test **only the fans that moved a temperature at Low**. Use a **coarser** speed list than Low: 20 / 40 / 70 / 100, then 55 / 85 on movers. Reuse Watch’s idle BIOS snapshot if it settled; if not, one idle sit with **no fan writes**. That gives a cooler temperature column for later graphs.

| | |
| --- | --- |
| **In scope** | Idle observe reuse (or one extra idle settle, no writes). Everyday probes on **Low-movers only**. Screen **20/40/70/100**, refine **55/85**. Loud then quiet. Restore every hold. **Per-heat 30 min cap** (do not share one 30 min timer across Low+Everyday). GPU gate per heat: **do not write NVIDIA fans at Everyday**; Everyday GPU-target points stay Unknown. Walk status copy only. Persist heat id on samples so later builder can tell Everyday from Low. Everyday runs **only if Low individuals Completed**. |
| **Out of scope** | Hot lamp. Confirm-on-lamp. Point builder UI. Pairs at Everyday. D6. Home editor. Changing Low’s dense grid. |
| **Files likely** | `FanTestRunner`, `FanTestSchedule` (Everyday vs Low grids), `FanTestSample` / `SqliteFanTestStore` (heat id), `OptimizeWalk` / `MainWindow.xaml.cs` (order + copy, not a new page), `IWorkloadActuator` / `SyntheticWorkloadActuator` (apply stored Everyday), `SafetyLimits` usage in `FanTestRunner` (per-stage clock). Tests: `FanTestRunnerTests`, `FanTestScheduleTests`. |
| **Tests / proof** | Unit: Low Completed → Everyday runs movers only with coarse grid; Low abort/cancel → Everyday **does not** start; BIOS 70 still writes 20/40; stall dropped; NVIDIA skipped at Everyday. Local (after merge, on Danny’s PC): SQLite has Everyday **and** Low samples; Watch temps still show two bands (~58 vs ~62 CPU historically). |
| **Done when** | Low-movers have Everyday holds (Measured or Unknown, never guessed). Low abort skips Everyday. Ceilings unchanged. |
| **Risks** | Extra wall-clock (~4–8 min). Everyday GPU will usually stay near idle — tag Unknown, do not write GPU fans. Quiet Everyday + leftover heat could abort — restore, keep what settled. |

### F2 — Hot lamp if it separates

**Goal (plain language):** Only after Low **Completed** (and after Everyday in the walk). Start from the stored Low lamp and add **more GPU work per frame** (same calibrate-then-freeze idea as Watch Low). **CPU workers stay Everyday — no all-core High.** Stop calibration ~**8 °C under abort** (about **82 °C CPU / 75 °C GPU**). Then sit on BIOS at that frozen Hot lamp. **PLAN DEFAULT (confirm, not a new locked temperature):** keep Hot only if preferred CPU **or** preferred GPU is **≥ 5 °C hotter** than that sensor’s Low BIOS-settled temp. If neither is, **skip Hot** and label that band **Unknown** — do not invent temps. If kept: same **dense** grid as Low, **Low-movers only**, persist the Hot profile.

| | |
| --- | --- |
| **In scope** | Calibrate-then-freeze from Low via GPU work-per-frame. Hard stop ~8 °C under 90/83. Confirm-default skip if neither CPU nor GPU ≥ 5 °C above Low. Dense grid on Low-movers when kept. Persist Hot profile **only when kept**. NVIDIA writes at Hot only if Hot GPU Core is ≥ 15 °C above Watch idle. Per-heat 30 min cap. Status copy. |
| **Out of scope** | All-core High. Extra CPU threads. Inventing a Hot band. Home editor. Confirm-on-lamp (next slice). Repeating pairs at Hot. Changing Everyday’s coarse grid. |
| **Files likely** | `HeatCalibrator` (Hot start-from-Low, 8 °C-under-abort stop), `HeatProfile` / `BaselineRun` / `SqliteBaselineStore` (optional Hot profile), `IWorkloadActuator` (`LockedHot` or equivalent — KISS, not a plugin bus), `FanTestRunner`, `FanTestSchedule`, `GpuHeat`, walk orchestration in `MainWindow.xaml.cs`. Tests: `HeatCalibratorTests`, `FanTestRunnerTests`. |
| **Tests / proof** | Unit: Hot skipped when Δ < 5 °C; kept when CPU or GPU ≥ 5 °C; no High CPU workers; abort stop at ~82/75; Low abort ⇒ Hot never starts; missing Hot after skip is OK not an error. Local: either Hot BIOS ≥ 5 °C above Low **or** stage skipped + Unknown. |
| **Done when** | Hot is Measured **or** explicitly skipped Unknown. Never a stretched Low. Ceilings still 90 / 83 (hard stop is a calibration brake, not a new floor). |
| **Risks** | On this PC Hot may skip often — that is correct. Calibration can approach abort — restore, do not loosen. Time ~8–20 min if kept, 0 if skipped. |

### F3 — Point builder + confirm-on-lamp + duty-scaled expected (D3 / D4)

**Goal (plain language):** From stored holds, Core can list honest points: “this fan, this sensor, this settled temperature, this duty, this heat.” Timeout / stall / skip = Unknown, not a drawn guess. Then when Optimize **confirms** the policy, keep the **Low lamp on** during that sit, and predict the temperature from the **duties we actually wrote**, not the biggest historical drop. After confirm, **stop the lamp** and Hold today’s two-end policy (unheated), as locked.

| | |
| --- | --- |
| **In scope** | `TemperatureDutyPoint` (Idle / Everyday / Low / Hot). Build-on-read from samples (prefer no new framework; extra sample columns / thin heat table only if needed). Measured only if settled + usable RPM + target STABLE at that heat + GPU +15 °C gate when claiming GPU. D3: scale expected Δ by applied duty (or only groups we wrote). D4: Low lamp on through confirm settle + miss/extra 5%; then `Stop` heater before idle Hold. Persist lamp-on + applied duties on confirmation. |
| **Out of scope** | Home XY editor. Modeled interpolation. Wiring points into `PolicySession`. BIOS → AUTO → BIOS protocol (F4). D6. Gap-fill. |
| **Files likely** | New Core `TemperatureDutyPoint` + `TemperatureDutyPointBuilder`; `FanTestSample` heat id / settled (already); `PolicyConfirmer.ExpectedSettled`, `ThermalModel.Predict` / `Explain` (small duty argument); `MainWindow.ConfirmAppliedAsync` / `ApplyRecommendedPolicyAsync`; `SqlitePolicyConfirmationStore`. Tests: new builder tests + `PolicyConfirmerTests`. |
| **Tests / proof** | Fixtures spanning idle/everyday/low/(hot-or-skip); timeout excluded; BIOS leftovers not knots (reuse v1.14 hold splitting). Quiet blend does **not** predict Low’s full GPU drop. Fake actuator still Low during confirm collect; `Stop` after. Local: one Optimize — confirmation GPU start ~ fan-test GPU, not idle; stored `lamp-on = true`. |
| **Done when** | Tests can print the knot list Phase 2 will read. Confirmation is on-heat and duty-honest. Hold still two-end policy, heater off. |
| **Risks** | Confirm + Low lamp may approach abort — restore on miss path still required. Mis-tagging BIOS leftover as a knot. |

### F4 — BIOS → AUTO → BIOS on locked Low (validation bar)

**Goal (plain language):** On the **same frozen Low heat**, sit on motherboard BIOS + GPU driver, then apply today’s **policy** (not curves), sit again, then restore BIOS/driver and sit again. Compare with the Watch minimum detectable effect. Save all three legs. This is the honesty check that AUTO at Low is not worse than BIOS. It is **not** a Home screen and **not** claiming Path B curves are done.

| | |
| --- | --- |
| **In scope** | Observe protocol: BIOS settle → apply current two-end **policy** → settle → restore BIOS/driver → settle. Same locked Low lamp. Persist three legs. Compare with MDE. Pike gate copy: AUTO cooler at similar RPM **or** as warm at less RPM; return-to-BIOS matches first BIOS within wander. |
| **Out of scope** | Curve editor. Applying `TemperatureDutyPoint`. Claiming the product is validated as FanControl curves. Changing abort floors. A second heat’s A/B/A. |
| **Files likely** | New small runner (e.g. `BiosAutoBiosObserver`) in Core; store table or rows; `MainWindow` after confirm / before idle Hold **or** Advanced-only **if Pike prefers not to add ~6 min to every first Optimize** — **default for this plan: run once at the end of a completed Low walk (Low Completed), still under the Low lamp, then stop heat and Hold.** Pike may Ship-after-fixes to Advanced-only. `SafeFanSession` for the AUTO leg only; BIOS legs are restore + observe. Tests with fake hardware. |
| **Tests / proof** | Fixture: three legs persist; AUTO duties are the policy; restore happened; comparison uses MDE. Local: one full Optimize on Danny’s PC after F1–F3. |
| **Done when** | Three legs in SQLite. If AUTO is **hotter at similar or higher fan effort**, **do not start Phase 2 UI** — that is a Pike stop, not a graph. |
| **Risks** | Extra time. Policy A/B/A is not curve A/B/A — still the right heat. Confirm+A/B/A both on lamp — abort restore must still run. |

---

## 4. Dependencies, abort / skip, safety

```text
v1.16 settled ─┐
v1.17 lamps  ─┼─ already on master
v1.18 Low grid┘
        │
        ▼
   F1 Everyday  ──requires── Low individuals Completed
        │
        ▼
   F2 Hot-if-separates  ──requires── Low Completed; uses Everyday data already stored
        │                     skip Hot if < 5 °C (confirm default) → Unknown band
        ▼
   F3 Point builder + D3/D4 confirm-on-lamp
        │                     needs heat ids from F1 (and F2 keep or skip)
        ▼
   F4 BIOS → AUTO → BIOS on Low lamp
        │
        ▼
   Phase 2 Home editor only if F4 is not a loss
```

**Must ship before what**

| This | Waits on |
| --- | --- |
| F1 Everyday | This finish plan **Shipped**; v1.16–v1.18 on master |
| F2 Hot | F1 (walk already has Everyday; skip rules share “Low Completed”) |
| F3 Builder + confirm | F1 at minimum (multi-heat points). Hot id in the builder even when skipped (Unknown). D3/D4 need the Low lamp from v1.17 |
| F4 BIOS A/B/A | F3 so confirmation is already on-lamp; comparing policy vs BIOS on a dishonest confirm is noise |
| Phase 2 editor | F1–F4 **and** F4 not a loss |
| D6 | Nothing in this chain — optional anytime, not a gate |

**Abort / skip (locked — do not reopen)**

- **Low thermal abort** (hit CPU 90 / GPU 83 / rate-of-rise near ceiling, or cancel with a partial Low map): **skip Everyday, skip Hot, skip pairs.** May Hold the quieter conservative two-end policy. Confirmation still runs if Hold applies. **Do not start a new experiment kind** after a thermal abort in Low.
- Everyday / Hot / pairs run **only if Low individuals Completed**.
- If an Everyday or Hot **stage** hits its own 30 min cap: persist what settled, skip the rest of **that** heat; do not one-shot the whole walk’s 30 min across heats.
- Quiet hold aborting toward 90/83: restore, keep louder Measured points, skip remaining quieter duties for that group.
- Lost preferred temps or GPU device-loss during a **test** stage: abort that stage, restore, **retry that walk step** (today’s rule). D6 would extend that to Hold — optional, not in F1–F4.
- NVIDIA GPU fans: skip writes at a heat whose GPU rise from Watch idle is **< 15 °C**. Everyday: do not write them.

**Safety ceilings (unchanged)**

- CPU **90** / GPU **83** / other **95**. User abort fields may only **tighten** (65–90 / 65–83).
- Hot calibration hard-stop ~8 °C under those floors (~82 / 75) is **not** a new ceiling and **not** permission to run closer to abort.
- Every write through `SafeFanSession`. Stop / abort / crash / exit: motherboard BIOS + NVIDIA driver restore; heater off. Never leave PWM stuck.
- Do not leave a duty held across heats.

---

## 5. Optional Edges (mention only — not scheduled)

**D6 — Hold abort on lost preferred temps.** Today a missing CPU reading can count as “under target” and the Hold loop can walk fans **quieter**. The fix: abort + restore, same as during heating. Files likely: `PolicySession`, `SafeFanSession.EvaluateSafety`, `PolicySessionTests`. Proof: unit test only (unsafe to fake on a real PC). **Not required** for “foundation ready.” **Do not** put D6 in F1–F4.

**Adaptive duty gap-fill** — Later. Not Phase 1.

---

## 6. What this plan did not verify

- No live Optimize or fan writes in this docs pass.
- No `dotnet test` on this Linux VM (`net10.0-windows`) — none required; no product code.
- Whether Hot can separate ≥ 5 °C from Low under ~82/75 on this PC — skip is allowed.
- Exact Everyday vs Low CPU gap (~58 vs ~62 historically) — Everyday still runs when Low Completed; GPU Everyday stays Unknown if the +15 °C gate fails.

---

## Pike gate

**Ship / Ship after fixes / Do not ship this plan.**

Pike **must Ship** before any build agent starts F1. This PR stays **draft**. Do not merge until Pike says so. Do not implement F1 in this PR.

**Recommendation (one sentence):** Finish Phase 1 as four PRs — Everyday coarse on Low-movers, Hot only if it sits ≥ 5 °C above Low (else Unknown), then honest temp/duty points plus confirm **on** the Low lamp, then BIOS → AUTO → BIOS on that same lamp — **no editor, no gap-fill, no High, no invented Hot, ceilings 90 / 83.**

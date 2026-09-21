# Direction decisions (second-opinion review)

This file is a rationale memo from the 2026-09-19 review. It is **not** the spec.

- Product truth: [App.md](App.md) — especially **What one Optimize run does** and the first-session guided path
- What to build now: [IMPLEMENTATION.md](IMPLEMENTATION.md) snapshot (v1.9 shipped; live confirmation missed on GPU)
- Hard limits: [AGENTS.md](AGENTS.md)

Do not implement from this file. Do not revive items in **Dropped**.

---

## What we are building

The app should press **Optimize**, measure how *this* PC’s fans actually move heat, and apply a quiet-enough / cool-enough policy. The user chooses priorities. The software does the rest.

Accuracy comes from better experiments, not from a research optimizer. A one-bump, fixed-timer test will lie. A 90-minute Gaussian-process campaign that aborts at 90 °C is not “automatic.”

FanControl executes a curve the user drew. This product decides what curve should exist by perturbing fans and measuring temperatures.

---

## Kept

These are product rules. They already live in App.md.

**Settle by measurement.** Wait until the relevant temperatures have stopped moving. Use a timeout and the existing abort ceilings. Do not use a fixed 45/75 second kitchen timer. Do not add a formal time-constant (τ) science step.

**Screen, then refine.** Cheap coarse speeds on every writable motherboard group that reports RPM. Extra speeds only on groups that actually moved a temperature. That refinement curve is what the 1 °C plateau needs.

**Experiment unit.** A writable group with an RPM reading: a motherboard header, or the NVIDIA fans on one card (they move together). A hub that gangs several physical fans is still one knob. Pumps and AMD/Intel GPU fans are not written. Commanded duty with 0 RPM or no tach match is a stall: drop the point, do not fit through it.

**Smart pairs, including slight effects.** Test pairs that already affected the same sensor. “Slight” counts — the App.md example (front −1.5 °C, top −0.7 °C, together −3.8 °C) must stay eligible. Do not pick the first three headers the motherboard listed.

**One confirmation, not a full search.** Pairwise tests will miss some three-way effects and some weak+weak pairs. After Optimize picks a setting, apply it, settle, and compare to the prediction. If it misses, add a little airflow and say so. Do not hunt every combination.

**One model, then re-optimize.** Gaming vs rendering vs idle changes *heat* (CPU/GPU power), not what a given fan physically does. Characterize once (ideally two *stable* heat levels: everyday and Low). Do not run an all-core High pass that hits the 90 °C abort. Do not re-run the experiment tour per activity.

**Slider stays.** Quiet–Cool plus optional CPU/GPU targets. Do not replace the slider with a ceiling-only control.

**Noise is RPM** unless a real sound meter exists. Say so in the UI (Inferred). Do not use a microphone. Room noise and permissions would make “measured” worse than the guess.

**Diminishing returns = 1 °C plateau.** Quietest measured speed within 1 °C of the coolest point. Needs the refinement curve, not a single +25% bump. No Kneedle. No extra slope knob for the user.

**Cancel still helps.** Interruptible: keep the best finished measurements and a conservative policy. A thermal abort or cancel during individual tests with a partial map skips pairs (pair knowledge Unknown) and may Hold; confirmation still runs if Hold applies. Design for useful in a shorter run, better if they let it continue. Do not plan a 45–90 minute first experience as the default.

**Review rule (not an engine).** Don’t spend a test unless its result could change the policy. When a finished dataset shows a specific miss, add that one named experiment. Do not build `NextExperiment()`, a protocol framework, or speculative hooks.

**Low-heat maps are not gaming truth.** They cannot claim High confidence for a hotter workload. That cashes out in the **live control loop**: if the PC runs hotter than the test load, fans may move toward the already-computed cool end. It does not start louder just because the label says Low.

**Safety.** Hard abort ceilings (do not loosen). Rate-of-rise abort (stop on a sudden climb **near the ceiling**, not only after 90 °C, and not on the first seconds of intended heat). Restore-on-exit / watchdog already required. Userspace restore is the layer we control; we do not depend on motherboard firmware to save a stuck duty.

**Case geometry** may *order* screening. It must not skip a confirmatory point on this unit. If many independent PWM channels show up later, cluster by zone (P12). Do not design for 8+ channels now.

**N is controllable groups**, not physical fans. Typical target is a handful of motherboard headers plus one NVIDIA GPU set.

---

## Live Optimize (2026-09-20) — how it behaved

Facts from stored runs, not guesses.

- **Watch (v1.6 heat) completed.** Idle ~47 °C CPU. Everyday ~58. Stronger heat ~69 CPU / ~47 GPU. Cooldown ran. CPU is stable. GPU barely moved (~2.6 °C).
- **Fan tests ran ~5 minutes, then aborted.** CPU Fan and System Fan #1 (hub) showed large CPU effects. GPU Fan 1 spun 0 → 100%. The window was opened before v1.7, so GPU Fan 2/3 were still separate; Fan 2 skipped at 100%, Fan 3 refine died when CPU jumped ~64 → ~78 °C in ~2 s while GPU Core was ~35 °C.
- **Pairs aborted immediately** on the same kind of CPU spike (CPU Fan + hub). No combined leftover. No confirmation row.
- **Cancel-still-helps fired.** The walk had a partial map, so it continued. That is not a finished Optimize.

---

## Decided (2026-09-20) — v1.9 walk honesty

1. **GPU heat / GPU map.** Watch still uses v1.8 calibrate-then-freeze. GPU-target influence is **Unknown** (not none, not weak) unless GPU Core rose about 15 °C from idle. Do not write NVIDIA GPU fans for mapping until then. CPU-target measurements still count. No more heater architecture in v1.9. If a live Watch with v1.8 heat still cannot move GPU Core, that is a later named heater slice.
2. **CPU spikes / RateOfRise.** Ceiling stays 90 / 83. Rate-of-rise still aborts the **current** individual test. It does **not** start pairs. Do not loosen the abort.
3. **Walk after a partial fan map.** Skip pairs, say so, Continue to Hold with the quieter conservative policy. Confirmation still runs if Hold applies. Empty map, lost temps, or GPU reset: retry Fans, not Hold.
4. **Close the old window.** Done for this live run: v1.8 heat and v1.9 walk were in-process.

## Live Optimize (2026-09-20) — v1.8 heat + v1.9 walk

Facts from stored runs, not guesses. About 15 minutes Watch → fans → pairs → confirmation.

- **Watch completed.** Idle ~49 °C CPU / ~46 °C GPU Core. Everyday ~59 CPU / ~46 GPU. Stronger heat ~62 CPU / ~59 GPU. GPU rise **+15.1 °C** (the mapping gate). Cooldown ran. CPU stayed under 64 °C.
- **Fan tests completed** (~4.5 min). CPU Fan, System Fan #1 (hub), and GPU Fan 1 were measured. GPU Fan 2 and 3 skipped as coupled. Empty headers skipped. No rate-of-rise abort.
- **Pairs completed** (~5 min). Three pairs finished, including combined readings: GPU Fan 1 + CPU Fan, CPU Fan + hub, GPU Fan 1 + hub.
- **Confirmation missed and added airflow.** Measured CPU 52 °C vs expected 64 °C (CPU was cooler than the guess). Measured GPU Core 40 °C vs expected 20 °C. 20 °C is below idle (~46 °C); the model added several large GPU-cooling numbers. The 5% extra airflow is the designed catch, not a finished GPU prediction.

## Open behavior questions

The GPU confirmation miss is the specific dataset miss. Do not start a planner. Decide whether the next named slice is: ignore GPU “cooling” that would predict below idle; ignore a fan whose duty barely changed; or leave confirmation’s extra airflow as the catch.

None of the parked heater / pair-grid / τ work is unlocked by this run.

---

## Dropped

Do not build these. They were proposed in the original second opinion and rejected.

| Idea | Why not |
| --- | --- |
| Gaussian process, Expected Improvement, NSGA-II, Plackett-Burman, Kneedle | Overkill and fragile at 4–6 knobs with ~25 noisy points. Discrete pick + additive model + measured leftovers is enough. No Math.NET / Python sidecar for search. |
| Formal τ step-response calibration | Settle-until-it-stops-moving gets the benefit without a science pre-test. |
| Replicate every point 2–3 times | Doubles the run. Replicate later only on the 2–3 plateau speeds if noise is a real problem. |
| Sweep every fan both directions (hysteresis) | Lab-correct, too slow for first Optimize. Check one lopsided curve later if needed. |
| Microphone dB | Permissions and room noise. RPM stays the proxy. |
| Replace Quiet–Cool with a temperature ceiling only | Most people do not know a target number. Optional targets already exist. |
| Always recommend more airflow when confidence is Low | On this class of PC, Low-only is the normal case. Quiet would stop meaning quiet. Use live headroom instead. |
| Slope threshold (e.g. 0.5 °C per 100 RPM) as the stopping rule | Fires on one noisy step. Keep the 1 °C plateau. |
| Three-way combination search | Rare, expensive. Confirmation at the end is the catch. |
| Per-workload experiment tours | Same physics, different heat. Re-optimize the same model. |
| Treat 45–90 minutes as the planned first run | Too long. Anytime cancel + useful-early is the UX. |
| Experiment planner / `NextExperiment()` / `OptimizationProtocol` / `ExperimentReason` | Review rule only. Fixed walk + named runners. Add one named test later if a finished dataset needs it. |
| 3×3 / 9-point pair grids, triple search, diagnostic-escape-hatch type | One confirmation is the catch. Candidate-duty pairs may **replace** discovery pairs later, only after a pair actually finishes. |
| `IThermalModel`, Pareto types, million virtual configs | Additive + leftovers inside `Predict()`. Quiet–Cool slider is the tradeoff. |
| Extra settle telemetry / τ model | Existing 5-sample / ~1 °C settle is enough until a finished run shows otherwise. |
| Two-strikes abort / new 20-minute UI budget | Classify existing abort reasons. 30-minute cap already exists. |

---

## Known holes we accept on purpose

- **Smart pairs miss some interactions.** Catch with the one confirmation, not a heavier model.
- **Low-heat effectiveness may not scale to gaming heat.** Catch with live headroom, not a louder default recipe.
- **No dB sensor.** Policies will sometimes mis-rank a loud high-pressure fan vs a quiet high-airflow fan at the same RPM. Be honest in explanations.
- **Fixed-timer v1 tests were revised in the P0–P10 catch-up.** Settle-by-measurement, screen/refine, smart pairs, and one confirmation now ship. P11–P13 shipped. v1.5 is the guided Optimize walk plus NVIDIA GPU fan writes. v1.6 is stable CPU experiment heat. v1.7 is coupled NVIDIA GPU fans. v1.8 is calibrate-then-freeze GPU heat. v1.9 skips pairs after a thermal abort in individual tests and keeps GPU-target influence Unknown until GPU Core actually rose. A full walk through confirmation **has** succeeded on this PC; the GPU prediction missed (~20 °C expected vs ~40 °C live) and added 5% airflow.

---

## FanControl (kept as positioning, not a feature list)

FanControl maps duty → RPM and runs curves the user authored. It does not speed a fan up and ask what happened to the GPU. That gap is the product.

# Fan Policy Mental Model

**Locked. Do not expand this conceptual model.** Further debate is churn. The next useful work is diagnosing the GPU confirmation miss, not more architecture.

> **AUTO Fan measures how each fan changes the thermal system, then uses that measured influence to generate a quiet-to-cool fan policy for the CPU/GPU workload actually occurring.**

That is substantially more differentiated than a traditional fan-curve editor. FanControl executes a curve the user drew. This product decides what policy should exist by perturbing fans and measuring temperatures — then it must actually generate that policy, not only pick two duties and interpolate.

This file is the **eventual Core specification**. It is not what v1.9 does today. [App.md](App.md) remains what ships now. Do not implement later sections because they are written here.

---

## 1. What AUTO Fan is today vs what this spec is

**Today (v1.9):** after Optimize, the app holds a Quiet–Cool **policy**: two duties per fan group (quiet end and cool end). A live loop walks between those two in 5% steps from power, workload, and temperature. “What we learned” is text. That solved “don’t make me draw fan curves.” It did not yet solve “we know how this case actually moves heat.”

A mid-load moment is effectively:

```text
Quiet = 45%
Cool  = 75%
"You're halfway there, so let's run ~60%."
```

That can work. It is not a learned fan curve. Real demand can be uneven (stay low, jump, plateau, jump again). Two-stop interpolation cannot represent that.

**This spec:** keep measuring, the Quiet–Cool slider, and safety. Change the **output** from “hold two duties and nudge” to a generated per-fan policy from CPU and GPU heat demand, shown as two different graphs, never edited by the user.

---

## 2. The live run that forced the lock

A full Watch → fan tests → pairs → Hold → confirmation completed on this PC (~15 minutes).

- **Watch:** Idle ~49 °C CPU / ~46 °C GPU Core. Everyday ~59 / ~46. Stronger heat ~62 / ~59. GPU Core rose **+15.1 °C** (enough to map GPU fans). Cooldown ran. CPU stayed under 64 °C.
- **Fan tests:** Finished. CPU Fan, System Fan #1 (hub), and GPU Fan 1 were measured. GPU Fan 2 and 3 skipped as one set. No temperature-spike abort.
- **Pairs:** All three pairs finished.
- **Confirmation:** Did not match the guess, so it added 5% more fan speed. CPU was actually cooler than predicted (52 °C vs 64 °C). GPU was hotter than predicted (40 °C vs 20 °C). **20 °C is below idle (~46 °C).** That GPU guess is not believable.

The walk proved the experiment can run and take over fans. It did not prove GPU cooling on this machine. CPU was 12 °C off too, just in the safe direction — confirmation only treats *hotter than predicted* as a miss, so CPU did not trip the catch.

**5% extra airflow can rescue the current session. It can never upgrade an unvalidated GPU model to “trusted.”**

This run also did not prove quieter-than-BIOS (no BIOS comparison, no loudness reading) and is not a game (confirmation settled after the tests).

---

## 3. Two graphs because there are two truths

### Graph 1 — What we measured (evidence)

During fan tests the heat is **frozen**. AUTO Fan changes speed; temperature is the result.

**X = actual RPM** (what we changed)

**Y = settled temperature** (what we saw)

```text
Temperature
    │
 75 │ ●
    │
 73 │
    │       ●
 71 │
    │
 69 │              ●────●
    │
    └────────────────────────── RPM
      600    900   1200  1500
```

Mark the 1 °C plateau:

```text
900 → 1200 RPM = meaningful gain
1200 → 1500 RPM = diminishing return
```

Points only. Never interpolate across a missing or stalled measurement. Do not put °C on X — that looks like a BIOS curve and recaptures the confusion this spec kills.

A live marker on this graph is misleading unless heat matches the frozen test load. The live marker belongs on graph 2.

This graph exists because AUTO Fan **actually performed those experiments**. It is the trust feature. It does not call `Predict()`.

### Graph 2 — What AUTO does (policy, later)

**X = this fan’s demand** (not “system heat”)

**Y = duty %**

```text
Fan Duty
 100│
    │                         ●
  80│                    ●
    │
  60│             ●
    │
  40│       ●
    │
  20│  ●
    └──────────────────────────
       This fan's demand
```

Show named anchors and a live demand/duty marker:

- Quiet floor
- Diminishing-returns boundary
- Cool end
- Current: 57% (example)

The visual can stay one-dimensional. Internally the policy still depends on both CPU and GPU heat:

```text
              CPU heat demand
                 │
                 ▼
              ┌─────┐
GPU heat ────►│ Fan │──► duty
demand        └─────┘
```

---

## 4. What we will not build

- A FanControl-style **editable** curve. The user views, inspects, understands, and chooses Quiet ↔ Cool. They do not drag points.
- A BIOS graph of **one temperature → one duty**. That is the guesswork App.md listed (which sensor, CPU vs GPU, pairs).
- One PC-wide **“thermal demand → duty”** scalar. Total watts throws away the influence map. A rear exhaust at 120 W CPU / 30 W GPU is a different job from 50 W CPU / 180 W GPU.
- Invented GPU (or CPU) coefficients so a graph looks complete.
- A 100-point mathematically elegant curve from three RPM samples.
- A second Optimize tour per activity (desktop / gaming / render). CPU vs GPU heat *is* those activities.
- “System heat” / VRM / case as a third control axis until a live run shows a fan that CPU and GPU heat cannot explain. Those targets stay in the writeup.
- Planner, extra heater architecture, or generated-curve UI until measured data justifies them.

---

## 5. Eventual control law

```text
Duty_i = f_i(H_CPU, H_GPU)
```

**H is a thermal-demand signal, not raw temperature.** Temperature is the *result* of the cooling you are controlling.

```text
CPU power / workload ──┐
                       ├─► CPU heat-demand state
CPU temperature ───────┘       (correction / feedback)

GPU power / workload ──┐
                       ├─► GPU heat-demand state
GPU temperature ───────┘       (correction / feedback)
```

Then:

```text
CPU heat demand
GPU heat demand
       ↓
fan-specific influence (measured only)
       ↓
desired duty
```

**Power first. Temperature corrects.** Watts establish demand (baseline Idle → Everyday → Low as 0 → mid → 1). Temperature only pushes off that (over target, hotter than the test load, or idle enough to walk quieter). Do not invent a third fused “H” that is neither watts nor temp. This matches today’s `PolicySession` intent (power-lead, then temp correction) — today those are buckets (Cpu / Gpu / Both) walking between two duties.

### Per-fan mix

Do not jump to a fancy 2D surface. Start with measured influence:

```text
Fan A demand =
    CPUDemand × CPUInfluence
  + GPUDemand × GPUInfluence
```

Unknown influence contributes **nothing**. Never renormalize unknown → known. Never fill a GPU coefficient because the mixer wants one.

```text
Fan B:
CPU influence = measured
GPU influence = Unknown
→ GPUDemand does not move Fan B
```

### Quiet–Cool still picks the family

The slider chooses the quieter or cooler family of anchors, not a single flat setting. Desktop / gaming / render / mixed are different places on those curves — not a second experiment tour.

### Named anchors, not a dense line

Generate a **small number of explainable anchors** (3–5) and interpolate piecewise linearly until there is evidence for more.

| Label | Existing meaning |
| --- | --- |
| Quiet floor | Quiet-end duty (slider family) |
| Diminishing returns begin | 1 °C plateau / useful-RPM cap |
| Cool end | Cool-end duty, not past that plateau |

Do not add a new experiment called “first meaningful gain.” Map labels to things Core already knows.

Architecture:

```text
                    MEASURE
                       │
             ┌─────────┴─────────┐
             ▼                   ▼
       Fan response          CPU/GPU influence
       RPM → °C              measured only
             │                   │
             └─────────┬─────────┘
                       ▼
                 POLICY MODEL
                       │
             CPU watts + GPU watts
                       │
             ┌─────────┴─────────┐
             ▼                   ▼
        Quiet family          Cool family
             │                   │
             └─────────┬─────────┘
                       ▼
                 named anchors
                       │
                       ▼
                 policy curve
                       │
                       ▼
                    duty
                       │
                       ▼
                  actual RPM
```

---

## 6. GPU hard rule

A generated GPU-aware policy is **invalid** until the model can reproduce the measured GPU response **directionally**.

If the model predicts roughly 20 °C GPU while the confirmed run is ~40 °C, AUTO Fan must not merely lower confidence and proceed. That is evidence something upstream is wrong: influence coefficient, baseline/start temperature, heat-demand mapping, double-counted pairs, duty scaling, or confirmation workload correspondence.

Until a subsequent confirmation is reasonably close:

```text
GPU influence: measured
GPU policy:    untrusted
GPU generated curve: blocked
```

Invariant:

> **A confirmation miss can rescue the current session, but it can never upgrade an unvalidated GPU model to “trusted.”**

---

## 7. Immediate investigation (Slice 1 done)

The problem is **not** the curve UI, planner, heater, or further optimization. It is that the current model can produce a GPU prediction that is physically inconsistent with the observed confirmation.

Confirmation does **not** start from idle. It reads GPU at the moment Hold starts confirming, then:

```text
expected GPU = confirmation-start temperature − Predict(all policy fans)
```

`Predict` is: each fan’s stored GPU Δ°C, **added together**, plus pair leftovers. There is **no** power/load correction. Influence Δ°C is reference minus faster-fan temperature (positive = cooling), taken from the **largest** test step, **not** scaled to the Quiet–Cool duty actually held. Pair leftover is **together − (first + second)**.

### This PC’s failed run (stored confirmation)

```text
GPU prediction
--------------
Confirmation-start GPU:    41.7°C
Watch idle GPU (compare):  45.9°C
Predicted contribution CPU Fan: -5.3°C
Predicted contribution System Fan #1: -5.4°C
Predicted contribution GPU Fan 1: -7.3°C
Pair leftover CPU Fan + System Fan #1: -0.9°C
Pair leftover CPU Fan + GPU Fan 1: -2.4°C
Pair leftover System Fan #1 + GPU Fan 1: -1.0°C
Pair leftovers total:      -4.3°C
Other corrections:         none
--------------------------------
Predicted:                 19.6°C
Actual confirmation:       39.8°C
Error:                     +20.3°C
```

Start GPU is reconstructed as expected + Predict (41.7 = 19.6 + 22.2 °C of stacked cooling). It was not stored on the confirmation row.

### What the five checks showed

1. **Start semantics** — Confirmation used the **live start** (~41.7 °C), not Watch idle (~45.9 °C). The start base is not the bug. Subtracting ~22 °C from 41.7 still lands below idle.
2. **Sign and units** — Individual GPU influence is +Δ°C cooling (correct direction). Pair-test first/second deltas on this run were slightly **negative** (tiny warming), then a **positive** leftover was added as extra cooling on top of the individual-map positives.
3. **Pair leftovers** — Three leftovers (−0.9 / −2.4 / −1.0 as displayed cooling) were stacked on three full singles (−5.3 / −5.4 / −7.3). That is the bulk of the 22 °C drop.
4. **Duty scaling** — Full test-step GPU Δ was applied. CPU Fan 60→85%. GPU Fan 1 30→85% (1130→3360 RPM). **System Fan #1 50→52% with RPM 982→915 (down)** still contributed −5.4 °C GPU. That hub number is not a believable GPU effect for a 2% duty bump.
5. **Heat-load correspondence** — Fan-test GPU was ~53 °C; confirmation settled ~40 °C. The same large cooling was subtracted at a cooler load.

Other corrections: **none** (no power/load term in `Predict`).

Do not treat this miss as a trusted GPU model. Next code is fix/validate `Predict()`, then re-confirm. Not graphs.

---

## 8. Build order

```text
1. Diagnose GPU confirmation prediction
2. Fix/validate Predict()
3. Get confirmation within a few °C
4. Only then build the measured RPM → temperature graph
5. Then replace two-end Hold with named anchors / policy graph
```

No planner. No extra heater. No generated curve until the data justifies it. Do not decorate empty or untrusted GPU points.

The measured graph does not call `Predict()`, so delaying it is a process choice: one scientific thread, UI cannot get ahead of the model.

---

## 9. How this changes the app

- **You can see the proof, not just a writeup.** RPM → temperature with the 1 °C plateau marked. “Faster than 1200 RPM barely helped” is visible.
- **Hold stops being a two-speed dimmer.** Mid-load becomes named anchors, not “halfway between Quiet and Cool.”
- **Fans react to the right heat.** GPU-heavy vs CPU-heavy raise different fans, without a second Optimize.
- **Power moves fans before the spike; temperature only corrects.** AUTO Fan does not become another “CPU is 70 °C → 65%” BIOS curve.
- **Broken GPU math cannot quietly become policy.** Extra 5% airflow can still catch a miss; it cannot certify a bad model.
- **Unknown stays empty.** Missing GPU influence stays missing.
- **Two screens, two truths.** Measured = what happened when a fan changed. Policy = what AUTO will command. Not FanControl with an auto-drawn line.
- **You still only pick Quiet–Cool.** No dragging points.

---

## 10. Source of truth

| File | Job |
| --- | --- |
| [App.md](App.md) | What the product is **now**. Implement from here for current slices. |
| [IMPLEMENTATION.md](IMPLEMENTATION.md) | What we are building **now**. Next code slice is the `Predict` fix from §7. |
| This file | Eventual Core control/policy model. Parked until the sequence in §8. |
| [Auto Optimize Mental Model.md](Auto Optimize Mental Model.md) | Earlier research dump. Do not implement GP / curve-synthesis chapters from it. |
| [Stress Test Mental Model.md](Stress Test Mental Model.md) | Heater: calibrate, then freeze. Still in force. |

Do not implement later sections of this file because they are written here. Do not loosen abort ceilings.

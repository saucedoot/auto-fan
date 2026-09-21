# Intelligent PC Cooling Optimizer

## The Problem

PC fan control is still largely based on manual trial and error.

A user can choose a temperature and build a fan curve, but that does not answer the harder questions:

* Which fans actually matter?
* Which fans should react to CPU temperature versus GPU temperature?
* Is increasing intake more effective than increasing exhaust?
* Are two fan groups working together, or fighting each other?
* Is a particular fan creating useful airflow, or simply creating more noise?
* What is the quietest configuration that still keeps temperatures under control?
* Does the optimal configuration change between gaming, rendering, and everyday use?

The difficulty is that every PC is different.

Case geometry, fan position, fan characteristics, GPU design, CPU cooler, radiator placement, airflow restrictions, dust filters, cable obstructions, fan orientation, ambient temperature, and hardware power consumption all interact.

The result is that finding an actually good fan configuration can require hours of experimentation.

## The Product

**The app automatically measures this PC's cooling system, then builds real multi-point fan curves from data.**

Instead of asking the user to manually draw fan curves by trial and error, AUTO Fan:

1. Locks in repeatable heat
2. Tests which fans actually affect which temperatures
3. Builds temperature → duty fan curves from those measurements
4. Shows the user **editable curves** and **what we learned** (the evidence)

> **"Like FanControl or similar curve editors, but the first draft is automatic — and you see the data behind it."**

The user installs the app, gets this PC ready, and starts one **Optimize**.

The first session is a **guided sequence**, not a toolbox. Each step should lead to the next: this PC is ready → which headers actually have fans → Optimize (a walk: watch this PC, then test fans, then hold). The user should not have to open Advanced or run pieces by hand.

The app performs controlled testing, learns the thermal behavior of the system, and presents **editable fan curves** on the Home screen. The curves are computed to be the quietest configuration that meets cooling requirements, but the user can adjust them if desired.

A **Quiet ↔ Cool** slider helps set the initial balance, but it becomes secondary once the curves are visible — users who want manual control can tweak the curves directly.

The user does not need to understand airflow theory or spend hours testing by hand. If they cancel mid-test, they still get whatever the software has already learned — a partial map and conservative curves — not a blank result.

## What Makes This Different

Traditional fan-curve editors (FanControl, Armoury Crate, iCUE, etc.) require the user to draw curves by hand, then test them manually.

AUTO Fan is a **measurement-driven curve builder**.

Traditional approach:

> User guesses temperature → duty curve → user manually tests → user iterates

AUTO Fan approach:

> Lock repeatable heat → Measure fan effects → Build temperature → duty curves from data → Show curves + evidence → User may adjust

The key difference is that the software does not assume what the fans should do.

It **measures what they actually do on this PC**, then builds curves from those measurements.

The result is visible on the **Home** screen:

* **Editable fan-curve graphs** (temperature → duty for each controllable fan group)
* **What we learned** — readable visualized evidence (which fans moved which temperatures, by how much, and where diminishing returns set in)

AUTO Fan is not validated until a real PC shows a repeatable BIOS → AUTO → BIOS result on the same locked heat, where AUTO is cooler at similar fan effort or about as warm at less fan effort, and returning to BIOS reproduces the first BIOS reading above this PC’s measured wander. That comparison is the acceptance bar. It is not built yet. The next product work after these docs is a **plan** for the smallest Home curve-seed slice (editable temperature → duty curves from existing settled measurements). Isolated GPU mapping is only as needed for curve-quality data — not the next product proof.

---

# How It Works

## What one Optimize run does

One **Optimize** action measures this PC, then builds temperature → duty fan curves from the data. It is not a new experiment tour for every activity (gaming, rendering, idle).

1. Discover hardware. Confirm this PC can talk to motherboard sensors, then find which writable headers actually have a fan (set each to 100%; a header is connected if it has RPM). NVIDIA GPU fans on one card are probed once — they move together. The unit of every later test is a **writable fan group that reports RPM** — a motherboard header or the NVIDIA fans on one card, not each physical fan on a hub, not each GPU fan, and not pumps or AMD/Intel GPU fans. Empty headers stay out of the way. NVIDIA GPU fans are written through NVAPI and restored to the driver curve. AMD and Intel GPU fans are not written.
2. **Optimize** opens a walk window. The user starts **Watch this PC** (baseline, no fan writes), then **Test fans** (screen and refine, then pairs only if those individual tests finished), then **Hold**. Each step asks before the next test starts. An aborted Watch, empty fan tests, a lost temperature sensor, or a GPU reset stay on that step for retry — they are not Continue.
3. Observe a baseline without changing fans: idle, a lighter everyday heat, then a stronger heat used later for fan tests. Watch may raise GPU work per frame (still ~60 Hz, not more frames per second) until GPU Core is about **15 °C above idle**, then **freezes** that workload. Stronger heat keeps the everyday CPU load. After the load is frozen, still on BIOS/driver fans, settle and measure that same condition **three times**. Label each of CPU and GPU Core **STABLE / DRIFTING / NOISY / UNAVAILABLE**. Do not stop the whole Optimize because one sensor is bad. A temperature change smaller than that sensor’s minimum detectable effect is **no effect**. Do not characterize a target that is not STABLE. If GPU Core does not rise that far, Watch may still finish; GPU cooling stays **Unknown** and NVIDIA GPU fans are not written for mapping. Recorded settle times here are a baseline metric. They are not the wait used when fans are later moved. Do not add an all-core High pass, or extra CPU threads, that hit the abort on this class of PC. Repeating the **BIOS control** is required. Do not re-run every fan speed to copy the same perturb.
4. Wait until the relevant temperatures have **stopped moving**, with a timeout and hard abort ceilings. Do not use a fixed kitchen-timer dwell.
5. **Screen** each writable group at a few coarse speeds. Skip stalled fans (commanded duty but 0 RPM or no tach match), empty 0 RPM headers, pumps, and AMD/Intel GPU fans. Skip NVIDIA GPU fans for mapping when Watch did not raise GPU Core enough. Drop stalled points; do not fit a curve through them.
6. **Refine** only the groups that actually moved a temperature: a few more speeds in the useful range. Stop with the 1 °C plateau rule in section 8. CPU-target measurements still count even when GPU cooling is Unknown.
7. Test a few **pairs** among groups that affected the same sensor, including slight effects — not only the strongest fans, and not the first headers the motherboard listed. Pairs run only if screen and refine **finished**. A thermal abort, cancel, or the 30-minute cap during individual tests with a partial map skips pairs; pair knowledge stays Unknown; the user may Continue to Hold. Do not start pairs after a thermal abort in refine.
8. Fit a simple model of this machine (individual effects plus measured pair leftovers). Build **temperature → duty fan curves** from the measured data — one curve per fan group, showing how duty should respond to CPU or GPU temperature. The app computes the **quietest curves** that meet cooling requirements (using the Quiet–Cool slider and any optional temperature targets as guidance). Noise is **RPM** unless a real sound meter exists.
9. Show the user **editable fan curves** and **what we learned** on the Home screen. The user may adjust curves manually if desired. The Quiet–Cool slider can regenerate curves at different priorities, but manual editing takes precedence.
10. **Confirm once:** apply the generated curves, wait until temperatures settle, and compare to the prediction. If it misses, add a little airflow and say the check failed. A miss does not make GPU curves trusted. A predicted GPU temperature below idle, or a plunge below an already-cool start, is not a cooling measurement. Do not search every three-fan combination. Confirmation still runs if Hold applies a setting after skipped pairs.
11. If the user cancels, or fan tests stop after some groups were measured, keep those results and build conservative quieter curves from them. Pair tests that abort before a combined reading do not add a pair map and are not retried; Hold and confirmation may still use the individual map. Confirmation only happens if Hold actually applies a setting.
12. Afterward, if the PC runs hotter than the test load, the live control may move along the already-generated curves toward higher duty. The curves are not regenerated louder just because the tests were at a low heat.

Abort if any monitored temperature hits a hard ceiling, a needed temperature reading disappears, the GPU resets, or temperature rises unexpectedly fast **near that ceiling** (within 5 °C). Never loosen those ceilings (CPU 90 °C / GPU 83 °C / other 95 °C). Optional CPU/GPU “stop test at” fields may only stop sooner. A modern CPU can jump several degrees in one second in the middle of the range; that is not the same as a runaway into 90 °C. Use the highest **stable** experiment heat: everyday CPU workers plus the frozen GPU work from Watch. Do not run a High pass or a heavier CPU thread count that we already know will abort. Do not change the heater during fan tests to hold a temperature.

The user never has to see settle math, experiment design, or optimizer names.

---

## 1. Build an Initial Model of the PC

The application first discovers the available hardware and sensors.

It identifies, where supported:

* CPU
* GPU
* motherboard sensors
* CPU/GPU power
* fan controllers
* fan RPM
* pump speed
* relevant thermal sensors
* controllable fan groups

The user can also identify their:

* case
* fan positions
* fan sizes
* fan orientation
* CPU cooler or radiator
* GPU
* radiator locations
* intake and exhaust configuration

A case database can provide known information about supported fan locations and approximate airflow geometry.

This information is useful, but it is treated as **prior knowledge**, not ground truth.

The real PC gets the final say.

---

# 2. Establish a Baseline

Before changing anything, the application observes the system under controlled workloads: idle, a lighter everyday heat (closer to normal use), and a stronger heat so later fan tests can see a clear change. GPU heat is a hidden Direct3D 12 3D burn, not a game and not a compute buffer. Everyday is 720p and lighter. Stronger heat stays at about 60 frames per second and adds more shader work per frame until GPU Core has risen about 15 °C from idle (20 °C is plenty; do not keep adding work toward the abort). CPU Low uses the same worker count as everyday — extra CPU threads hit the abort the same way an all-core High pass does. Skip High. Fans stay on BIOS and the GPU driver during this Watch. That locked workload is what fan tests reuse. If GPU Core still cannot rise about 15 °C under the permitted heater, Watch continues anyway; GPU-fan mapping stays honest rather than inventing heat. After that load is frozen, Watch still does not write fans. It waits until temperatures stop moving and records the same BIOS condition three times, then labels CPU and GPU Core STABLE, DRIFTING, NOISY, or UNAVAILABLE. One noisy sensor does not abort Watch.

It records:

* temperatures
* fan RPM
* fan duty cycle
* CPU/GPU power
* workload level
* clock behavior
* temperature rise
* temperature decay
* thermal stabilization time

The goal is to understand the machine in its existing state.

Ambient temperature should also be accounted for so that results can be normalized when possible.

Stabilization time recorded here describes how the machine behaved during this observe-only run. Later fan tests wait until the temperature has actually stopped moving. They do not reuse this number as a fixed wait.

---

# 3. Intelligently Test Individual Fans

The application then performs controlled experiments on **writable fan groups that report RPM** (motherboard headers and NVIDIA GPU fans on one card).

A hub that puts several physical fans on one header is one group. NVIDIA fans on the same card also move together — test one representative, not each GPU fan. Pumps and AMD/Intel GPU fans are not written. NVIDIA GPU fans go through NVAPI, not LibreHardwareMonitor `SetSoftware`. A group that is commanded but does not spin, or that already reads 0 RPM, is skipped or excluded — not stored as “this fan does nothing useful.” GPU-target influence is **Unknown** unless Watch raised GPU Core about 15 °C above idle — not “none,” not “weak.” A ~2 °C GPU rise is not enough to map GPU fans; do not write NVIDIA GPU fans for that mapping until the rise is enough. A temperature change smaller than that sensor’s measured minimum detectable effect is no effect, not a weak fan. CPU-target influence stays Unknown when Watch labeled CPU anything but STABLE; same for GPU Core. Motherboard groups can still be screened for a STABLE target.

Testing is two passes, not one bump on every header:

**Screen.** Each candidate group is tried at a few coarse speeds. The app waits until the relevant temperatures have stopped moving (with a timeout and abort ceilings), then records what changed.

**Refine.** Only groups that actually moved a temperature get extra speeds in the interesting range. That refinement curve is what diminishing returns (section 8) needs. A single +25% bump is not a curve.

For example:

> Increase front intake by 100 RPM.

It observes what changes.

Perhaps:

* GPU temperature decreases
* CPU temperature barely changes
* case temperature decreases
* VRM temperature decreases

Then it returns to the previous state and tests another fan or group.

This allows the application to learn a **fan influence map** for the actual computer.

For example:

| Fan Group     |    CPU |       GPU |    VRM | Case Temperature |
| ------------- | -----: | --------: | -----: | ---------------: |
| Front intake  |   High |      High | Medium |             High |
| Bottom intake |    Low | Very High |    Low |           Medium |
| Rear exhaust  |   High |    Medium |   High |           Medium |
| Top exhaust   | Medium |       Low |    Low |           Medium |

These aren't hard-coded assumptions.

They are experimentally measured relationships.

---

# 4. Discover Fan Interactions

Testing fans individually is not enough.

A fan can have little effect alone but become extremely valuable when combined with another fan.

The application therefore performs selective combination experiments — **only after individual screen and refine finished**. It chooses pairs from groups that already moved the **same** temperature, **including slight effects**, not from motherboard discovery order and not only from “strong” fans. A thermal abort or cancel during individual tests with a partial map skips pairs. Pair knowledge is Unknown. The user may still Hold quieter conservative curves from the finished groups.

For example:

> Front intake alone → GPU -1.5°C
> Top exhaust alone → GPU -0.7°C
> Front + top → GPU -3.8°C

Top exhaust alone is only a slight effect. That pair must still be eligible. If “smart” meant “strong fans only,” this example would never be tested.

The system has now learned that these fans have an interaction effect.

This allows it to discover actual airflow paths through the case.

It can determine whether a particular configuration:

* improves airflow through the GPU
* improves CPU exhaust
* creates recirculation
* causes competing airflow paths
* provides useful pressure/flow behavior
* provides diminishing returns

Pairwise tests will miss some three-fan effects and some weak+weak pairs. That is accepted. The catch is **one confirmation after Optimize picks a setting**: apply it, wait until temperatures settle, and compare to the prediction. If the result is clearly worse than predicted, add a little airflow and say so. Do not brute-force every combination, and do not search three-fan sets up front.

The app does not need to claim that it has directly measured air pressure or airflow velocity unless appropriate sensors are available.

Instead, it can report **measured effects and inferred airflow characteristics** separately.

---

# 5. Learn the Thermal Model

Over time, the app builds a model of the computer.

It learns relationships such as:

> Increasing bottom intake strongly affects GPU temperature.

> Increasing rear exhaust strongly affects CPU temperature.

> Increasing top-front exhaust provides little benefit at moderate GPU load.

> Increasing front intake beyond 850 RPM produces very little additional cooling.

This is essentially a **digital thermal profile of the individual PC**.

The model is the measured individual effects plus any measured pair leftovers. That is enough. It is not a generic recommendation for a case model, and it does not pretend to have mapped every possible combination.

If the experiments only ran at a low heat, the model must not claim high confidence for a much hotter workload. A low-heat map can still say which fans matter. It cannot treat those deltas as guaranteed gaming-load truth.

---

# 6. Optimize for the User's Priorities

There is no universally perfect fan curve.

A configuration that produces the lowest possible temperature may be unnecessarily loud.

The application therefore optimizes multiple objectives simultaneously:

### Temperature

Keep important components below user-defined targets and within safe limits.

### Noise

Avoid unnecessary RPM. Unless a real sound-level meter is present, **RPM is the noise proxy**. That estimate is inferred, not measured loudness. The app does not listen through a microphone.

### Responsiveness

Prevent temperature spikes without making fans constantly ramp up and down.

### Stability

Avoid oscillation and excessive fan-speed changes.

The user can choose something as simple as:

**Quiet ←────────→ Cool**

or use more detailed controls (optional CPU and GPU temperature targets, and optional CPU/GPU stop-test temperatures that cannot exceed 90/83 °C).

The slider stays as a way to regenerate the first-draft curves at a different Quiet–Cool balance. Once the curves are on Home, manual editing takes precedence. A target ceiling is an extra constraint, not a replacement for the slider.

The optimizer then searches for the quietest temperature → duty curves within those constraints.

If the PC later runs hotter than the heat used in the tests, live control may move along the **already-generated curves** toward higher duty. It does not quietly pick a louder default just because the model’s confidence label is low.

---

# 7. Same curves, different heat

The user-facing deliverable is the **editable temperature → duty curves** on Home — not a hidden policy the user never sees.

Different workloads produce different **heat** (CPU power, GPU power). They do not change what a given fan physically does to this case, and they do not replace those curves with a second experiment tour.

The software characterizes the machine **once** — ideally at two stable heat levels (everyday, then the locked Low with the same CPU workers and heavier GPU). The heavier GPU pass only counts if GPU Core actually rises enough for fan tests to see a change (about 15 °C from idle). Do not add an all-core High synthetic pass or extra CPU threads that hit the abort ceiling. Fan tests use that same frozen Low.

Live control may move along those same curves as heat changes:

**Desktop**

Stay toward the quiet end of the curves.

**Gaming**

Prioritize GPU- and case-linked groups along the same curves.

**CPU rendering**

Prioritize CPU-linked groups along the same curves.

**Mixed workloads**

Balance CPU- and GPU-linked groups along the same curves.

**Sustained workloads**

Stay toward steady-state cooling on the same curves.

**Short bursts**

Avoid unnecessary fan ramping along the same curves.

There is no second experiment tour per activity. Workload detection can be as simple as CPU package power plus GPU power.

This allows the system to become predictive rather than purely reactive.

Instead of waiting for temperature to rise, it can learn:

> "This workload causes a rapid increase in GPU power, so increasing airflow slightly now prevents a much larger temperature increase later."

---

# 8. Automatically Find Diminishing Returns

One of the most valuable things the optimizer can discover is where additional fan speed stops being worthwhile.

That needs the refinement curve from section 3 — several measured speeds on groups that mattered — not a single bump.

For example:

| Front Fan Speed | CPU Temp |
| --------------: | -------: |
|         600 RPM |   74.2°C |
|         700 RPM |   71.8°C |
|         800 RPM |   70.5°C |
|         900 RPM |   69.9°C |
|        1000 RPM |   69.5°C |
|        1200 RPM |   69.2°C |

The software treats a speed as already on the plateau when it is within **1 °C of the coolest measured point**, and recommends the **quietest** of those points. In this table that is about 900–1000 RPM. After tests, the app shows those measured RPM versus settled temperature points, with axis titles, a labeled recommended-speed line, and the 1 °C plateau marked. That is not a BIOS temperature-to-duty curve.

Instead of running at 1200 RPM, it can save noise with almost no thermal penalty.

This is the core optimization philosophy:

> **Find the minimum amount of airflow required to achieve the desired thermal result.**

---

# 9. Use Case Information to Make Testing Faster

Case information matters, but not because the application should blindly trust manufacturer specifications.

It matters because it helps reduce the search space.

Suppose a case has:

* 3 front intake positions
* 2 bottom intake positions
* 3 top exhaust positions
* 1 rear exhaust position

The system can use that geometry to hypothesize likely airflow paths and **order** the screening pass.

It can start with:

> Front → GPU

> Bottom → GPU

> Rear → CPU

> Top → CPU

rather than blindly testing every possible combination.

Geometry must not skip a confirmatory data point. A “known” front-to-GPU path still gets at least one measurement on this unit.

If a later machine has many independently writable groups, the first move is to **cluster by zone** and screen the zones — not to test every header against every other header.

As measurements accumulate, those assumptions are updated.

In other words:

**Case data provides the initial hypothesis.**

**Testing provides the evidence.**

**The learned model becomes the final understanding of the system.**

---

# 10. Show what we learned with the curves

The Home deliverable is the editable curves. **What we learned** is the evidence behind them — not a substitute for the curves, and not a hidden second product.

For example:

## Cooling Optimization Complete

**Primary GPU cooling path**

Front intake + bottom intake

**Primary CPU cooling path**

Rear exhaust + rear section of top exhaust

**Most effective fan**

Bottom intake

**Largest source of diminishing returns**

Top exhaust above 900 RPM

**Detected interaction**

Bottom intake becomes substantially more effective when rear exhaust increases.

**Recommended operating point**

* Front intake: 780 RPM
* Bottom intake: 720 RPM
* Rear exhaust: 840 RPM
* Top exhaust: 650 RPM

**Expected result**

* GPU: approximately 2–3°C cooler than baseline
* CPU: approximately 1°C cooler
* Significant reduction in unnecessary high-RPM operation

**How steady this PC was**

CPU and GPU Core during the three BIOS holds: steady, wandered, noisy, or missing — and how large a change must be before it counts as a fan effect.

**Confirmation**

The recommended setting was applied and temperatures settled near the prediction — or they did not, and airflow was raised. Extra airflow does not make an unvalidated GPU model trusted.

The application should clearly distinguish between:

**Measured**

What the system directly observed (including the confirmation temperatures).

**Modeled**

What the thermal model predicts.

**Inferred**

What the software believes is happening based on measured behavior. RPM-as-noise is inferred. Airflow paths are inferred. A gaming result guessed from low-heat tests is modeled or unknown — not measured gaming truth. GPU cooling guessed without a ~15 °C GPU Core rise is Unknown, not Measured. A target Watch labeled anything but STABLE is Unknown, not a fan curve. Skipped pair tests are Unknown, not “no leftover.”

That distinction is important for user trust.

---

# The Long-Term Vision

The app gradually becomes a **personal cooling model** for the computer.

The more the computer is used, the better the model can become.

It can learn that:

* a specific game creates a particular thermal pattern
* a specific fan has a useful operating range
* certain fan combinations work well together
* particular RPM ranges have diminishing returns
* the case behaves differently at different power levels
* seasonal ambient temperature changes affect the optimal strategy

The system periodically **confirms** the current curves and watches for drift (dust, ambient, a new hot game). It does not automatically run a full re-characterization unless that confirmation fails badly. The user is not forced to rebuild fan curves by hand.

---

# What the User Ultimately Gets

The user experience should be extremely simple. Home is the path. Advanced is optional retry, not the first session.

They should not have to:

* Manually test each fan at different speeds
* Draw curves by hand and iterate
* Understand hysteresis, PID controllers, or thermal time constants
* Guess which fans matter for which components
* Spend hours tweaking and re-testing

They should be able to:

1. Press **Optimize**
2. Let the app measure their PC
3. Review **editable fan curves** and **what we learned** on the Home screen
4. Adjust curves only if they want to

A walk window asks them to watch this PC, then test fans, then hold. Each step waits for them. If Watch hits a temperature stop, or fan tests finish with nothing measured, or a needed temperature disappears, or the GPU resets, that step is retry — not the next page. If they stop it early after some fans were measured, or individual tests hit a temperature limit with a partial map, pair tests are skipped and they still get usable quieter/safer conservative curves from whatever finished. Confirmation still runs if Hold applies that setting. A finished Watch plus a partial fan map is not the same as confirmed curves.

At the end, the Home screen shows:

* **Editable fan-curve graphs** (temperature → duty) for each controllable fan group
* **What we learned** — which fans moved which temperatures, measured RPM → settled temperature data, and where diminishing returns set in
* A **Quiet ↔ Cool** slider that can regenerate curves at different priorities (secondary to manual curve editing)

The curves are the quietest configuration that meets cooling requirements, but the user may tweak them. Motherboard fans return to BIOS and NVIDIA fans return to the driver curve on Stop, close, abort, or crash.

---

# What This Product Really Is

The product is a **fan-curve utility that does the measurement work for you**.

It is:

> **An automated thermal testing system that builds real multi-point fan curves from data.**

The core value is the ability to:

1. Observe a PC under repeatable heat.
2. Safely test individual fans and combinations.
3. Measure the thermal response to each fan speed.
4. Learn which fans matter for which temperatures.
5. Discover interactions and diminishing returns.
6. Build temperature → duty curves from those measurements.
7. Show the user **editable curves** and **what we learned** (the evidence).
8. Continuously adapt when conditions change — by confirming and correcting, not by re-testing everything.

The difference from traditional curve editors is that AUTO Fan **measures first, then generates curves**, rather than asking the user to draw and test manually.

## The Core Promise

**Install it. Optimize. Get solid fan curves from measured data. Tweak them only if you want.**

The user's job is deciding how much they want to adjust the generated curves.

The software's job is doing the measurement and building the first draft.

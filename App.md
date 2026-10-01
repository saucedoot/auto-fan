# Intelligent PC Cooling Optimizer

## The Problem

PC fan control is still largely manual. A person can draw a temperature-to-duty curve, then guess whether it is quiet enough and cool enough. Every case, cooler, and graphics card is different, and more fan speed is not always better cooling. Fans can also work against each other.

## The Product

**AUTO Fan measures this PC, then builds temperature-to-duty fan curves from settings that were actually run together.**

One **Optimize** is the whole job. The person does not draw the first draft, label the case, or run separate tests for gaming and the desktop. Heat changes the temperature. The same curves change the duty.

The first draft is the lowest fan effort the measurements support while temperatures stay acceptable. Effort uses every fan RPM the PC reports, including fans left on BIOS or the driver. It is a stand-in for noise, not a sound reading. "Likely quieter" means no observed fan is meaningfully faster and at least one is slower. If one fan speeds up while another slows, that is a lower effort score, not a quieter room. A hub with one tachometer cannot prove how several fans sound. The curves and the measurements are on the Home screen. The person can edit a curve. An edit needs a short check before it is trusted again.

If Optimize cannot prove a profile, the fans return to the motherboard BIOS and the NVIDIA driver. The graphs can stay on screen as a draft. A draft does not keep control of the fans.

The full method is in [PRODUCT_DIRECTION.md](PRODUCT_DIRECTION.md). This file is the product. That file is how it is built.

## What Makes This Different

A manual editor asks the person to draw a curve and then test it by living with it.

AUTO Fan locks repeatable heat, measures fan groups alone only far enough to learn which ones matter, then applies candidate combinations and waits until the temperatures and the workload are valid. The curves are built from those joint results. Lines between measured points are a model. Temperatures that were never reached stay unknown, except for a visible safety ramp near the abort limit.

AUTO Fan is not validated until this PC shows BIOS, then these curves, then BIOS again on the same frozen heat. AUTO is cooler at similar fan effort, or about as warm at less effort, and the return to BIOS matches the first BIOS reading within this PC's measured wander. That comparison has not passed.

---

# How It Works

## What one Optimize run does

One yes starts the run. The screen says AUTO Fan will heat the PC and take supported fans, that the first run takes tens of minutes and can take longer if this case settles slowly, and that Stop puts motherboard fans back on BIOS and NVIDIA fans back on the driver. After that, the stages run on their own. Stop is always available. Cancel saves whatever evidence finished and restores the fans. It does not apply a partial setting.

1. Check that this PC can talk to its sensors, that competing fan programs are closed, and which writable headers actually have a fan. A header or hub is one group. NVIDIA fans on one card are one group. Pumps and AMD or Intel GPU fans are not written. A missing GPU does not block a CPU-only result; those GPU fans stay on the driver.
2. At idle, learn each group's duty-to-RPM behavior, its maximum RPM, and the lowest duty that keeps it spinning stably. This is not a temperature test. Do not treat 0% as a safe stop.
3. Freeze separate heat anchors: idle with no synthetic heat, a CPU load using the known-safe Everyday worker count and no GPU burn, a GPU load calibrated until GPU Core rises about 15 °C from idle or the calibration stops about 8 °C under the GPU abort, and a mixed load of those two. A hotter extension runs only if it opens a distinct band without crowding the abort limits. Do not add an all-core CPU burn.
4. Screen each fan that might matter with reference, step, reference: hold the other fans still, change that one duty, then put it back and require the temperatures to return. A temperature change that does not come back is not a fan effect. A five-second flat reading is not a finished settle. A timeout, a power change, or a clock drop is not a fan effect. About 15 °C of GPU rise is the aim while finding a useful GPU workload. It does not by itself prove a fan moved the temperature.
5. Search a small set of combined duty settings in software. Apply the likely winners as a whole, and hold them until temperature slope, power, clocks, and RPM say the hold is real. If the combination misses the prediction by more than this PC's wander, test the pair that could explain it. Do not search every combination.
6. Build one temperature-to-duty request per group that earned it. CPU-linked fans follow CPU temperature. GPU-linked fans follow GPU temperature. A fan that moves both uses the higher request. Two separated validated points are required. Otherwise that fan stays on BIOS or the driver.
7. Show the graphs and the evidence on Home, marked Draft until the proof passes.
8. On each anchor the curves claim to cover, settle on BIOS, then on the curves, then on BIOS again. The workload stays frozen. The second BIOS reading has to match the first. Winners stay in control. Losers restore the fans and leave a draft.
9. Save a passing profile on this PC. The next launch may apply it only when the hardware still matches and the required sensors are healthy. A later check is short and only when the person agrees. Synthetic heat never starts by itself.

Abort if a monitored temperature hits a hard ceiling, a needed temperature disappears, the GPU resets, a pump that had RPM goes silent during heat, or temperature rises too fast near the ceiling. Ceilings stay CPU 90 °C, GPU 83 °C, and other sensors 95 °C. Optional stop-test fields can only stop sooner.

The person does not have to see the search math.

## 1. Discover this PC

The app identifies the CPU, GPU, motherboard, power, clocks, load, temperatures, fan controllers, RPM, and pumps where the hardware reports them.

The person does not have to name the case. A generic layout may only change the order of the first screen. It cannot skip a measurement or pick a speed.

Connected-fan discovery sets each writable motherboard header to 100%, and NVIDIA fans once per card, and keeps a header that shows RPM. The later actuation pass, still without waiting for the case to heat-soak, maps duty to RPM and the lowest stable running duty.

## 2. Establish repeatable heat

Before the combination search, the app observes BIOS and the NVIDIA driver under the anchors above. It records temperatures, RPM, duty, power, clocks, load, and ambient temperature when a sensor exists.

The CPU anchor uses the same safe worker count as today's Everyday load, with the GPU burn off. The GPU anchor is a hidden Direct3D 12 raster burn at about 60 frames per second, with more work per frame until GPU Core has risen about 15 °C from idle, or the calibration stops about 8 °C under the GPU abort, then frozen. That rise is the aim of the calibration. A later fan effect still has to beat that anchor's own noise, slope, power, and return drift. Skip software GPUs and the integrated GPU. Do not change the heater during a hold to chase a temperature.

A sensor that wanders, drifts, or is missing is not used for a curve. One bad sensor does not throw away the other. The old three-snapshot wander number and a fixed 5 °C gap do not by themselves make a fan effect measured.

## 3. Learn which fans matter

Screening is reference, step, reference on a few informative speeds, not a dense grid at every heat. The other fans stay on the reference vector. The tested fan returns to that same vector, and the temperatures have to come back within the anchor's uncertainty. NVIDIA GPU fans are not written for mapping until the GPU workload has aimed for about 15 °C of Core rise. A stalled fan is dropped. Speeds quieter than BIOS are allowed.

The result is a ranking and a useful duty range. It is a model of the response, not a measured equilibrium, until a later joint hold passes the full settle test. A temperature change without a repeated reference is not a measured fan effect. A single fan's RPM-versus-temperature plot remains evidence of diminishing returns. It does not choose the combination.

## 4. Choose combinations

The app minimizes observed fan effort subject to the temperatures staying acceptable.

Effort for every group that reports RPM, including fans left on BIOS or the driver, is its RPM divided by that group's measured maximum RPM. Pump RPM is recorded and cancels out of the score when it does not change. The score prefers a lower loudest observed fan, then a lower total, then fewer duty changes. It is not decibels. "Likely quieter" requires that no observed group is meaningfully faster and at least one is slower.

Acceptable means: not hotter than BIOS on that same anchor beyond that anchor's uncertainty, unless the person chose a warmer target on purpose; optional CPU and GPU targets when set; no material worsening of another stable temperature; no clock drop that means throttling. Unknown sensors cannot create a pass.

The default draft is Balanced: no warmer than BIOS within wander, with less fan effort when the data support it. A quieter draft and a cooler draft wait until enough validated combinations exist to regenerate them. Editing beats the slider until the person asks for a new draft.

## 5. Build the curves

A curve is temperature across and duty up.

- Measured dots are joint settings that settled.
- Lines between them are a model.
- Duty does not decrease as temperature rises. If fixing that changes a measured point, the corrected setting is checked again.
- Below the coolest measured temperature, the fan does not go quieter than the validated idle or minimum-stable request.
- Above the hottest measured temperature, a safety extension reaches full duty no later than 5 °C under the effective abort limit. That segment is marked as a safety rule.
- A group without two separated points does not get a curve.

Desktop, gaming, and rendering are not separate experiments and not separate curves.

## 6. Run the curves

While a validated profile is active, the app reads the driving temperature and sets the duty. A shared fan takes the higher of its CPU and GPU requests. Temperature increases are allowed to raise duty promptly. Decreases wait through a small deadband taken from how much that sensor wandered, so the fan does not hunt.

A missing required temperature, a GPU reset, a competing fan program, a hardware mismatch, Stop, sleep, exit, or a crash restores BIOS and the NVIDIA driver. A missing reading is never treated as a cool PC. Power blips do not raise fans in this version.

## 7. Prove it, then reuse it

Validation is the same frozen anchor three times: BIOS, the curves, BIOS. It runs for the CPU anchor when CPU curves exist, and for the GPU or mixed anchor when GPU curves exist. An unsupported target stays on firmware and does not block a proved profile for the other target.

The second BIOS leg has to match the first. Otherwise the result is inconclusive. A fail or an inconclusive result restores the fans and keeps a draft. The word optimized is reserved for a profile that passed.

A passing profile is stored on this PC and tied to the motherboard, CPU, selected GPU, sensor ids, and fan groups that produced it. The next launch applies it only when that fingerprint still matches and the required sensors and RPMs are healthy. Start with Windows is allowed only after that, and only with crash restore already proved on this machine.

A later drift check is something the person starts. The app does not heat the PC in the background.

## 8. Show the evidence

Home shows one graph per controlled group, a line saying what AUTO Fan controls and what stays on BIOS or the driver, and the BIOS-versus-AUTO temperatures and RPMs from the proof.

Labels:

- **Measured** — a settled joint hold, or a proof temperature.
- **Modeled** — a line between points, a response rank, or a max-of-two-requests choice.
- **Inferred** — a lower RPM effort score, and any sentence about an airflow path. "Likely quieter" is used only when no observed fan got meaningfully faster and at least one got slower.
- **Unknown** — a skipped anchor, an unstable sensor, a GPU that never heated enough, a hold that never settled, a temperature never tested.

RPM-versus-temperature charts are per heat. They show where extra speed on one fan stopped helping. They are not the curve.

---

# What the user gets

1. Press **Optimize**.
2. Leave it, or press Stop.
3. Come back to curves and the evidence, or to a plain statement that the fans were returned to BIOS because the proof did not pass.
4. Adjust a curve only if they want to. That adjustment needs a check before the PC treats it as proved.

The curves are the quietest effort the measurements support for the cooling they proved. They are not a promise about temperatures the run never reached.

## The Core Promise

**Install it. Optimize. Get fan curves from measurements of this PC, including combinations that were run together. Tweak them only if you want. If the proof fails, the fans go back.**

The person's job is deciding whether to adjust the draft. The software's job is the measurement, the joint choice, and the first curves. This PC has not yet passed the BIOS → curves → BIOS proof.

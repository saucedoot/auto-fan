# AUTO Fan product direction

**Date:** 2026-09-21  
**Status:** Controlling direction. Historical experiment plans do not override this file.  
**This PC is not validated.** Validation is BIOS, then these curves, then BIOS again on the same frozen heat. That comparison has not passed.

## What a person can observe

After one guided Optimize, AUTO Fan either leaves this PC running validated temperature-to-duty curves that use a lower measured-RPM fan-effort proxy for equivalent cooling, or that cool better at comparable effort, with the evidence on Home — or it restores motherboard fans to BIOS and NVIDIA fans to the driver and says why it could not prove a profile.

"Quieter" is honest only in this sense:

- There is no calibrated sound meter. Acoustic loudness is Unknown.
- Raw RPM is not comparable between unlike fans. Blade shape, tone, restriction, and a hub that reports one tachometer for several fans are unmeasured.
- The score uses every observed fan RPM, including fans still on BIOS or the driver, and pump RPM as context. An unreported fan, such as a PSU fan, stays Unknown.
- "Likely quieter" is Inferred, and only when no observed group is meaningfully faster and at least one is slower. If one group rises and another falls, the result is a lower effort score, not quieter.
- Per-group RPM is Measured. A person may edit a curve when a fan tone is annoying. That edit is `Edited — check required` until a short same-anchor check passes.

## The method

AUTO Fan is a hybrid measured optimizer. A small transparent model may rank candidate fan settings. Only a setting that was applied to the fans together, held until the temperatures and the workload were valid, and confirmed against BIOS may drive the PC.

1. Calibrate what each controllable fan group can physically do.
2. Learn which groups matter, at separate CPU, GPU, and mixed heat anchors.
3. Search a small set of joint duty vectors in Core.
4. Apply and rigorously settle only the likely winners.
5. Build monotone CPU-temperature and GPU-temperature request curves from those joint points.
6. A fan that affects both sensors uses the higher request.
7. Keep the profile only if BIOS → the curves → BIOS passes on every anchor those curves claim to cover.

This is not a Gaussian process, a reinforcement learner, a general planner, or model-predictive control. It is not a fork of a manual curve editor. [FanCtrl](https://github.com/lich426/FanCtrl) follows a graph a person drew. It does not measure this PC, and it is not imported.

A target-temperature PID loop and a full predictive controller were considered and parked. A PC's fans, sensors, and heat are delayed and cross-coupled. Those controllers do not produce an editable measured first draft, and a wrong model could command too little airflow. A simple response estimate may rank candidates. It never outranks a settled joint measurement.

## What the app does today

v1.20 is a measurement prototype. It can discover headers, freeze a GPU-weighted heat lamp, command duties including speeds quieter than BIOS, and store settled or timed-out holds. After that it still holds a two-speed Quiet–Cool blend. That blend is not the product. Home does not show temperature-to-duty curves. The product is not validated.

Known limits of that prototype, which later slices must not treat as finished product behavior:

- A hold can be called settled after five one-second samples spanning 1 °C. That is not thermal equilibrium.
- CPU and GPU power are recorded and are not required before a temperature change is called a fan effect.
- Everyday, Low, and Hot raise GPU work while the CPU worker count stays the same. That is not an independent CPU-load curve.
- Each fan is judged mostly alone, against BIOS on the other fans. The chosen speeds are not a measured combination.
- One RPM-versus-temperature chart can mix heats and then influence the held speed.
- Cancel after a test can apply that unvalidated two-speed setting.
- Confirmation can run with the heater already off.
- A missing CPU temperature during Hold can be treated as cool enough to step fans quieter.
- Saved identity is names and header ids. NVIDIA fan writes can match a card by display name. That is not enough to reapply a profile automatically.

The raw one-second snapshots from those runs stay useful for replay. The derived rows do not become curve evidence: a "settled" flag from five flat seconds, an influence delta, a mixed-heat plateau, a pair leftover, or a heater-off confirmation. They are not promoted into validated curve points.

## What stays

- Core stays free of hardware I/O. Hardware talks to sensors, fans, and synthetic heat. Storage is local SQLite and JSON. The app is WPF.
- Ceilings stay CPU 90 °C, GPU 83 °C, other sensors 95 °C. A user stop can only be sooner. Duty stays 0–100%.
- Rate-of-rise abort stays armed only near the ceiling.
- Every fan write goes through `SafeFanSession`. Stop, abort, close, dispose, crash, and the next launch restore motherboard fans to BIOS and NVIDIA fans to the driver.
- Pumps are not written. AMD and Intel GPU fans are not written.
- One motherboard header, including a hub, is one group. NVIDIA fans on one card are one group.
- A missing saved heat profile does not silently fall back to a default lamp.
- A stalled or 0 RPM point is not cooling evidence.
- About 15 °C of GPU Core rise remains the calibration aim before NVIDIA GPU fans are written for mapping. It is not, by itself, proof that a later temperature change was caused by a fan.
- Trust labels stay Measured, Modeled, Inferred, and Unknown.
- Case geometry may order a screen. It does not choose speeds.
- Data stays on this PC.

Older dense holds remain raw traces for replay. Their settled flags, influence rows, plateaus, pair leftovers, and confirmations are not curve knots.

## What is no longer the product

- The two-end Quiet–Cool hold as the thing Optimize leaves running.
- Live nudges from Desktop, Gaming, Render, or Mixed labels.
- Heater-off confirmation, and a timer that calls that confirmation.
- Applying a partial setting when the person presses Cancel.
- A required pair-test stage before any curve exists.
- A mixed-heat RPM chart choosing a duty.
- An independent per-fan "quietest point within 1 °C of the coolest isolated result" as the system optimum. That rule remains evidence of one fan's diminishing returns. It is not the optimizer.
- Calling the result optimized before BIOS → these curves → BIOS passes.

The 1 °C plateau answers "more speed on this one fan, at this one heat, stopped helping." It does not answer "this combination is the least fan effort that still cools." Example: if 30% holds 72 °C, 60% holds 69 °C, and 100% holds 68 °C, the plateau can pick 60% even when 30% already meets the accepted temperature. Picking that plateau separately for every fan can run all of them harder than the case needs. Those duties were also never measured together.

## One Optimize

### Preflight and one yes

Before heat or fan control:

- Administrator and PawnIO are present, or the screen says what is missing.
- Competing fan programs are closed.
- The preferred CPU temperature is available. A discrete GPU temperature is required only for GPU-linked curves. A missing or unsupported GPU stays on its driver and does not block a CPU-only profile.
- Power, clocks, load, duty, RPM, and other temperatures are inventoried.
- Writable groups have a tachometer.
- A pump is never written. If it reported RPM before the run and that RPM disappears during synthetic heat, the run aborts.
- Temperatures are cool enough to start.
- If power or load leaves the frozen workload's tolerance, the run pauses or aborts. That change is not credited to a fan.
- A topology fingerprint is stored before any later profile may be reused.

The person sees one consent: AUTO Fan will apply controlled heat and take supported fans. The first run takes tens of minutes and can take longer on a slow-settling PC. After preflight, the estimate is based on how many fan groups were found. It is not a fixed promise. Stop always restores BIOS and the NVIDIA driver.

After that yes, stages advance on their own. The walk pauses only for something the person must fix. Stop is always available.

### Fan actuation, at idle

This calibrates the motor. It is not a thermal map.

For each writable group, one at a time: record the reference duty and RPM, command 100% long enough to see RPM, then step downward without waiting for the case to reach thermal equilibrium. Store the duty-to-RPM response, maximum RPM, the lowest duty that keeps a stable RPM, tachometer repeatability, and, when the hardware shows it, a start duty that is separate from the running minimum. Do not treat 0% as a safe stop. Coupled fans stay one group. Restore after each group.

This extends connected-fan discovery. It does not turn that discovery into a temperature test.

### Heat anchors

Heat inputs are repeatable workloads, not activity names.

| Anchor | Work | Role |
| --- | --- | --- |
| Idle | No synthetic heat | A quiet joint vector may be checked here. There is no full thermal duty sweep. |
| CPU | The existing safe Everyday CPU worker count, GPU work off | CPU-cooler coverage. If it does not separate, the CPU curve stays short. |
| GPU | Calibrated raster work aimed at a useful GPU Core rise, with only the CPU work that raster needs | GPU and case coverage. |
| Mixed | The safe CPU anchor plus the frozen GPU anchor | Checks fans that serve both, and the max-of-requests rule. |
| Hotter extension | Optional extra GPU work | Only if it opens a distinct band without crowding the safety margin. A valid short curve does not require it. |

The first implementation uses the known-safe CPU worker count. An all-core High pass and the known aborting half-thread CPU recipe stay forbidden. A later hotter CPU calibration is its own reviewed slice, incremental, and stops about 8 °C under the abort ceilings.

Each anchor stores the exact heat profile, BIOS reference temperatures and RPMs, CPU and GPU power, clocks, load, other stable temperatures, the hold assessment, and ambient temperature when a sensor exists.

### When a hold counts

`TemperatureSettle` can today accept five samples spanning 1 °C. That is not steady state for a heatsink or a case.

A joint anchor is `SettledMeasured` only when that anchor's own evidence says the effect is larger than the uncertainty. The check uses window noise, temperature slope, drift between the two reference windows, power, load, clocks, and RPM. A fixed rise such as 15 °C, the old three-snapshot minimum detectable effect, and the old 5 °C Hot separation do not grant that authority. No four-second settle can qualify.

The exact window, slope, and power limits are chosen from stored traces of this PC, and from replay of those traces, before they ship. They are not user settings. A simulated PC is not enough to pick them.

Assessment labels:

- `SettledMeasured`
- `TransientModeled`
- `TimedOut`
- `PowerUnstable`
- `Throttled`
- `TelemetryLost`
- `FanStalled`
- `Aborted`

If power sensors are missing, the frozen workload command plus a BIOS return that repeats can support a lower-confidence result. That result is labeled. Missing power is not treated as stable power.

Professional cooler tests treat uncontrolled power as fatal to a comparison, and they wait until temperature has actually stopped rising before they average a window. AUTO Fan follows that standard. Sources: [GamersNexus](https://gamersnexus.net/guides/3561-cpu-cooler-testing-methodology-most-tests-are-flawed), [TechPowerUp](https://www.techpowerup.com/review/cpu-cooler-test-system-update-2023/).

### Screening, then a few real settles

Holding every duty at every heat until equilibrium would make the first run hours long. Dense grids are no longer the default protocol.

For every fan admitted to the joint search, the screen is reference, step, reference:

1. Hold a reference vector and record it.
2. Apply the test duty, record the transient, and restore that same vector.
3. Require the return to repeat the first reference within that anchor's uncertainty. If it does not, the step is not a fan effect.

Rank the response from the two reference windows and the step between them. Those ranks are Modeled. Try a few low, mid, and high duties from the actuation profile, only where an effect can change the candidate list. Do not run a dense grid at every heat. Loud-then-quiet without a repeated reference is not a screen.

Older Low, Everyday, and Hot samples may seed which duties are worth trying. They do not become the new screen, and they do not become curve points.

### Joint choice

For each anchor:

1. Build a small duty set per relevant group from the measured minimum stable duty, the reference, the useful knee, and a high duty.
2. Predict the combined temperature effect from the per-duty responses.
3. Enumerate and prune combinations in Core. Keep the ones that can meet the thermal constraints.
4. Rank them by observed fan effort across the whole machine, not only the groups AUTO Fan commands.
5. Apply the best candidate as one vector.
6. Hold it to the rigorous assessment.
7. If it misses, store the leftover and try the next candidate, or one targeted pair.
8. Stop after a small fixed budget. Return Draft rather than searching without end.

For every observed group `i`, effort is `RPM_i / measuredMaximumRPM_i`, clamped to 0–1. That includes groups AUTO Fan commands, groups left on BIOS or the driver, and a pump when it reports RPM. Do not subtract the minimum running RPM. A spinning fan still has a cost. Minimum stable RPM is a limit on what the controller may command, not the zero of the score. A pump that stays at the same RPM cancels out of the comparison. A fan with no RPM sensor stays Unknown and cannot be called quieter.

Rank valid candidates by the highest single observed effort, then total observed effort, then how much the commanded duties move. Do not call that score decibels. Fan power and pressure change sharply with speed, and sound depends on the fan and the installation; RPM is not loudness. Source: [U.S. DOE fan sourcebook](https://www1.eere.energy.gov/manufacturing/tech_assistance/pdfs/fan_sourcebook.pdf).

"Likely quieter" requires that no observed group is meaningfully faster and at least one is slower. A trade where one fan speeds up and another slows is a lower effort score, not a quieter result.

Thermal constraints:

- Abort ceilings stay.
- Optional CPU and GPU targets are extra upper limits.
- The default profile may not run hotter than BIOS on that same anchor beyond that anchor's measured uncertainty.
- A stable VRM, case, or motherboard temperature that gets materially worse also rejects the candidate.
- Clocks may not show a material performance loss.
- An unknown sensor cannot create a pass.

Quiet, Balanced, and Cool:

- Quiet is the lowest validated effort that still meets the targets and the other constraints.
- Balanced, the default, is no hotter than BIOS within wander, with lower effort when that exists.
- Cool is the coolest validated point before the combined fans stop helping.

The first validated product may ship Balanced only. Quiet and Cool appear after the data contain enough validated choices to regenerate them. A slider change that is only modeled is `Check required`.

Pairs run only when a joint candidate misses the additive prediction by more than the measured wander, and that pair could change the next choice. There is no up-front pair tour, no full factorial, and no three-fan search.

### Curves

Each controlled group gets requests, not a separate gaming or desktop profile.

- A CPU-linked group follows CPU temperature, from idle and CPU-anchor points.
- A GPU-linked group follows GPU temperature, from idle and GPU-anchor points.
- A group that moves both uses whichever request is higher at that moment. The mixed anchor is where that combination is checked.

Rules:

- Two separated validated points for a target, or that target gets no curve. The group stays on BIOS or the driver.
- The dots are jointly settled operating points. They are Measured.
- The lines between them are Modeled.
- Duty does not fall as temperature rises. If enforcing that changes a measured point, the corrected vector is checked before it is trusted.
- Below the coolest measured point, do not go quieter than the validated idle or minimum-stable request.
- Above the hottest measured point, a visible safety extension reaches 100% no later than 5 °C under the effective abort limit, or sooner if a user target requires it. That extension is a safety rule, not a measurement.
- NVIDIA stop and start behavior is measured or left to the driver. A 0 RPM point is not invented.

Profile states: `Draft`, `Validated`, `Edited — check required`, `Stale — check required`, `Invalid`.

### Live control

The controller looks up duty on the curve, mixes CPU and GPU requests by maximum, uses a faster response when temperature rises and a slower one when it falls, and sizes the downward deadband from measured wander. Safety checks use the raw temperature, not a smoothed one. Commands do not chatter. The controller respects minimum and start duty. After a meaningful command it reads RPM. One unexpected stall gets one spin-up attempt; then the profile is invalid and fans restore. A missing required temperature, a GPU reset, a competing program, a topology mismatch, Stop, sleep, exit, or a crash restores. The hard ceilings do not change.

Power is for judging experiments. The first live curve does not spin fans up because power blipped.

FanControl's graph, max mix, hysteresis, and response time are the controller behavior users already understand ([FanControl documentation](https://getfancontrol.com/docs/)). The automatic first draft is the part AUTO Fan adds.

### Proof

On each anchor the curves claim to cover, with the workload frozen:

1. BIOS and the NVIDIA driver. Settle and record.
2. The curves. Settle and record.
3. BIOS and the driver again, without changing the workload. Settle and record.

The second BIOS reading must match the first within temperature wander, RPM repeatability, and power or load tolerance. Otherwise the trial is Inconclusive, not a pass or a fail.

Balanced passes when AUTO is no hotter than BIOS beyond that anchor's uncertainty and the whole observed RPM picture is no worse, or AUTO is cooler by at least that uncertainty without a meaningful effort increase. "Likely quieter" uses the rule above. No monitored stable target gets materially worse. Clocks do not show throttling. Every proof leg records every observed RPM, power, and clock. Every anchor required by the groups AUTO Fan intends to control must pass. An unsupported target stays on BIOS or the driver and does not block a validated partial profile for another target.

Fail or Inconclusive: stop the heat, restore, save the profile as Draft with the failed leg visible, and do not apply it.

Stop or Cancel: stop the heat, restore, save the raw evidence that finished. Never apply a partial or unvalidated setting.

Until this passes on this PC, the product is not validated.

## What Home shows

- Not ready: the one thing missing.
- Ready: one Optimize action.
- Measuring: which fan or anchor, live temperatures, progress, Stop.
- Draft: the graphs and the evidence, with BIOS and the driver still in control.
- Validated: the graphs, a live marker, the last check, and Stop.

Each controlled group has one graph: temperature across, duty up. Measured joint points, modeled lines, and the safety extension look different. The screen says which sensor drives the fan, or that the fan takes the higher of two requests. It says which groups AUTO Fan controls and which stay on BIOS or the driver. Beside the graphs: BIOS versus AUTO at each proved anchor, per-group RPM, where extra effort stopped helping, and what stayed Unknown.

An edit keeps the curve monotone and cannot remove the safety endpoint. A duty below the measured stable or start behavior needs its own check. The profile is `Edited — check required` until that check passes.

## Persistence and the next launch

Schema changes use versioned SQLite migrations. Ad hoc column adds are not how curve data grows.

Every sample belongs to one optimization session, one hardware fingerprint, one exact workload, one anchor, one reference or candidate vector, one hold, one commanded duty set, and one hold assessment. The newest baseline, fan test, and pair run are not joined when they are not that same session.

Store the topology fingerprint, actuation profiles, heat anchors, hold assessments, raw run ids, response estimates, joint candidates and their settled results, both request curves and where each point came from, profile state, edits, and the three validation legs. Each validation leg stores commanded and observed duties, every observed RPM, power, clocks, ambient, and the source run ids.

The fingerprint includes motherboard and CPU identity, the selected GPU hardware id rather than only its name, the temperature and power sensor ids the profile uses, controllable group ids and controller paths, which fans move together, and the RPM response ranges. NVIDIA writes must use that selected GPU id. Today they can match a card by display name.

On launch or wake: restore anything left dirty, rediscover the hardware, and apply a profile only when it is Validated, the fingerprint matches, and the required sensors and RPMs are healthy. A mismatch leaves BIOS and the driver in control and offers a check or a remap. Synthetic heat never starts in the background.

A later passive check may offer a short user-approved comparison. It is not validation, and it is not a full remap.

## How the code is shaped

Do not keep growing `MainWindow.xaml.cs`. A Core session owns the steps, cancellation, and saved results. The window sends Start and Stop and shows state. Hardware and storage stay behind the existing boundaries.

A tests-only simulated PC, with lag, cross-coupling, noise, power drift, stalls, and sensor loss, must be able to run that session in virtual time. Replay fixtures from real recorded traces are also required before the slope and power limits ship. Unit fixtures on static snapshots are not enough to prove that one Optimize ends safe and useful. The simulation has to show a pass that restores, a fan-fighting case that does not get a false pass, a slow temperature creep that is not "settled" in five seconds, power drift that is not called cooling, a missing sensor that does not quiet the fans, and a failed or cancelled run that does not leave software control on.

## Ordered slices

No new P-phase. One slice at a time. Do not skip ahead. Slices 1–10 are in the working tree and are not tagged. The next slice is Slice 11 only.

1. **Contain the old hold.** Default Optimize does not finish by applying the two-speed policy. Stop and Cancel always stop heat and restore. RPM-versus-temperature evidence is split by heat and cannot choose a duty. Any leftover two-speed hold is labeled legacy under Advanced.
2. **Measurement truth.** A hold assessment replaces the five-sample settle. Power, load, clocks, and throttling can reject a hold. A timeout or unstable power cannot be Measured. Samples carry session, fingerprint, workload, anchor, vector, hold, commanded duty, and assessment. Replay fixtures from real traces are in this slice. The slope and power limits are not locked until those traces have been reviewed. Old derived influence, plateau, pair, and confirmation rows stay replay-only.
3. **Session shell and simulated PC.** Core session contracts, the tests-only plant, and the replay harness. Existing runners stay callable.
4. **Actuation and fingerprint.** Minimum stable duty, start duty, duty-to-RPM, repeatability, topology fingerprint, NVIDIA writes bound to the selected GPU, and abort if a pump that had RPM goes silent during heat.
5. **Independent anchors.** CPU, GPU, and mixed frozen workloads, using the known-safe CPU worker count and the existing GPU raster calibration. About 15 °C of GPU rise is the calibration aim, not the evidence rule. A hotter CPU pass is not in this slice.
6. **Reference, step, reference.** Every fan admitted to the joint search gets a repeated reference around each test duty. Ranks stay Modeled. No generated curves yet.
7. **Joint candidates.** Whole-machine observed RPM, thermal constraints, a small set of whole vectors, rigorous settles, and a pair only when a joint residual requires it. "Likely quieter" uses the no-group-faster rule.
8. **Curve records.** Temperature-to-duty requests, provenance, monotone synthesis, the safety extension, profile state, and versioned migrations. Draft only.
9. **Curve actuator.** Lookup, max mix, asymmetric hysteresis, ramp limits, minimum and start duty, RPM feedback, and restore on every loss of authority. No startup reuse yet.
10. **Home graphs.** Measured, modeled, safety, and unknown regions. Edits that mark the profile as needing a check.
11. **BIOS, curves, BIOS.** CPU anchor and the GPU or mixed anchor the curves claim. Only a pass becomes Validated and stays in control.
12. **Reuse.** Atomic load of a Validated matching profile, wake from sleep, a user-approved short drift check, and start-with-Windows only after a local crash and resume proof. Remove the two-speed hold, the activity classifier, and the old confirmation once nothing calls them.

## Out of these slices

- AMD or Intel GPU fan writes.
- A microphone.
- MPC, PID autotuning, Bayesian search, or reinforcement learning.
- Raising fans from a power blip.
- Per-game profiles.
- Cloud upload.
- Background heat or background remaps.
- Full pair or triple search.
- A manufacturer case catalog.
- Installer and driver-signing work.

Before a public release, choose a project license and ship third-party notices. Do not import GPL code unless the product explicitly chooses GPL.

## Still unmeasured on this PC

- The real settle traces and the window constants.
- Whether the safe CPU-only anchor separates enough for a CPU curve.
- Which fans have a start duty different from their minimum running duty.
- Whether the RPM effort score matches what the room sounds like.
- How many joint candidates a normal run needs.
- Whether GPU or mixed coverage is enough without a hotter extension.
- Whether fan ids survive a reboot or a driver update.
- NVIDIA restore and selected-GPU behavior on this driver.
- A killed process, sleep, and the fingerprint check.

Until those exist, say "not validated."

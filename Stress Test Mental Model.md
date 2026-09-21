Yes. That context changes the answer substantially, and the Cursor critique is directionally correct.

I was solving the wrong problem before: **a universal stress tester**. AUTO Fan needs a **thermal measurement instrument**: create a repeatable heat stimulus, manipulate one fan or fan group, and measure the resulting temperature response. The heater must therefore be **adaptive only during calibration, then immutable during the experiment**.

That distinction should drive the entire implementation.

## The correct product model

Think of AUTO Fan as:

```text
             CALIBRATION
                  │
        find useful heat level
                  │
                  ▼
        ┌───────────────────┐
        │  LOCKED HEATER    │
        │                   │
        │ CPU intensity     │
        │ GPU intensity     │
        │ GPU shader/work   │
        │ frame cadence     │
        │ worker count      │
        └─────────┬─────────┘
                  │
                  ▼
       FAN EXPERIMENTS ONLY
                  │
       fan changes ←→ temperature
                  │
                  ▼
             measured ΔT
```

The heater is **not allowed to react to temperature once calibration finishes**.

That is the central architectural rule.

---

# 1. The calibration/experiment split is the key

There are actually two different control problems.

### Calibration

It is acceptable to do:

```text
GPU work = 20%
↓
too cold
↓
GPU work = 30%
↓
too cold
↓
GPU work = 40%
↓
useful temperature rise
↓
LOCK
```

because there is no fan experiment happening yet.

### Experiment

Then:

```text
GPU work = 40%
GPU work = 40%
GPU work = 40%
GPU work = 40%
...
```

regardless of whether the GPU temperature goes:

```text
70 → 74 → 71 → 76 °C
```

because those changes are now supposed to come from the fan configuration.

That is exactly what you need for causal attribution.

---

# 2. I would keep the existing D3D12 implementation

I agree with Cursor here.

There is no compelling reason to rewrite the GPU heater from D3D12 to D3D11.

D3D12 already gives you explicit command submission, fences, and GPU timestamp queries. Microsoft documents timestamp queries specifically for measuring GPU execution time, and direct/compute queues support them. ([Microsoft Learn][1])

That is actually useful for AUTO Fan because you can record:

```text
requested workload
actual GPU execution time
frame cadence
```

as part of the experiment.

So the GPU subsystem should remain:

```text
Vortice.D3D12
      ↓
existing raster workload
      ↓
fixed work per frame
      ↓
fixed cadence
      ↓
GPU timestamps
```

not a new D3D11 backend.

---

# 3. I would change the meaning of “GPU intensity”

This is an important implementation detail.

Do **not** define:

```text
GPU intensity = reported GPU utilization
```

because that isn't under your direct control.

OCCT's current documentation makes essentially the same distinction: its “intensity” is a workload parameter and does not necessarily correspond directly to the utilization percentage reported by the driver. ([OCBASE][2])

For AUTO Fan, intensity should mean:

> **How much deterministic GPU work AUTO Fan submits per unit of experiment time.**

For example:

```text
GPU intensity 40
    ↓
N fullscreen passes
M shader iterations
K texture operations
frame cadence = 60 Hz
```

The exact implementation can vary, but the workload definition must be application-owned.

---

# 4. The GPU heater I would build

I would keep the existing raster engine and turn it into a **fixed-rate thermal renderer**.

Instead of:

```text
render
Present(vsync)
render
Present(vsync)
...
```

I would prefer:

```text
fixed experiment clock
       ↓
render a fixed amount of work
       ↓
submit
       ↓
wait/pacing
       ↓
render same amount again
```

Ideally, the heat scene can render into an off-screen resource so that display refresh no longer determines the thermal workload.

That's attractive because Microsoft's DXGI documentation confirms that VSync/presentation is tied to vertical blanking, while frame-latency mechanisms exist specifically to control presentation cadence. ([Microsoft Learn][3])

For AUTO Fan, the display should not be the thing determining how much heat the GPU receives.

### So:

**Preferred**

```text
D3D12
offscreen render target
fixed-rate submission
fixed shader workload
```

**Acceptable**

```text
D3D12
windowed rendering
fixed Present cadence
bounded frame latency
```

**Avoid**

```text
Present(0)
uncapped FPS
```

because then the GPU can submit as fast as possible and the workload becomes dependent on the particular GPU, driver, display mode, and instantaneous clock behavior.

---

# 5. Don't make the GPU heat source heavier by simply increasing FPS

This is probably the most important change for the 5070-class problem.

Suppose:

```text
1440p
60 FPS
```

is too cool.

The naive solution is:

```text
1440p
240 FPS
```

That increases work by increasing frame frequency.

It is also exactly the kind of behavior that can create undesirable acoustic behavior.

Instead:

```text
60 frames/sec
+
more work per frame
```

For example:

```text
Pass 1
Pass 2
Pass 3
Pass 4
...
Pass N
```

with each pass doing deterministic pixel/shader work.

You can make the frame more expensive without making the GPU render hundreds of frames per second.

That gives you:

```text
stable cadence
+
stable workload definition
+
higher average thermal power
```

This is much closer to what AUTO Fan needs.

---

# 6. Use shader work, not just resolution

I would not rely purely on:

```text
1920×1080
→
2560×1440
→
3840×2160
```

as the intensity knob.

Instead, have several workload dimensions:

```text
resolution
pass count
shader iteration count
texture sampling density
overdraw
```

For example:

```text
GPU Heat Profile

Resolution:       2560×1440
Cadence:          60 Hz
Passes/frame:     5
Shader iterations: 24
Texture reads:    8
```

That gives you much finer control.

The GPU can receive substantially more work per frame while the submission cadence remains fixed.

---

# 7. Why this is especially relevant to the RTX 5070

The RTX 5070 is a useful concrete example because its reference TGP is about **250 W**, but real power depends heavily on the workload. Tom's Hardware measured the Founders Edition averaging around 242 W at 4K while noting lower power use at lower resolutions/settings. ([Tom's Hardware][4])

So:

```text
1440p + light shader + 60/144 Hz
```

can be dramatically different thermally from:

```text
same 1440p
+ expensive shader passes
```

even though both look superficially like “GPU load.”

That explains the behavior you're seeing much better than simply saying “GPU utilization is low.”

---

# 8. Use GPU timestamps during calibration

This gives you an extremely useful diagnostic.

Each calibration interval can record:

```text
GPU workload parameter
GPU execution time
GPU temperature
GPU clock
GPU power, if available
```

For example:

```text
Profile 12
------------
Work/frame:       5 passes
Cadence:          60 Hz
GPU execution:    11.8 ms
GPU core:         58 °C
GPU power:        132 W
```

Then:

```text
Profile 16
------------
Work/frame:       8 passes
Cadence:          60 Hz
GPU execution:    15.1 ms
GPU core:         64 °C
GPU power:        167 W
```

And eventually:

```text
Profile 18
------------
Work/frame:       9 passes
Cadence:          60 Hz
GPU execution:    15.8 ms
GPU core:         68 °C
GPU power:        181 W
```

Then:

**lock profile 18.**

From that point onward:

```text
9 passes
60 Hz
same shader
same resolution
```

period.

D3D12 provides the timestamp-query machinery needed to measure GPU execution rather than merely assuming that “100% utilization” represents the workload. ([Microsoft Learn][1])

---

# 9. The target shouldn't be an absolute temperature

I would change the calibration target from:

```text
GPU = 70°C
```

to something more like:

```text
GPU temperature rise from baseline = target ΔT
```

because AUTO Fan cares about **signal-to-noise**, not about reaching some arbitrary silicon temperature.

Your current situation:

```text
47°C → 49°C
```

is only:

```text
ΔT = 2°C
```

That's a poor experimental signal.

A reasonable engineering default would be something like:

```text
target GPU rise:      8–12°C
minimum usable rise:   ~5°C
```

Those are **product-design targets**, not hardware safety limits.

Then constrain them with your existing safety ceilings:

```text
requested target:
    baseline + 10°C

but never:
    > existing GPU ceiling
```

So:

```text
baseline = 55°C
requested = 65°C
ceiling = 83°C
→ fine
```

whereas:

```text
baseline = 77°C
requested = 87°C
ceiling = 83°C
→ don't attempt it
```

Instead report:

> Insufficient thermal headroom to establish a useful GPU heat signal.

That is much better than increasing workload until you're sitting at the safety boundary.

---

# 10. Calibration should have a hard stop

I'd implement:

```text
START
  ↓
verify telemetry
  ↓
baseline stabilization
  ↓
increase workload
  ↓
measure ΔT
  ↓
enough signal?
 ┌───────┴───────┐
 yes             no
 ↓               ↓
LOCK          increase
                │
         safety margin?
          /          \
        yes           no
        ↓             ↓
    continue        FAIL
```

The heater should never say:

> "I can't get enough heat, therefore let's approach 90/83/95°C."

The correct outcome can be:

> "This GPU does not produce enough thermal signal under the permitted heater envelope to map its fan reliably."

That is scientifically honest.

---

# 11. CPU should use the same architecture

The CPU needs:

```text
CPU calibration
      ↓
fixed workers
      ↓
fixed instruction loop
      ↓
LOCK
```

not:

```text
CPU temp rises
↓
reduce workers
```

after calibration.

And there is a good reason not to overreact to Ryzen spikes.

AMD explicitly documents that Precision Boost 2 responds to temperature, workload, active-core count, power and current, and can adjust frequency extremely rapidly. AMD notes that light workloads can produce the highest boost frequencies, while sustained multicore workloads tend toward a steady state. ([AMD][5])

So this:

```text
64°C
↓
68°C
↓
78°C
```

over a short interval is not automatically evidence that your heater is unsafe.

It can simply be the processor changing its operating point.

That makes your existing:

```text
"temperature rate > X → abort"
```

rule particularly problematic.

---

# 12. I would remove the rate-of-rise abort from normal fan experiments

Not the temperature ceiling.

The distinction should be:

### Hard aborts

```text
CPU ≥ existing CPU ceiling
GPU ≥ existing GPU ceiling
sensor becomes stale
GPU device lost/hung/reset
fan emergency path triggered
user stop
```

### Not hard aborts

```text
CPU rose 8°C quickly
GPU rose 5°C quickly
CPU boost temporarily increased
fan RPM briefly changed
```

The latter should be logged.

They should not necessarily terminate the experiment.

The purpose of the heater is to establish a steady thermal system, and thermal systems inherently have transient behavior. Thermal engineering commonly models device heating using resistance/capacitance behavior rather than assuming instantaneous equilibrium. ([Analog Devices][6])

---

# 13. Settling should be adaptive — but load must remain fixed

This is an important distinction.

Adaptive **load**:

```text
temperature changed
→ change heater
```

Bad for AUTO Fan.

Adaptive **settling**:

```text
temperature still changing
→ wait longer
```

Completely fine.

I'd do:

```text
fan change
    ↓
wait
    ↓
calculate temperature slope
    ↓
still settling?
   /      \
 yes      no
 ↓         ↓
wait     measure
```

For example, require the recent regression slope to be approximately:

```text
|dT/dt| < 0.1–0.2 °C/min
```

for a sustained window before calling the state "settled."

That number should be tunable based on what you observe in your actual machines.

---

# 14. Your fan experiments should use A/B/A rather than one before/after

This is one place where I would improve AUTO Fan's experimental design beyond simply fixing the heater.

Suppose:

```text
A = original fan configuration
B = test fan configuration
```

Run:

```text
A
↓
stabilize
↓
measure

B
↓
stabilize
↓
measure

A
↓
stabilize
↓
measure
```

Then the counterfactual baseline at the time of B can be estimated from the two A measurements.

Conceptually:

```text
A1 ─────────────── A2
      expected A
         │
         ▼
        A*
         │
B ───────┴───────
```

Then:

```text
fan_effect = T(B) - T(A*)
```

This protects you from:

```text
room temperature drift
case heat accumulation
CPU/GPU background activity
ambient HVAC changes
sensor drift
```

You don't need fancy statistics. The principle is extremely valuable.

---

# 15. Do not compare a fan test based only on absolute temperature

Suppose:

```text
Test #1:
GPU = 67°C

Test #2:
GPU = 69°C
```

That doesn't tell you much.

What matters is:

```text
same heater
same starting condition
same workload
same cadence
different fan
```

and therefore:

```text
ΔTfan
```

The experimental record should explicitly capture:

```text
heater profile ID
CPU intensity
GPU intensity
GPU workload fingerprint
cadence
resolution
fan configuration
baseline temperature
test temperature
ambient, if available
GPU clock
GPU power, if available
```

---

# 16. Create a “heater fingerprint”

This is something I'd add specifically for AUTO Fan.

Every calibrated heater gets an immutable fingerprint:

```text
GPU:
  adapter ID
  driver version
  shader version/hash
  resolution
  target cadence
  passes
  shader iterations
  texture format
  render target format
  work profile

CPU:
  logical CPU count
  worker count
  kernel ID/version
  workload parameters
```

Then store:

```text
HEAT_PROFILE = SHA-256(...)
```

You don't need SHA-256 for compute-error validation; here it's just a convenient configuration identity.

That lets your data say:

```text
Fan Test A
Heat profile: GPU-7C91
```

and you can know that Test B used the exact same thermal stimulus.

---

# 17. The heater should measure itself

During the locked experiment, don't dynamically fix the workload — but **do verify that it remains the workload you think it is**.

For GPU:

```text
target cadence = 60 Hz
observed cadence = 59.98 Hz
GPU execution time = 14.8 ms ± 0.3 ms
```

That is good.

If suddenly:

```text
GPU execution = 5 ms
```

or:

```text
GPU execution = 33 ms
```

something changed substantially.

Record it and potentially invalidate the measurement rather than quietly pretending the experiment stayed identical.

This is a major benefit of using D3D12 timestamps. ([Microsoft Learn][1])

---

# 18. GPU device errors still absolutely matter

Cursor is right here.

If your existing D3D12 loop currently does:

```text
render error
↓
swallow
↓
wait 50ms
↓
continue
```

that is wrong for AUTO Fan.

Microsoft documents GPU device removal/reset as a condition applications need to handle, and D3D12 command/fence management gives you the mechanisms to stop cleanly. ([Microsoft Learn][7])

The sequence should be:

```text
GPU error
   ↓
HEATER ABORT
   ↓
stop submitting work
   ↓
restore fan state
   ↓
wait/cleanup GPU resources
   ↓
mark experiment invalid
   ↓
show exact error
```

Not:

```text
GPU error
↓
keep spinning
```

---

# 19. I agree with rejecting WHEA/stability scoring

For this product:

```text
WHEA count
CRC
GPU result validation
memory errors
PASS/FAIL
```

are largely orthogonal.

They're appropriate for:

```text
overclock validation
hardware stability testing
burn-in
```

AUTO Fan's question is:

> “When I change this fan, how does the thermal system respond?”

So keep the measurement surface narrow.

The less unrelated machinery you put into the experiment, the easier it is to make the result scientifically interpretable.

---

# 20. I also agree that combined should stay the default

For AUTO Fan, this is an important distinction from a conventional stress tester.

You aren't asking:

> What can my CPU do in isolation?

You're trying to understand:

> How does this cooling system respond when the PC is actually generating heat?

So:

```text
CPU heat
+
GPU heat
```

is a useful default.

The solution is exactly what Cursor described:

```text
CPU intensity = X
GPU intensity = Y
```

rather than:

```text
LOW = one predetermined combined load
```

Then calibration can produce profiles such as:

### GPU-heavy

```text
CPU = 25
GPU = 70
```

for mapping GPU/case fans.

### CPU-heavy

```text
CPU = 70
GPU = 20
```

for CPU-related fan mapping.

### Balanced

```text
CPU = 50
GPU = 50
```

for system/case behavior.

And once selected:

**freeze both values.**

---

# 21. One additional refinement: separate “heater intensity” from “heater target”

I'd explicitly model:

```text
CalibrationTarget
```

and:

```text
LockedIntensity
```

rather than one field called `Intensity`.

For example:

```csharp
public sealed record HeatProfile(
    CpuWorkProfile Cpu,
    GpuWorkProfile Gpu,
    TimeSpan FramePeriod,
    double TargetRiseC,
    string Fingerprint);
```

Calibration produces the `HeatProfile`.

Experiments consume the `HeatProfile`.

The experiment controller should never have a method like:

```csharp
AdjustGpuIntensityForTemperature();
```

That method simply should not exist in the experiment layer.

That architectural separation makes the wrong behavior difficult to introduce accidentally.

---

# 22. The resulting architecture

I would now structure AUTO Fan approximately like this:

```text
┌────────────────────────────────────────────────────────┐
│                    AUTO FAN CORE                       │
│                                                        │
│  Fan Controller ───────────────────────┐              │
│                                        │              │
│  Heat Profile Manager                  │              │
│       │                                │              │
│       ├── Calibration                  │              │
│       │      │                         │              │
│       │      └──> LOCKED PROFILE       │              │
│       │                                │              │
│       └── Experiment Engine            │              │
│              │                         │              │
│              └── constant heat ────────┤              │
│                                        │              │
│  Telemetry ────────────────────────────┤              │
│                                        │              │
│  Safety Supervisor ────────────────────┘              │
│                                                        │
└────────────────────────────────────────────────────────┘
```

The safety supervisor is still independent.

But unlike the previous design, it doesn't regulate temperature by modifying workload.

It only says:

```text
CONTINUE
```

or:

```text
ABORT
```

---

# 23. The actual GPU loop

Conceptually:

```csharp
while (experimentRunning)
{
    WaitForExperimentTick();

    RecordGpuTimestampStart();

    RenderFixedHeatScene(lockedProfile);

    RecordGpuTimestampEnd();

    Submit();

    CheckDeviceState();

    RecordTelemetry();
}
```

And critically:

```csharp
lockedProfile
```

doesn't change during this loop.

It may be:

```text
passes = 8
iterations = 32
resolution = 2560x1440
cadence = 60Hz
```

for the entire experiment.

---

# 24. Calibration loop

Different code:

```csharp
HeatProfile profile = initialProfile;

while (!EnoughThermalSignal(profile))
{
    RunFixedDuration(profile);

    var telemetry = ReadTelemetry();

    if (telemetry.GpuTemperature >= gpuCeiling)
        return CalibrationFailed;

    profile = IncreaseWork(profile);
}

return profile;
```

Once calibration returns:

```text
LOCK PROFILE
```

and the experiment engine receives an immutable object.

That's the clean separation you're looking for.

---

# 25. The safety model I would keep

Your existing ceilings remain product policy:

```text
90 / 83 / 95 °C
```

or whatever each one currently represents.

I would **not** replace those with:

```text
TjMax
NVIDIA thermal threshold
AMD thermal threshold
```

because those are hardware protection mechanisms, not necessarily appropriate measurement boundaries.

And I would not loosen them because the silicon is capable of surviving hotter temperatures.

The application's job is to stay comfortably inside the machine's own normal operating envelope.

---

# 26. What I would explicitly reject from my previous answer

| Previous idea                      | AUTO Fan decision                       |
| ---------------------------------- | --------------------------------------- |
| Closed-loop thermal load           | **Reject**                              |
| Adaptive load during fan test      | **Reject**                              |
| Adaptive 40→70→45→85 GPU           | **Reject**                              |
| 100% CPU/GPU burner                | **Reject**                              |
| Vendor thermal limits as abort     | **Reject**                              |
| WHEA stability scoring             | **Reject**                              |
| GPU result validation              | **Reject for this product**             |
| D3D11 rewrite                      | **Reject**                              |
| NVML/ADLX prerequisite             | **Reject for now**                      |
| Never touch fans                   | **Reject** — fan control is the product |
| Supervisor owns emergency stop     | **Keep**                                |
| Fail closed on telemetry loss      | **Keep**                                |
| GPU device-reset = abort           | **Keep**                                |
| Short GPU workloads                | **Keep**                                |
| Independent CPU/GPU intensity      | **Keep**                                |
| Calibration followed by fixed load | **Strongly keep**                       |

---

# 27. The one part I'd change from Cursor's proposed wording

I wouldn't describe it merely as:

> "raise GPU work until GPU Core moves several degrees, then hold."

I'd make it:

> **Calibrate a deterministic CPU/GPU workload until it produces a sufficiently large, stable thermal signal without approaching the application's safety ceilings; then freeze that workload and use it unchanged for all fan experiments.**

That's slightly more precise.

The target is not just temperature.

You want:

```text
large enough ΔT
+
stable enough thermal response
+
enough safety margin
+
repeatable workload
```

---

# 28. My current recommended AUTO Fan heater

This is the design I would implement based on the actual product purpose:

### GPU

```text
Existing D3D12 engine
        ↓
offscreen raster heat scene
        ↓
fixed cadence, initially around 60 Hz
        ↓
multiple controlled shader passes
        ↓
shader work / pass count = calibration knob
        ↓
GPU timestamps record actual work
        ↓
calibrate
        ↓
FREEZE
```

No uncapped `Present(0)`.

No temperature-driven adjustment during experiments.

No D3D11 rewrite.

### CPU

```text
fixed CPU kernel
        ↓
calibrate worker count/work quantum
        ↓
reach useful thermal signal
        ↓
FREEZE
```

No reacting to ordinary Ryzen boost spikes.

No changing worker count halfway through a fan test.

### Experiment

```text
A: baseline fan
↓
settle
↓
measure

B: target fan
↓
settle
↓
measure

A: restore baseline
↓
settle
↓
measure
```

All three phases use the **exact same immutable heat profile**.

### Abort

Only:

```text
existing temperature ceiling
telemetry lost/stale
GPU removed/hung/reset
fan safety failure
user stop
application shutdown
```

Then:

```text
STOP HEAT
RESTORE FANS
CLEAN UP
INVALIDATE TEST
REPORT WHY
```

---

## The most important conclusion

The best conceptual model for AUTO Fan is not **stress testing**.

It is **controlled thermal excitation**.

You're essentially performing a small system-identification experiment on the cooling system:

```text
known heat input
       +
known fan intervention
       ↓
measured temperature response
```

Thermal systems naturally have lag and thermal resistance, so the correct experiment needs a stable stimulus and a settling period; thermal engineering commonly models this using thermal resistance/capacitance behavior. ([Analog Devices][8])

That means your biggest engineering objective isn't achieving the highest possible CPU/GPU utilization. It is achieving a **repeatable, sufficiently strong, acoustically reasonable, safely bounded heat input**.

And that makes the **calibrate → freeze → experiment** architecture the right foundation.

The next design question is therefore very focused: **what exact D3D12 workload should AUTO Fan use to turn a 5070-class GPU from the observed ~47°C / +2°C behavior into a stable ~5–12°C thermal signal without falling back to uncapped rendering or creating unacceptable coil whine?** That is the part worth prototyping carefully rather than guessing.

[1]: https://learn.microsoft.com/hi-in/windows/win32/direct3d12/timing?utm_source=chatgpt.com "Timing (Direct3D 12 Graphics) - Win32 apps | Microsoft Learn"
[2]: https://www.ocbase.com/news/occt-gpu-stress-testing-modern-adaptive-approach?utm_source=chatgpt.com "Inside OCCT's modern adaptive approach to GPU stress testing - OCBASE/OCCT"
[3]: https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-present?utm_source=chatgpt.com "DXGI_PRESENT (DXGI.h) - Win32 apps | Microsoft Learn"
[4]: https://www.tomshardware.com/pc-components/gpus/nvidia-geforce-rtx-5070-review-founders-edition/8?utm_source=chatgpt.com "Nvidia RTX 5070 Founders Edition: Power, Clocks, Temps, and Noise - Nvidia GeForce RTX 5070 review: $549 price and performance look decent on paper - Page 8 | Tom's Hardware"
[5]: https://www.amd.com/en/resources/support-articles/faqs/CPU-PB2.html?utm_source=chatgpt.com "AMD Ryzen™ Technology: Precision Boost 2 Performance Enhancement"
[6]: https://www.analog.com/en/resources/technical-articles/use-thermal-analysis-to-predict-an-ics-transient-behavior-and-avoid-overheating.html?utm_source=chatgpt.com "Use Thermal Analysis to Predict an IC's Transient Behavior and Avoid Overheating | Analog Devices"
[7]: https://learn.microsoft.com/en-us/windows/win32/direct3d12/porting-from-direct3d-11-to-direct3d-12?utm_source=chatgpt.com "Porting from Direct3D 11 to Direct3D 12 - Win32 apps | Microsoft Learn"
[8]: https://www.analog.com/en/resources/technical-articles/data-sheet-intricacies-absolute-maximum-ratings-and-thermal-resistances.html?utm_source=chatgpt.com "Data Sheet Intricacies— Absolute Maximum Ratings and Thermal Resistances | Analog Devices"

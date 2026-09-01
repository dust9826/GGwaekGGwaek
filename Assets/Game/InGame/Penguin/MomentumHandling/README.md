# Momentum Handling Lab

`Penguin_MomentumHandling_Test.unity` is the isolated tuning scene for braking and
Nunu-style momentum handling. It is not included in Build Settings.

The production `PF_Penguin` prefab intentionally has no `PenguinMomentumHandling`
component. `PF_Penguin_MomentumHandling` is a prefab variant that enables the experiment,
so ordinary gameplay keeps the previous controls until the tuning is accepted.

## Controls

- Slide/carry: `Shift` propulsion, `W` tuck, `S` brake, `A/D` steer.
- Snowball side push: `E` attach, `W` push, `S` brake, `A/D` orbit.
- Cargo: `F` pick up or put down.

The legacy random right-click co-op timing window and its impulse bonus are disabled in both
isolated snowball test scenes, so low initial drive force is measured only as physical acceleration.

## Test lanes

- 70 x 240 m flat runway with braking markers across a 220 m measurement span.
- Four long slalom lanes with 15 collision poles each (60 total) for sustained steering,
  avoidance, and A/D reversal.
- Separate 80 m-long 10° and 20° slopes for gravity, overspeed coasting, and
  service-brake checks.
- 8 kg/120 kg gifts remain available for carry comparison. Snowballs are not preplaced: press
  `E` on the snow field to create one and grow it by rolling across the 100 x 240 m snow area.

`PPack > Penguin > Measure Momentum Acceleration` measures the first time each object reaches
95% of its final flat-ground target speed and reports the result in the test output.

Snowball handling uses five tunable points: seed, each of the three stage-entry radii, and maximum
radius. Carry handling uses light/heavy endpoints. Every value is interpolated continuously by the
current radius or cargo share, so crossing a stage boundary never causes a handling discontinuity.
Each point exposes only initial target speed, maximum speed, acceleration time, coast-stop time,
brake-stop time, and steering response time. Drive and brake forces are derived internally from
those values and the current mass. Derived drive force keeps a
20% tracking reserve for the discrete kick pulse and changing smooth-step target; this is not an
additional tuning parameter.

The reference defaults are intentionally game-feel values rather than a simulation claim:

| Snowball point | Initial / max speed | Acceleration | Coast / brake | Steer response | Steer authority at max speed, immediate / committed |
|---|---:|---:|---:|---:|---:|
| Seed (0.18 m) | 2.4 / 3.2 m/s | 0.65 s | 2.2 / 0.70 s | 0.35 s | 64.6% / 74.4% |
| Stage 1 entry (0.51 m) | 2.15 / 3.65 m/s | 1.20 s | 2.7 / 0.90 s | 0.80 s | 48.5% / 64.1% |
| Stage 2 entry (0.84 m) | 1.8 / 4.1 m/s | 2.00 s | 3.4 / 1.20 s | 1.40 s | 32.3% / 53.7% |
| Stage 3 entry (1.17 m) | 1.4 / 4.55 m/s | 3.00 s | 4.3 / 1.55 s | 2.10 s | 16.2% / 43.4% |
| Stage 4 entry / maximum (1.50 m) | 1.1 / 5.0 m/s | 4.20 s | 5.2 / 1.90 s | 3.00 s | 0% / 33.0% |

The authority values above use each point's tuned maximum speed. Both immediate and committed
authority decrease linearly with radius between points; the global speed curve still multiplies
them, so a slower ball retains more authority than the table and a faster ball retains less.

| Carry point | Initial / max speed | Acceleration | Coast / brake | Steer response |
|---|---:|---:|---:|---:|
| Light | 3.8 / 7.2 m/s | 1.10 s | 3.0 / 1.0 s | 0.15 s |
| Heavy | 2.2 / 9.0 m/s | 2.80 s | 5.0 / 1.8 s | 0.85 s |

Snowball steering authority has only two shared parameters: `1.00` at 0 m/s and `0.28` at
9 m/s. The line is not clamped at 9 m/s: it continues with the same slope until authority reaches
zero at 12.5 m/s, then remains zero. A fully committed turn at the same actual speed therefore has
the same authority in every growth stage. Size changes only how long a reversed A/D input takes to
recover that authority.

The result is a responsive seed ball and a large ball that starts deliberately, keeps building to a
higher top speed, coasts farther, and needs a sustained A/D direction after a reversal. Carrying
keeps the production slide's speed-dependent yaw and lateral grip; the profile changes only the
load-dependent response time, avoiding a second speed-authority multiplier.

Snowballs use zero artificial linear damping, 0.015 rolling resistance, and sphere aerodynamic
drag (`Cd 0.47`). Effective mass still follows `m = 300(r / 1.5)³`: 0.5184 kg at the 0.18 m seed and
300 kg at 1.5 m. The configured coast resistance replaces the smaller passive resistance while an
attached penguin has released propulsion and after `E` detach until the ball falls below 0.05 m/s.
Reattaching cancels that detached coast state. It does not clamp overspeed. Carry coast resistance
is applied in the current ground plane. Gravity remains a separate Rigidbody force, so slope
acceleration can add to or overcome the configured service resistance.

The growth timer finalizes the controlled radius after snow harvesting, then the handling-mass
component applies the continuous 0.5184–300 kg mass before `SnowBallCarrier` consumes the existing
`SubmitPush` request. This keeps one propulsion path and prevents the source snow-density mass from
changing acceleration for one tick.

The runtime growth presentation stores the previous and current fixed-step radii and interpolates
its child render proxy in `LateUpdate`. Rigidbody position/rotation interpolation and visual size
therefore advance together even when a 60 Hz render frame falls between 50 Hz physics steps. The
root scale and collider remain the authoritative physical radius.

Plain sliding keeps its production target speed, gravity, carving, and steering response. The
interpolated speed envelope and load-dependent steering commitment apply to carrying and snowball
pushing only.

The brake is a service brake, not a hill-hold. A steep enough slope or load can overpower it.
Cruise targets only limit positive propulsion; they never clamp velocity or cancel downhill gravity.

# Authoring a Craftwar AI (`.ai` profiles)

The computer opponent is a **utility AI**: every think, each domain (build, train,
tech, economy, expansion, attack, defend) proposes *scored candidate actions*, and an
arbiter runs the best ones that fit the command budget. You tune that scoring — the
opponent's whole personality — in a plain-text `.ai` **profile**. No engine recompile,
no C#.

Drop a file in `<persistentDataPath>/Ai/<name>.ai` and it appears in the lobby's AI
picker. The built-in default is `StreamingAssets/Ai/land-attack.ai` — copy it and start
editing. A broken file never crashes the game; it logs a warning and falls back to the
default.

## Determinism / lockstep contract

Profiles are **integer-only by construction** — the DSL admits no floating point and no
nondeterministic constructs, and the parser (`AiProfileParser`) lives inside the sealed
deterministic `Craftwar.Sim` library. Every weight and curve you author is stored as
fixed-point (Q16.16) and evaluated with integer math, so a modded AI is **safe for
networked lockstep**: all clients compute the same decisions from the same
`(map, seed, command log)`. You cannot author an AI that desyncs a multiplayer game.
(This is the deliberate trade for not embedding Lua — see the design plan.)

## File format

Line-oriented. `#` starts a comment. One directive per line. Order does not matter.
The only required line is `profile <name>`; everything else has a sensible default, so
override just what you care about.

```
profile my-rusher
defaultTier normal
personality aggression=80 greed=40 defensiveness=30 expansiveness=20

economy workerTarget=14 minGold=500 lowGold=1000 lowTree=500 plentyTree=2000
rebuildOnly gold=200 lumber=100
military waveSize=5 suicideBuildingCount=3 postWaveSleep=400 dryWave=1500

build Hall,Barracks,Barracks,LumberMill,Blacksmith,Keep
army Soldier:8,Archer:2
research Weapon1,Armor1,Weapon2

weights farm=300 build=100 worker=90 army=140 research=40 expand=30 wave=160 defend=400 harvest=200 scout=12
curve affordability logistic 300 40
curve waveReadiness logistic 400 40
curve relativeStrength linear 120 0
```

### Directives

| Directive | Keys / form | Meaning |
|---|---|---|
| `profile` | `<name>` | Profile name (required). |
| `defaultTier` | `dumb\|normal\|smart\|god` | Suggested difficulty when none is chosen. |
| `personality` | `aggression greed defensiveness expansiveness` (0–100) | Flavour dials, kept for the debug overlay; tune behaviour through `weights`/`curve`. |
| `economy` | `workerTarget minGold lowGold lowTree plentyTree` | Worker count **per hall**, and the gold/wood balancing thresholds. |
| `rebuildOnly` | `gold lumber` | Below **both**, build/train nothing but a hall (emergency rebuild). |
| `military` | `waveSize suicideBuildingCount postWaveSleep dryWave` | Muster size to attack; all-in triggers; pacing (ticks). |
| `build` | `Role,Role,...` | Cumulative build order — each occurrence of a role raises its target by one (a 2nd `Barracks` = a second barracks). `Keep`/`Castle`/`GuardTower`/`CannonTower` are tier **upgrades**, not new sites. |
| `army` | `Role:Count,...` | Standing-army composition target. |
| `research` | `Upgrade,...` | Research priority, in order. |
| `weights` | `key=percent ...` | Per-domain base priority. `100` = 1.0×. Raise `wave` for aggression, `expand` to grab bases, `defend` to turtle. |
| `curve` | `<name> <kind> <a> [b]` | A response curve (see below). |

### Roles (`build` / `army`) and upgrades (`research`)

Race-neutral — resolved to Human/Orc types automatically.

- **Roles**: `Worker Soldier Archer Cavalry Siege Hall Keep Castle Farm Barracks
  LumberMill Blacksmith ScoutTower GuardTower CannonTower CavalryHall Church MageHall
  AirHall`
- **Upgrades**: `Weapon1 Weapon2 Armor1 Armor2 Missile1 Missile2 RangedUnlock
  CavalryUnlock`

### Weights

`farm build worker army research expand wave defend harvest scout` — integer percents
(`100` = 1.0×). These set each domain's standing in the arbiter; a candidate's final
score is `weight × response-curves`. Bigger weight ⇒ that domain wins the budget more
often.

### Response curves

`curve <name> <kind> <a> [b]` — `a` and `b` are integer **percents** (`50` = 0.5,
`300` = 3.0; `a` may be negative). All curves take a normalized input in `[0,1]` and
return a score in `[0,1]`.

| Kind | Formula | `a`, `b` |
|---|---|---|
| `constant` | `a` | `a` = the value |
| `linear` | `clamp01(b + a·x)` | `a` = slope, `b` = intercept |
| `quadratic` | `clamp01(b + a·x²)` | ease-in / ease-out |
| `logistic` | smooth S-curve around `b`, steepness `a` | `a` = steepness, `b` = midpoint |
| `step` | `x ≥ b ? 1 : 0` | `b` = threshold |

Tunable curves and what feeds them:

- `affordability` — resource comfort for a purchase.
- `threatSafety` — high when the base is calm, low under enemy influence (damps building
  in the open while attacked). Default `linear -100 100` (down-slope).
- `waveReadiness` — army size vs. `waveSize`.
- `relativeStrength` — own army vs. the strongest enemy's (0.5 = even). Lower the slope
  for a cautious AI that waits until ahead.
- `mineDepletion` — how tapped-out the home mine is (drives expansion).
- `foodSafety` — headroom before the supply cap.

## Difficulty tiers

Difficulty (`AiTier`: `dumb/normal/smart/god`) is orthogonal to the profile — any profile
plays at any tier. Tiers change **think cadence** and switch on capabilities: `smart`
adds active defense, focus-fire, reinforcement and scouting; `god` also expands and
receives the menu's resource/vision handicaps. A profile is the *personality*; the tier
is the *skill*.

## The hard spatial guarantee (you can't break it)

No profile can make the AI wall itself in. Every building placement is validated by an
occupancy-aware connectivity check (`ReachabilityProbe`): a plot that would sever the
base from its gold, wood, or a map exit is never chosen — regardless of weights. Author
freely; the base stays playable.

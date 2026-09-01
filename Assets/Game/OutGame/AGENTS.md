# OutGame — everything before the stage

Assembly `PPack.OutGame`. References `PPack.Core` only — **never `PPack.InGame`**. Enforced by the assembly definition.

| | |
|---|---|
| `Lobby/` | room creation and joining, room code, player list, readiness |
| `Matchmaking/` | finding a session, and the handoff into the stage |

Target size is 2–4 players online. Whether a session is joinable mid-run is still undecided in the design doc — don't build either assumption in deeply yet.

Anything the stage needs to know at handoff is a type in `../Core/`, never a call back into `OutGame`.

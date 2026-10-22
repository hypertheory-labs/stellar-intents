# Example: placing a hold through the outbox (Angular)

A real page from the course these ideas came out of, kept as a reference for how the
`outbox` library is used in an app. **Not runnable on its own here:** there's no app shell,
and it calls endpoints the reference server doesn't have (`/branches`, patron prefs, and a
separate catalog service for titles).

| File                    | Shows                                                                                         |
| ----------------------- | --------------------------------------------------------------------------------------------- |
| `place-hold-intent.ts`  | Declaring an intent type: request, receipt, candidate, apology.                               |
| `place-hold.ts`         | The page: enqueue, events out, the three rungs of hearing "done", merge with candidates, retire. |
| `hold-events.ts`        | The SSE stream as an Observable, with reconnects (PROTOCOL §3).                               |
| `outbox-log.ts`         | A debugging view of `[Outbox]` events.                                                        |
| `api.ts`                | The app's API types and URLs.                                                                 |

The server side of the same flow is `server/dotnet/ReferenceApi/Holds/`.

import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, DOCUMENT, effect, inject, signal, untracked } from '@angular/core';
import {
  cachedHttpResource,
  isCandidate,
  mergeWithCandidates,
  OutboxStore,
  readyToRetire,
} from 'outbox';
import { EventLog } from './event-log';
import { cancelHold, Hold, placeHold, titleName, TITLES } from './holds';

interface SessionInfo {
  isAuthenticated: boolean;
  patronId: string | null;
}

/** A row on screen: the server's hold, or a guess at one. */
interface Row {
  hold: Hold;
  guess: boolean;
  cancelling: boolean;
}

@Component({
  selector: 'app-root',
  imports: [DatePipe, EventLog],
  template: `
    <header>
      <h1>Holds</h1>
      @if (patronId(); as patron) {
        <p>
          Signed in as <strong>{{ patron }}</strong>
          <button type="button" (click)="signOut()">Sign out</button>
        </p>
      } @else if (session.hasValue()) {
        <p>
          Sign in as
          @for (p of patrons; track p) {
            <button type="button" (click)="signIn(p)">{{ p }}</button>
          }
          <span class="hint">(Development sign-in, then a full page load, like a real one.)</span>
        </p>
      }
    </header>

    @if (patronId(); as patron) {
      <main>
        <section aria-labelledby="place-heading">
          <h2 id="place-heading">Place a hold</h2>
          <label>
            Title
            <select [value]="titleId()" (change)="titleId.set(+$any($event.target).value)">
              @for (t of titles; track t.id) {
                <option [value]="t.id">{{ t.name }}</option>
              }
            </select>
          </label>
          <button type="button" (click)="place(patron)">Place hold</button>
        </section>

        <section aria-labelledby="notices-heading" aria-live="polite">
          <h2 id="notices-heading">What we can say right now</h2>
          <ul class="notices">
            @for (r of outbox.receipts(); track r.intentId) {
              <li>
                <span class="status">{{ r.status }}</span> {{ r.text }}
                @if (r.status === 'received' || r.status === 'unknown') {
                  <button type="button" (click)="outbox.checkStatus(r.intentId)">
                    Check status
                  </button>
                }
              </li>
            }
            @for (a of outbox.apologies(); track a.intentId) {
              <li class="apology">
                {{ a.message }}
                @if (a.remedy === 'try-again') {
                  <button type="button" (click)="outbox.resend(a.intentId)">Try again</button>
                }
                <button type="button" (click)="outbox.dismiss(a.intentId)">Dismiss</button>
              </li>
            }
          </ul>
        </section>

        <section aria-labelledby="mine-heading">
          <h2 id="mine-heading">My holds</h2>
          <p class="hint">
            @if (myHolds.fetchedAt(); as at) {
              As of {{ at | date: 'HH:mm:ss' }}
            }
            @if (myHolds.stale()) {
              · <span class="stale">may be out of date</span>
            }
            @if (myHolds.isLoading()) {
              · refreshing…
            }
          </p>
          <table>
            <thead>
              <tr>
                <th scope="col">Title</th>
                <th scope="col">Status</th>
                <th scope="col"><span class="visually-hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.hold.id) {
                <tr [class.guess]="row.guess" [class.cancelling]="row.cancelling">
                  <td>{{ name(row.hold.titleId) }}</td>
                  <td>
                    @if (row.guess) {
                      requested <em>(not confirmed yet)</em>
                    } @else if (row.cancelling) {
                      cancelling…
                    } @else {
                      {{ row.hold.status }}
                    }
                  </td>
                  <td>
                    @if (!row.guess && !row.cancelling) {
                      <button type="button" (click)="cancel(row.hold)">Cancel</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="3">No holds.</td>
                </tr>
              }
            </tbody>
          </table>
        </section>

        <section aria-labelledby="waitlist-heading">
          <h2 id="waitlist-heading">Wait list: {{ name(titleId()) }}</h2>
          <p class="hint">
            Everyone's holds on this title. Another patron's change arrives here too.
            @if (waitList.fetchedAt(); as at) {
              As of {{ at | date: 'HH:mm:ss' }}.
            }
          </p>
          <p>{{ waitList.value()?.length ?? '…' }} holds ({{ placedOnWaitList() }} placed)</p>
        </section>
      </main>
    }

    <app-event-log />
  `,
  styles: `
    :host {
      display: block;
      max-width: 60rem;
      margin: 0 auto;
      padding: 1rem;
    }
    table {
      border-collapse: collapse;
      width: 100%;
    }
    th,
    td {
      text-align: left;
      padding: 0.35rem 0.5rem;
      border-bottom: 1px solid var(--rule);
    }
    tr.guess td {
      font-style: italic;
      color: var(--muted);
    }
    tr.cancelling td {
      opacity: 0.6;
    }
    .notices {
      padding-left: 1.25rem;
    }
    .apology {
      color: var(--warn);
    }
    .status {
      font-family: monospace;
      color: var(--muted);
    }
    .hint {
      color: var(--muted);
    }
    .stale {
      color: var(--warn);
    }
    button {
      margin-left: 0.5rem;
    }
  `,
})
export class App {
  protected readonly outbox = inject(OutboxStore);
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);

  protected readonly patrons = ['P0042', 'P0043'];
  protected readonly titles = TITLES;
  protected readonly name = titleName;
  protected readonly titleId = signal<number>(TITLES[0].id);

  protected readonly session = cachedHttpResource<SessionInfo>(() => '/session');
  protected readonly patronId = computed(() => this.session.value()?.patronId ?? undefined);

  // Memory: only what the server said. Guesses are merged in for display, never stored here.
  protected readonly myHolds = cachedHttpResource<Hold[]>(() => {
    const patron = this.patronId();
    return patron ? `/holds?patronId=${patron}` : undefined;
  });
  protected readonly waitList = cachedHttpResource<Hold[]>(() =>
    this.patronId() ? `/holds?titleId=${this.titleId()}` : undefined,
  );
  protected readonly placedOnWaitList = computed(
    () => this.waitList.value()?.filter((h) => h.status === 'placed').length ?? 0,
  );

  /** Intents to cancel that haven't been declined: their holds show "cancelling…". */
  private readonly cancelling = computed(() =>
    this.outbox
      .entities()
      .filter((i) => i.type === cancelHold.name)
      .filter((i) => i.state.status !== 'decided' || i.state.outcome === 'accepted')
      .map((i) => ({ intentId: i.id, holdId: (i.payload as { holdId: string }).holdId })),
  );

  protected readonly rows = computed<Row[]>(() => {
    const cancelling = new Set(this.cancelling().map((c) => c.holdId));
    return mergeWithCandidates(
      this.myHolds.value() ?? [],
      this.outbox.candidates(placeHold),
      (h) => h.id,
    ).map((item) =>
      isCandidate(item)
        ? { hold: item.value, guess: true, cancelling: false }
        : { hold: item, guess: false, cancelling: cancelling.has(item.id) },
    );
  });

  constructor() {
    // Promotion, both kinds. A place-hold is done when memory HAS its hold; a cancel-hold
    // is done when memory NO LONGER has it. Either way the guess has done its job.
    effect(() => {
      const memory = this.myHolds.value();
      if (!memory) return;
      const done = this.outbox
        .entities()
        .filter((i) => i.state.status === 'decided' && i.state.outcome === 'accepted');
      const placed = readyToRetire(
        done.filter((i) => i.type === placeHold.name),
        memory,
        (h) => h.id,
      );
      const inMemory = new Set(memory.map((h) => h.id));
      const cancelled = this.cancelling().filter(
        (c) => done.some((i) => i.id === c.intentId) && !inMemory.has(c.holdId),
      );
      untracked(() =>
        [...placed, ...cancelled.map((c) => c.intentId)].forEach((id) => this.outbox.retire(id)),
      );
    });
  }

  protected place(patronId: string) {
    this.outbox.enqueue(placeHold, { patronId, titleId: this.titleId(), branchCode: 'CEN' });
  }

  protected cancel(hold: Hold) {
    this.outbox.enqueue(cancelHold, {
      holdId: hold.id,
      titleId: hold.titleId,
    });
  }

  // Development sign-in, then a full page load: what an OIDC redirect would do. The event
  // stream starts with the app, so it starts signed in.
  protected signIn(patronId: string) {
    this.http
      .post(`/api/dev/sign-in?patronId=${patronId}`, null)
      .subscribe(() => this.document.location.reload());
  }

  protected signOut() {
    this.http.post('/api/dev/sign-out', null).subscribe(() => this.document.location.reload());
  }
}

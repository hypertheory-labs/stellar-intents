import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Events } from '@ngrx/signals/events';
import { invalidationEvents, outboxEvents, serverEvents } from 'outbox';

interface LogLine {
  readonly at: number;
  readonly type: string;
  readonly detail: string;
}

/** Every event the client publishes, newest first: what happened, and how we heard. */
@Component({
  selector: 'app-event-log',
  imports: [DatePipe],
  template: `
    <h2 id="log-heading">Memories, guesses, and apologies</h2>
    <p class="hint">Everything the client announced, newest first.</p>
    <ol aria-labelledby="log-heading" class="log">
      @for (line of lines(); track $index) {
        <li>
          <time>{{ line.at | date: 'HH:mm:ss.SSS' }}</time>
          <code>{{ line.type }}</code>
          <span>{{ line.detail }}</span>
        </li>
      }
    </ol>
  `,
  styles: `
    .log {
      list-style: none;
      padding: 0;
      margin: 0;
      font-size: 0.85rem;
      max-height: 24rem;
      overflow-y: auto;
    }
    .log li {
      display: flex;
      gap: 0.75rem;
      padding: 0.15rem 0;
      border-bottom: 1px solid var(--rule);
    }
    time {
      color: var(--muted);
      font-variant-numeric: tabular-nums;
    }
    .hint {
      color: var(--muted);
      margin-top: 0;
    }
  `,
})
export class EventLog {
  protected readonly lines = signal<LogLine[]>([]);

  constructor() {
    inject(Events)
      .on(
        ...Object.values(outboxEvents),
        ...Object.values(serverEvents),
        invalidationEvents.invalidated,
      )
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((event) => {
        const line = { at: Date.now(), type: event.type, detail: describe(event.payload) };
        this.lines.update((lines) => [line, ...lines].slice(0, 100));
      });
  }
}

function describe(payload: unknown): string {
  if (!payload || typeof payload !== 'object') return '';
  if ('intent' in payload) {
    const intent = (payload as { intent: { id: string; type: string } }).intent;
    return `${intent.type} ${intent.id.slice(0, 8)}`;
  }
  if ('keys' in payload) {
    const { keys } = payload as { keys: string[] };
    return keys.join(', ');
  }
  return '';
}

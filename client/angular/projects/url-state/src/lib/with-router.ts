import { computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, PRIMARY_OUTLET, Router, type Event } from '@angular/router';
import {
  patchState,
  signalStoreFeature,
  withComputed,
  withHooks,
  withProps,
  withState,
} from '@ngrx/signals';
import { filter } from 'rxjs';

export function withRouter() {
  return signalStoreFeature(
    // the url is the single source of truth - everything else is derived from it
    withState({ url: '' }),
    withProps(() => ({ _router: inject(Router) })),
    withComputed(({ url, _router }) => {
      const tree = computed(() => _router.parseUrl(url()));
      return {
        queryParams: computed(() => tree().queryParams),
        // matrix params from every segment along the primary outlet, merged (deeper segments win).
        // not just the last segment - child routes (e.g. /products;sortOption=price/3) would hide them
        matrixParams: computed(() => {
          const params: Record<string, string> = {};
          let group = tree().root.children[PRIMARY_OUTLET];
          while (group) {
            for (const segment of group.segments) {
              Object.assign(params, segment.parameters);
            }
            group = group.children[PRIMARY_OUTLET];
          }
          return params;
        }),
      };
    }),
    withHooks({
      onInit(store) {
        // the store is created lazily, so the initial NavigationEnd has already happened
        patchState(store, { url: store._router.url });
        store._router.events
          .pipe(
            filter((e: Event): e is NavigationEnd => e instanceof NavigationEnd),
            takeUntilDestroyed(),
          )
          .subscribe((e) => patchState(store, { url: e.urlAfterRedirects }));
      },
    }),
  );
}

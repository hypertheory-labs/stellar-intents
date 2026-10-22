import { computed, type Signal } from '@angular/core';
import {
  PRIMARY_OUTLET,
  Router,
  UrlSegment,
  UrlSegmentGroup,
  UrlTree,
  type Params,
} from '@angular/router';
import { signalStoreFeature, type, withComputed, withMethods } from '@ngrx/signals';

// anything with a `parse` works - a zod schema, valibot, or a hand-rolled function.
// use something that never throws (e.g. zod's `.catch(default)`) - the url is user input.
export interface ParamParser<T> {
  parse(data: unknown): T;
}

type Primitive = string | number | boolean;
type Setter<Name extends string> = `set${Capitalize<Name>}`;

// what these features need from withRouter()
const routerProps = {
  state: type<{ url: string }>(),
  props: type<{
    _router: Router;
    queryParams: Signal<Params>;
    matrixParams: Signal<Record<string, string>>;
  }>(),
};

/**
 * A typed, validated query param. `withQueryParam('q', z.string().catch(''))` adds:
 * - `q: Signal<string>`
 * - `setQ(value: string): Promise<boolean>` - navigates; the signal updates from the url
 */
export function withQueryParam<Name extends string, T extends Primitive>(
  name: Name,
  parser: ParamParser<T>,
  options: { replaceUrl?: boolean } = {},
) {
  return signalStoreFeature(
    routerProps,
    withComputed(
      (store) =>
        ({ [name]: computed(() => parser.parse(store.queryParams()[name])) }) as Record<
          Name,
          Signal<T>
        >,
    ),
    withMethods(
      (store) =>
        ({
          [setterName(name)]: (value: T) =>
            store._router.navigate([], {
              queryParams: { [name]: toParam(value, parser) },
              queryParamsHandling: 'merge',
              preserveFragment: true,
              replaceUrl: options.replaceUrl,
            }),
        }) as Record<Setter<Name>, (value: T) => Promise<boolean>>,
    ),
  );
}

/**
 * A typed, validated matrix param, read from whichever segment holds it.
 * `withMatrixParam('sortOption', z.enum(['name', 'price']).catch('name'))` adds:
 * - `sortOption: Signal<'name' | 'price'>`
 * - `setSortOption(value): Promise<boolean>`
 *
 * The setter updates the param on the segment that already holds it, and drops any child
 * segments below it (changing the list's sort closes the detail view - same as a routerLink).
 * If no segment holds it yet, it goes on the last segment. For "put it on *this* route's
 * segment", a `[routerLink]="['.', { sortOption }]"` in the template is still the better tool -
 * and it gives you a real href.
 */
export function withMatrixParam<Name extends string, T extends Primitive>(
  name: Name,
  parser: ParamParser<T>,
  options: { replaceUrl?: boolean } = {},
) {
  return signalStoreFeature(
    routerProps,
    withComputed(
      (store) =>
        ({ [name]: computed(() => parser.parse(store.matrixParams()[name])) }) as Record<
          Name,
          Signal<T>
        >,
    ),
    withMethods(
      (store) =>
        ({
          [setterName(name)]: (value: T) => {
            const tree = setMatrixParam(
              store._router.parseUrl(store.url()),
              name,
              toParam(value, parser),
            );
            return store._router.navigateByUrl(tree, { replaceUrl: options.replaceUrl });
          },
        }) as Record<Setter<Name>, (value: T) => Promise<boolean>>,
    ),
  );
}

function setterName<Name extends string>(name: Name) {
  return `set${name.charAt(0).toUpperCase()}${name.slice(1)}` as Setter<Name>;
}

// the default value is left out of the url entirely - /products, not /products;sortOption=name
function toParam<T extends Primitive>(value: T, parser: ParamParser<T>): string | null {
  let fallback: T | undefined;
  try {
    fallback = parser.parse(undefined);
  } catch {
    // no default - always write the value
  }
  return value === fallback ? null : String(value);
}

// exported for tests; not part of the public api
export function setMatrixParam(tree: UrlTree, name: string, value: string | null): UrlTree {
  const chain: UrlSegmentGroup[] = [];
  for (let g = tree.root.children[PRIMARY_OUTLET]; g; g = g.children[PRIMARY_OUTLET]) {
    chain.push(g);
  }
  if (chain.length === 0) {
    return tree; // at the root url - no segment to hold a matrix param
  }

  // the deepest segment that already holds the param, or else the very last segment
  let groupIndex = chain.length - 1;
  let segmentIndex = chain[groupIndex].segments.length - 1;
  search: for (let i = chain.length - 1; i >= 0; i--) {
    for (let j = chain[i].segments.length - 1; j >= 0; j--) {
      if (name in chain[i].segments[j].parameters) {
        groupIndex = i;
        segmentIndex = j;
        break search;
      }
    }
  }

  const rebuild = (i: number): UrlSegmentGroup => {
    const group = chain[i];
    if (i < groupIndex) {
      return new UrlSegmentGroup(group.segments, {
        ...group.children,
        [PRIMARY_OUTLET]: rebuild(i + 1),
      });
    }
    const segments = group.segments.slice(0, segmentIndex + 1);
    const { path, parameters } = segments[segmentIndex];
    const { [name]: _, ...others } = parameters;
    segments[segmentIndex] = new UrlSegment(path, value === null ? others : { ...others, [name]: value });
    const { [PRIMARY_OUTLET]: __, ...namedOutlets } = group.children;
    return new UrlSegmentGroup(segments, namedOutlets);
  };

  const root = new UrlSegmentGroup(tree.root.segments, {
    ...tree.root.children,
    [PRIMARY_OUTLET]: rebuild(0),
  });
  return new UrlTree(root, tree.queryParams, tree.fragment);
}

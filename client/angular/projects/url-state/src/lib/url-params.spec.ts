import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DefaultUrlSerializer, provideRouter, Router } from '@angular/router';
import { signalStore } from '@ngrx/signals';
import { z } from 'zod';
import { setMatrixParam, withMatrixParam, withQueryParam } from './url-params';
import { withRouter } from './with-router';

const serializer = new DefaultUrlSerializer();
const set = (url: string, name: string, value: string | null) =>
  serializer.serialize(setMatrixParam(serializer.parse(url), name, value));

describe('setMatrixParam', () => {
  it('updates the segment that holds the param, and drops the segments below it', () => {
    expect(set('/products;sort=price/3', 'sort', 'name')).toBe('/products;sort=name');
  });

  it('puts a new param on the last segment', () => {
    expect(set('/products/3', 'sort', 'name')).toBe('/products/3;sort=name');
  });

  it('removes the param for null', () => {
    expect(set('/products;sort=price;view=grid', 'sort', null)).toBe('/products;view=grid');
  });

  it('keeps the query and fragment', () => {
    expect(set('/products;sort=price?q=dune#top', 'sort', 'name')).toBe(
      '/products;sort=name?q=dune#top',
    );
  });

  it('leaves the root url alone', () => {
    expect(set('/', 'sort', 'name')).toBe('/');
  });

  it('keeps named outlets', () => {
    expect(set('/products;sort=price(side:help)', 'sort', 'name')).toBe(
      '/products;sort=name(side:help)',
    );
  });
});

@Component({ template: '' })
class Blank {}

const TestStore = signalStore(
  { providedIn: 'root' },
  withRouter(),
  withMatrixParam('sort', z.enum(['name', 'price']).catch('name')),
  withQueryParam('q', z.string().catch('')),
);

describe('withQueryParam and withMatrixParam', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: Blank }])],
    });
    router = TestBed.inject(Router);
  });

  it('reads the url as it is when the store is created', async () => {
    await router.navigateByUrl('/products;sort=price?q=dune');
    const store = TestBed.inject(TestStore);

    expect(store.sort()).toBe('price');
    expect(store.q()).toBe('dune');
  });

  it('follows later navigations', async () => {
    const store = TestBed.inject(TestStore);
    await router.navigateByUrl('/products;sort=price?q=dune');

    expect(store.sort()).toBe('price');
    expect(store.q()).toBe('dune');
  });

  it('falls back to the default for values the parser rejects', async () => {
    await router.navigateByUrl('/products;sort=bogus');
    const store = TestBed.inject(TestStore);

    expect(store.sort()).toBe('name');
  });

  it('setters navigate, and the signals follow the url', async () => {
    await router.navigateByUrl('/products');
    const store = TestBed.inject(TestStore);

    await store.setSort('price');
    await store.setQ('dune');

    expect(router.url).toBe('/products;sort=price?q=dune');
    expect(store.sort()).toBe('price');
    expect(store.q()).toBe('dune');
  });

  it('leaves default values out of the url', async () => {
    await router.navigateByUrl('/products;sort=price?q=dune');
    const store = TestBed.inject(TestStore);

    await store.setSort('name');
    await store.setQ('');

    expect(router.url).toBe('/products');
  });
});

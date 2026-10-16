import { describe, expect, it } from 'vitest';

import { cellText } from '../src/tanStackTable.js';

/**
 * Northwind products whose `Products.Discontinued` column is the text `"1"`.
 * The showcase must render those cells as Yes (Alice Mutton, Chef Anton's Gumbo Mix).
 */
const DISCONTINUED_AS_TEXT = [
  { productName: 'Alice Mutton', discontinued: '1' },
  { productName: "Chef Anton's Gumbo Mix", discontinued: '1' },
] as const;

describe('TanStack Discontinued cell', () => {
  it('renders Yes for Alice Mutton and Chef Anton\'s Gumbo Mix when the flag is the text "1"', () => {
    for (const product of DISCONTINUED_AS_TEXT) {
      expect(cellText('Discontinued', product.discontinued)).toBe('Yes');
    }
  });

  it.each([
    [true, 'Yes'],
    [1, 'Yes'],
    ['1', 'Yes'],
    ['true', 'Yes'],
    ['TRUE', 'Yes'],
    ['True', 'Yes'],
    [false, 'No'],
    [0, 'No'],
    ['0', 'No'],
    ['false', 'No'],
    ['FALSE', 'No'],
    ['False', 'No'],
    ['yes', 'No'],
    ['', 'No'],
    [null, 'No'],
    [undefined, 'No'],
  ] as const)('renders %j as %s', (value, expected) => {
    expect(cellText('Discontinued', value)).toBe(expected);
  });

  it('still renders other columns, including a blank for null', () => {
    expect(cellText('ProductName', 'Chai')).toBe('Chai');
    expect(cellText('ProductName', null)).toBe('');
    expect(cellText('UnitsInStock', 0)).toBe('0');
  });
});

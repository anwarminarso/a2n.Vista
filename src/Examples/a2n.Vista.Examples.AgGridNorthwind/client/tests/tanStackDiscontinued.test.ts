import { describe, expect, it } from 'vitest';

import { cellText } from '../src/tanStackTable.js';

describe('TanStack Discontinued cell', () => {
  it('renders Yes for boolean true (Alice Mutton) and No for boolean false (Chai)', () => {
    expect(cellText('Discontinued', true)).toBe('Yes');
    expect(cellText('Discontinued', false)).toBe('No');
  });

  it.each([
    [true, 'Yes'],
    [false, 'No'],
    [1, 'No'],
    ['1', 'No'],
    ['true', 'No'],
    ['TRUE', 'No'],
    ['True', 'No'],
    [0, 'No'],
    ['0', 'No'],
    ['false', 'No'],
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

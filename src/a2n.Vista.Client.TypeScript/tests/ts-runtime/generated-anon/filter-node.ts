// filter-node.ts
// The presence-discriminated FilterNode filter tree (no discriminator property).
//
// A FilterNode value narrows to exactly one variant by which member is present:
// FilterLeaf (field + op), FilterAnd (and), FilterOr (or), FilterNot (not). The tree
// is recursive and self-contained, so this module needs no imports.

export type FilterOperator = "Equals" | "NotEquals" | "GreaterThan" | "GreaterThanOrEqual" | "LessThan" | "LessThanOrEqual" | "Contains" | "StartsWith" | "EndsWith" | "In" | "Between" | "IsNull";

export interface FilterLeaf {
  field: string;
  op: FilterOperator;
  value?: unknown | null;
}

export interface FilterAnd {
  and: FilterNode[];
}

export interface FilterOr {
  or: FilterNode[];
}

export interface FilterNot {
  not: FilterNode;
}

export type FilterNode = FilterLeaf | FilterAnd | FilterOr | FilterNot;

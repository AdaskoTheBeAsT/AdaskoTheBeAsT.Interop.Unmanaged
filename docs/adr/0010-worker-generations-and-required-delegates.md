# 0010. Issue worker generations and required typed delegates

**Status:** Accepted in 5.1.0

## Context

Callers leasing worker slots independently track generations for staged native modules. Required delegates also need duplicate checks when the existing typed lookup returns null.

## Decision

Issue a generation with each successful lease, independently per slot and allocator. Use the existing 0–1295 range for isolated module names. Add a required counterpart to nullable typed export lookup, using the existing delegate marshaling path.

## Consequences

An allocator should be scoped to its isolation namespace. Generations wrap and do not replace the loader's collision checks or directory ownership leases. Callers still own native module lifetimes, and nullable typed lookup remains available.

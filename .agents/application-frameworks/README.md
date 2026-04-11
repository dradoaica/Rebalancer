# Application frameworks

**Framework-specific** coding conventions, patterns, and constraints (e.g., Angular, ASP.NET Core). Each subdirectory
names one framework or stack family.

## When to read

Before writing or refactoring **application code** that targets a listed framework, read that framework’s file(s) under
the matching subdirectory.

## Conventions

- One subdirectory per framework or major variant.
- Prefer small, scannable files: naming, layering, DI, error handling, testing expectations, and anti-patterns.
- If a framework subdirectory is empty, treat it as **not yet governed**; align with human-approved specs and propose
  content before assuming team standards.

## Authority

If `.agents/rules/` or an accepted decision record conflicts with a framework note, **rules and approved records win**;
surface the conflict to the human.

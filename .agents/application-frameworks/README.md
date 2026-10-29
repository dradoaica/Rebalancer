# Application frameworks

**Framework-specific** coding conventions, patterns, and constraints (e.g., Angular, ASP.NET Core). Each subdirectory
names one framework or stack family.

## When to read

Before writing or refactoring **application code** that targets a listed framework, read that framework’s files under
the matching subdirectory.

## Conventions

- One subdirectory per framework or major variant.
- Prefer small, scannable files: naming, layering, DI, error handling, testing expectations, and antipatterns.
- An empty subdirectory means **no written standard yet** and is **not applicable**: ignore it unless the task's
  code targets that framework. Then follow the approved specs and the existing code's conventions, note the gap once
  in your summary, and draft standards only when the human asks.

## Authority

If `.agents/rules/` or an accepted decision record conflicts with a framework note, **rules and approved records win**;
surface the conflict to the human.

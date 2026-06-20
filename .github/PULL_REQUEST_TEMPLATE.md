<!-- Thanks for contributing! Keep PRs focused; unrelated changes belong in separate PRs. -->

## Summary

<!-- What does this PR change, and why? -->

## Related issue

<!-- e.g. Closes #123 -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Refactor (no behavior change)
- [ ] Documentation
- [ ] Build/CI/tooling

## How was this tested?

<!-- Commands run, scenarios covered. e.g. dotnet build / dotnet test / example self-test -->

## Checklist

- [ ] Builds with no new warnings (`dotnet build src/a2n.Vista.slnx`)
- [ ] Tests pass (`dotnet test src/a2n.Vista.slnx`); added/updated tests where relevant
- [ ] Public APIs have XML docs
- [ ] Respects the package dependency rules (Core is EF/HTTP-free; EF and AspNetCore do not reference each other)
- [ ] Conventional Commit message(s)
- [ ] Updated `CHANGELOG.md` (Unreleased) if user-facing

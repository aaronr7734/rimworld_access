# Contributing to RimWorld Access

Thank you for your interest in contributing to RimWorld Access! This document provides guidelines for contributing to the project.

## Getting Started

### Prerequisites

- .NET Framework 4.7.2 SDK
- RimWorld 
- A text editor or IDE (Visual Studio, VS Code, etc.)

### Building the Mod

To build the mod for testing:

```bash
dotnet build
```

This will compile the mod and automatically copy the DLL and dependencies to your RimWorld Mods folder via a post-build script.

**Custom RimWorld Install Location:**

The project defaults to the standard Steam install path (`C:\Program Files (x86)\Steam\steamapps\common\RimWorld`). If your RimWorld is installed elsewhere:

1. Copy `GamePaths.props.template` to `GamePaths.props`
2. Edit `GamePaths.props` and set `RimWorldDir` to your install path

The `GamePaths.props` file is gitignored, so your local configuration won't affect the repository.

### Prism Native Libraries

The mod depends on [Prism](https://github.com/ethindp/prism) native libraries for screen reader integration. These are **not** checked into Git.

**First build:** Libraries are downloaded automatically when you run `dotnet build` for the first time.

**Updating to latest Prism:** Run this when you want to pull the latest Prism release:

```bash
dotnet msbuild -t:UpdatePrism
```

The CI pipeline always uses the latest Prism release, so your local copy only needs updating if you want to test against a newer version.

### Building a Release Package

To create a release package:

```bash
dotnet build -c Release
```

This will build the mod in Release configuration and package all necessary files (DLLs, readme, etc.) into the release folder.

## Contribution Workflow

### 1. Opening an Issue

**Before starting work on a new feature:**
- Open an issue describing the feature you want to implement
- Explain the motivation and expected behavior
- Please wait for feedback from maintainers before beginning implementation, we will try to get back to you as soon as is possible.
- This helps to ensure we avoid duplicate work.

**For bug fixes:**
- If an issue already exists for the bug, you can proceed directly to implementation
- If no issue exists, consider opening one to document the bug and your proposed fix

### 2. Making Changes

- Fork the repository
- Create a feature branch from `master` (`git checkout -b feature/your-feature-name`)
- Make your changes, following the project's coding style
- Test your changes thoroughly in-game
- Use **conventional commit style** for all commits:
  - `feat: Add keyboard navigation for research tree`
  - `fix: Resolve null reference in scanner state`
  - `docs: Update architecture section in CLAUDE.md`
  - `refactor: Simplify menu navigation logic`
  - `test: Add unit tests for TolkHelper`
  - `chore: Update dependencies`

### 3. Creating a Pull Request

- Push your branch to your fork
- Open a pull request against the `master` branch
- **Link your PR to the related issue** using GitHub keywords:
  - `Closes #123` (for features)
  - `Fixes #123` (for bug fixes)
- Provide a clear description of your changes
- Include testing notes (how you tested the feature/fix)

## Commit Message Guidelines

We use [Conventional Commits](https://www.conventionalcommits.org/) format:

```
<type>: <description>

[optional body]

[optional footer]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `refactor`: Code refactoring (no functional changes)
- `test`: Adding or updating tests
- `chore`: Maintenance tasks, dependency updates

**Examples:**

```
feat: Add Alt+M shortcut to display pawn mood information

Implements a new keyboard shortcut that announces the selected pawn's
current mood level and active thoughts affecting mood.
```

```
fix: Prevent crash when scanner encounters null items

Adds null checks in ScannerHelper.CollectItems() to handle edge cases
where map objects may be destroyed between collection and navigation.
```

## Code Style Guidelines

- Follow existing patterns in the codebase
- Use meaningful variable and method names
- Use RimWorld's native systems when possible (don't reinvent the wheel)

### Architecture Patterns

The mod follows a **State + Patch** pattern:
- **State classes** (`*State.cs`) maintain navigation state and handle input
- **Patch classes** (`*Patch.cs`) use Harmony to intercept RimWorld methods
- **Helper classes** (`*Helper.cs`) provide reusable utility functions

See `CLAUDE.md` for detailed architecture documentation.

## Testing

- Test your changes in-game before submitting
- Verify screen reader announcements work correctly (use NVDA or JAWS)
- Test keyboard navigation flows end-to-end
- Check for conflicts with existing features

## Questions?

If you have questions about contributing, feel free to:
- Open a discussion in the GitHub Discussions tab
- Ask in your issue before starting work
- Reach out to maintainers in your pull request or on [discord](https://discord.gg/XdxfyvSKaT).

Thank you for helping make RimWorld more accessible!

# Building ServiceKiller

## Requirements

- Windows
- .NET Framework 4.8
- Either:
  - Visual Studio with .NET Framework desktop development tools, or
  - the C# compiler installed with .NET Framework

No NuGet packages or bundled third-party binaries are required by the current source tree.

## Portable build script

From the repository root:

```bat
BUILD_RELEASE.bat
```

Output:

```text
artifacts\ServiceKiller.exe
artifacts\ServiceKiller.exe.config
```

The script also prints the SHA-256 of `ServiceKiller.exe` and does not execute it automatically.

To explicitly run the freshly compiled executable:

```bat
BUILD_RELEASE.bat --run
```

## Visual Studio / MSBuild

Open:

```text
src\ServiceKiller\ServiceKillerV1.sln
```

Build the `Release | Any CPU` configuration.

## Reproducibility statement

The project documents the exact source and build inputs needed to rebuild the application, but **does not currently claim bit-for-bit reproducible builds across different Windows/Visual Studio toolchains**. Compiler version and PE metadata can affect the final binary hash.

Official releases should therefore publish:

1. the Git tag/commit;
2. the release binary;
3. the SHA-256 of that exact binary;
4. the build environment/toolchain used.

See [docs/RELEASE-CHECKLIST.md](docs/RELEASE-CHECKLIST.md).

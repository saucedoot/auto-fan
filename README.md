# AUTO Fan

A Windows app that measures how this PC’s fans actually change temperatures, then builds a quiet-versus-cool policy instead of asking you to draw fan curves by hand.

This is unfinished. It is not validated until a real machine shows a repeatable BIOS vs AUTO comparison on the same locked heat.

- Product: [App.md](App.md)
- Current slice: [IMPLEMENTATION.md](IMPLEMENTATION.md) (v1.15)
- Needs Administrator + LibreHardwareMonitor’s PawnIO driver for real sensors

```text
dotnet restore
dotnet test
dotnet run --project src/AutoFan.App
```

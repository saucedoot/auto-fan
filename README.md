# AUTO Fan

A Windows app that measures how this PC’s fans actually change temperatures, then builds editable multi-point temperature → duty fan curves from that data. Like FanControl or similar editors, but the first draft is automatic. A Quiet ↔ Cool slider still helps set the first balance; it is secondary once the curves are on screen.

This is unfinished. It is not validated until a real machine shows a repeatable BIOS vs AUTO comparison on the same locked heat.

- Product: [App.md](App.md)
- Current slice: [IMPLEMENTATION.md](IMPLEMENTATION.md) (v1.15)
- Needs Administrator + LibreHardwareMonitor’s PawnIO driver for real sensors

```text
dotnet restore
dotnet test
dotnet run --project src/AutoFan.App
```

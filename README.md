# Incantation

An agentic assistant that runs natively on Windows XP, powered by [GitHub Copilot SDK](https://github.com/github/copilot-sdk). Features classic Microsoft Agent integration -- Merlin the wizard provides visual feedback while the AI thinks, reads, writes, and searches.

This project is also a fun test of vibe-coding legacy software. 😅

![Windows XP](https://img.shields.io/badge/Windows-XP-blue?logo=windows-xp) ![.NET 2.0](https://img.shields.io/badge/.NET_Framework-2.0-purple)

> [!IMPORTANT]
> This is a just-for-fun experiment. Windows XP has been unsupported for 12 years. Fine to tinker with, but understand the risks and don't use it for anything serious.

## Architecture

Incantation works around Windows XP's TLS 1.0 limitation by splitting into three components that communicate over HTTP:

```
┌─────────────────────┐         ┌──────────────────────┐         ┌─────────────┐
│   Incantation       │  SSE    │   Incantation.Proxy  │         │  GitHub     │
│   (WinForms on XP)  │◄──────►│   (ASP.NET Core)     │◄───────►│  Copilot    │
│                     │  HTTP   │   on Windows 11      │  SDK    │             │
└────────┬────────────┘         └──────────────────────┘         └─────────────┘
         │ HTTP
         ▼
┌─────────────────────┐
│ Incantation         │
│ .ToolServer         │
│ (on XP, .NET 2.0)   │
└─────────────────────┘
```

- **Incantation** -- WinForms chat UI targeting .NET Framework 2.0. Streams responses via SSE, renders markdown with rich text formatting, and drives Merlin animations in response to AI activity.
- **Incantation.Proxy** -- ASP.NET Core app (.NET 10) running on a modern host. Bridges the XP client to GitHub Copilot using the [Copilot SDK](https://www.nuget.org/packages/GitHub.Copilot.SDK), manages sessions, and registers XP-native tools so the AI can interact with the XP filesystem and shell.
- **Incantation.ToolServer** -- Lightweight HTTP server (.NET 2.0) running alongside the client on XP. Exposes file read/write, directory listing, and command execution endpoints that the AI calls through the proxy.

## Prerequisites

- A **Windows XP** machine (or VM) with .NET Framework 2.0 and Microsoft Agent installed
- Any modern machine with the .NET 10 SDK to run the proxy
- [GitHub Copilot](https://github.com/features/copilot) subscription and [GitHub Copilot CLI](https://github.com/features/copilot/cli/) authenticated
- Network connectivity between the two machines

## Building

The XP-targeting projects use a Visual Studio 2005 solution and must be built with the .NET 2.0 MSBuild:

```
MSBuild.exe Incantation.sln /p:Configuration=Debug
```

The .NET 2.0 projects bundle `Newtonsoft.Json.dll` in their `Lib/` folders because NuGet restore isn't available on this target. These are checked into the repo intentionally.

The proxy is a standard .NET 10 project:

```
cd Incantation.Proxy
dotnet build
```

## Running

1. Start the proxy on the modern machine:

   ```
   cd Incantation.Proxy
   dotnet run
   ```

2. Start the tool server on XP:

   ```
   Incantation.ToolServer.exe 8888
   ```

3. Launch `Incantation.exe` on XP. Configure the proxy address (defaults to the host machine on port 5000).

## Disclaimer

This project is not affiliated with, endorsed by, or sponsored by Microsoft or GitHub. Microsoft Agent, Merlin, Windows XP, and GitHub Copilot are trademarks of their respective owners.

## License

This project is licensed under the [MIT License](LICENSE).

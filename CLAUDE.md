# Environment Setup

## Remote XP VM
This project targets Windows XP. To execute commands on the XP VM, use SSH:
```
ssh xp-vm "<command>"
```

## Path Mapping
Files are edited locally at the current project directory on Win11.
The XP VM accesses the same files via VMware shared folder:
- UNC path (Cygwin): `//vmware-host/Shared Folders/incantation/`
- Note: The Z: drive mapping is unreliable. Prefer UNC paths.

Files are synced to a local working directory on the XP VM at `C:\Projects` (`/cygdrive/c/Projects/`).

## Syncing
Use rsync to sync the project from the shared folder to the local working directory:
```bash
ssh xp-vm 'rsync -av "//vmware-host/Shared Folders/incantation/" /cygdrive/c/Projects/'
```
This is incremental — only changed files are copied.

## Building
The project uses a Visual Studio 2005 compatible solution (`Incantation.sln`). Build with MSBuild:
```bash
ssh xp-vm 'sln=$(cygpath -w /cygdrive/c/Projects/Incantation.sln); /cygdrive/c/Windows/Microsoft.NET/Framework/v2.0.50727/MSBuild.exe "$sln" /p:Configuration=Debug'
```

To build a single project:
```bash
ssh xp-vm 'proj=$(cygpath -w /cygdrive/c/Projects/HelloXP/HelloXP.csproj); /cygdrive/c/Windows/Microsoft.NET/Framework/v2.0.50727/MSBuild.exe "$proj" /p:Configuration=Debug'
```

For standalone .cs files without a project, use CSC directly:
```bash
ssh xp-vm 'src=$(cygpath -w /cygdrive/c/Projects/Program.cs); out=$(cygpath -w /cygdrive/c/Projects/Program.exe); /cygdrive/c/Windows/Microsoft.NET/Framework/v2.0.50727/csc.exe "/out:$out" "$src"'
```

All compilers require Windows-style paths — use `cygpath -w` to convert.

## Running
```bash
ssh xp-vm '/cygdrive/c/Projects/HelloXP/bin/Debug/HelloXP.exe'
```

## Workflow
1. Edit and create files locally on Win11 in the project directory (Claude Code handles this)
2. Sync to XP VM: `ssh xp-vm 'rsync -av "//vmware-host/Shared Folders/incantation/" /cygdrive/c/Projects/'`
3. Build via MSBuild: `ssh xp-vm 'sln=$(cygpath -w /cygdrive/c/Projects/Incantation.sln); /cygdrive/c/Windows/Microsoft.NET/Framework/v2.0.50727/MSBuild.exe "$sln" /p:Configuration=Debug'`
4. Run and test via `ssh xp-vm` to execute on genuine Windows XP
5. All output from compilation and execution will come back through the SSH session
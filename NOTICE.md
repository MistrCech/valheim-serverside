# Notice

## What the MIT licence covers

[`LICENSE`](LICENSE) (MIT) covers the work of MistrCech / Sarkastic.gg in this repository: every file that is not
named below, and the changes MistrCech / Sarkastic.gg made to the files named below.

## What it does not cover

### Serverside Simulations

This repository is a fork of [Serverside Simulations](https://github.com/ddormer/valheim-serverside) by Darren
Dormer (ddormer) and its contributors. That project has no licence (checked 12 October 2026), and we have asked
its author for one ([ddormer/valheim-serverside#121](https://github.com/ddormer/valheim-serverside/issues/121)).
What still comes from it is not licensed by us; it stays under its authors' rights. These files carry upstream
content (`git blame` shows which lines):

- `src/Valheim_Serverside/Features/Core.cs`: partly, about 320 lines
- `src/Valheim_Serverside/Features/Compat_ValheimPlus.cs`: mostly (contributed upstream by jsza)
- `src/Valheim_Serverside/ServersidePlugin.cs`, `Configuration.cs`, `Features/Debugging.cs`,
  `Properties/AssemblyInfo.cs`, `Serverside_Simulations.csproj`: partly
- `src/Valheim_Serverside/Features/MaxObjectsPerFrame.cs`, `FeaturesLib.cs`, `Patching.cs`, `Requirements.cs`,
  `Utils.cs`: whole files
- `src/TestValheim_Serverside/`: whole files
- `README.md` and `CHANGELOG.md`: partly (the changelog entries up to 1.1.9 are upstream's)
- `README.bbcode`, `.changes/`, `.github/workflows/`, `.editorconfig`, `.gitignore`, `.depot-downloader-file-list.txt`,
  `Valheim_Serverside.sln`, `bin/`, `cacofonix.yaml`, `example_post_build.bat`, `ss-gh.png`: upstream's build and
  release files

### Third-party components

`lib/BepInEx/` (BepInEx, HarmonyX, MonoMod, Mono.Cecil) and `contrib/AssemblyPublicizer/` are third-party and keep
their own licences; the texts for the AssemblyPublicizer and its dependencies are in
`contrib/AssemblyPublicizer/Licenses/`.

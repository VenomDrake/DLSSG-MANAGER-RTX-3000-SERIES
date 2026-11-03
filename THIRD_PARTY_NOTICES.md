# Third-party components

DLSSG Mod Manager is an independent application. It is not affiliated with or endorsed by NVIDIA, Intel, AMD, Valve, Microsoft, or the authors of DLSSG for SM86.

The manager downloads complete release assets directly from [sdli1995/dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) at runtime. It does not redistribute those DLLs, models or kernels in its source, portable download, or installer. Downloaded packages retain their upstream `LICENSE.md` and `THIRD_PARTY_NOTICES.txt` in the verified cache. The manager never executes an installation script from a downloaded repository.

The manager's MIT license applies to the manager's original code only. Consult the downloaded package's notices for its NVIDIA, Intel and other third-party material. Upstream documentation and package license statements are not fully consistent in release 0.4.1; this project does not resolve or relicense that material.

The self-contained executable contains the Microsoft .NET runtime and Windows Desktop libraries, distributed under their respective .NET Foundation/Microsoft licenses. `DOTNET-RUNTIME-LICENSE.txt`, `WPF-LICENSE.txt` and `DOTNET-THIRD-PARTY-NOTICES.txt` accompany the binaries; source copies are in `docs`. Build tooling uses the WiX Toolset 4.0.6; it is not part of the running application. Review these notice copies when updating the bundled runtime.

Product names and trademarks belong to their respective owners.

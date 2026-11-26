# Third-party notices

Checked on 26 Nov 2026 against the package metadata on api.nuget.org and the upstream license pages named below.

## Included license texts
- xterm.js (MIT): `MibExplorer.Android/Resources/Raw/terminal/vendor/xterm.LICENSE`
- xterm addon-fit (MIT): `MibExplorer.Android/Resources/Raw/terminal/vendor/addon-fit.LICENSE`

## NuGet packages used by the Android app (license as declared in each package's .nuspec; license texts in `LICENSES/`)
| Package | Version | License | Text |
|---|---|---|---|
| SSH.NET | 2026.0.0 | MIT | `LICENSES/SSH.NET-MIT.txt` (from the SSH.NET repository LICENSE, master branch) |
| Microsoft.Maui.Controls | 10.0.110 | MIT | `LICENSES/Microsoft.Maui.Controls-LICENSE.txt` (LICENSE.TXT from the package) |
| Microsoft.Extensions.Logging.Debug | 10.0.0 | MIT | `LICENSES/Microsoft.Extensions.Logging.Debug-dotnet-runtime-LICENSE.txt` (LICENSE.TXT of the dotnet/runtime repository, main branch; the package ships no license file) |

Texts from repository master branches are the current ones; they were not matched to the exact package version tag.

## Fonts and images
- `MibExplorer.Android/Resources/Fonts/OpenSans-Regular.ttf` and `OpenSans-Semibold.ttf`: Open Sans. Google Fonts publishes Open Sans under the SIL Open Font License 1.1 (Copyright 2020 The Open Sans Project Authors). The OFL text with that copyright line is in `LICENSES/OpenSans-OFL-1.1.txt` (from github.com/google/fonts, ofl/opensans/OFL.txt). The font files carry no embedded license text, so it is not verified that these exact files are that release; if they came from an older Open Sans release the applicable license may differ.
- `MibExplorer.Android/Resources/Images/dotnet_bot.png`: .NET MAUI project template image, not used by any screen. Its license was not verified.
- Other images and icons in `Resources/`: origin and license not documented in this repository.

## Binary payloads
See "Items with unresolved redistribution rights" in [NOTICE.md](NOTICE.md).

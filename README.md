# Pawchive Downloader

Windows desktop downloader for [Pawchive](https://pawchive.pw/) with download history, incremental downloads, link saving, and automatic archive extraction.

Pawchive Downloader helps you keep a local library of posts from selected creators. Add creator URLs, check their posts, and download the available files when you choose. The saved catalog and download history make later runs faster and let you pick up missing files without downloading completed ones again.

**Current version: 1.8 · Windows x64 · English and Russian interface**

This is an independent desktop application and is not affiliated with Pawchive or the platforms whose posts appear there.

## Features

- **Manual operation.** Checking for posts and downloading files start only when you click a button. There are no scheduled checks or background downloads.
- **Multiple creators.** Add Pawchive creator URLs, select which creators to process, and keep their settings between sessions. Patreon and Fanbox creator URLs are supported; other service paths depend on what Pawchive exposes through the same API.
- **Persistent post catalogs.** Previously discovered posts are loaded from disk on startup. You do not need to scan the website again just to see or download known posts.
- **Quick checks and full scans.** Check for new posts uses the saved catalog to avoid rescanning unchanged older pages. Full scan traverses the creator's listing again when you want to refresh older posts too.
- **Incremental downloads.** Download history tracks completed files. Missing files and failed transfers can be retried while completed files are kept.
- **Two simultaneous file transfers.** The limit applies to media downloads, including the time spent reading each response body. Per-file progress shows transferred bytes and, when available, a percentage.
- **Single-post downloads.** Paste a specific post URL without scanning an entire creator first.
- **Original media and optional previews.** Available original files are downloaded from the media locations supplied by Pawchive. Image previews are optional, disabled by default, and stored separately from originals.
- **Automatic archive extraction.** Downloaded archives are extracted into a separate folder, and the original archive is retained. Extraction failures are recorded so extraction can be retried later.
- **Post text and links.** A single readable `post.txt` contains errors first, then skipped files, links, and post information/content. `post.json` preserves the raw post metadata separately.
- **Open post folders.** Open the local folder of a fully or partially downloaded post directly from the app. Double-click a post in the table to open its web page instead.
- **Recheck downloaded posts.** An optional setting refreshes post details and checks for files that became available later, without downloading already completed files again.
- **Interruption handling.** Stopping a download keeps completed files and removes the incomplete `.part` files created by that attempt. An interrupted post is not marked complete.
- **Modern desktop interface.** Light, dark, or Windows-following theme; system or custom accent color; file-type icons; themed scrollbars; fixed table row heights; filtering and pagination.
- **English and Russian.** Switch the interface language in Settings without restarting. Creator names, post titles, filenames, URLs, and original post content are preserved.

## Requirements

### To run a published application

- Windows 10 or Windows 11, x64.
- A writable application folder for `settings.json` and a writable library folder with sufficient free space for downloads and extracted archives.
- Internet access to Pawchive and its media servers for checking posts and downloading files.

A self-contained build produced with the command below includes the .NET runtime, so users do not need to install it separately. This repository contains source code; running the source requires the SDK described below.

### To work with the source

- Windows x64. The interface uses Windows Forms and runs on Windows.
- **.NET SDK 10.0** with Windows Desktop support. A runtime alone is not enough to build the project.
- Git to clone the repository, or download and extract a source ZIP from GitHub.
- NuGet access for the initial package restore. `NuGet.Config` points to nuget.org.
- An editor of your choice. Visual Studio is optional; if you use it, install a version that supports .NET 10 and the .NET desktop development workload.

The application targets `net10.0-windows` and uses **SharpCompress 0.50.8** for archive extraction. Its license notice is included in [SharpCompress-LICENSE.txt](SharpCompress-LICENSE.txt). No separate archive utility or external database is required.

## Build and run from source

Clone the repository and open a terminal in its root directory:

```powershell
git clone https://github.com/NimaxLight/Pawchive-Downloader.git
cd Pawchive-Downloader
```

Check your installed SDK, restore dependencies, and build:

```powershell
dotnet --version
dotnet restore Pawchive/Pawchive.csproj
dotnet build Pawchive/Pawchive.csproj -c Release --no-restore
```

Run the application:

```powershell
dotnet run --project Pawchive/Pawchive.csproj -c Release
```

During development, `settings.json` and the default `Downloads` folder are created next to the built application, inside the project's output directory. They are excluded from version control.

### Publish a portable Windows x64 build

Run this command from the repository root:

```powershell
dotnet publish Pawchive/Pawchive.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o publish
```

The resulting `publish/PawchiveDownloader.exe` includes the runtime and embedded icon and translations. For redistribution, include `LICENSE` and `SharpCompress-LICENSE.txt` alongside the executable:

```powershell
Copy-Item LICENSE, SharpCompress-LICENSE.txt -Destination publish
```

Use a writable location such as a folder under your user profile. The app stores settings beside its executable, so a protected installation directory can prevent settings from being saved.

## Getting started

1. Open **Settings** and choose your downloads/history folder. If needed, change **Interface language** to English, choose a theme and accent color, then save. The default language is Russian.
2. Use the **+** button in the creators panel to add a Pawchive creator URL, for example `https://pawchive.pw/patreon/user/12345`. The app includes a small initial creator list; you can remove or replace those entries.
3. Select the creators to process and click **Check for new posts**. The first check builds their catalogs; later checks reuse the saved information.
4. Click **Download missing** to save the available files from known posts for the selected creators.
5. On later visits, click **Check for new posts**, then **Download missing** again.

For a single post, click **Post by URL** and enter a URL such as `https://pawchive.pw/patreon/user/12345/post/67890`. The numeric IDs in these examples are placeholders.

Use **Full scan** if you want to revisit the complete listing, including older posts. Quick checks prioritize new posts and may stop before reaching changes deep in an older listing.

### Previews and files that become available later

Enable **Download image previews** in Settings if you also want preview images. They go into a `previews` subfolder and do not replace missing originals.

Enable **Recheck downloaded posts** to revisit posts already shown as downloaded. The download action becomes **Recheck and download**: it refreshes post details, downloads newly available files, and keeps files already recorded as complete and present on disk. This is a manual action and does not create a background task.

### Stopping and retrying

Click **Stop** and allow the current operation to stop. Completed files remain on disk and in the history. Incomplete temporary downloads are removed, so a later attempt restarts those files rather than resuming their byte ranges.

If one file fails, the app can continue processing the other files and extract archives that were downloaded successfully. Check the post's status and `post.txt` for the details, then run the download action again to retry missing files or extraction.

## Where files and history are stored

Settings and library metadata have different locations:

| Data | Location |
| --- | --- |
| Interface settings, creator list, selected library path | `settings.json` next to the executable |
| Default library | `Downloads` next to the executable |
| Download history and extraction records | `<library>/.pawchive/history.json` |
| Saved creator catalogs | `<library>/.pawchive/catalog/<creator-key-hash>.json` |
| Downloaded files, reports, and extracted archives | Creator and post subfolders inside the library |

A typical library looks like this:

```text
Downloads/
  .pawchive/
    history.json
    catalog/
      <creator-key-hash>.json
  <creator>_<service>_<id>/
    <date>_<post-id>_<title>/
      <downloaded files>
      <archive-name>_unpacked/
      previews/
      post.txt
      post.json
```

The `previews` and extraction folders appear only when applicable. JSON metadata writes also keep `.bak` copies where configured. The `.pawchive` directory may be hidden by your file manager; the catalogs are stored in the selected library, rather than as a single `catalog.json` next to the executable.

Back up the entire library, including `.pawchive`, to preserve the relationship between files, post history, and catalogs. Back up `settings.json` separately. When updating the executable, preserve both your settings file and library. If you move the library, select its new location in Settings; changing the selected path does not move existing downloads for you.

Ordinary completion checks use history records and file sizes rather than hashing every local file on every scan. A file changed externally without changing its size may therefore still be treated as present.

## Availability and limitations

- Downloads depend on what Pawchive and its media servers currently expose. Deleted, unavailable, incomplete, or inaccessible originals cannot be recovered by the app.
- A **Downloaded** status means the app completed the files it could identify as available for that post and the enabled options. Skipped or unavailable originals can still be listed in `post.txt`. Use rechecking if their availability changes later.
- Links in post content are saved as information. The app does not automatically download arbitrary external link targets or embedded third-party videos.
- Archive recognition covers ZIP, RAR, 7z, TAR, GZ/GZIP, BZ2, XZ, TGZ, TBZ2, and TXZ. Actual extraction depends on the archive format, compression method, and integrity supported by SharpCompress. Password-protected archives, archives with missing volumes, or unsupported entries may require manual extraction; the downloaded original remains available.
- Post reports use the interface language selected when they are written. Changing the interface language does not automatically rewrite existing reports.
- API or CDN changes can affect discovery and downloads. This app is not a browser and does not implement a login workflow for Patreon, Fanbox, or other original platforms.

## Tests and interface preview

The projects below are executable test harnesses, so run them with `dotnet run` rather than `dotnet test`. Their default runs use local fixtures and simulated HTTP responses without requesting Pawchive:

```powershell
dotnet run --project Tests/Tests.csproj -c Release
dotnet run --project GridTests/GridTests.csproj -c Release
dotnet run --project AppearanceTests/AppearanceTests.csproj -c Release
```

| Project | Purpose |
| --- | --- |
| `Tests` | API parsing, catalogs, incremental downloads, transfer limits, cancellation, reports, preview handling, and archive extraction |
| `GridTests` | Accessibility navigation and performance of large tables in both interface languages |
| `AppearanceTests` | Layout, settings behavior, control styling, language switching, and translation coverage |
| `UiHarness` | A separate sample interface with simulated downloads and a large sample post list |

Preview the interface using sample data:

```powershell
dotnet run --project UiHarness/UiHarness.csproj -c Release
```

The UI harness writes its own `ui-settings-1.8.json` and `Downloads-1.8` library next to its executable. It does not use the production application's settings or library. Some backend test entry points also support explicitly requested live probes; those are not part of the default offline test run.

## Source layout

| Path | Responsibility |
| --- | --- |
| `Pawchive/Pawchive.csproj` | Windows Forms application, package reference, version, and embedded resources |
| `Pawchive/Program.cs` | Application entry point, operations, filtering, pagination, progress, and history integration |
| `Pawchive/Workspace.cs` | Workspace layout, creator/post dialogs, settings, and language switching |
| `Pawchive/Appearance.cs` | Theme colors, system accent integration, custom controls, icons, and scrollbars |
| `Pawchive/Core.cs` | Models, API calls, media discovery, downloads, history, and post reports |
| `Pawchive/Catalog.cs` | Persistent catalogs and incremental post discovery |
| `Pawchive/Archives.cs` | Archive extraction, output validation, and extraction records |
| `Pawchive/FastGrid.cs` | Efficient accessibility providers for large post tables |
| `Pawchive/Locale.cs` | Message translation and formatting |
| `Pawchive/UiLanguage.cs` | Reversible translation bindings for existing controls |
| `Pawchive/Locales/en.json` | English interface and message translations |
| `Pawchive/Assets/` | Embedded paw icon in PNG and multi-size ICO formats |

Russian source strings are retained as canonical keys. Internal post states and file identities remain stable across language changes. When adding interface text, add its English translation and include it in the existing translation bindings as needed.

Build outputs, downloaded media, local settings, catalog/history files, temporary downloads, and generated test libraries are excluded by `.gitignore`.

## Reporting issues and contributing

For a bug report, include the app version, Windows version, steps to reproduce, the visible status/error, and a relevant post URL when appropriate. Remove personal filesystem paths and private or signed links before sharing settings or report excerpts. Do not attach downloaded media or your complete library/history unless it is specifically needed and you intend to share it.

For code changes, run the relevant test harnesses and check the interface in both languages and themes when changing visual controls.

## License

This project is licensed under the [MIT License](LICENSE).

Copyright (c) 2026 [NimaxLight](https://github.com/NimaxLight).

SharpCompress is distributed under its own MIT notice, retained in [SharpCompress-LICENSE.txt](SharpCompress-LICENSE.txt). The software license covers this application, not the content downloaded from external services.

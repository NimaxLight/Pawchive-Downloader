Pawchive Downloader 1.9.3 — Windows x64

FIXED IN 1.9.3
Fully fetched text-only posts no longer fail because has_full is false.
Unavailable media is still treated separately from complete text-only posts.
Columns cannot be resized manually. Metadata columns fit both languages;
creator and title columns share remaining space, with most reserved for titles.
Hover over a creator name or post title to see the full text.


Windows desktop application for saving available files from Pawchive posts.
Interface languages: English and Russian. Downloads start only by button.

START AND UPDATE
Extract the entire ZIP into a writable folder and run PawchiveDownloader.exe.
The .NET runtime is included; no separate installation is required.
A clean first launch has no creators. The release includes no settings,
catalogs, history, downloaded media, or test data.

To update, close the application and replace the EXE. Keep settings.json and
your complete downloads library, including its hidden .pawchive directory.
You can continue using an existing 1.8 or 1.9 library without a full scan.
Extract into a new folder if you want a clean start instead.

LANGUAGE, APPEARANCE AND FOLDER
Open Настройки / Settings. Choose English or Русский under Язык интерфейса /
Interface language, then save. No restart is needed.
Choose a light, dark, or Windows-following theme and a system or custom accent.
Select your downloads/history folder. Selecting a new folder does not move
existing downloads; move the complete library yourself if needed.

CREATORS AND DOWNLOAD ORDER
Use + to add a Pawchive creator page URL. Creators are split into Active and
Inactive. Check a creator to add them to the end of the active queue.
Unchecking moves them to Inactive and hides their posts without deleting data.
Change active order with the arrow buttons, Alt+Up/Down, or the dotted drag
handle. The order is saved immediately and the post table follows it.
The expand button beside + opens an embedded creator manager with a dimmed
backdrop. It has the same groups and updates the main list immediately.
Close it with Done, the close button, Escape, or a click outside the panel.
Queue editing is locked during operations. A creator is highlighted only
while their posts are being downloaded; the highlight follows the real queue.
Removing a creator from the list keeps their downloaded files and history.

CHECKING AND DOWNLOADING
1. Select active creators, then click Check for new posts. The first check
   builds their catalogs; subsequent checks reuse the saved information.
2. Click Download missing. Active creators are processed in your chosen order;
   within each creator, newer posts come first. Completed files are kept.
3. On a later visit, check for new posts and download missing files again.
Full scan revisits the complete listing, including older posts.
The status and creator filters affect only the table. Downloads still process
all active creators. Post by URL downloads a single specific post.
Post folder opens the selected fully or partially downloaded post directory.
Double-click a table row to open its web page. History shows earlier downloads.

SKIP, STOP AND RETRY
Skip post, beside Stop, cancels the current post and continues with the next.
Both current transfers are cancelled; completed files stay and unfinished
.part files are removed. A skipped post remains incomplete and is retried on
the next download run. Previously completed files are not downloaded again.
For a single-post download, Skip post ends that operation.
Stop cancels the entire queue. Wait for the operation to finish stopping.

FILES, PREVIEWS AND ARCHIVES
At most two media files download at once. Progress is shown separately.
Available original files are downloaded; unavailable originals cannot be
recovered. A failure on one file does not prevent other files or valid archives
from being processed. External links are saved as text, not downloaded.
Archives are extracted automatically; the original archive is retained.
Password-protected, incomplete, or unsupported archives may need manual work.

Optional settings, both disabled by default:
- Download image previews: saves previews separately in previews subfolders.
  A saved preview does not replace an unavailable original.
- Recheck downloaded posts: refreshes known post details and downloads files
  that became available later, while keeping completed files.

SAVED DATA
settings.json beside the EXE: preferences, creators, order and library path.
<library>/.pawchive/history.json: file and archive extraction history.
<library>/.pawchive/catalog/*.json: saved creator catalogs.
Each post folder contains media, post.json metadata and a combined post.txt:
errors first, then skipped files, links and post content. Reports use the
interface language selected when written; old reports are not auto-translated.
Back up the entire library, including .pawchive, and settings.json separately.

LICENSE AND SOURCE
MIT License — NimaxLight: https://github.com/NimaxLight
Source: https://github.com/NimaxLight/Pawchive-Downloader
SharpCompress's separate MIT notice is in SharpCompress-LICENSE.txt.

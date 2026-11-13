Pawchive Downloader 1.10 — Windows x64

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

EMBEDDED DIALOGS
Settings, history, creator/post URL entry, folder and color pickers, and
messages open inside the application on a dimmed backdrop. Use Escape or
the close button to cancel. Settings are applied only with Save.
In the color picker, use the spectrum and hue strip or enter #RRGGBB.
In the folder picker, double-click a folder, use Up, or type a path and
press Enter to open it. Select folder confirms the path and creates it
if it does not exist. Closing a nested picker returns to Settings.

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
2. Optionally set a publication period above the table or mark specific posts.
3. Click Download… and choose All unfinished posts, New posts only,
   Retry problem posts, or Selected posts. Review the total and click Start
   download. Completed files are kept; creators follow your chosen order,
   with newer publications first within each creator.
4. Later, check for new posts again and choose the desired download mode.
Full scan revisits the complete listing, including older publications.
Post by URL downloads one specific post without scanning an entire creator.
Post folder opens the selected fully or partially downloaded post directory.
Double-click a table row to open its web page. History shows earlier downloads.

NEW POSTS
The first complete scan is a baseline: existing posts are not marked New.
Later checks mark newly discovered post IDs, even with old publication dates.
The New badge is independent of download status and persists in the catalog
between sessions. Errors, a skipped post, or Stop do not clear it; successful
processing does. An edited known post is not automatically marked New.
Upgrading an existing library does not mark its entire catalog as new.
Use the New table filter or the New posts only download mode to see/use it.

ONE PUBLICATION PERIOD
The From and To fields above the table are shared by the table, Select all
matching, and every download mode. Both endpoint dates are inclusive. Leave
either field blank for an open boundary, or use Clear period to remove both.
Type dd.mm.yyyy (ISO yyyy-mm-dd is also accepted), then press Enter or leave
the field. Or use the adjacent calendar: click the year to choose a year,
then a month and day. Today and Clear date are available in the calendar.
Invalid or reversed dates must be corrected before starting a download.
The download panel displays the same period; Change period returns to the
fields above the table. There is no separate date range in the panel.

MARKING POSTS AND VIEW FILTERS
Use the checkboxes in the table to mark posts. Shift-click marks/unmarks a
range. Select all matching applies to all matching pages, not just the page
currently displayed; it uses the current creator/status/period filters.
Clear selection removes all marks. Paging and changing filters or the period
preserve marks; the counters show the total and how many are in the period.
Completed posts can also be marked for a manual recheck; saved files are kept.

Creator and status filters affect the table and Select all matching. They do
not restrict All unfinished posts, New posts only, or Retry problem posts.
Only active creators can enter any batch queue. Selected posts uses their
marked posts that are also inside the shared publication period.
Retry problem posts includes failed, partial, skipped or incomplete posts,
missing files, and posts that need archive extraction.

The download panel previews at most five posts and an And N more count.
Its total always represents the complete prepared queue. Start download
freezes membership and order. While downloading you can browse pages, change
view filters and the period, and adjust marks for a later run; these changes
do not alter the running queue. Creator queue editing stays locked.

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
- Recheck downloaded posts: the first download mode becomes All posts (recheck
  and download); it refreshes known details and saves files that became
  available later, keeping completed files. Selected posts can include
  completed posts without enabling this global setting.

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

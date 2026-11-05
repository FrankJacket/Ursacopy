# Ursacopy

Copies folders of a NAS onto an **external USB disk** plugged into it, incrementally: only new and changed files are written. Runs in Docker, with a web interface to define jobs, schedule them and follow the copy.

- **Readable copy**: the disk holds a plain mirror of your folders (open it in any file explorer), not a proprietary archive.
- **Incremental**: a file is copied when it is missing or its size or modification time differs. Files are written under a temporary name and renamed, so a cable pulled mid-copy never leaves a truncated file.
- **Safe against a missing disk**: a job only writes to a disk that carries its marker file (`.ursacopy-target`). If the USB disk is not mounted, the container sees an empty folder of the NAS system disk and the run refuses to write anything.
- **Safe against a missing source**: if the source folder is empty while the copy holds data (share not mounted), nothing is deleted.
- **Trash**: files deleted from the source, and previous versions of changed files, go to `.ursacopy-trash/<folder>/<date>` on the disk and are purged after N days (default 30). Set 0 to delete right away.
- **Folder picker**: browse the source and tick the folders to copy (e.g. `photos` and `docs/2024` out of `/source`); they keep their relative path on the disk. Nothing ticked = the whole source. A folder unticked later is removed from the disk at the next run (into the trash).
- **Triggers**: manual, every day at a time, or whenever the disk is connected.
- **Optional verification**: re-reads each copied file and compares its SHA-256.
- The sources are mounted read-only in the container: it cannot alter your data.

## Install (Docker Compose)

Edit the two bind mounts of `compose.yaml` (the NAS folder to copy, and where the NAS mounts the USB disk), then:

    docker compose up -d --build

Open `http://<nas>:5090`:

1. **New job**: name, source folder (e.g. `/source`) then **Choose folders...** to tick what to copy, mount point of the disk (`/target`), folder on the disk (e.g. `photos`), schedule.
2. **Initialise the disk** (once per disk). The dialog shows the size of the disk behind `/target`: check that it is your USB disk and not the NAS system disk. Several jobs can share one disk.
3. **Run now**, or wait for the schedule.

The web interface has no login: keep it on your LAN, or put it behind your NAS reverse proxy with authentication.

## Good to know

- **Disk format**: ext4 or Btrfs keeps permissions and any file name. exFAT / NTFS are readable on Windows and macOS too but reject some characters in names (`: * ? " < > |`): such files are reported as errors in the history, the rest is copied. FAT32 cannot hold files over 4 GB.
- **What is copied**: files and folders with their modification times. Symbolic links are skipped. Owners and permissions are not preserved (they would not exist on exFAT / NTFS anyway).
- **Not a bootable image**: this is a file copy. For full-machine images of Windows PCs use Ursavault.
- **Unplugging**: wait until the status is no longer "running", then use your NAS's "eject USB disk" function. Each copied file is flushed to the disk before it is renamed.
- **User**: the container runs as root so that it can read every share and write any disk format. To run unprivileged, set `user:` in `compose.yaml`.
- **Mount propagation** (`rslave`): lets the container notice a disk plugged in after it started. On NAS firmwares that do not support it, plug the disk first or restart the container after plugging.

## Layout

- `src/Ursacopy.Core`: the copy engine (no dependency on the web app).
- `src/Ursacopy.Server`: Blazor web interface (MudBlazor), SQLite catalog (Dapper), job runner and scheduler.
- `tests/Ursacopy.Tests`: engine tests (`dotnet test`).

Development: `dotnet run --project src/Ursacopy.Server` then open `http://localhost:5090` (set `AllowAnyTarget=true` to initialise a plain folder that is not a mount point).

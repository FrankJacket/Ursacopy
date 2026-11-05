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

## Source on Btrfs or ZFS, disk on NTFS or exFAT

- **Copy from a snapshot, not from the live volume.** Files that change during the copy (databases, VM disks) would be copied in an inconsistent state. Mount a read-only snapshot as the source instead: ZFS exposes them under `<dataset>/.zfs/snapshot/<name>`, a Btrfs snapshot is a read-only sub-volume you can bind-mount. Create the snapshot in a scheduled task of the NAS just before the job, or rotate a fixed snapshot name.
- **Prefer NTFS to exFAT.** NTFS has a journal; exFAT has none, so an unplugged cable leaves it "dirty" and may leave files with the right size but unwritten data. Ursacopy protects against that (see below), but NTFS is the safer disk. On Linux use the kernel `ntfs3` driver (kernel 5.15 and later; check with `cat /proc/filesystems`): `ntfs-3g` (FUSE) is much slower. Mount with `noatime`.
- **Cluster size.** exFAT often defaults to 128 KB clusters on a large disk: every small file then takes 128 KB. For many small files format NTFS with 4 KB clusters, or exFAT with 32 KB.
- **File names.** Tick "The disk is formatted NTFS or exFAT" in the job (default). Names that NTFS / exFAT cannot store (`: * ? " < > |`, trailing dot or space, `CON`, `NUL`..., over 255 characters) and names that differ only by case (`Photo.jpg` / `photo.jpg`, which Btrfs and ZFS allow) are not copied and are listed as errors in the history. Rename them in the source.
- **Dates.** exFAT stores local time: a daylight-saving change shifts every date by one hour. With the option ticked, a file whose date differs from the source by whole hours (and that has the same size) is not copied again.
- **Space.** A ZFS / Btrfs source with compression or deduplication takes more room on the disk. The job card warns when the source (uncompressed size seen by the last run) is larger than the disk, and a run stops with a clear message when the next file does not fit.
- **Not preserved**: permissions, ACLs, extended attributes, hard links (each link becomes a copy), reflinks, symbolic links (skipped).

## How the copy is made safe when the cable is pulled

Writing every file and flushing it to the disk one by one is very slow on USB. Ursacopy works in batches of about 200 files: the files are copied under a temporary name, then a small journal listing the batch is written to the disk (`.ursacopy-state/`), the files are renamed, and the whole disk is flushed once. If the run is interrupted during a batch, the next run finds the journal and copies the listed files again, even if they look complete. A run that is cancelled still gives their final names to the files that were completely copied.

## Good to know

- **FAT32** cannot hold files over 4 GB: do not use it.
- **Not a bootable image**: this is a file copy. For full-machine images of Windows PCs use Ursavault.
- **Unplugging**: wait until the status is no longer "running", then use your NAS's "eject USB disk" function. Each copied file is flushed to the disk before it is renamed.
- **User**: the container runs as root so that it can read every share and write any disk format. To run unprivileged, set `user:` in `compose.yaml`.
- **Mount propagation** (`rslave`): lets the container notice a disk plugged in after it started. On NAS firmwares that do not support it, plug the disk first or restart the container after plugging.

## Layout

- `src/Ursacopy.Core`: the copy engine (no dependency on the web app).
- `src/Ursacopy.Server`: Blazor web interface (MudBlazor), SQLite catalog (Dapper), job runner and scheduler.
- `tests/Ursacopy.Tests`: engine tests (`dotnet test`).

Development: `dotnet run --project src/Ursacopy.Server` then open `http://localhost:5090` (set `AllowAnyTarget=true` to initialise a plain folder that is not a mount point).

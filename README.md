# Sedhep

A private, offline-first family cookbook for Android, iOS and the desktop. Recipes are written and
kept on the device; they travel to other phones directly, over the local network, without an
account, a server or a subscription.

![The cookbook at home](docs/screenshots/home.png)

## What is in the app

**The cookbook.** A searchable list of recipes with covers, category filters and three orders
(recent, newest, A–Z). Search reads the local database and covers names, ingredients, steps, notes
and tags — it works with no connection at all.

**Recipes.** A name is the only required field. Everything else — photo, description, category,
times, servings, ingredients, ordered steps, notes, tags — is optional and can be filled in as the
recipe is used. Ingredients and steps can be added, reordered and removed one row at a time.

**Cooking mode.** One step at a time in large type, a tick-off ingredient list, a timer for steps
that carry a duration, and no way to edit anything by accident.

**Categories.** The nine from the plan are created on first run and can be renamed, added to and
deleted. Deleting a category never deletes a recipe: those recipes move to “No category”.

**Sharing between devices.** Phones on the same Wi-Fi find each other and exchange whole cookbooks
or single recipes. The receiving device is shown who is sending and how much is coming, and nothing
is transferred until the person holding it says yes. The connection is encrypted with a key both
devices derive from an ephemeral Diffie–Hellman exchange, so the recipes are not readable on the
way. No accounts, no cloud.

**Backups.** The whole cookbook can be written to a `.cookbook` file and opened again on another
device. That file is a zip holding `manifest.json` (with the format version), `recipes.json`, and
the photo files.

**Settings.** Light, dark or system theme, three text sizes that scale the whole app (cooking mode
included), the device name other phones see, and where the cookbook is stored.

| | | |
|---|---|---|
| ![Favorites](docs/screenshots/favorites.png) | ![Recipe](docs/screenshots/recipe.png) | ![Cooking mode](docs/screenshots/cooking.png) |
| ![Editor](docs/screenshots/editor.png) | ![Settings](docs/screenshots/settings.png) | ![Sharing](docs/screenshots/share.png) |

Dark theme:

![The cookbook in dark mode](docs/screenshots/home-dark.png)

## How it is put together

```
src/Sedhep.Domain          recipes, categories, sync metadata, sharing contracts
src/Sedhep.Infrastructure  SQLite persistence, photo storage, .cookbook files, LAN transfer
src/Sedhep                 Avalonia UI: view models, views, styles, navigation
platforms/                 Android, iOS, browser and desktop hosts
```

The UI depends on the domain interfaces only. The sharing transport knows nothing about recipes: it
moves an encrypted package and hands it to the cookbook file reader on the other side.

Data choices worth knowing:

- One SQLite connection, opened and migrated on first use, with WAL and foreign keys on.
- Saving a recipe writes the recipe, its ingredients, steps and photos in one transaction.
- Every recipe carries a revision number that is bumped on save, and deletions leave a tombstone,
  so two cookbooks can be compared without trusting either device's clock.
- Photos are stored as files next to the database and decoded one at a time, never all at once.

## Running it

```bash
# desktop (Windows, macOS, Linux)
dotnet run --project platforms/Sedhep.Desktop

# Android / iOS / browser hosts exist too and build with the matching workloads
dotnet build Sedhep.slnx
```

The cookbook lives in `%LOCALAPPDATA%\Sedhep` on Windows (the equivalent per-user data folder
elsewhere): `cookbook.db`, `photos/`, `settings.json`. Set `SEDHEP_DATA_DIR` to keep the cookbook
somewhere else — useful for a portable install or for running two "devices" on one machine.

## Deliberately not here yet

The plan's “could have” list: voice recipe entry, step photos, ingredient scaling, printable
cookbooks, optional cloud backup, and the field-level conflict review that would replace the
current “keep the newer copy and say so” rule during sharing.

There is no public feed, no likes, no accounts and no analytics — and there will not be.

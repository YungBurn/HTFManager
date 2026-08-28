# First source upload to YungBurn/HTFManager

Repository:

```text
https://github.com/YungBurn/HTFManager
```

The remote repository already has a `main` branch. Preserve that history instead of force-pushing over it.

## Recommended first upload from the existing project folder

Open PowerShell in the HTFManager source root.

First confirm the project still builds:

```powershell
dotnet build HTFManager.slnx
```

Then check whether the folder is already a Git repository:

```powershell
git status
```

If Git reports that this is **not** a repository, initialize it and attach the existing remote history:

```powershell
git init
git remote add origin https://github.com/YungBurn/HTFManager.git
git fetch origin
git checkout -B main origin/main
```

Because the existing remote currently contains the repository history (including its LICENSE), this starts the local `main` branch from the remote `main` commit while leaving unrelated local source files available to add.

Now inspect exactly what will be committed:

```powershell
git status --short
git add .
git status --short
```

Before committing, verify that the staged list does **not** contain `bin/`, `obj/`, game files, BepInEx/MelonLoader runtime binaries, downloaded Mod packages, local cache or `%LOCALAPPDATA%` data.

Commit and push:

```powershell
git commit -m "Add HTF Manager v0.3.5.1 source baseline"
git push -u origin main
```

After GitHub receives the commit, open the Actions tab and confirm the `Build` workflow succeeds.

## If the local folder is already a Git repository

Do not run `git init` again. Check remotes:

```powershell
git remote -v
```

If `origin` is missing:

```powershell
git remote add origin https://github.com/YungBurn/HTFManager.git
```

Then fetch and inspect history before pushing:

```powershell
git fetch origin
git status
git log --oneline --decorate --graph --all -n 20
```

If the local history was created independently from the GitHub repository, merge the remote history rather than force-pushing:

```powershell
git branch -M main
git merge origin/main --allow-unrelated-histories
```

Resolve any real file conflicts, build again, commit the merge if necessary, and then:

```powershell
git push -u origin main
```

Avoid `git push --force` for the initial source upload.

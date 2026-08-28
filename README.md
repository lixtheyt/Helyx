<div align="center">

<img src="Helyx/assets/banner.png" alt="Helyx" width="800">

---

**A terminal app for Windows that keeps every project you have in one list, and lets you commit, branch, resolve conflicts, repair broken repositories and answer GitHub issues and pull requests without opening anything else.**

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6.svg)](#run-it-locally)
[![Languages](https://img.shields.io/badge/languages-8-green.svg)](#helyx-speaks-your-language)<!-- HELYX_STATUS_START -->
![Status](https://img.shields.io/badge/status-Active-008000)
<!-- HELYX_STATUS_END -->

![Helyx](Helyx/assets/hero.gif)

<a href="https://github.com/lixtheyt/Helyx/releases/latest">
  <img src="https://img.shields.io/badge/DOWNLOAD%20FOR%20WINDOWS-0078D6?style=for-the-badge&logo=windows&logoColor=white" width="440" alt="Download for Windows">
</a>

***For the best experience, please use Windows Terminal. (not cmd.exe or powershell.exe)***

</div>

## Table of contents

- [Quick start](#quick-start)
- [Features](#features)
- [Projects list](#projects-list)
- [Git features](#git-features)
- [Diagnostics](#diagnostics)
- [Managing GitHub repository](#managing-github-repository)
    - [Issues](#issues)
    - [Pull requests](#pull-requests)
    - [Repository statistics](#repository-statistics)
    - [Workflow runs](#workflow-runs)
    - [Status in README](#status-in-readme)
- [Everything else](#everything-else)
- [Languages support](#languages-support)
- [Run it locally](#run-it-locally)
- [How it works](#how-it-works)
- [Credits](#credits)
- [License](#license)

## Quick start

Download the installer from the [latest release](https://github.com/lixtheyt/Helyx/releases/latest)
and run it:

```
Helyx-1.0.0-setup.exe
```

It installs into your user folder and adds Helyx to `PATH`, so you can start it from Windows Terminal:

```
helyx
```

No administrator rights are needed.

Then pick **Add project** and choose any folder on your machine
Helyx finds the Git repository inside the folder by itself, if there is one.

## Features

- **All your projects in one list**, with a name, status and custom badges.
- **Git workflow in one window**: stage, commit, undo and redo commits, view and scroll through diffs, check history,
- manage branches and stashes, and push, pull and fetch from your remote.
- **Repository diagnostics**, which can find leftover lock files, unfinished rebases, detached HEADs,
  committed build files and files that are too large for GitHub, and help you fix them.
- **A built-in conflict resolver**, that opens the conflicted part of a file in its own editor instead of making you use Windows notepad.
- **GitHub issues and pull requests** can be read, filtered, commented on and reviewed directly from the terminal.
- **Status and badge shields in your README**, automatically updated, committed and pushed when the project status changes.
- **Encrypted notes, project backups and scripts**, all stored together with the project they belong to.

## Projects list

Every time you add a project, Helyx asks you to give it a name. It then automatically assigns a
default status, which you can change later.

![Project list](Helyx/assets/project-list.gif)

When you open a project, you can immediately see a header with your project information,
such as name, current status, last modified date and detected languages.

![Project header](Helyx/assets/project-header.png)

## Git features

Status marks your files based on what happened to them. Helyx can detect many statuses, such as 
staged, modified, untracked and conflicted.

![Git status](Helyx/assets/git-status.gif)

Helyx has simple interface. Files on the left, the diff on the right.

![Diff](Helyx/assets/diff.gif)

The log is a table. Select any commit you want to check and press Enter to see its details, including
the author, date, branches and every file it touched.

![Log and commit details](Helyx/assets/log-and-commit-details.gif)

![Commit details](Helyx/assets/commit-details.png)

When you commit something you did not mean to, you can always undo it.
When undoing, Helyx asks you what should happen to the changes the commit carried.
You can select from three options, soft reset, mixed reset or hard reset.

Redo puts the commit back.

Push, pull, fetch and Sync all communicate with GitHub. Push pushes your commit to the remote, pull pulls the commit from the remote, 
fetch fetches changes from the remote and Sync automatically checks if your local branch needs to push or pull and if there is a
divergence, it will prompt you which one you want to keep.

![Sync and undo](Helyx/assets/sync-and-undo.gif)

Helyx allows you to create, switch, merge and delete branches. Save a stash, look inside it
before you apply it, pop it.

![Branches](Helyx/assets/branches.gif)

![Stashes](Helyx/assets/stashes.gif)

![Stash info](Helyx/assets/stash-info.png)

## Diagnostics

Diagnostics scans your repository and tells you what is wrong.
For instance, a leftover `index.lock` from a process that crashed, a rebase nobody ever finished, 
a detached HEAD, build output somebody committed two years ago, a file too large for GitHub to accept, 
or a branch that tracks nothing.

Then it offers you options to fix each problem it found.

![Diagnostics](Helyx/assets/diagnostics.gif)

When a merge leaves you with conflict markers, Helyx opens the conflicted part of the file in
its own editor where you can delete the part you do not want to keep.

![Conflict solver](Helyx/assets/conflict-solver.gif)

## Managing GitHub repository

Helyx signs in via GitHub OAuth.
Helyx has a predefined set of permissions and it only requests the permissions
that it needs, no extra permissions for your account that it does not use.

![GitHub synchronization](Helyx/assets/github-sync.gif)

### Issues

A browsable list with filters you can invert. Open an issue and
you will see the same information as in GitHub, so you are not losing anything
and only getting the cool TUI style interface.

![Issues](Helyx/assets/issues.gif)

### Pull requests

The same list, plus a detail screen with the state, the branches, how many files changed, whether
it is mergeable, and every review left on it.

![Pull requests](Helyx/assets/pull-requests.gif)

### Repository statistics

Stars, forks, the language breakdown and the activity calendar. The wiki opens from the same menu.

![Repository statistics](Helyx/assets/repo-stats.gif)

### Workflow runs

Every workflow run your repository has had is color coded by how it ended, with the branch,
the commit and who started it.

![Workflow runs](Helyx/assets/workflow-runs.gif)

Open one and you get its jobs, and under each job every step it went through.

![A workflow run](Helyx/assets/workflow-run.png)

Helyx pulls the log archive down, unpacks it and shows it right there,
with anything that looks like an error marked out in red.

![Workflow log](Helyx/assets/workflow-log.gif)

You can also start a workflow on any branch, rerun a finished one, rerun only the jobs
that failed, or cancel one that is still going.

![Dispatching a workflow](Helyx/assets/workflow-dispatch.gif)

### Status in README

Set a status in Helyx and it becomes a shield in your README.

![Badge synchronization](Helyx/assets/badge-sync.gif)

Change the status later and the shield on GitHub changes with it. The same works for badges.

![The shield on GitHub](Helyx/assets/readme-shield.png)

## Everything else

Analyze Project reads the whole tree and shows the language breakdown and the biggest files.

![Analyze project](Helyx/assets/analyze-project.gif)

Every project gets a notes file with an editor. Turn encryption on in Settings and Helyx encrypts
them with Windows DPAPI, so they can only be read by you.

![Notes](Helyx/assets/notes.gif)

Turn encryption on in Settings and Helyx converts every note you already wrote. Turn it off and it converts them back to normal text.

![Notes encryption](Helyx/assets/notes-encryption.gif)

Backups zip a whole project, or skip everything your `.gitignore` already ignores.
You can restore your backup later, if something goes wrong with your project or if you just feel like it.

![Backup](Helyx/assets/backup.gif)

Scripts let you write the actions you always do for a project all the time and you do not want to keep doing them again and again.

Scripts have a lot of actions to select from and with them you can build a script, that will exactly do what you need it to do. 
Each action is called a block and each one has its own color.

The output of anything you execute comes back into Helyx.

![User scripts](Helyx/assets/user-scripts.gif)

Helyx supports thirteen editors from VS Code
and Visual Studio to Neovim, Emacs and the JetBrains editors.
That means you can directly open any of these editors from your Helyx project, and the selected editor will open the project folder.

![IDE settings](Helyx/assets/ide-settings.png)

Helyx allows you to create your own statuses and badges, with your own names and your own colors.

![Custom statuses](Helyx/assets/custom-statuses.gif)

Settings also
hold your editor paths, which identity Helyx commits under, whether
notes are encrypted, and a check for new versions of Helyx itself.

![Manage badges](Helyx/assets/manage-badges.png)

![Settings](Helyx/assets/settings.png)

## Languages support

- English
- French (Français)
- German (Deutsch)
- Italian (Italiano)
- Portuguese (Português)
- Russian (Русский)
- Slovak (Slovenčina)
- Spanish (Español)

Helyx is translated into eight languages.
This improves the user experience because Helyx is made for everyone.

![Languages](Helyx/assets/languages.gif)

## Run it locally

You need Windows and [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Nothing
else.

```sh
git clone https://github.com/lixtheyt/Helyx.git
cd Helyx
dotnet build Helyx.slnx -c Release
```

The executable is exported to `Helyx/bin/Release/net10.0-windows`.

If you want the same single file the download button above gives you, publish it instead:

```sh
dotnet publish Helyx/Helyx.csproj -p:PublishProfile=SingleFile
```

## How it works

**LibGit2Sharp is the brain, not a feature.** Helyx talks to repositories through
[LibGit2Sharp](https://github.com/libgit2/libgit2sharp) instead of running `git` and reading what
comes back. Parsing the output of `git` is fragile and makes no sense. Everything in Helyx that talks
to Git is done through LibGit2Sharp.

**Spectre.Console is the heart, not a helper library.**
[Spectre.Console](https://github.com/spectreconsole/spectre.console) draws every table, panel and
prompt. Every screen that Helyx has is rendered by Spectre.Console. The pretty design that Helyx has
couldn't have been done without [Spectre.Console](https://github.com/spectreconsole/spectre.console) and 
their developers who keep [Spectre.Console](https://github.com/spectreconsole/spectre.console) updated.

**One file holds everything, and it is treated as if it will be corrupted.**
If `%AppData%\Helyx\config.json` becomes unreadable, Helyx will repair it as soon as possible, 
so you can resume using Helyx.
Settings are stored by name rather than by index.

**Secrets will always stay secret.** Your GitHub token and your encrypted notes go through Windows
DPAPI, scoped to your account. A configuration copied
to another machine cannot be decrypted there, so the notes screen refuses to open rather than
showing you a wall of base64 that you might then save over the note it was hiding.

## Credits

Helyx exists because of [Spectre.Console](https://github.com/spectreconsole/spectre.console), Helyx's heart. The
whole interface runs on it and it can genuinely do anything you ask of it.

And [LibGit2Sharp](https://github.com/libgit2/libgit2sharp), Helyx's brain, which is what lets Helyx
talk to Git properly, instead of running the git command and trying to parse whatever it prints.

[Markdig](https://github.com/xoofx/markdig) parses the
Markdown that GitHub hands back before Helyx turns it into something a console can draw, and
[TextCopy](https://github.com/CopyText/TextCopy) puts the device code into your clipboard.

The credits window is drawn with a figlet font created by [xero](https://github.com/xero/figlet-fonts).

## License

Helyx is MIT. See [LICENSE](LICENSE).

Licenses of used libraries:

| Package | License |
| --- | --- |
| [Spectre.Console](https://github.com/spectreconsole/spectre.console) | MIT |
| [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) | MIT |
| [LibGit2Sharp.NativeBinaries](https://github.com/libgit2/libgit2sharp.nativebinaries) | GPLv2 with a linking exception [libgit2](https://github.com/libgit2/libgit2) |
| [Markdig](https://github.com/xoofx/markdig) | BSD-2-Clause |
| [TextCopy](https://github.com/CopyText/TextCopy) | MIT |

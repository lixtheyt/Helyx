#!/bin/sh
# Helyx installer for Linux and macOS.
#
#   curl -fsSL https://raw.githubusercontent.com/lixtheyt/Helyx/main/install.sh | sh
#
# Options:
#   --accept-license   accept the MIT license without being asked
#   --uninstall        remove Helyx again (your projects and settings are kept)
#
# Set HELYX_PREFIX to install somewhere other than ~/.local.

set -eu

REPO="lixtheyt/Helyx"
PREFIX="${HELYX_PREFIX:-$HOME/.local}"
APP_DIR="$PREFIX/share/helyx"
BIN_DIR="$PREFIX/bin"
ACCEPT=0
UNINSTALL=0

for arg in "$@"; do
    case "$arg" in
        --accept-license) ACCEPT=1 ;;
        --uninstall) UNINSTALL=1 ;;
        -h|--help)
            sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "Unknown option: $arg" >&2
            exit 2
            ;;
    esac
done

if [ -t 1 ]; then
    BOLD=$(printf '\033[1m')
    DIM=$(printf '\033[2m')
    BLUE=$(printf '\033[34m')
    GREEN=$(printf '\033[32m')
    RED=$(printf '\033[31m')
    YELLOW=$(printf '\033[33m')
    RESET=$(printf '\033[0m')
else
    BOLD="" DIM="" BLUE="" GREEN="" RED="" YELLOW="" RESET=""
fi

step() { printf '%s\n' "${BLUE}>${RESET} $1"; }
ok() { printf '%s\n' "${GREEN}✓${RESET} $1"; }
warn() { printf '%s\n' "${YELLOW}!${RESET} $1"; }
fail() { printf '%s\n' "${RED}x${RESET} $1" >&2; exit 1; }

case "$(uname -s)" in
    Linux) OS="linux" ;;
    Darwin) OS="osx" ;;
    *) fail "This installer is for Linux and macOS. On Windows, use the installer from https://github.com/$REPO/releases/latest" ;;
esac

case "$(uname -m)" in
    x86_64|amd64) ARCH="x64" ;;
    aarch64|arm64) ARCH="arm64" ;;
    *) fail "Helyx is not built for this processor ($(uname -m))." ;;
esac

RID="$OS-$ARCH"

if [ "$OS" = "osx" ]; then
    CONFIG_DIR="$HOME/Library/Application Support/Helyx"
else
    CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/Helyx"
fi

COLUMNS_NOW=$( (stty size 2>/dev/null </dev/tty || echo "0 80") | awk '{print $2}')

printf '\n'

if [ "$OS" = "osx" ]; then
    printf '%s\n' "  ${BOLD}Helyx Setup${RESET}"
    printf '%s\n' "  ${DIM}───────────${RESET}"
elif ! printf '%s' "${LC_ALL:-${LC_CTYPE:-${LANG:-}}}" | grep -qi 'utf-\{0,1\}8'; then
    printf '%s\n' "  ${BOLD}Helyx Setup${RESET}"
    printf '%s\n' "  ${DIM}-----------${RESET}"
elif [ "${COLUMNS_NOW:-80}" -ge 85 ]; then
    printf '%s' "$BLUE"
    cat <<'TITLE'
 ██      ██          ██                     ████████           ██
░██     ░██         ░██  ██   ██           ██░░░░░░           ░██           ██████
░██     ░██  █████  ░██ ░░██ ██  ██   ██  ░██         █████  ██████ ██   ██░██░░░██
░██████████ ██░░░██ ░██  ░░███  ░░██ ██   ░█████████ ██░░░██░░░██░ ░██  ░██░██  ░██
░██░░░░░░██░███████ ░██   ░██    ░░███    ░░░░░░░░██░███████  ░██  ░██  ░██░██████
░██     ░██░██░░░░  ░██   ██      ██░██          ░██░██░░░░   ░██  ░██  ░██░██░░░
░██     ░██░░██████ ███  ██      ██ ░░██   ████████ ░░██████  ░░██ ░░██████░██
░░      ░░  ░░░░░░ ░░░  ░░      ░░   ░░   ░░░░░░░░   ░░░░░░    ░░   ░░░░░░ ░░
TITLE
    printf '%s' "$RESET"
else
    printf '%s' "$BLUE"
    cat <<'TITLE'
 ██      ██          ██
░██     ░██         ░██  ██   ██
░██     ░██  █████  ░██ ░░██ ██  ██   ██
░██████████ ██░░░██ ░██  ░░███  ░░██ ██
░██░░░░░░██░███████ ░██   ░██    ░░███
░██     ░██░██░░░░  ░██   ██      ██░██
░██     ░██░░██████ ███  ██      ██ ░░██
░░      ░░  ░░░░░░ ░░░  ░░      ░░   ░░
TITLE
    printf '%s\n' "$RESET"
    printf '%s\n' "  ${BOLD}Setup${RESET}"
fi

printf '\n'

if [ "$UNINSTALL" = 1 ]; then
    if [ ! -e "$BIN_DIR/helyx" ] && [ ! -d "$APP_DIR" ]; then
        fail "Helyx is not installed in $PREFIX."
    fi

    rm -f "$BIN_DIR/helyx"
    rm -rf "$APP_DIR"

    ok "Helyx was removed from $PREFIX."
    printf '%s\n' "  ${DIM}Your projects and settings are still in $CONFIG_DIR${RESET}"
    exit 0
fi

if [ "$OS" = "linux" ] && (ldd --version 2>&1 || true) | grep -qi musl; then
    fail "This system uses musl (Alpine), and Helyx is only built for glibc."
fi

printf '%s\n\n' "${BOLD}License${RESET}"

cat <<'LICENSE'
  MIT License

  Copyright (c) 2026 lixtheyt

  Permission is hereby granted, free of charge, to any person obtaining a copy
  of this software and associated documentation files (the "Software"), to deal
  in the Software without restriction, including without limitation the rights
  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
  copies of the Software, and to permit persons to whom the Software is
  furnished to do so, subject to the following conditions:

  The above copyright notice and this permission notice shall be included in all
  copies or substantial portions of the Software.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
  SOFTWARE.
LICENSE

printf '\n'

if [ "$ACCEPT" = 1 ]; then
    ok "License accepted with --accept-license."
else
    if [ -t 0 ]; then
        printf '%s' "${BOLD}Do you accept the license?${RESET} [y/N] "
        read -r answer || answer=""
    elif (exec </dev/tty) 2>/dev/null; then
        printf '%s' "${BOLD}Do you accept the license?${RESET} [y/N] "
        read -r answer </dev/tty || answer=""
    else
        fail "There is no terminal to ask on. Run the installer again with --accept-license to accept the MIT license."
    fi

    case "$answer" in
        y|Y|yes|Yes|YES) ok "License accepted." ;;
        *) fail "Setup was cancelled, because the license was not accepted." ;;
    esac
fi

printf '\n'

WORK=$(mktemp -d 2>/dev/null || mktemp -d -t helyx)
trap 'rm -rf "$WORK"' EXIT INT TERM

HERE=$(CDPATH= cd -- "$(dirname -- "$0")" 2>/dev/null && pwd || echo "")
SOURCE=""

case "$0" in
    *install.sh)
        if [ -n "$HERE" ] && [ -f "$HERE/Helyx" ]; then
            SOURCE="$HERE/Helyx"
            step "Using the Helyx that came with this installer."
        fi
        ;;
esac

if [ -z "$SOURCE" ]; then
    BASE="https://github.com/$REPO/releases/latest/download"
    SUMS=""
    ARCHIVE=""

    if command -v curl >/dev/null 2>&1; then
        fetch() { curl -fsSL "$1" -o "$2"; }
    elif command -v wget >/dev/null 2>&1; then
        fetch() { wget -q "$1" -O "$2"; }
    else
        fail "Neither curl nor wget is installed, so Helyx cannot be downloaded."
    fi

    step "Downloading Helyx for $RID..."

    if fetch "$BASE/SHA256SUMS" "$WORK/SHA256SUMS" 2>/dev/null; then
        SUMS="$WORK/SHA256SUMS"
        ARCHIVE=$(awk -v tail="-$RID.tar.gz" 'substr($2, length($2) - length(tail) + 1) == tail { print $2; exit }' "$SUMS")
    fi

    [ -n "$ARCHIVE" ] || ARCHIVE="Helyx-$RID.tar.gz"

    fetch "$BASE/$ARCHIVE" "$WORK/$ARCHIVE" ||
        fail "Could not download $BASE/$ARCHIVE"

    if [ -n "$SUMS" ]; then
        expected=$(grep " $ARCHIVE\$" "$SUMS" | awk '{print $1}')

        if command -v sha256sum >/dev/null 2>&1; then
            actual=$(sha256sum "$WORK/$ARCHIVE" | awk '{print $1}')
        elif command -v shasum >/dev/null 2>&1; then
            actual=$(shasum -a 256 "$WORK/$ARCHIVE" | awk '{print $1}')
        else
            actual=""
        fi

        if [ -z "$expected" ] || [ -z "$actual" ]; then
            warn "The download could not be verified, because there is no checksum tool or no checksum for $ARCHIVE."
        elif [ "$expected" != "$actual" ]; then
            fail "The download does not match its checksum. Nothing was installed."
        else
            ok "Download verified."
        fi
    else
        warn "This release has no checksums, so the download was not verified."
    fi

    tar -xzf "$WORK/$ARCHIVE" -C "$WORK" || fail "Could not unpack $ARCHIVE"

    [ -f "$WORK/Helyx" ] || fail "$ARCHIVE does not contain Helyx."

    SOURCE="$WORK/Helyx"
fi

step "Installing into $APP_DIR..."

mkdir -p "$APP_DIR" "$BIN_DIR"
cp "$SOURCE" "$APP_DIR/Helyx.new"
chmod 755 "$APP_DIR/Helyx.new"
mv -f "$APP_DIR/Helyx.new" "$APP_DIR/Helyx"

if [ "$OS" = "osx" ]; then
    xattr -d com.apple.quarantine "$APP_DIR/Helyx" 2>/dev/null || true

    if command -v codesign >/dev/null 2>&1; then
        codesign --force --sign - "$APP_DIR/Helyx" 2>/dev/null ||
            warn "Helyx could not be signed. If macOS refuses to start it, allow it in System Settings > Privacy & Security."
    fi
fi

ln -sf "$APP_DIR/Helyx" "$BIN_DIR/helyx"

case ":$PATH:" in
    *":$BIN_DIR:"*) ;;
    *)
        LINE="export PATH=\"$BIN_DIR:\$PATH\""

        case "$(basename "${SHELL:-sh}")" in
            zsh) RC="$HOME/.zshrc" ;;
            bash) if [ "$OS" = "osx" ]; then RC="$HOME/.bash_profile"; else RC="$HOME/.bashrc"; fi ;;
            fish) RC="" ;;
            *) RC="$HOME/.profile" ;;
        esac

        if [ -z "$RC" ]; then
            warn "$BIN_DIR is not on your PATH. Add it with: fish_add_path $BIN_DIR"
        elif [ -f "$RC" ] && grep -qF "$LINE" "$RC"; then
            warn "$BIN_DIR is already in $RC. Open a new terminal to use it."
        else
            printf '\n# Added by the Helyx installer\n%s\n' "$LINE" >> "$RC"
            ok "Added $BIN_DIR to your PATH in $RC. Open a new terminal to use it."
        fi
        ;;
esac

if [ "$OS" = "linux" ]; then
    LIBRARIES=$( (ldconfig -p || /sbin/ldconfig -p) 2>/dev/null || true)

    if [ -n "$LIBRARIES" ] && ! printf '%s' "$LIBRARIES" | grep -q libicu; then
        warn "The ICU library was not found. If Helyx does not start, install it (libicu on Debian, Ubuntu and Fedora, icu on Arch)."
    fi
fi

printf '\n'
ok "${BOLD}Helyx is installed.${RESET}"
printf '%s\n' "  Start it with: ${BOLD}helyx${RESET}"
printf '%s\n\n' "  ${DIM}To remove it later, run this installer with --uninstall${RESET}"

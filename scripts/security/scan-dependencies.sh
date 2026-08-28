#!/usr/bin/env bash
#
# Vulnerable-dependency gate — Track 7 milestone 7.2.1.
#
# Runs `dotnet list package --vulnerable --include-transitive` across the solution and fails when it
# reports anything that is not covered by an unexpired entry in security/dependency-suppressions.yml.
#
# Why a script rather than inline workflow YAML: a developer has to be able to run exactly what CI
# runs. A gate that can only be reproduced by pushing a commit is a gate people argue with instead of
# fixing.
#
# Two deliberate properties:
#
#  * The suppression file is parsed here, in the gate, rather than by passing flags to the scanner.
#    `dotnet list package` has no suppression mechanism, so the alternative is a grep that quietly
#    matches more than intended.
#  * An expired suppression is a failure, not a warning. That is what stops the file from becoming a
#    permanent list of things nobody looks at.
#
# Usage:
#   ./scripts/security/scan-dependencies.sh [--solution <path>] [--report <path>]
#
# Exit codes: 0 clean (or fully suppressed), 1 vulnerable packages outside the suppression file,
# 2 the suppression file itself is invalid.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SOLUTION="${REPO_ROOT}/src/netrisk.sln"
SUPPRESSIONS="${REPO_ROOT}/security/dependency-suppressions.yml"
REPORT="${REPO_ROOT}/artifacts/security/dependency-scan.txt"
MAX_SUPPRESSION_DAYS=180

while [[ $# -gt 0 ]]; do
    case "$1" in
        --solution) SOLUTION="$2"; shift 2 ;;
        --report)   REPORT="$2";   shift 2 ;;
        --suppressions) SUPPRESSIONS="$2"; shift 2 ;;
        -h|--help)
            sed -n '2,25p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
            exit 0 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

mkdir -p "$(dirname "${REPORT}")"

today_epoch() { date -u +%s; }

# GNU date and BSD date disagree on everything, so both forms are tried.
to_epoch() {
    local iso="$1"
    date -u -d "${iso}" +%s 2>/dev/null || date -u -j -f "%Y-%m-%d" "${iso}" +%s 2>/dev/null
}

# ---------------------------------------------------------------------------------------------
# Validate the suppression file.
#
# Parsed with plain bash rather than a YAML library: this script has to run on a bare CI image with
# nothing installed beyond bash, coreutils and the .NET SDK. The schema is fixed and flat, which is
# what makes that reasonable — and ManagedDependencySuppressionsTest in Packaging.Tests parses the
# same file with a real YAML parser, so a shape this script would misread fails there.
# ---------------------------------------------------------------------------------------------
declare -a SUPPRESSED_ADVISORIES=()
declare -a SUPPRESSED_PACKAGES=()
suppression_errors=0

if [[ -f "${SUPPRESSIONS}" ]]; then
    current_package=""
    current_advisory=""
    current_expires=""
    current_reason=""
    current_owner=""

    finish_entry() {
        [[ -z "${current_package}${current_advisory}" ]] && return 0

        if [[ -z "${current_expires}" ]]; then
            echo "ERROR: suppression for '${current_package:-${current_advisory}}' has no 'expires' date" >&2
            suppression_errors=$((suppression_errors + 1))
            return 0
        fi

        local expires_epoch
        expires_epoch="$(to_epoch "${current_expires}")"

        if [[ -z "${expires_epoch}" ]]; then
            echo "ERROR: suppression for '${current_package:-${current_advisory}}' has an unparseable expires '${current_expires}' (want YYYY-MM-DD)" >&2
            suppression_errors=$((suppression_errors + 1))
            return 0
        fi

        local now
        now="$(today_epoch)"

        if (( expires_epoch < now )); then
            echo "ERROR: the suppression for '${current_package:-${current_advisory}}' expired on ${current_expires}. Fix the dependency or consciously renew the acceptance." >&2
            suppression_errors=$((suppression_errors + 1))
            return 0
        fi

        local max_epoch=$((now + MAX_SUPPRESSION_DAYS * 86400))
        if (( expires_epoch > max_epoch )); then
            echo "ERROR: the suppression for '${current_package:-${current_advisory}}' expires ${current_expires}, more than ${MAX_SUPPRESSION_DAYS} days out. That is a remediation plan, not a suppression — record it in docs/security/FINDINGS.md instead." >&2
            suppression_errors=$((suppression_errors + 1))
            return 0
        fi

        if [[ -z "${current_reason}" ]]; then
            echo "ERROR: suppression for '${current_package:-${current_advisory}}' has no 'reason'" >&2
            suppression_errors=$((suppression_errors + 1))
        fi

        if [[ -z "${current_owner}" ]]; then
            echo "ERROR: suppression for '${current_package:-${current_advisory}}' has no 'owner'" >&2
            suppression_errors=$((suppression_errors + 1))
        fi

        [[ -n "${current_advisory}" ]] && SUPPRESSED_ADVISORIES+=("${current_advisory}")
        [[ -n "${current_package}" ]] && SUPPRESSED_PACKAGES+=("${current_package}")

        current_package=""; current_advisory=""; current_expires=""; current_reason=""; current_owner=""
    }

    while IFS= read -r line || [[ -n "${line}" ]]; do
        # Strip comments and trailing space.
        stripped="${line%%#*}"
        stripped="${stripped%"${stripped##*[![:space:]]}"}"
        [[ -z "${stripped}" ]] && continue

        if [[ "${stripped}" =~ ^[[:space:]]*-[[:space:]]*package:[[:space:]]*(.*)$ ]]; then
            finish_entry
            current_package="${BASH_REMATCH[1]//\"/}"
        elif [[ "${stripped}" =~ ^[[:space:]]+advisory:[[:space:]]*(.*)$ ]]; then
            current_advisory="${BASH_REMATCH[1]//\"/}"
        elif [[ "${stripped}" =~ ^[[:space:]]+expires:[[:space:]]*(.*)$ ]]; then
            current_expires="${BASH_REMATCH[1]//\"/}"
        elif [[ "${stripped}" =~ ^[[:space:]]+reason:[[:space:]]*(.*)$ ]]; then
            current_reason="${BASH_REMATCH[1]}"
        elif [[ "${stripped}" =~ ^[[:space:]]+owner:[[:space:]]*(.*)$ ]]; then
            current_owner="${BASH_REMATCH[1]//\"/}"
        fi
    done < "${SUPPRESSIONS}"

    finish_entry
fi

if (( suppression_errors > 0 )); then
    echo "" >&2
    echo "${suppression_errors} problem(s) in ${SUPPRESSIONS}. See the header of that file for the rules." >&2
    exit 2
fi

# ---------------------------------------------------------------------------------------------
# Run the scan.
# ---------------------------------------------------------------------------------------------
echo "Scanning ${SOLUTION} for known-vulnerable packages (direct and transitive)..."

# `dotnet list package` enumerates every project in the solution, and needs an assets file for each
# one. `dotnet restore <solution>` does not produce one for every project it contains: a project
# mapped with `ActiveCfg` but no `Build.0` — which is how Nuke registers build/build.csproj, so that
# building the solution does not build the build script — is skipped by restore and then fails the
# listing with "No assets file was found". It cost this gate its first green run.
#
# Restoring those projects individually is the fix rather than skipping them: build/build.csproj
# pulls Nuke and its transitive graph into the release process, which is exactly the supply chain
# this gate exists to watch. Any project the solution declines to build is restored here by name.
UNBUILT_PROJECTS=("${REPO_ROOT}/build/build.csproj")

for project in "${UNBUILT_PROJECTS[@]}"; do
    [[ -f "${project}" ]] || continue

    if ! dotnet restore "${project}" > /dev/null 2>&1; then
        echo "Failed to restore ${project}, which the solution does not restore for us." >&2
        exit 2
    fi
done

if ! dotnet list "${SOLUTION}" package --vulnerable --include-transitive > "${REPORT}" 2>&1; then
    echo "dotnet list package failed:" >&2
    cat "${REPORT}" >&2
    exit 2
fi

cat "${REPORT}"

# `dotnet list package --vulnerable` prints a table only when there is something to report, and its
# wording is localised — so the reliable signal is the advisory URL column, which is not.
#
# A while-read loop rather than `mapfile`: macOS still ships bash 3.2, and the whole point of putting
# the gate in a script is that a developer can run the same thing CI runs.
declare -a findings=()
while IFS= read -r finding_line; do
    findings+=("${finding_line}")
done < <(grep -E "https://github\.com/advisories/|GHSA-|CVE-" "${REPORT}" || true)

if (( ${#findings[@]-0} == 0 )); then
    echo ""
    echo "No known-vulnerable packages. (${#SUPPRESSED_ADVISORIES[@]-0} suppression(s) on file, none needed.)"
    exit 0
fi

# ---------------------------------------------------------------------------------------------
# Compare against the suppressions.
# ---------------------------------------------------------------------------------------------
unsuppressed=0

for finding in "${findings[@]}"; do
    covered=0

    for advisory in "${SUPPRESSED_ADVISORIES[@]-}"; do
        [[ -n "${advisory}" && "${finding}" == *"${advisory}"* ]] && covered=1 && break
    done

    if (( covered == 0 )); then
        for package in "${SUPPRESSED_PACKAGES[@]-}"; do
            [[ -n "${package}" && "${finding}" == *"${package}"* ]] && covered=1 && break
        done
    fi

    if (( covered == 0 )); then
        echo "UNSUPPRESSED: ${finding}" >&2
        unsuppressed=$((unsuppressed + 1))
    fi
done

echo ""

if (( unsuppressed > 0 )); then
    cat >&2 <<MESSAGE
${unsuppressed} vulnerable package finding(s) are not covered by security/dependency-suppressions.yml.

Fix, in order of preference:
  1. Upgrade the package. For a transitive dependency, add a direct PackageReference pinning the
     fixed version — that is the supported way to lift a transitive package.
  2. If it genuinely cannot be upgraded yet, add an entry to security/dependency-suppressions.yml
     with an advisory id, an owner, a real reason and an expiry no more than ${MAX_SUPPRESSION_DAYS} days out, and
     record it in docs/security/FINDINGS.md.

Do not widen the grep in this script.
MESSAGE
    exit 1
fi

echo "All ${#findings[@]-0} finding(s) are covered by unexpired suppressions."
exit 0

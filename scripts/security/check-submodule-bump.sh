#!/usr/bin/env bash
#
# Submodule provenance gate — Track 7 milestone 7.2.3.
#
# NetRisk vendors five upstream repositories as git submodules under libs/. A submodule bump is a
# one-line diff — the recorded commit SHA changes — and it can pull in any amount of code. That makes
# it the highest-leverage, lowest-visibility change anyone can make to this repository.
#
# So: a pull request that moves a submodule pointer has to say what moved. The check requires the
# pull-request body to name each bumped submodule and to state the upstream commit range, which is
# what forces the author to have looked. It cannot verify that the review was *good*; what it can do
# is make skipping it a deliberate act rather than an oversight.
#
# That requirement alone is not enough, and this repository has the scar to prove it. Dependabot #81
# proposed moving libs/Aura.UI *backwards* — the fork's default branch sat ten commits behind the
# `avalonia12` tip NetRisk pins, so the "bump" reverted the Avalonia 12 / .NET 10 port. The body
# named the submodule and both SHAs, so the substring check passed; the solution compiled clean with
# zero warnings, every unit test passed, and the desktop client died at startup with a
# MissingMethodException from Aura.UI's theme. A prose instruction to "confirm the new commit is not
# a rewind" is not a control. So the direction of the move is now checked mechanically: if the new
# commit is an ancestor of the old one, the pointer is going backwards and the gate fails, whatever
# the description says.
#
# Inputs, all from the environment so that no untrusted value is ever interpolated into a shell
# command (the workflow-injection pattern):
#   BASE_REF  base commit SHA of the pull request
#   HEAD_REF  head commit SHA of the pull request
#   PR_BODY   the pull-request description
#
# Exit codes: 0 no submodule change, or a change with the review attached; 1 a bump with no review,
#             or a bump that moves a pointer backwards.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${REPO_ROOT}" || exit 2

BASE_REF="${BASE_REF:-}"
HEAD_REF="${HEAD_REF:-HEAD}"
PR_BODY="${PR_BODY:-}"

if [[ -z "${BASE_REF}" ]]; then
    echo "BASE_REF is not set; nothing to compare against. Skipping."
    exit 0
fi

# --submodule=short makes a pointer move show as "Subproject commit <sha>" lines, which is what makes
# the range extractable.
# A while-read loop rather than `mapfile`, for bash 3.2 (macOS) compatibility.
declare -a changed=()
while IFS= read -r changed_path; do
    changed+=("${changed_path}")
done < <(git diff --name-only "${BASE_REF}" "${HEAD_REF}" -- libs .gitmodules || true)

if (( ${#changed[@]-0} == 0 )); then
    echo "No submodule pointers changed."
    exit 0
fi

echo "Submodule-affecting paths in this pull request:"
printf '  %s\n' "${changed[@]}"
echo ""

missing=0
rewound=0

# Establishes whether ${2} is an ancestor of ${1} inside the submodule at ${3} — i.e. whether the
# pointer is moving backwards along its own history. Needs both commits present locally, and the
# checkout only guarantees one of them, so all remote heads are fetched first. A submodule whose
# objects cannot be reached (network, or a private upstream) returns 2 for "unknown": that leaves the
# description requirement as the only gate, which is the pre-existing behaviour, and says so loudly
# rather than passing quietly.
is_rewind() {
    local old_sha="${1}" new_sha="${2}" sm_path="${3}"

    [[ -d "${sm_path}/.git" || -f "${sm_path}/.git" ]] || return 2

    git -C "${sm_path}" fetch -q --tags origin '+refs/heads/*:refs/remotes/origin/*' 2>/dev/null || true

    git -C "${sm_path}" cat-file -e "${old_sha}^{commit}" 2>/dev/null || return 2
    git -C "${sm_path}" cat-file -e "${new_sha}^{commit}" 2>/dev/null || return 2

    # Ancestor in this direction means the new pointer is older than the one it replaces.
    git -C "${sm_path}" merge-base --is-ancestor "${new_sha}" "${old_sha}" 2>/dev/null
}

for path in "${changed[@]}"; do
    [[ "${path}" == ".gitmodules" ]] && continue

    name="$(basename "${path}")"

    old_sha="$(git ls-tree "${BASE_REF}" "${path}" | awk '{print $3}')"
    new_sha="$(git ls-tree "${HEAD_REF}" "${path}" | awk '{print $3}')"

    if [[ "${old_sha}" == "${new_sha}" ]]; then
        continue
    fi

    echo "libs/${name}: ${old_sha:0:12} -> ${new_sha:0:12}"

    # Direction first. A rewind is not a reviewable bump, so it is rejected before the description is
    # consulted at all — no description can make moving backwards correct.
    is_rewind "${old_sha}" "${new_sha}" "${path}"
    case $? in
        0)
            echo "  REWIND: ${new_sha:0:12} is an ancestor of ${old_sha:0:12}. This moves the pointer" >&2
            echo "          backwards and reverts everything in between." >&2
            rewound=$((rewound + 1))
            continue
            ;;
        2)
            echo "  WARNING: could not read ${name}'s history, so the direction of this move was not" >&2
            echo "           verified. The description requirement below is the only gate." >&2
            ;;
    esac

    # The body has to mention the submodule and both SHAs (at least their short forms). Checked with
    # a case-insensitive substring match on the *variable*, never by interpolating it into a command.
    # `tr` rather than ${var,,}: bash 3.2 has no case-modification expansion. Both values go through
    # a here-string, never a command line, so neither is interpolated anywhere a shell would parse.
    body_lower="$(printf '%s' "${PR_BODY}" | tr '[:upper:]' '[:lower:]')"
    name_lower="$(printf '%s' "${name}" | tr '[:upper:]' '[:lower:]')"

    if [[ "${body_lower}" != *"${name_lower}"* ]]; then
        echo "  MISSING: the pull-request description does not mention '${name}'." >&2
        missing=$((missing + 1))
        continue
    fi

    if [[ "${body_lower}" != *"${old_sha:0:7}"* ]] || [[ "${body_lower}" != *"${new_sha:0:7}"* ]]; then
        echo "  MISSING: the description mentions '${name}' but not the commit range ${old_sha:0:12}..${new_sha:0:12}." >&2
        missing=$((missing + 1))
        continue
    fi

    echo "  OK: reviewed range recorded in the description."
done

echo ""

if (( rewound > 0 )); then
    cat >&2 <<'MESSAGE'
A submodule pointer moved backwards.

The proposed commit is an ancestor of the one this repository already pins, so the "bump" reverts
every commit in between. This is almost always Dependabot tracking the wrong branch: it follows the
submodule's *default* branch when .gitmodules names none, and it cannot tell that the pinned commit
is ahead of that branch.

Fix the cause rather than the pull request:
  * name the branch NetRisk actually consumes in .gitmodules (`branch = <name>`), or
  * make that branch the fork's default branch.

Then close the pull request. Do not merge it: a rewind of a submodule that is compiled from source
passes the build and the unit tests, and surfaces as a runtime failure in whatever consumes it.
MESSAGE
    exit 1
fi

if (( missing > 0 )); then
    cat >&2 <<'MESSAGE'
A submodule pointer moved without a recorded review.

Add to the pull-request description, for each bumped submodule:
  * the submodule name;
  * the old and new commit SHAs (short form is enough);
  * a summary of the upstream diff — what changed, and whether any of it touches parsing,
    networking, cryptography or file handling;
  * confirmation that the new commit is on the upstream default branch and is not a force-push
    over a SHA this repository previously pinned.

The full procedure and the per-submodule owners are in docs/security/SUPPLY_CHAIN.md.
MESSAGE
    exit 1
fi

echo "Every submodule bump in this pull request carries a recorded review."
exit 0

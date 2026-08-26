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
# Inputs, all from the environment so that no untrusted value is ever interpolated into a shell
# command (the workflow-injection pattern):
#   BASE_REF  base commit SHA of the pull request
#   HEAD_REF  head commit SHA of the pull request
#   PR_BODY   the pull-request description
#
# Exit codes: 0 no submodule change, or a change with the review attached; 1 a bump with no review.

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

for path in "${changed[@]}"; do
    [[ "${path}" == ".gitmodules" ]] && continue

    name="$(basename "${path}")"

    old_sha="$(git ls-tree "${BASE_REF}" "${path}" | awk '{print $3}')"
    new_sha="$(git ls-tree "${HEAD_REF}" "${path}" | awk '{print $3}')"

    if [[ "${old_sha}" == "${new_sha}" ]]; then
        continue
    fi

    echo "libs/${name}: ${old_sha:0:12} -> ${new_sha:0:12}"

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

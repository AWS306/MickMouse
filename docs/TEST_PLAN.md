# Verification plan

The automated rule tests and HTTP smoke script run locally. Record their real results when they finish. The AWS checks below remain manual until the project is run against an actual AWS account.

## Automated rules

```sh
dotnet test tests/MickMouse.Tests/MickMouse.Tests.csproj
python3 tests/run_local_checks.py
```

The xUnit tests exercise:

- Comment author and UTC time rules: just before 24 hours, exactly at 24 hours, after the deadline, and a future creation timestamp.
- Store-side enforcement even when the UI is bypassed, version conflicts, and concurrent edits with the same version.
- Original creation/ownership data surviving edits and a new store instance reopening the file.
- Incoming/outgoing objects not allowing callers to mutate saved comments indirectly.
- Media extension/content-type agreement and the 25 MiB upload boundary.
- DynamoDB SDK request deadline precision at the last tick of the original edit window, using a fake client without network calls.

The HTTP runner builds and launches a disposable Local preview, runs both suites, and stops it. The smoke suite checks actual server responses, registration, login, comment escaping/editing, role denial, and play tracking. The lifecycle suite uses HTTP for mutations and read-only SQLite checks to verify creation/upload, ownership denial, Admin approval, pending visibility, metadata/media replacement, byte-range playback, concurrent counts/toggles, and deletion cleanup. The ten original episodes remain intact. These tests do not provision resources or prove that AWS storage works.

To check an already running local preview separately, use `python3 tests/http_smoke.py http://localhost:5000`. This standalone script creates one clearly named test account and comment in that preview. Prefer the disposable runner for routine verification.

## Manual behavior checks

| Scenario | Expected result |
|---|---|
| Duplicate email / incorrect password | Friendly validation; no second account; no secret detail leaked |
| Anonymous Studio/Admin request | Authentication required |
| Listener submits Podcaster/Admin request | Access denied on the server |
| Podcaster edits another owner's episode | Denied even with a manually changed URL/form ID |
| Episode is pending or rejected | Hidden from public browsing and playback |
| Empty and combined searches | Useful empty state; topic and host filters both apply |
| Two playback requests overlap | Both increments retained |
| Comment contains HTML/script text | Displayed as text, never executed |
| Edit form stays open past the deadline | Server rejects the stale submission |
| Two tabs edit the same comment | Second stale version rejected |
| Oversized or mismatched upload | Rejected before storage; useful validation |
| Delete episode / replace media | Content becomes inaccessible; related cleanup can be retried if a service fails |
| Subscription or like submitted twice | No duplicate record |
| Keyboard-only and narrow mobile use | All controls reachable; no horizontal overflow; visible focus |

## Live AWS evidence

1. Run with `App__Mode=Aws`; verify no Local preview label or local fallback.
2. Register and query the new user in RDS. Check every application table has five or more sample rows.
3. Add/edit a comment; verify the exact item, author ID, UTC timestamp, and incremented version in DynamoDB. Check table keys have the documented types.
4. Play private seeded media and a newly uploaded file. Confirm the bucket is not public, and direct unsigned object access is denied.
5. Confirm the instance role can read the exact SecureString. Remove that permission in a controlled test only if appropriate; startup must fail clearly rather than use a local secret.
6. Exercise the app on its deployed Beanstalk URL and inspect healthy environment status.
7. Check ownership, approval, comment expiry, and search on that deployment, then record the required video.

The project is designed for a small course deployment. Scaling to multiple instances also requires shared, durable ASP.NET Data Protection keys and a deliberate distributed rate-limit/session strategy; those are not implied by a successful single-instance demonstration.

## Account deletion

Check My Account -> Delete My Account with a disposable Listener and Podcaster.
Require the current password and exact `delete my account` phrase. Verify old
sessions lose access, comments on other shows are removed, owned shows and
uploads are cleaned, and other accounts remain. The last active admin must be
blocked. If AWS cleanup fails, verify account recovery and retry instead of
claiming that deletion succeeded.

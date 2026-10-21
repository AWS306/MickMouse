# Verification results

Verified on 21 October 2026 with .NET 10 SDK 10.0.402. These checks use disposable
Local preview data; no live AWS resources were created or tested.

| Check | Result |
|---|---|
| Solution build | Passed with no warnings or errors |
| xUnit tests | 45 passed, 0 failed, 0 skipped |
| Existing HTTP smoke and episode lifecycle | 18 + 42 assertions passed |
| Account deletion HTTP checks | 37 assertions passed |
| Desktop/mobile browser checks | 16 page checks passed; Arial, Mick Mouse branding, blue/yellow palette, no body overflow or console errors |
| SQL sample export | 5 users, 5 podcasts, 10 episodes, 6 subscriptions and 6 likes |
| Release publishing script | Release build and Beanstalk ZIP creation passed; live deployment remains unverified |

## Account deletion coverage

The HTTP tests reject anonymous requests, missing anti-forgery tokens, incorrect
confirmation phrases and incorrect passwords. They preserve the last active
admin, ignore forged target user IDs, remove both Listener and Podcaster
accounts with their related data, revoke other sessions, and retain other
users and the original podcast catalogue. Uploaded owned media is removed;
shared sample files remain.

Additional xUnit tests verify user-scoped comment removal, DynamoDB filtered
scan pagination and retry behavior using a request-capture client, and account
recovery when an external comment store fails. These are not live AWS tests.

## Reproduce

```sh
dotnet test tests/MickMouse.Tests/MickMouse.Tests.csproj
python3 tests/run_local_checks.py
```

The HTTP runner starts its own Local server with temporary data and stops it
after the checks. Existing preview data is not modified. Browser review was
performed at desktop 1440px and mobile 390px widths, with screenshots visually
inspected. Browser screenshots are not included in the submission.

## Still requires AWS verification

RDS SQL Server script execution, DynamoDB requests/permissions, private S3
playback and cleanup, Parameter Store, the RDS CA hook and Elastic Beanstalk
health must be verified in your own account. Use the updated scoped IAM policy,
which includes `dynamodb:Scan` for user comment cleanup.

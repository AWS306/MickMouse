# AWS setup

This guide prepares the genuine AWS version of the lab. The Local preview is useful for development, but its SQLite database and JSON comments are not substitutes for the services required by the assignment.

## 1. Choose one region and prepare SQL Server

Use the same AWS region for Elastic Beanstalk, RDS, DynamoDB, S3, and Parameter Store. The examples use `eu-central-1`; change all region placeholders together.

1. In RDS, create a **Microsoft SQL Server** database instance using an edition, instance size, and storage allocation permitted by your account and course. Review the displayed estimate before creating it.
2. Place it in the same VPC as the planned Elastic Beanstalk environment. Prefer private access. Permit inbound TCP **1433 from the Beanstalk EC2 security group**, rather than from the whole internet.
3. Connect with a SQL Server client from a permitted machine or a temporary connection path. Create a database called `MickMouse` and run `database/schema-and-seed.sql` against that database. Its script header documents whether it is intended for a new database.
4. Use a dedicated SQL login with access only to this application database. Runtime needs normal reads and writes; it does not need to create/drop databases or administer the RDS server.
5. Verify five users, five podcasts, ten episodes, and at least five subscriptions and likes. Change demonstration passwords before exposing seeded accounts.

The connection string below verifies the RDS server certificate. Beanstalk's supplied predeploy hook imports RDS root CAs from AWS's official HTTPS certificate bundle and refreshes the Amazon Linux trust store. The instance needs outbound HTTPS access for that bundle and the AWS services. For a local AWS-mode run, import the applicable RDS root CA into your own operating system's trust store too. Do not work around a certificate error by disabling verification.

Default Identity claim/login/token tables are intentionally not padded with fabricated authentication data. See `RUBRIC_MAP.md` for the exact interpretation of “each table has at least five rows.”

## 2. Put the connection string in Parameter Store

In **Systems Manager -> Parameter Store**, create:

- Name: `/mickmouse/lab3/sql-connection`
- Type: **SecureString**
- Value: the complete connection string below, with your real values entered directly in the console:

```text
Server=YOUR_RDS_ENDPOINT,1433;Database=MickMouse;User Id=YOUR_APP_LOGIN;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;
```

Do not paste the completed value into source control, screenshots, terminal history, or the ZIP. The application requests this parameter with decryption at startup. A changed parameter takes effect after restarting the app; the code does not poll for secret changes.

The supplied IAM policy grants `ssm:GetParameter` only for this parameter. If you select your own KMS key, also configure that key's policy and the optional scoped `kms:Decrypt` statement described in `deploy/README.md`.

## 3. Create the DynamoDB table in the console

The assignment explicitly asks for console creation and sample data:

1. Open **DynamoDB -> Tables -> Create table**.
2. Name it `MickMouseComments`.
3. Partition key: **EpisodeId**, type **Number**.
4. Sort key: **CommentId**, type **String**.
5. Choose on-demand capacity for a small demonstration unless your course account requires another setting. No secondary index is needed.
6. Once active, choose **Explore table items -> Create item -> JSON view**. Add each of the five objects from `deploy/dynamodb-sample-items.json` separately. This file contains DynamoDB attribute-value JSON, not one item containing an array.

Do not enable TTL on `EditUntilEpoch`: this value controls editing, not comment deletion. The sample dates are intentionally fixed; use a newly created comment in the video to demonstrate the open 24-hour edit window. Item user IDs and episode IDs match the SQL sample catalog.

Alternatively, regenerate a sample file with fresh timestamps before console insertion:

```sh
dotnet run --project src/MickMouse.Web/MickMouse.Web.csproj -- --export-dynamo database/dynamodb-items-fresh.json
```

Paste the five exported items individually into the console. This command writes a local file only and makes no AWS requests. The existing SQL script can similarly be regenerated with `--export-sql database/schema-and-seed.sql`.

## 4. Prepare private S3 media

Create a private bucket with **Block Public Access enabled**. Upload all ten bundled clips:

| Local file | S3 object key |
|---|---|
| `src/MickMouse.Web/wwwroot/media/samples/episode-01.mp3` | `samples/episode-01.mp3` |
| `src/MickMouse.Web/wwwroot/media/samples/episode-02.mp3` through `episode-10.mp3` | Corresponding `samples/episode-02.mp3` through `samples/episode-10.mp3` |

Set their content type to `audio/mpeg`. These are 20-second credited excerpts, not full episodes. Keep their credits and source links when redistributing the project; see `MEDIA_CREDITS.md`. The application uploads new media under `uploads/` and produces short-lived signed playback URLs. It never requires a public bucket or public object ACL. Keep the shared sample files while episodes reference them.

## 5. Attach the instance permissions

Create or select a Beanstalk EC2 **instance profile**, distinct from the Beanstalk service role. Add the scoped application policy in `deploy/iam-app-policy.template.json` after replacing its placeholders. Retain the normal platform permissions required by Beanstalk itself.

The app uses the AWS SDK credential chain. A deployed instance receives credentials from its IAM role; access keys are not environment variables. For a local AWS-mode review, use your organization's approved AWS profile or temporary credentials, outside the source folder.

## 6. Publish and deploy

Use the current **.NET 10 on 64-bit Amazon Linux 2023** platform branch. AWS lists this branch in its supported-platforms documentation; select the latest available patch in your region rather than hard-coding a platform version.

The recommended packaging command, run from the project root, is:

```sh
python3 scripts/publish-beanstalk.py
```

It creates `artifacts/mick-mouse-beanstalk.zip` from a temporary Release publish folder and includes the startup/configuration files below. It does not connect to AWS. If you package manually, use these steps:

1. Publish the project in Release mode into a clean folder. The root publishing script may do this for you. The equivalent core command is:

   ```sh
   dotnet publish src/MickMouse.Web/MickMouse.Web.csproj -c Release -o publish
   ```

2. Copy `deploy/Procfile` to the **root of that publish folder**. It starts `MickMouse.Web.dll` on port 5000, matching Beanstalk's default proxy port. Copy `deploy/.platform/` into the publish folder as `.platform/` too: its nginx setting permits the 25 MiB media upload plus multipart overhead, and its predeploy hook installs the RDS root trust certificates. Preserve the hook's executable permission and LF line endings.
3. ZIP the **contents** of the publish folder. `Procfile` and the DLL must be at archive root, not nested inside `publish/` or the source-project folder.
4. In Beanstalk, create a Web server environment with the .NET 10/AL2023 branch and your instance profile. Attach the environment to the VPC/security groups prepared for RDS.
5. Configure these environment properties before starting the final app version:

   ```text
   ASPNETCORE_ENVIRONMENT=Production
   App__Mode=Aws
   App__SeedDemo=false
   AWS__Region=eu-central-1
   AWS__CommentsTable=MickMouseComments
   AWS__MediaBucket=YOUR_PRIVATE_BUCKET
   AWS__ConnectionParameter=/mickmouse/lab3/sql-connection
   ```

6. Deploy the published bundle. Use an HTTPS listener/certificate for real account use. Confirm environment health and inspect logs if startup fails; the app deliberately does not fall back to local storage.

After the HTTPS listener is working, set `App__RequireHttps=true` so the application enables HTTPS redirection and HSTS. Beanstalk's local nginx proxy is trusted for forwarded protocol information.

For a temporary local run against genuine AWS services, set the same configuration variables in your own shell, authenticate with a scoped AWS profile, and allow that machine to reach RDS. Never commit those shell settings. Keep `App__SeedDemo=false` and apply the SQL script first.

## 7. Verify and record

Confirm in AWS that registration writes to RDS, new comments appear in DynamoDB, uploads appear in S3, and the running instance reads the named Parameter Store secret. Complete `DEMO_CHECKLIST.md` and record the required video. After recording, remove the lab RDS instance and Beanstalk deployment as instructed, and **keep the DynamoDB table** for the professor. Do not run broad account cleanup commands.



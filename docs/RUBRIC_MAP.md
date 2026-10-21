# Requirements and evidence

Use this map when preparing the video. An implementation entry identifies where to inspect the behavior; live AWS evidence still needs to be collected in your account.

| Requirement | Implementation / evidence to show |
|---|---|
| GUI, 2 marks | Blue and yellow responsive library, episode player/comments, Studio, Admin |
| Registration/login, 2 marks | Identity forms; verify the new account in RDS SQL Server |
| SQL and DynamoDB modeling, 5 marks | SQL script with populated application tables; console-created DynamoDB table and five items |
| Add/delete/update episodes, 5 marks | Studio episode form, metadata changes, S3 upload/replacement, deletion |
| Update viewers, 1 mark | Actual play action followed by a larger view count |
| Popular/recent episodes, 2 marks | Most-played and newest-first library sorting |
| Topic/host search, 2 marks | Separate topic and host queries and their combined result |
| Add/list comments, 2 marks | Episode comments saved to and read from DynamoDB |
| Owner-only comment editing within 24 hours, 2 marks | Recent own comment editable; another author's and old comments denied |
| Parameter Store credentials, 2 marks | SecureString metadata plus code configuration; hide the value |
| Elastic Beanstalk, 2 marks | Healthy deployed environment and working application URL |
| Non-functional, 3 marks | Clear validation, access checks, maintainable layers, tests, readable UI |

Additional specification items are included in the review: podcast management, Listener subscriptions and likes, Podcaster statistics, Admin episode approval, and user access management. Episode view statistics come from SQL, while comment activity comes from DynamoDB, matching where those records are stored.

## Table-count interpretation

Every **application data table** has at least five meaningful sample records: users, podcasts, episodes, subscriptions, and episode likes. Identity uses a Role value on each user and emits that role as a claim; the three required roles do not need a padded role lookup table.

Identity's optional framework tables for claims, external logins, and authentication tokens may be empty. The project does not fabricate login providers, claims, or persistent tokens to reach a row count. This is a documented interpretation of the assignment's “each table” wording, not a claim that every framework table has five rows. Confirm it with the instructor if the wording is enforced literally.

The supplied DynamoDB console sample has five items. Local JSON comments demonstrate application behavior but do not count as DynamoDB evidence.

## What has not been assumed

Source code, setup instructions, or local test success do not prove a deployed AWS integration. Registration in RDS, DynamoDB item writes, private S3 playback, Parameter Store access, and Beanstalk health must be checked in the user's AWS account. The required demonstration video is a separate deliverable.

The PDF lists a due date of October 26, 2025. Use the current course/Luminate deadline rather than assuming that printed year is current.

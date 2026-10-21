# Demonstration checklist

Keep the finished video **under ten minutes**. Both group members must have their cameras on and visible. Clearly show one member's AWS account information, without exposing secret values, passwords, or access keys. Demonstrate behavior instead of explaining the source code.

## Suggested recording sequence

| Time | Show |
|---|---|
| 0:00-0:40 | Both members, AWS account context, healthy Beanstalk environment and deployed URL |
| 0:40-1:35 | Register a Listener, log out/in, and confirm the account in RDS |
| 1:35-2:20 | SQL tables/sample counts and console-created DynamoDB table with five items |
| 2:20-4:05 | Podcaster creates an episode, uploads media, modifies title/media, and shows its own Studio |
| 4:05-4:50 | Admin approves the pending episode; Listener can now find and play it |
| 4:50-5:45 | Topic and host searches, newest/popular sorts, view count before/after playing |
| 5:45-7:05 | Add/list/edit a new own comment; show another user's and a 24-hour-old comment cannot be edited |
| 7:05-7:55 | Subscribe, like, and display Podcaster/Admin statistics and user-access controls |
| 7:55-8:40 | Parameter Store SecureString metadata, instance policy, and private S3 uploaded object |
| 8:40-9:20 | Delete the demonstration episode; recap only any incomplete requirement honestly |

Use a newly added comment for the permitted edit. Keep an older seeded comment for the locked state. The deadline uses the original UTC creation time, so restarting or editing does not reopen it.

## Before submitting

- Check registration and login against AWS mode, not the Local preview.
- Check the database script and C# source are in the submission ZIP.
- Rename the final package to `group#N(Besli&PartnerSurname)_Lab#3.zip` with actual group details.
- Have one member submit the required package/video through Luminate.
- Verify the video duration and both visible cameras.

## After the recording

The assignment asks you to delete the lab's RDS server and deployed Elastic Beanstalk application/environment after recording, and **retain the DynamoDB table** so the professor can run the application locally.

Identify the exact resources belonging to this lab before deleting them. Do not delete shared resources. Check whether retaining an RDS snapshot or other resources will continue to incur charges. Keep the source ZIP and the DynamoDB table information needed by the professor. Configure scoped reviewer access through the instructor's approved process; never place an AWS access key in the submission.

The app's AWS mode requires SQL Server as well as DynamoDB. After RDS cleanup, a reviewer can apply the supplied SQL script to their own SQL Server and provide that connection through Parameter Store. The Local preview remains available for a quick source review, but does not claim cloud equivalence.

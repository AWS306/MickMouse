-- Mick Mouse COMP306 Lab 3. Run on an EMPTY SQL Server database.
-- Demo accounts only: password TrailDemo!42. Rotate these before public use.
-- Optional Identity claims/logins/tokens tables intentionally remain empty.
-- Playback counts below are example app data, not the source podcast's audience metrics.
CREATE TABLE [AspNetUsers] (
    [Id] uniqueidentifier NOT NULL,
    [DisplayName] nvarchar(100) NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [IsDisabled] bit NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NOT NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] uniqueidentifier NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserTokens] (
    [UserId] uniqueidentifier NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [Podcasts] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(120) NOT NULL,
    [Description] nvarchar(2000) NOT NULL,
    [HostName] nvarchar(100) NOT NULL,
    [Topic] nvarchar(80) NOT NULL,
    [CoverKey] nvarchar(40) NOT NULL,
    [CreatorId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Podcasts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Podcasts_AspNetUsers_CreatorId] FOREIGN KEY ([CreatorId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [Episodes] (
    [Id] int NOT NULL IDENTITY,
    [PodcastId] int NOT NULL,
    [Title] nvarchar(160) NOT NULL,
    [Summary] nvarchar(3000) NOT NULL,
    [Topic] nvarchar(80) NOT NULL,
    [CoverKey] nvarchar(40) NOT NULL,
    [ReleaseDateUtc] datetime2 NOT NULL,
    [DurationMinutes] int NOT NULL,
    [PlayCount] bigint NOT NULL,
    [MediaKey] nvarchar(512) NULL,
    [MediaContentType] nvarchar(100) NULL,
    [SourceUrl] nvarchar(1000) NULL,
    [MediaCredit] nvarchar(1000) NULL,
    [Status] nvarchar(20) NOT NULL,
    [IsDeleted] bit NOT NULL,
    [DeletedAtUtc] datetime2 NULL,
    [CleanupCompleted] bit NOT NULL,
    [Version] int NOT NULL,
    CONSTRAINT [PK_Episodes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Episodes_Podcasts_PodcastId] FOREIGN KEY ([PodcastId]) REFERENCES [Podcasts] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [Subscriptions] (
    [Id] int NOT NULL IDENTITY,
    [UserId] uniqueidentifier NOT NULL,
    [PodcastId] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Subscriptions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Subscriptions_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Subscriptions_Podcasts_PodcastId] FOREIGN KEY ([PodcastId]) REFERENCES [Podcasts] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [EpisodeLikes] (
    [Id] int NOT NULL IDENTITY,
    [UserId] uniqueidentifier NOT NULL,
    [EpisodeId] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_EpisodeLikes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_EpisodeLikes_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_EpisodeLikes_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [Episodes] ([Id]) ON DELETE CASCADE
);
GO


CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
GO


CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
GO


CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
GO


CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;
GO


CREATE INDEX [IX_EpisodeLikes_EpisodeId] ON [EpisodeLikes] ([EpisodeId]);
GO


CREATE UNIQUE INDEX [IX_EpisodeLikes_UserId_EpisodeId] ON [EpisodeLikes] ([UserId], [EpisodeId]);
GO


CREATE INDEX [IX_Episodes_IsDeleted_Status_ReleaseDateUtc] ON [Episodes] ([IsDeleted], [Status], [ReleaseDateUtc]);
GO


CREATE INDEX [IX_Episodes_PlayCount] ON [Episodes] ([PlayCount]);
GO


CREATE INDEX [IX_Episodes_PodcastId] ON [Episodes] ([PodcastId]);
GO


CREATE INDEX [IX_Episodes_Topic] ON [Episodes] ([Topic]);
GO


CREATE INDEX [IX_Podcasts_CreatorId] ON [Podcasts] ([CreatorId]);
GO


CREATE INDEX [IX_Podcasts_HostName] ON [Podcasts] ([HostName]);
GO


CREATE INDEX [IX_Podcasts_Topic] ON [Podcasts] ([Topic]);
GO


CREATE INDEX [IX_Subscriptions_PodcastId] ON [Subscriptions] ([PodcastId]);
GO


CREATE UNIQUE INDEX [IX_Subscriptions_UserId_PodcastId] ON [Subscriptions] ([UserId], [PodcastId]);
GO



SET XACT_ABORT ON;
BEGIN TRANSACTION;
INSERT INTO [AspNetUsers] ([Id],[DisplayName],[Role],[IsDisabled],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('10000000-0000-0000-0000-000000000001',N'Studio Admin',N'Admin',0,N'admin@mick.local',N'ADMIN@MICK.LOCAL',N'admin@mick.local',N'ADMIN@MICK.LOCAL',1,N'AQAAAAIAAYagAAAAEOjr5Ce2fSR/AKdElEknZD5bR45K+hZcRdgZQkRe2l+nagnP1AXQ8IPhBxCzR4EuMw==',N'438a8810-b331-448f-921d-81585eee31a3',N'cf06785b-0168-4512-af66-e5852f7af9e4',NULL,0,0,NULL,1,0);
INSERT INTO [AspNetUsers] ([Id],[DisplayName],[Role],[IsDisabled],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('10000000-0000-0000-0000-000000000002',N'Cem',N'Podcaster',0,N'cem@mick.local',N'CEM@MICK.LOCAL',N'cem@mick.local',N'CEM@MICK.LOCAL',1,N'AQAAAAIAAYagAAAAEGAJ7gXyLe2ZEx4P5nZJ0CqmZk2wMeX+QRanqCvlO9Byq/nImGcjy1mlIRYHTroOvw==',N'3e2ec6df-93d7-4959-af22-9038fb0cf651',N'6de702a3-30ce-4d81-a284-4bb642a746a3',NULL,0,0,NULL,1,0);
INSERT INTO [AspNetUsers] ([Id],[DisplayName],[Role],[IsDisabled],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('10000000-0000-0000-0000-000000000003',N'Maya',N'Podcaster',0,N'maya@mick.local',N'MAYA@MICK.LOCAL',N'maya@mick.local',N'MAYA@MICK.LOCAL',1,N'AQAAAAIAAYagAAAAEPPBeBWY5iRE0jv8FzhC/XNwjT+WAaDxqYTfOLIj+sB7y+F5FqNCNhM/CfdxzhB/bg==',N'1e9473b7-947a-4705-b949-fd33b8b2d379',N'209ef906-a814-4962-be9b-417b229412ea',NULL,0,0,NULL,1,0);
INSERT INTO [AspNetUsers] ([Id],[DisplayName],[Role],[IsDisabled],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('10000000-0000-0000-0000-000000000004',N'Alex',N'Listener',0,N'alex@mick.local',N'ALEX@MICK.LOCAL',N'alex@mick.local',N'ALEX@MICK.LOCAL',1,N'AQAAAAIAAYagAAAAELMCMS67bFA+RH+7a92YPlSVb1fHd0s5hs+iRSAYH5C4pwbxyGDNlDl7S9U0nP2hag==',N'c64003b1-0172-414d-b6ce-675c9953ba4f',N'b9358923-44c8-4140-916f-4834756f3a72',NULL,0,0,NULL,1,0);
INSERT INTO [AspNetUsers] ([Id],[DisplayName],[Role],[IsDisabled],[UserName],[NormalizedUserName],[Email],[NormalizedEmail],[EmailConfirmed],[PasswordHash],[SecurityStamp],[ConcurrencyStamp],[PhoneNumber],[PhoneNumberConfirmed],[TwoFactorEnabled],[LockoutEnd],[LockoutEnabled],[AccessFailedCount]) VALUES ('10000000-0000-0000-0000-000000000005',N'Rain',N'Listener',0,N'rain@mick.local',N'RAIN@MICK.LOCAL',N'rain@mick.local',N'RAIN@MICK.LOCAL',1,N'AQAAAAIAAYagAAAAEPe+QwzopK5BX2mRn4s2JFiBYHqWbf4jYgN/E3IEwRfL6kL/ckpqFKWOnaZsIyDaiw==',N'1ee1e180-a839-4a49-b875-8902f0c2698b',N'ceb07cf7-c5ff-4b0d-b76d-201f482cd780',NULL,0,0,NULL,1,0);
SET IDENTITY_INSERT [Podcasts] ON;
INSERT INTO [Podcasts] ([Id],[Title],[Description],[HostName],[Topic],[CoverKey],[CreatorId],[CreatedAtUtc]) VALUES (1,N'LINUX Unplugged',N'Community conversations about Linux systems, open software, and practical experiments.',N'Brent Gervais, Chris Fisher, Wes Payne',N'Linux',N'trail','10000000-0000-0000-0000-000000000002','2026-10-03T00:00:00.0000000');
INSERT INTO [Podcasts] ([Id],[Title],[Description],[HostName],[Topic],[CoverKey],[CreatorId],[CreatedAtUtc]) VALUES (2,N'Coder Radio',N'A discussion of software development, engineering choices, and the working life of developers.',N'Chris Fisher, Michael Dominick',N'Software development',N'systems','10000000-0000-0000-0000-000000000002','2025-01-14T00:00:00.0000000');
INSERT INTO [Podcasts] ([Id],[Title],[Description],[HostName],[Topic],[CoverKey],[CreatorId],[CreatedAtUtc]) VALUES (3,N'Self-Hosted',N'Practical stories about running your own services, home servers, and connected devices.',N'Alex Kretzschmar, Chris Fisher',N'Self-hosting',N'rain','10000000-0000-0000-0000-000000000003','2025-05-01T00:00:00.0000000');
INSERT INTO [Podcasts] ([Id],[Title],[Description],[HostName],[Topic],[CoverKey],[CreatorId],[CreatedAtUtc]) VALUES (4,N'Linux Action News',N'A focused review of changes across the Linux and open-source ecosystem.',N'Chris Fisher, Wes Payne',N'Linux news',N'workshop','10000000-0000-0000-0000-000000000002','2023-06-28T00:00:00.0000000');
INSERT INTO [Podcasts] ([Id],[Title],[Description],[HostName],[Topic],[CoverKey],[CreatorId],[CreatedAtUtc]) VALUES (5,N'Jupiter EXTRAS',N'Longer interviews with people building software, technology, and open communities.',N'Brent Gervais',N'Interviews',N'map','10000000-0000-0000-0000-000000000003','2022-06-18T00:00:00.0000000');
SET IDENTITY_INSERT [Podcasts] OFF;
SET IDENTITY_INSERT [Episodes] ON;
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (1,1,N'Eyes on Deck',N'A Steam Deck becomes the starting point for an experiment with locally hosted security monitoring.',N'Linux',N'trail','2026-10-11T00:00:00.0000000',74,1248,N'samples/episode-01.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/linux-unplugged/688/',N'LINUX Unplugged 688 by Jupiter Broadcasting; Brent Gervais, Chris Fisher, Wes Payne. CC BY-NC 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (2,1,N'Linux by Proxy',N'The hosts investigate the Linux environments behind cloud desktop products and consider how to use them.',N'Linux',N'trail','2026-10-04T00:00:00.0000000',76,835,N'samples/episode-02.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/linux-unplugged/687/',N'LINUX Unplugged 687 by Jupiter Broadcasting; Brent Gervais, Chris Fisher, Wes Payne. CC BY-NC 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (3,2,N'The Startup Myth',N'A conversation about developer careers, framework choices, and the appeal of startup work.',N'Software development',N'systems','2025-01-22T00:00:00.0000000',39,978,N'samples/episode-03.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/coder-radio/604/',N'Coder Radio 604 by Jupiter Broadcasting; Chris Fisher, Michael Dominick. CC BY-SA 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (4,2,N'COSMIC Defenders',N'Software decisions take centre stage in a discussion spanning desktop development and difficult application releases.',N'Software development',N'systems','2025-01-15T00:00:00.0000000',44,642,N'samples/episode-04.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/coder-radio/603/',N'Coder Radio 603 by Jupiter Broadcasting; Chris Fisher, Michael Dominick. CC BY-SA 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (5,3,N'Notify Thyself',N'The team explores home-server operating systems and ways to send notifications from self-hosted services.',N'Self-hosting',N'rain','2025-05-16T00:00:00.0000000',67,1536,N'samples/episode-05.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/self-hosted/149/',N'Self-Hosted 149 by Jupiter Broadcasting; Alex Kretzschmar, Chris Fisher. CC BY-SA 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (6,3,N'Homelab Disaster Prep',N'An extended power outage prompts a practical discussion about keeping a home lab resilient.',N'Self-hosting',N'rain','2025-05-02T00:00:00.0000000',63,1104,N'samples/episode-06.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/self-hosted/148/',N'Self-Hosted 148 by Jupiter Broadcasting; Alex Kretzschmar, Chris Fisher. CC BY-SA 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (7,4,N'Linux Action News 299',N'An archived news briefing covering embedded Linux, LXD governance, and ZFS development.',N'Linux news',N'workshop','2023-07-06T00:00:00.0000000',14,724,N'samples/episode-07.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/linux-action-news/299/',N'Linux Action News 299 by Jupiter Broadcasting; Chris Fisher, Wes Payne. CC BY-SA 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (8,4,N'Linux Action News 298',N'An archived look at Linux kernel changes, graphics development, and enterprise distribution debates.',N'Linux news',N'workshop','2023-06-29T00:00:00.0000000',14,512,N'samples/episode-08.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/linux-action-news/298/',N'Linux Action News 298 by Jupiter Broadcasting; Chris Fisher, Wes Payne. CC BY-SA 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (9,5,N'Brunch With Brent: Tim Canham',N'Brent interviews Tim Canham about spacecraft software, open tools, and the engineering behind Mars exploration.',N'Interviews',N'map','2022-07-10T00:00:00.0000000',59,905,N'samples/episode-09.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/jupiter-extras/87/',N'Jupiter EXTRAS 87 by Jupiter Broadcasting; Brent Gervais with Tim Canham. CC BY-SA 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
INSERT INTO [Episodes] ([Id],[PodcastId],[Title],[Summary],[Topic],[CoverKey],[ReleaseDateUtc],[DurationMinutes],[PlayCount],[MediaKey],[MediaContentType],[SourceUrl],[MediaCredit],[Status],[IsDeleted],[DeletedAtUtc],[CleanupCompleted],[Version]) VALUES (10,5,N'Brunch with Brent: Quentin Stafford-Fraser',N'A conversation with Quentin Stafford-Fraser about invention, computing history, and a career of experiments.',N'Interviews',N'map','2022-06-19T00:00:00.0000000',79,468,N'samples/episode-10.mp3',N'audio/mpeg',N'https://www.jupiterbroadcasting.com/show/jupiter-extras/86/',N'Jupiter EXTRAS 86 by Jupiter Broadcasting; Brent Gervais with Quentin Stafford-Fraser. CC BY-SA 4.0. Edited 20-second excerpt, re-encoded for this course demo.',N'Published',0,NULL,0,1);
SET IDENTITY_INSERT [Episodes] OFF;
SET IDENTITY_INSERT [Subscriptions] ON;
INSERT INTO [Subscriptions] ([Id],[UserId],[PodcastId],[CreatedAtUtc]) VALUES (1,'10000000-0000-0000-0000-000000000004',1,'2026-10-20T12:00:00.0000000');
INSERT INTO [Subscriptions] ([Id],[UserId],[PodcastId],[CreatedAtUtc]) VALUES (2,'10000000-0000-0000-0000-000000000004',2,'2026-10-20T12:00:00.0000000');
INSERT INTO [Subscriptions] ([Id],[UserId],[PodcastId],[CreatedAtUtc]) VALUES (3,'10000000-0000-0000-0000-000000000005',3,'2026-10-20T12:00:00.0000000');
INSERT INTO [Subscriptions] ([Id],[UserId],[PodcastId],[CreatedAtUtc]) VALUES (4,'10000000-0000-0000-0000-000000000005',5,'2026-10-20T12:00:00.0000000');
INSERT INTO [Subscriptions] ([Id],[UserId],[PodcastId],[CreatedAtUtc]) VALUES (5,'10000000-0000-0000-0000-000000000002',3,'2026-10-20T12:00:00.0000000');
INSERT INTO [Subscriptions] ([Id],[UserId],[PodcastId],[CreatedAtUtc]) VALUES (6,'10000000-0000-0000-0000-000000000003',1,'2026-10-20T12:00:00.0000000');
SET IDENTITY_INSERT [Subscriptions] OFF;
SET IDENTITY_INSERT [EpisodeLikes] ON;
INSERT INTO [EpisodeLikes] ([Id],[UserId],[EpisodeId],[CreatedAtUtc]) VALUES (1,'10000000-0000-0000-0000-000000000004',1,'2026-10-20T12:00:00.0000000');
INSERT INTO [EpisodeLikes] ([Id],[UserId],[EpisodeId],[CreatedAtUtc]) VALUES (2,'10000000-0000-0000-0000-000000000004',3,'2026-10-20T12:00:00.0000000');
INSERT INTO [EpisodeLikes] ([Id],[UserId],[EpisodeId],[CreatedAtUtc]) VALUES (3,'10000000-0000-0000-0000-000000000005',5,'2026-10-20T12:00:00.0000000');
INSERT INTO [EpisodeLikes] ([Id],[UserId],[EpisodeId],[CreatedAtUtc]) VALUES (4,'10000000-0000-0000-0000-000000000005',9,'2026-10-20T12:00:00.0000000');
INSERT INTO [EpisodeLikes] ([Id],[UserId],[EpisodeId],[CreatedAtUtc]) VALUES (5,'10000000-0000-0000-0000-000000000002',7,'2026-10-20T12:00:00.0000000');
INSERT INTO [EpisodeLikes] ([Id],[UserId],[EpisodeId],[CreatedAtUtc]) VALUES (6,'10000000-0000-0000-0000-000000000003',1,'2026-10-20T12:00:00.0000000');
SET IDENTITY_INSERT [EpisodeLikes] OFF;
COMMIT TRANSACTION;
SELECT 'Users' AS [TableName], COUNT(*) AS [Rows] FROM [AspNetUsers] UNION ALL SELECT 'Podcasts',COUNT(*) FROM [Podcasts] UNION ALL SELECT 'Episodes',COUNT(*) FROM [Episodes] UNION ALL SELECT 'Subscriptions',COUNT(*) FROM [Subscriptions] UNION ALL SELECT 'EpisodeLikes',COUNT(*) FROM [EpisodeLikes];

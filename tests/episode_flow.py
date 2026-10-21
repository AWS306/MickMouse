#!/usr/bin/env python3
"""Local HTTP episode lifecycle checks with read-only SQLite assertions.

Run through run_local_checks.py to get a disposable database. All mutations use
the application's HTTP endpoints. The original ten seeded episodes are retained.
"""

import concurrent.futures
import datetime
import json
from pathlib import Path
import sqlite3
import sys
import uuid
from urllib.error import HTTPError
from urllib.parse import urljoin, urlparse, urlencode
from urllib.request import Request

from http_smoke import Client, Page, check


def login(base, email):
    client = Client(base)
    page = Page(client.get("/Account/Login"))
    status, _, _ = client.post("/Account/Login", page.token(), Email=email, Password="TrailDemo!42")
    check(status == 200 and email.lower() in client.get("/Account/Profile").lower(), f"Login {email}")
    return client


def multipart(client, path, token, fields, recording):
    boundary = "MickBoundary" + uuid.uuid4().hex
    chunks = []
    for name, value in {"__RequestVerificationToken": token, **fields}.items():
        chunks.append((f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n").encode())
    chunks.extend([
        (f"--{boundary}\r\nContent-Disposition: form-data; name=\"Media\"; filename=\"course-fixture.mp3\"\r\n"
         "Content-Type: audio/mpeg\r\n\r\n").encode(), recording, f"\r\n--{boundary}--\r\n".encode(),
    ])
    request = Request(urljoin(client.base, path.lstrip("/")), data=b"".join(chunks),
                      headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    try:
        with client.opener.open(request, timeout=30) as response:
            return response.status, response.read().decode(), response.geturl()
    except HTTPError as error:
        return error.code, error.read().decode(), error.geturl()


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: episode_flow.py http://127.0.0.1:PORT /absolute/disposable/mick-mouse.db")
    base, database = sys.argv[1:]
    check(urlparse(base).hostname in {"localhost", "127.0.0.1", "::1"}, "Lifecycle target is loopback")
    anonymous = Client(base)
    check("Local preview" in anonymous.get("/Episodes"), "Lifecycle target is visibly Local preview")
    database = Path(database).resolve(strict=True)
    check(database.name == "mick-mouse.db", "Explicit local database used for read-only assertions")
    root = Path(__file__).resolve().parents[1]
    first_audio = (root / "src/MickMouse.Web/wwwroot/media/samples/episode-01.mp3").read_bytes()
    second_audio = (root / "src/MickMouse.Web/wwwroot/media/samples/episode-02.mp3").read_bytes()
    db = sqlite3.connect(database)
    db.row_factory = sqlite3.Row

    def row(sql, *values):
        return db.execute(sql, values).fetchone()

    def scalar(sql, *values):
        return row(sql, *values)[0]

    def token(client, path="/Account/Profile"):
        return Page(client.get(path)).token()

    marker = uuid.uuid4().hex[:10]
    title = "Lifecycle episode " + marker
    show_title = "Lifecycle show " + marker
    cem = login(base, "cem@mick.local")
    maya = login(base, "maya@mick.local")
    admin = login(base, "admin@mick.local")
    listener = login(base, "alex@mick.local")

    status, _, _ = cem.post("/Studio/CreatePodcast", token(cem, "/Studio/CreatePodcast"),
                           Title=show_title, Description="A disposable lifecycle-check podcast.",
                           HostName="Lifecycle Host", Topic="Lifecycle", CoverKey="systems")
    show = row('SELECT * FROM "Podcasts" WHERE "Title" = ?', show_title)
    check(status == 200 and show is not None, "Podcaster creates a podcast through HTTP")
    podcast_id = show["Id"]
    check(show["CreatorId"].lower() == "10000000-0000-0000-0000-000000000002", "Podcast ownership persists")

    fields = {
        "PodcastId": podcast_id, "Title": title, "Summary": "Disposable uploaded episode for lifecycle checks.",
        "Topic": "Lifecycle", "CoverKey": "systems", "DurationMinutes": 2,
        "ReleaseDateUtc": (datetime.datetime.now(datetime.timezone.utc).date() - datetime.timedelta(days=1)).isoformat(),
        "SourceUrl": "https://www.jupiterbroadcasting.com/show/linux-unplugged/688/",
        "MediaCredit": "Jupiter Broadcasting, CC BY-NC 4.0; 20-second course excerpt. See MEDIA_CREDITS.md.",
    }
    forged = {**fields, "PodcastId": 3, "Title": "Forbidden owner swap " + marker}
    status, _, _ = multipart(cem, "/Studio/CreateEpisode", token(cem, "/Studio/CreateEpisode"), forged, first_audio)
    check(status == 403 and scalar('SELECT COUNT(*) FROM "Episodes" WHERE "Title" = ?', forged["Title"]) == 0,
          "Cem cannot create an episode under Maya's podcast 3")
    status, _, _ = multipart(cem, "/Studio/CreateEpisode", token(cem, "/Studio/CreateEpisode"), fields, first_audio)
    episode = row('SELECT * FROM "Episodes" WHERE "Title" = ?', title)
    check(status == 200 and episode is not None, "Valid MP3 uploads and creates an episode")
    episode_id, first_key = episode["Id"], episode["MediaKey"]
    check(episode["Status"] == "Pending" and first_key.startswith("uploads/"), "New episode is Pending with a stored generated media key")
    first_path = database.parent / first_key
    check(first_path.read_bytes() == first_audio, "Uploaded file bytes persist")
    check(anonymous.request(f"/Episodes/Details/{episode_id}")[0] == 404, "Pending episode is hidden publicly")
    check(cem.request(f"/Episodes/Details/{episode_id}")[0] == 200, "Owner can preview pending episode")
    check(maya.request(f"/Studio/EditEpisode/{episode_id}")[0] == 404, "Another podcaster cannot edit owner's episode")
    check(cem.request("/Studio/EditEpisode/5")[0] == 404, "Cem cannot open Maya's seeded episode editor")

    def approve():
        status, _, _ = admin.post(f"/Admin/Approve/{episode_id}", token(admin, "/Admin"))
        check(status == 200 and scalar('SELECT "Status" FROM "Episodes" WHERE "Id" = ?', episode_id) == "Published",
              "Admin approval persists")

    approve()
    check(anonymous.request(f"/Episodes/Details/{episode_id}")[0] == 200, "Approved episode becomes publicly visible")
    search = anonymous.get("/Episodes?" + urlencode({"topic": "Lifecycle", "host": "Lifecycle Host"}))
    check(title in search, "Combined topic and host search finds the created episode")

    play_token = token(anonymous, f"/Episodes/Details/{episode_id}")
    before = scalar('SELECT "PlayCount" FROM "Episodes" WHERE "Id" = ?', episode_id)
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
        results = list(executor.map(lambda _: anonymous.post(f"/Episodes/Play/{episode_id}", play_token), range(8)))
    check(all(result[0] == 200 for result in results)
          and scalar('SELECT "PlayCount" FROM "Episodes" WHERE "Id" = ?', episode_id) == before + 8,
          "Eight concurrent plays retain all eight increments")

    listener_id = "10000000-0000-0000-0000-000000000004"
    for path, table, foreign_key, target_id in [
        (f"/Episodes/Like/{episode_id}", "EpisodeLikes", "EpisodeId", episode_id),
        (f"/Podcasts/Subscribe/{podcast_id}", "Subscriptions", "PodcastId", podcast_id),
    ]:
        action_token = token(listener, f"/Episodes/Details/{episode_id}")
        for expected in (1, 0):
            status, _, _ = listener.post(path, action_token)
            count = scalar(f'SELECT COUNT(*) FROM "{table}" WHERE "{foreign_key}" = ? AND lower("UserId") = ?', target_id, listener_id)
            check(status == 200 and count == expected, f"{table} toggle persists expected count {expected}")
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
            results = list(executor.map(lambda _: listener.post(path, action_token), range(4)))
        count = scalar(f'SELECT COUNT(*) FROM "{table}" WHERE "{foreign_key}" = ? AND lower("UserId") = ?', target_id, listener_id)
        check(all(result[0] == 200 for result in results) and count <= 1, f"Concurrent {table} actions never create duplicates")
        if count == 0: listener.post(path, action_token)

    status, _, _ = listener.post(f"/Episodes/Comment/{episode_id}", token(listener, f"/Episodes/Details/{episode_id}"), text="Lifecycle cleanup note " + marker)
    check(status == 200 and "Lifecycle cleanup note " + marker in listener.get(f"/Episodes/Details/{episode_id}"),
          "Comment is available before episode cleanup")

    def edit_fields():
        episode = row('SELECT * FROM "Episodes" WHERE "Id" = ?', episode_id)
        return {**fields, "Id": episode_id, "Version": episode["Version"], "Title": title + " revised"}

    edit = edit_fields()
    status, _, _ = cem.post(f"/Studio/EditEpisode/{episode_id}", token(cem, f"/Studio/EditEpisode/{episode_id}"), **edit)
    check(status == 200 and scalar('SELECT "Title" FROM "Episodes" WHERE "Id" = ?', episode_id) == edit["Title"],
          "Owner updates episode metadata")
    check(scalar('SELECT "Status" FROM "Episodes" WHERE "Id" = ?', episode_id) == "Pending"
          and anonymous.request(f"/Episodes/Details/{episode_id}")[0] == 404,
          "Editing resets approval and hides the episode until review")
    check(scalar('SELECT "MediaKey" FROM "Episodes" WHERE "Id" = ?', episode_id) == first_key,
          "Metadata-only edit retains existing media")
    approve()

    edit = edit_fields()
    status, _, _ = multipart(cem, f"/Studio/EditEpisode/{episode_id}", token(cem, f"/Studio/EditEpisode/{episode_id}"), edit, second_audio)
    second_key = scalar('SELECT "MediaKey" FROM "Episodes" WHERE "Id" = ?', episode_id)
    check(status == 200 and second_key != first_key and (database.parent / second_key).read_bytes() == second_audio,
          "Replacing media saves the new recording")
    check(not first_path.exists(), "Replacing media removes the unused prior upload")
    approve()
    media_url = "/Media/Play?" + urlencode({"key": second_key})
    request = Request(urljoin(anonymous.base, media_url.lstrip("/")), headers={"Range": "bytes=0-127"})
    with anonymous.opener.open(request, timeout=30) as response:
        check(response.status == 206 and response.read() == second_audio[:128], "Published media supports accurate byte-range playback")

    status, _, _ = cem.post(f"/Studio/DeletePodcast/{podcast_id}", token(cem, f"/Studio/DeletePodcast/{podcast_id}"))
    check(status == 200 and scalar('SELECT COUNT(*) FROM "Podcasts" WHERE "Id" = ?', podcast_id) == 1,
          "Podcast deletion waits until its episodes are deleted and cleaned")
    status, _, _ = cem.post(f"/Studio/DeleteEpisode/{episode_id}", token(cem, f"/Studio/DeleteEpisode/{episode_id}"))
    deleted = row('SELECT * FROM "Episodes" WHERE "Id" = ?', episode_id)
    check(status == 200 and deleted["IsDeleted"] == 1 and deleted["CleanupCompleted"] == 1 and deleted["MediaKey"] is None,
          "Episode delete completes its retryable cleanup state")
    comments = json.loads((database.parent / "comments.json").read_text())
    check(not any(comment["episodeId"] == episode_id for comment in comments)
          and scalar('SELECT COUNT(*) FROM "EpisodeLikes" WHERE "EpisodeId" = ?', episode_id) == 0
          and not (database.parent / second_key).exists(), "Episode cleanup removes its comments, likes, and uploaded media")
    check(anonymous.request(f"/Episodes/Details/{episode_id}")[0] == 404, "Deleted episode is inaccessible publicly")
    status, _, _ = cem.post(f"/Studio/DeletePodcast/{podcast_id}", token(cem, f"/Studio/DeletePodcast/{podcast_id}"))
    check(status == 200 and scalar('SELECT COUNT(*) FROM "Podcasts" WHERE "Id" = ?', podcast_id) == 0
          and scalar('SELECT COUNT(*) FROM "Subscriptions" WHERE "PodcastId" = ?', podcast_id) == 0,
          "Empty podcast and dependent subscriptions can be removed")
    check(scalar('SELECT COUNT(*) FROM "Episodes" WHERE "Id" BETWEEN 1 AND 10 AND "IsDeleted" = 0') == 10,
          "All ten original sample episodes remain intact")
    db.close()
    print("Completed Local HTTP lifecycle checks.")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"FAIL {error}", file=sys.stderr)
        sys.exit(1)
